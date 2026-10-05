using AgriGuard.Domain.Common;
using AgriGuard.Domain.Identity;

namespace AgriGuard.Domain.Inventory;

/// <summary>Whether an order's money has been received. Only a succeeded payment makes it Paid.</summary>
public enum OrderPaymentStatus
{
    Unpaid,
    Paid
}

public enum PaymentMethod
{
    /// <summary>Visa or Mastercard, through the payment provider's hosted checkout page.</summary>
    Card,

    /// <summary>Cash at the dealer's counter, recorded by the dealer.</summary>
    Cash
}

public enum PaymentAttemptStatus
{
    /// <summary>A card checkout is open: the farmer has the provider's page but has not paid yet.</summary>
    Pending,
    Succeeded,
    Failed,

    /// <summary>The checkout page timed out unpaid.</summary>
    Expired,

    /// <summary>Withdrawn before it was paid, e.g. because the order was paid in cash meanwhile.</summary>
    Cancelled
}

/// <summary>
/// One attempt to pay for an order. A card attempt starts Pending with the provider's checkout
/// session; it becomes Succeeded only when the provider itself says the session is paid (never on
/// the client's word). A cash payment is recorded Succeeded by the dealer at the counter.
///
/// AgriGuard never sees a card number: the farmer types it on the provider's page, and only the
/// card's brand and last four digits come back, for the receipt.
/// </summary>
public class Payment : AuditableEntity
{
    public Guid OrderId { get; set; }
    public InputOrder Order { get; set; } = null!;

    public PaymentMethod Method { get; set; }
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;

    /// <summary>Copied from the order when the attempt starts, so a later price change cannot alter it.</summary>
    public decimal Amount { get; set; }

    /// <summary>ISO 4217, upper case (LKR).</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>"stripe" for card attempts, "counter" for cash.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider's checkout session id (cs_…). Unique: one session belongs to one attempt.</summary>
    public string? ProviderReference { get; set; }

    /// <summary>The provider's payment id (pi_…), once paid. The reference for a refund.</summary>
    public string? ProviderPaymentId { get; set; }

    /// <summary>The hosted checkout page the farmer is sent to while the attempt is Pending.</summary>
    public string? CheckoutUrl { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>
    /// The card was charged after the order had already been paid another way (cash recorded at the
    /// same moment). The money was taken, so the attempt is Succeeded, but it must be refunded.
    /// </summary>
    public bool RefundDue { get; set; }

    /// <summary>The dealer who took a cash payment.</summary>
    public Guid? RecordedById { get; set; }
    public User? RecordedBy { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// The money rules for an input order, pure and unit-tested:
/// - an order can be paid once it is confirmed, until it is collected or cancelled;
/// - it is handed over only when paid;
/// - amounts go to the provider in the currency's smallest unit, exactly.
/// </summary>
public static class PaymentRules
{
    /// <summary>Currencies with no minor unit (Stripe's list of zero-decimal currencies, the ones that matter here).</summary>
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    /// <summary>
    /// Why an order cannot be paid now, or null when it can. The message reaches the farmer or the
    /// dealer, so it says what is going on.
    /// </summary>
    public static string? ExplainCannotPay(OrderStatus status, OrderPaymentStatus paymentStatus, decimal total)
    {
        if (paymentStatus == OrderPaymentStatus.Paid)
            return "This order is already paid.";

        return status switch
        {
            OrderStatus.Draft => "This order is not confirmed yet.",
            OrderStatus.Cancelled => "This order was cancelled, so there is nothing to pay.",
            OrderStatus.Collected => "This order has already been collected.",
            _ when total <= 0 => "This order has nothing to pay.",
            _ => null
        };
    }

    /// <summary>Why an order may not be handed over yet, or null when it may.</summary>
    public static string? ExplainHandOver(OrderPaymentStatus paymentStatus) =>
        paymentStatus == OrderPaymentStatus.Paid
            ? null
            : "This order has not been paid. Take the payment in cash first, or ask the farmer to pay by card in the app.";

    /// <summary>
    /// The amount in the currency's smallest unit (cents, or whole units for a zero-decimal
    /// currency). Refuses an amount that does not convert exactly, rather than rounding money.
    /// </summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A payment must be for more than zero.");

        var factor = ZeroDecimal.Contains(currency) ? 1m : 100m;
        var minor = amount * factor;
        if (minor != decimal.Truncate(minor))
            throw new ArgumentException($"{amount} {currency} has more decimal places than the currency allows.", nameof(amount));

        return (long)minor;
    }
}
