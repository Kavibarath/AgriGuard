using AgriGuard.Domain.Inventory;

namespace AgriGuard.UnitTests.Inventory;

public sealed class StockAllocationTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);

    private static BatchStock Batch(string no, int daysToExpiry, decimal available) =>
        new(Guid.CreateVersion7(), no, Today.AddDays(daysToExpiry), available);

    [Fact]
    public void The_batch_closest_to_expiry_is_drawn_first()
    {
        var fresh = Batch("B-NEW", 300, 10m);
        var old = Batch("B-OLD", 20, 5m);

        var plan = StockAllocation.PlanFefo([fresh, old], 2m);

        Assert.Equal([new BatchDraw(old.BatchId, 2m)], plan);
    }

    [Fact]
    public void An_order_larger_than_the_oldest_batch_spills_into_the_next()
    {
        var old = Batch("B-OLD", 20, 1.5m);
        var fresh = Batch("B-NEW", 300, 10m);

        var plan = StockAllocation.PlanFefo([fresh, old], 2m);

        Assert.Equal([new BatchDraw(old.BatchId, 1.5m), new BatchDraw(fresh.BatchId, 0.5m)], plan);
    }

    [Fact]
    public void Not_enough_stock_in_total_draws_nothing_at_all()
    {
        var plan = StockAllocation.PlanFefo([Batch("B-1", 20, 1m), Batch("B-2", 40, 0.5m)], 2m);

        Assert.Null(plan);
    }

    [Fact]
    public void Batches_with_the_same_expiry_are_drawn_in_batch_number_order()
    {
        var second = Batch("B-002", 30, 5m);
        var first = Batch("B-001", 30, 5m);

        var plan = StockAllocation.PlanFefo([second, first], 1m);

        Assert.Equal(first.BatchId, Assert.Single(plan!).BatchId);
    }

    [Fact]
    public void Empty_batches_are_ignored()
    {
        var empty = Batch("B-EMPTY", 5, 0m);
        var stocked = Batch("B-OK", 50, 3m);

        var plan = StockAllocation.PlanFefo([empty, stocked], 1m);

        Assert.Equal(stocked.BatchId, Assert.Single(plan!).BatchId);
    }

    [Theory]
    [InlineData(1.6, 1.0, 2)]
    [InlineData(0.48, 0.25, 2)]
    [InlineData(0.5, 0.25, 2)]
    [InlineData(0.51, 0.25, 3)]
    public void Orders_are_rounded_up_to_whole_packs(decimal quantity, decimal packSize, int packs) =>
        Assert.Equal(packs, StockAllocation.PacksFor(quantity, packSize));
}
