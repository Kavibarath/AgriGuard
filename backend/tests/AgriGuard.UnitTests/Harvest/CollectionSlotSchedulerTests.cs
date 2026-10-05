using AgriGuard.Infrastructure.Harvest;

namespace AgriGuard.UnitTests.Harvest;

public sealed class CollectionSlotSchedulerTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);
    private static readonly Guid Centre = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void Every_day_in_the_horizon_gets_the_three_standard_slots()
    {
        var slots = CollectionSlotScheduler.SlotsToOpen([(Centre, 4500m)], new HashSet<(Guid, DateOnly)>(), Today, horizonDays: 60);

        // Today through today + 60: 61 days of three slots.
        Assert.Equal(61 * 3, slots.Count);
        Assert.Equal(Today, slots.Min(s => s.SlotDate));
        Assert.Equal(Today.AddDays(60), slots.Max(s => s.SlotDate));
        var day = slots.Where(s => s.SlotDate == Today).OrderBy(s => s.SlotIndex).ToList();
        Assert.Equal([1, 2, 3], day.Select(s => s.SlotIndex));
        Assert.Equal([new TimeOnly(7, 0), new TimeOnly(9, 0), new TimeOnly(11, 0)], day.Select(s => s.StartTime));
        Assert.All(day, s => Assert.Equal(1500m, s.CapacityKg));
    }

    [Fact]
    public void A_day_a_planner_already_set_up_is_left_exactly_as_it_is()
    {
        var planned = new HashSet<(Guid, DateOnly)> { (Centre, Today.AddDays(2)) };

        var slots = CollectionSlotScheduler.SlotsToOpen([(Centre, 3000m)], planned, Today, horizonDays: 3);

        Assert.DoesNotContain(slots, s => s.SlotDate == Today.AddDays(2));
        Assert.Equal(3 * 3, slots.Count);
    }

    [Fact]
    public void Each_centre_is_topped_up_on_its_own()
    {
        var planned = new HashSet<(Guid, DateOnly)> { (Centre, Today) };

        var slots = CollectionSlotScheduler.SlotsToOpen([(Centre, 3000m), (Other, 3000m)], planned, Today, horizonDays: 0);

        Assert.All(slots, s => Assert.Equal(Other, s.CentreId));
        Assert.Equal(3, slots.Count);
    }

    [Fact]
    public void A_fully_planned_horizon_opens_nothing()
    {
        var planned = Enumerable.Range(0, 8).Select(d => (Centre, Today.AddDays(d))).ToHashSet();

        Assert.Empty(CollectionSlotScheduler.SlotsToOpen([(Centre, 3000m)], planned, Today, horizonDays: 7));
    }
}
