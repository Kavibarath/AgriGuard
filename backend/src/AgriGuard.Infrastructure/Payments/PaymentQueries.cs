using System.Linq.Expressions;
using AgriGuard.Domain.Inventory;

namespace AgriGuard.Infrastructure.Payments;

/// <summary>Shared filters, so the order lists and the payment views agree on what "paid by" means.</summary>
internal static class PaymentQueries
{
    /// <summary>The payment that paid the order: succeeded, and not a duplicate waiting for a refund.</summary>
    public static readonly Expression<Func<Payment, bool>> Settled =
        p => p.Status == PaymentAttemptStatus.Succeeded && !p.RefundDue;
}
