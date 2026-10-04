using AgriGuard.Domain.Inventory;

namespace AgriGuard.UnitTests.Inventory;

public sealed class OrderStatusRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Packed)]
    [InlineData(OrderStatus.Packed, OrderStatus.Collected)]
    [InlineData(OrderStatus.Draft, OrderStatus.Confirmed)]
    public void Each_step_leads_to_exactly_one_next_status(OrderStatus from, OrderStatus next)
    {
        Assert.Equal(next, OrderStatusRules.NextFulfilmentStep(from));
        Assert.Null(OrderStatusRules.ExplainFulfilment(from, next));
    }

    [Theory]
    [InlineData(OrderStatus.Collected)]
    [InlineData(OrderStatus.Cancelled)]
    public void A_finished_order_has_no_next_step(OrderStatus from) =>
        Assert.Null(OrderStatusRules.NextFulfilmentStep(from));

    [Fact]
    public void A_step_cannot_be_skipped() =>
        Assert.Equal("A Confirmed order goes to Packed next, not Collected.",
            OrderStatusRules.ExplainFulfilment(OrderStatus.Confirmed, OrderStatus.Collected));

    [Fact]
    public void A_collected_order_cannot_move_again() =>
        Assert.Contains("already been collected", OrderStatusRules.ExplainFulfilment(OrderStatus.Collected, OrderStatus.Packed));

    [Theory]
    [InlineData(OrderStatus.Packed, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Draft)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    public void Fulfilment_never_moves_backwards_or_cancels(OrderStatus from, OrderStatus to) =>
        Assert.NotNull(OrderStatusRules.ExplainFulfilment(from, to));
}

public sealed class BatchExpiryTests
{
    private static readonly DateOnly Today = new(2026, 9, 27);

    [Theory]
    [InlineData(-5, ExpiryState.Expired)]
    [InlineData(0, ExpiryState.Expired)]       // expires today: no longer sellable
    [InlineData(1, ExpiryState.ExpiringSoon)]
    [InlineData(BatchExpiry.WarningDays, ExpiryState.ExpiringSoon)]
    [InlineData(BatchExpiry.WarningDays + 1, ExpiryState.InDate)]
    public void Batches_are_flagged_by_days_to_expiry(int days, ExpiryState expected) =>
        Assert.Equal(expected, BatchExpiry.Classify(Today.AddDays(days), Today));
}
