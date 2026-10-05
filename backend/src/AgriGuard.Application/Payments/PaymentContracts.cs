using AgriGuard.Domain.Inventory;

namespace AgriGuard.Application.Payments;

// ── The payment provider, behind an interface ───────────────────────────────

/// <summary>
/// The card payment provider (Stripe in test mode). Behind an interface so the tests use a fake and
/// the rest of the system never depends on one provider's API.
///
/// The provider hosts the checkout page, so a card number never reaches AgriGuard. What AgriGuard
/// trusts is only what it reads back from the provider with its own secret key.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>"stripe": stored on each attempt, so a record says who took the money.</summary>
    string Name { get; }

    /// <summary>False when no provider key is configured; card payments are then switched off.</summary>
    bool IsConfigured { get; }

    /// <summary>Opens a hosted checkout for one attempt. Repeating the call for the same attempt returns the same session.</summary>
    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default);

    /// <summary>The session as the provider sees it now: the only source of truth for "paid".</summary>
    Task<CheckoutSession> GetCheckoutAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Closes an open session so it can no longer be paid. Harmless if it is already closed.</summary>
    Task ExpireCheckoutAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Checks a webhook's signature and reads which session it is about. Throws
    /// <see cref="WebhookSignatureException"/> when the signature is missing, wrong or stale.
    /// Returns null for an event that is not about a checkout session.
    /// </summary>
    GatewayEvent? ReadWebhook(string payload, string? signatureHeader);
}

public sealed record CheckoutRequest(
    Guid PaymentId,
    string OrderNo,
    string Description,
    long AmountMinor,
    string Currency,
    string? CustomerEmail,
    // Plain strings, not Uri: the success URL carries Stripe's literal {CHECKOUT_SESSION_ID}
    // placeholder, which Uri would escape.
    string SuccessUrl,
    string CancelUrl,
    DateTime ExpiresAt);

public enum CheckoutState
{
    Open,
    Complete,
    Expired
}

public sealed record CheckoutSession(
    string Id,
    Uri? Url,
    CheckoutState State,
    bool Paid,
    long? AmountTotal,
    string? Currency,
    string? PaymentIntentId,
    string? CardBrand,
    string? CardLast4,
    DateTime? ExpiresAt);

public sealed record GatewayEvent(string Type, string SessionId);

/// <summary>A webhook whose signature does not prove it came from the provider. Answered 400.</summary>
public sealed class WebhookSignatureException(string message) : Exception(message);

/// <summary>The provider refused a request or could not be reached.</summary>
public sealed class PaymentGatewayException(string message, Exception? inner = null) : Exception(message, inner);

// ── What the clients see ────────────────────────────────────────────────────

/// <summary>Where to send the farmer to pay, and until when that page stays open.</summary>
public sealed record CardCheckoutDto(Guid PaymentId, Uri CheckoutUrl, DateTime ExpiresAt);

/// <summary>An order's money, as the farmer and the dealer see it.</summary>
public sealed record OrderPaymentDto(
    Guid OrderId,
    string OrderNo,
    OrderPaymentStatus PaymentStatus,
    DateTime? PaidAt,
    PaymentMethod? PaidBy,
    string? CardBrand,
    string? CardLast4,
    // The latest card attempt, so the phone can say "still waiting" or "expired, try again".
    PaymentAttemptStatus? LatestAttemptStatus,
    string? LatestAttemptFailure);

/// <summary>What the "back from the payment page" page tells the farmer.</summary>
public sealed record PaymentReturnResult(bool Found, string? OrderNo, bool Paid, PaymentAttemptStatus? Status);

public interface IPaymentService
{
    /// <summary>
    /// Non-CRUD: the farmer starts paying an order by card. Reuses the open checkout if there is one,
    /// so a second tap does not open a second way to pay. 403 unless it is the farmer's own order.
    /// </summary>
    Task<CardCheckoutDto> StartCardPaymentAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Asks the provider how a card attempt stands and records the answer: the farmer's app calls
    /// this when they come back from the payment page.
    /// </summary>
    Task<OrderPaymentDto> SyncCardPaymentAsync(Guid orderId, Guid paymentId, CancellationToken ct = default);

    /// <summary>Non-CRUD: the dealer records cash taken at the counter. Closes any open card checkout.</summary>
    Task<OrderPaymentDto> RecordCashPaymentAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>The provider's signed notification that a session changed. Unknown sessions are ignored.</summary>
    Task HandleWebhookAsync(string payload, string? signatureHeader, CancellationToken ct = default);

    /// <summary>
    /// The browser lands here after paying. Anyone could call it with a session id, so it only ever
    /// re-reads the provider's own answer; it cannot mark anything paid by itself.
    /// </summary>
    Task<PaymentReturnResult> CompleteFromReturnAsync(string sessionId, CancellationToken ct = default);
}
