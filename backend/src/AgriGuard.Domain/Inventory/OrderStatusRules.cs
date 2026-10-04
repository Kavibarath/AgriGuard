namespace AgriGuard.Domain.Inventory;

/// <summary>
/// The dealer's fulfilment workflow for an input order: Confirmed → Packed → Collected.
///
/// An order is created Confirmed by the approval transaction, with its stock already drawn, so the
/// dealer's only job is to put the packs aside and hand them over. Each step is a separate click, so
/// a double-click can at worst repeat a step, never skip one.
///
/// Pure domain logic with no EF or HTTP dependency, so it is unit-tested directly.
/// </summary>
public static class OrderStatusRules
{
    /// <summary>The one status each fulfilment step leads to.</summary>
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus> NextStep = new Dictionary<OrderStatus, OrderStatus>
    {
        [OrderStatus.Draft] = OrderStatus.Confirmed,
        [OrderStatus.Confirmed] = OrderStatus.Packed,
        [OrderStatus.Packed] = OrderStatus.Collected
    };

    /// <summary>The next fulfilment step, or null when the order is finished (Collected, Cancelled).</summary>
    public static OrderStatus? NextFulfilmentStep(OrderStatus from) =>
        NextStep.TryGetValue(from, out var next) ? next : null;

    /// <summary>
    /// Explains why an order may not move from <paramref name="from"/> to <paramref name="to"/>, or
    /// returns null when it may. The message reaches the dealer, so it says what to do instead.
    /// </summary>
    public static string? ExplainFulfilment(OrderStatus from, OrderStatus to)
    {
        if (NextFulfilmentStep(from) == to)
            return null;

        return from switch
        {
            OrderStatus.Collected => "This order has already been collected.",
            OrderStatus.Cancelled => "This order was cancelled.",
            _ when to is OrderStatus.Draft or OrderStatus.Cancelled => $"Fulfilment cannot move an order to {to}.",
            _ => $"A {from} order goes to {NextFulfilmentStep(from)} next, not {to}."
        };
    }
}
