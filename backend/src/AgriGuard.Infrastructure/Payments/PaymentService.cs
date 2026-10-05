using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Payments;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Inventory;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Payments;

/// <summary>
/// Paying for an input order: by card through the provider's hosted checkout (the farmer, in the
/// app), or in cash at the counter (recorded by the dealer).
///
/// The rule that matters: **an order becomes Paid only on the provider's own word**, read back with
/// AgriGuard's secret key (<see cref="ApplyAsync"/>). Nothing the phone or a browser says can mark an
/// order paid. Three paths lead to that read-back, and all are safe to repeat:
/// - the app, when the farmer returns from the payment page (<see cref="SyncCardPaymentAsync"/>);
/// - the return page the browser lands on (<see cref="CompleteFromReturnAsync"/>);
/// - the provider's signed webhook (<see cref="HandleWebhookAsync"/>), when deployed with one.
///
/// Settling runs in a serializable transaction, so a card payment and a cash payment arriving at
/// the same moment cannot both mark the order paid unnoticed: the loser is flagged for a refund.
/// </summary>
public sealed class PaymentService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    IPaymentGateway gateway,
    IOptions<PaymentOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentService> logger) : IPaymentService
{
    public const string CounterProvider = "counter";

    /// <summary>An open checkout closer to expiry than this is replaced rather than handed out again.</summary>
    private static readonly TimeSpan ReuseMargin = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> SettlingEvents = new(StringComparer.Ordinal)
    {
        "checkout.session.completed",
        "checkout.session.async_payment_succeeded",
        "checkout.session.expired"
    };

    private PaymentOptions Options => options.Value;
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ── Card, from the farmer's app ─────────────────────────────────────────

    public async Task<CardCheckoutDto> StartCardPaymentAsync(Guid orderId, CancellationToken ct = default)
    {
        var farmerId = RequireFarmer();
        if (!gateway.IsConfigured)
            throw new ServiceUnavailableException("CARD_PAYMENTS_UNAVAILABLE", "Card payments are not switched on. You can pay in cash when you collect the order.");

        var order = await OwnOrderAsync(orderId, farmerId, ct);
        if (PaymentRules.ExplainCannotPay(order.Status, order.PaymentStatus, order.TotalAmount) is { } refusal)
            throw new BusinessRuleException("ORDER_NOT_PAYABLE", refusal);

        // A second tap must not open a second way to pay: hand out the open checkout while it has
        // time left. One close to expiry is settled (it may have just been paid) and then replaced.
        var open = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId && p.Method == PaymentMethod.Card && p.Status == PaymentAttemptStatus.Pending, ct);
        if (open is not null)
        {
            if (open.CheckoutUrl is { } url && open.ExpiresAt > Now + ReuseMargin)
                return new CardCheckoutDto(open.Id, new Uri(url), open.ExpiresAt.Value);

            await RetireAsync(open.Id, "Replaced by a new checkout.", ct);
            order = await OwnOrderAsync(orderId, farmerId, ct);
            if (order.PaymentStatus == OrderPaymentStatus.Paid)
                throw new BusinessRuleException("ORDER_NOT_PAYABLE", "This order is already paid.");
        }

        var payment = new Payment
        {
            OrderId = order.Id,
            Method = PaymentMethod.Card,
            Status = PaymentAttemptStatus.Pending,
            Amount = order.TotalAmount,
            Currency = Options.Currency.ToUpperInvariant(),
            Provider = gateway.Name,
            ExpiresAt = Now + Options.CheckoutLifetime
        };
        db.Payments.Add(payment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "one_pending_card"))
        {
            // Two taps at once: the database lets only one open card attempt exist per order.
            db.ChangeTracker.Clear();
            var winner = await db.Payments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.OrderId == orderId && p.Method == PaymentMethod.Card && p.Status == PaymentAttemptStatus.Pending, ct);
            if (winner is { CheckoutUrl: { } winnerUrl, ExpiresAt: { } winnerExpiry })
                return new CardCheckoutDto(winner.Id, new Uri(winnerUrl), winnerExpiry);
            throw new ConflictException("A payment page is being opened for this order already. Try again in a moment.");
        }

        CheckoutSession session;
        try
        {
            session = await gateway.CreateCheckoutAsync(new CheckoutRequest(
                payment.Id,
                order.OrderNo,
                order.Prescription is { } rx ? $"Agro-inputs for prescription {rx.PrescriptionNo}, from {order.Dealer.ShopName}" : $"Agro-inputs from {order.Dealer.ShopName}",
                PaymentRules.ToMinorUnits(payment.Amount, payment.Currency),
                payment.Currency,
                order.Farmer.Email,
                $"{BaseUrl}/payments/return?session_id={{CHECKOUT_SESSION_ID}}",
                $"{BaseUrl}/payments/cancelled",
                payment.ExpiresAt.Value), ct);
            if (session.Url is null)
                throw new PaymentGatewayException("Stripe opened a session without a payment page.");
        }
        catch (PaymentGatewayException ex)
        {
            payment.Status = PaymentAttemptStatus.Failed;
            payment.FailureReason = "The payment page could not be opened.";
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(ex, "Could not open a card checkout for order {OrderNo}", order.OrderNo);
            throw Unavailable();
        }

        payment.ProviderReference = session.Id;
        payment.CheckoutUrl = session.Url.ToString();
        payment.ExpiresAt = session.ExpiresAt ?? payment.ExpiresAt;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Card checkout {SessionId} opened for order {OrderNo}, {Amount} {Currency}",
            session.Id, order.OrderNo, payment.Amount, payment.Currency);
        return new CardCheckoutDto(payment.Id, session.Url, payment.ExpiresAt.Value);
    }

    public async Task<OrderPaymentDto> SyncCardPaymentAsync(Guid orderId, Guid paymentId, CancellationToken ct = default)
    {
        var farmerId = RequireFarmer();
        await OwnOrderAsync(orderId, farmerId, ct);

        var attempt = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId && p.OrderId == orderId)
            .Select(p => new { p.Status, p.ProviderReference })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Payment", paymentId);

        if (attempt is { Status: PaymentAttemptStatus.Pending, ProviderReference: { } sessionId })
            await ApplyAsync(paymentId, await FetchAsync(sessionId, ct), ct);

        return await ViewAsync(orderId, ct);
    }

    // ── Cash, from the dealer's counter ─────────────────────────────────────

    public async Task<OrderPaymentDto> RecordCashPaymentAsync(Guid orderId, CancellationToken ct = default)
    {
        var shopId = await db.RequireOwnShopAsync(currentUser, ct);
        var visible = await db.InputOrders.AnyAsync(o => o.Id == orderId && o.DealerId == shopId, ct);
        DealerScope.EnsureVisible(visible ? (object)true : null, await db.InputOrders.AnyAsync(o => o.Id == orderId, ct), "Order", orderId);

        var openCheckouts = await SerializableTransaction.RunAsync(db, async () =>
        {
            var order = await db.InputOrders.FirstAsync(o => o.Id == orderId, ct);

            // A repeated click finds it paid already and changes nothing.
            if (order.PaymentStatus == OrderPaymentStatus.Paid)
                return new List<Guid>();
            if (PaymentRules.ExplainCannotPay(order.Status, order.PaymentStatus, order.TotalAmount) is { } refusal)
                throw new BusinessRuleException("ORDER_NOT_PAYABLE", refusal);

            db.Payments.Add(new Payment
            {
                OrderId = order.Id,
                Method = PaymentMethod.Cash,
                Status = PaymentAttemptStatus.Succeeded,
                Amount = order.TotalAmount,
                Currency = Options.Currency.ToUpperInvariant(),
                Provider = CounterProvider,
                CompletedAt = Now,
                RecordedById = currentUser.UserId
            });
            order.PaymentStatus = OrderPaymentStatus.Paid;
            order.PaidAt = Now;

            return await db.Payments
                .Where(p => p.OrderId == orderId && p.Method == PaymentMethod.Card && p.Status == PaymentAttemptStatus.Pending)
                .Select(p => p.Id)
                .ToListAsync(ct);
        }, "This order was being paid at the same moment. Reload to see whether it is paid.", ct);

        // The farmer may have a card checkout open. Close it so it cannot be paid as well; if it was
        // paid in the meantime, settling it flags that card payment for a refund.
        foreach (var paymentId in openCheckouts)
            await RetireAsync(paymentId, "Paid in cash at the counter instead.", ct);

        logger.LogInformation("Cash payment recorded for order {OrderId} by dealer {DealerId}", orderId, shopId);
        return await ViewAsync(orderId, ct);
    }

    // ── The provider's word ─────────────────────────────────────────────────

    public async Task HandleWebhookAsync(string payload, string? signatureHeader, CancellationToken ct = default)
    {
        // Throws WebhookSignatureException (answered 400) unless Stripe signed exactly this body.
        var gatewayEvent = gateway.ReadWebhook(payload, signatureHeader);
        if (gatewayEvent is null || !SettlingEvents.Contains(gatewayEvent.Type))
            return;

        var paymentId = await db.Payments.AsNoTracking()
            .Where(p => p.ProviderReference == gatewayEvent.SessionId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (paymentId is null)
        {
            // Not ours (another app on the same Stripe account), or from before a reset.
            logger.LogInformation("Webhook {Type} for unknown checkout {SessionId} ignored", gatewayEvent.Type, gatewayEvent.SessionId);
            return;
        }

        // The signed event says which session changed; the session itself is read back fresh.
        await ApplyAsync(paymentId.Value, await FetchAsync(gatewayEvent.SessionId, ct), ct);
    }

    public async Task<PaymentReturnResult> CompleteFromReturnAsync(string sessionId, CancellationToken ct = default)
    {
        var attempt = await db.Payments.AsNoTracking()
            .Where(p => p.ProviderReference == sessionId)
            .Select(p => new { p.Id, p.OrderId, p.Status })
            .FirstOrDefaultAsync(ct);
        if (attempt is null)
            return new PaymentReturnResult(false, null, false, null);

        if (attempt.Status == PaymentAttemptStatus.Pending)
        {
            try
            {
                await ApplyAsync(attempt.Id, await gateway.GetCheckoutAsync(sessionId, ct), ct);
            }
            catch (PaymentGatewayException ex)
            {
                // The page still answers; the app or the webhook will settle it.
                logger.LogWarning(ex, "Could not read checkout {SessionId} on return", sessionId);
            }
        }

        var now = await db.Payments.AsNoTracking()
            .Where(p => p.Id == attempt.Id)
            .Select(p => new { p.Status, p.Order.OrderNo, Paid = p.Order.PaymentStatus == OrderPaymentStatus.Paid })
            .FirstAsync(ct);
        return new PaymentReturnResult(true, now.OrderNo, now.Paid, now.Status);
    }

    /// <summary>
    /// Records what the provider says about one card attempt. Repeating it changes nothing: once an
    /// attempt has left Pending it is final. Returns whether anything changed.
    /// </summary>
    private Task<bool> ApplyAsync(Guid paymentId, CheckoutSession session, CancellationToken ct) =>
        SerializableTransaction.RunAsync(db, async () =>
        {
            var payment = await db.Payments.Include(p => p.Order).FirstAsync(p => p.Id == paymentId, ct);
            if (payment.Status != PaymentAttemptStatus.Pending)
                return false;

            switch (session.State)
            {
                case CheckoutState.Complete when session.Paid:
                    var expected = PaymentRules.ToMinorUnits(payment.Amount, payment.Currency);
                    if (session.AmountTotal != expected || !string.Equals(session.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
                    {
                        // Money was taken, but not the amount asked for: never mark the order paid on it.
                        payment.Status = PaymentAttemptStatus.Failed;
                        payment.RefundDue = true;
                        payment.FailureReason = $"The provider took {session.AmountTotal} {session.Currency}, not {expected} {payment.Currency}. Refund it.";
                        logger.LogError("Amount mismatch on checkout {SessionId} for order {OrderNo}: {Taken} {TakenCurrency} instead of {Expected} {Currency}",
                            session.Id, payment.Order.OrderNo, session.AmountTotal, session.Currency, expected, payment.Currency);
                        break;
                    }

                    payment.Status = PaymentAttemptStatus.Succeeded;
                    payment.CompletedAt = Now;
                    payment.ProviderPaymentId = session.PaymentIntentId;
                    payment.CardBrand = session.CardBrand;
                    payment.CardLast4 = session.CardLast4;
                    payment.CheckoutUrl = null;

                    if (payment.Order.PaymentStatus == OrderPaymentStatus.Unpaid)
                    {
                        payment.Order.PaymentStatus = OrderPaymentStatus.Paid;
                        payment.Order.PaidAt = Now;
                        logger.LogInformation("Order {OrderNo} paid by card ({Brand} {Last4}), checkout {SessionId}",
                            payment.Order.OrderNo, session.CardBrand, session.CardLast4, session.Id);
                    }
                    else
                    {
                        // Paid twice: cash was recorded while the card went through.
                        payment.RefundDue = true;
                        payment.FailureReason = "The order had already been paid; refund this card payment.";
                        logger.LogWarning("Order {OrderNo} was already paid; card payment {PaymentIntent} needs a refund",
                            payment.Order.OrderNo, session.PaymentIntentId);
                    }
                    break;

                case CheckoutState.Expired:
                    payment.Status = PaymentAttemptStatus.Expired;
                    payment.CheckoutUrl = null;
                    break;

                default:
                    // Still open (or complete but not yet paid): nothing to record yet.
                    return false;
            }

            return true;
        }, "This payment was being recorded at the same moment. Reload to see whether the order is paid.", ct);

    /// <summary>
    /// Takes an open card attempt out of use: settles it first (it may have just been paid), then
    /// closes the session at the provider and marks the attempt Cancelled.
    /// </summary>
    private async Task RetireAsync(Guid paymentId, string reason, CancellationToken ct)
    {
        var sessionId = await db.Payments.Where(p => p.Id == paymentId).Select(p => p.ProviderReference).FirstAsync(ct);
        if (sessionId is not null)
        {
            try
            {
                // Already settled at the provider (paid, or timed out on its own): record that.
                var session = await gateway.GetCheckoutAsync(sessionId, ct);
                if (session.State != CheckoutState.Open)
                {
                    if (await ApplyAsync(paymentId, session, ct))
                        return;
                }
                else
                {
                    await gateway.ExpireCheckoutAsync(sessionId, ct);
                    // Paid in the instant before it closed? Then it is a payment, not a cancellation.
                    var after = await gateway.GetCheckoutAsync(sessionId, ct);
                    if (after is { State: CheckoutState.Complete, Paid: true } && await ApplyAsync(paymentId, after, ct))
                        return;
                }
            }
            catch (PaymentGatewayException ex)
            {
                // The provider will expire it on its own; marking it cancelled here keeps it out of use.
                logger.LogWarning(ex, "Could not close checkout {SessionId} at the provider", sessionId);
            }
        }

        await SerializableTransaction.RunAsync(db, async () =>
        {
            var payment = await db.Payments.FirstAsync(p => p.Id == paymentId, ct);
            if (payment.Status != PaymentAttemptStatus.Pending)
                return false;
            payment.Status = PaymentAttemptStatus.Cancelled;
            payment.CheckoutUrl = null;
            payment.FailureReason = reason;
            return true;
        }, "This payment changed at the same moment. Reload and try again.", ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<OrderPaymentDto> ViewAsync(Guid orderId, CancellationToken ct) =>
        await db.InputOrders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new OrderPaymentDto(
                o.Id,
                o.OrderNo,
                o.PaymentStatus,
                o.PaidAt,
                o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => (PaymentMethod?)p.Method).FirstOrDefault(),
                o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => p.CardBrand).FirstOrDefault(),
                o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => p.CardLast4).FirstOrDefault(),
                o.Payments.Where(p => p.Method == PaymentMethod.Card).OrderByDescending(p => p.CreatedAt).Select(p => (PaymentAttemptStatus?)p.Status).FirstOrDefault(),
                o.Payments.Where(p => p.Method == PaymentMethod.Card).OrderByDescending(p => p.CreatedAt).Select(p => p.FailureReason).FirstOrDefault()))
            .FirstAsync(ct);

    private Guid RequireFarmer() =>
        currentUser.Role == UserRole.Farmer && currentUser.UserId is { } id
            ? id
            : throw new ForbiddenAccessException("Only the farmer an order belongs to can pay for it in the app. Dealers record cash at /api/orders/{id}/payments/cash.");

    /// <summary>The farmer's own order, tracked, with what a checkout needs to describe it. 403 for someone else's.</summary>
    private async Task<InputOrder> OwnOrderAsync(Guid orderId, Guid farmerId, CancellationToken ct)
    {
        var order = await db.InputOrders
            .Include(o => o.Farmer)
            .Include(o => o.Dealer)
            .Include(o => o.Prescription)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.FarmerId == farmerId, ct);
        if (order is not null)
            return order;
        if (await db.InputOrders.AnyAsync(o => o.Id == orderId, ct))
            throw new ForbiddenAccessException("This order belongs to another farmer.");
        throw new NotFoundException("Order", orderId);
    }

    private async Task<CheckoutSession> FetchAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            return await gateway.GetCheckoutAsync(sessionId, ct);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "Could not read checkout {SessionId}", sessionId);
            throw Unavailable();
        }
    }

    private string BaseUrl => Options.PublicBaseUrl.ToString().TrimEnd('/');

    private static ServiceUnavailableException Unavailable() =>
        new("PAYMENT_PROVIDER_UNAVAILABLE", "The card payment service did not answer. Try again in a minute, or pay in cash when you collect the order.");
}
