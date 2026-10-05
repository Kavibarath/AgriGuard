using AgriGuard.Domain.Harvest;

namespace AgriGuard.UnitTests.Harvest;

public sealed class BookingStatusRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Theory]
    [InlineData(BookingStatus.Booked, BookingStatus.CheckedIn)]
    [InlineData(BookingStatus.CheckedIn, BookingStatus.Completed)]
    [InlineData(BookingStatus.Booked, BookingStatus.NoShow)]
    public void On_the_day_each_step_follows_the_one_before(BookingStatus from, BookingStatus to) =>
        Assert.Null(BookingStatusRules.ExplainRecord(from, to, Today, Today));

    [Theory]
    [InlineData(BookingStatus.CheckedIn)]
    [InlineData(BookingStatus.NoShow)]
    public void Nothing_is_recorded_before_the_collection_day(BookingStatus to) =>
        Assert.Contains("nothing can be recorded before then", BookingStatusRules.ExplainRecord(BookingStatus.Booked, to, Today.AddDays(1), Today));

    [Fact]
    public void A_late_record_after_the_day_is_allowed() =>
        Assert.Null(BookingStatusRules.ExplainRecord(BookingStatus.Booked, BookingStatus.NoShow, Today.AddDays(-3), Today));

    [Fact]
    public void The_weight_comes_after_the_check_in() =>
        Assert.Contains("Check the farmer in", BookingStatusRules.ExplainRecord(BookingStatus.Booked, BookingStatus.Completed, Today, Today));

    [Fact]
    public void A_checked_in_farmer_is_not_missed() =>
        Assert.Contains("record the weight instead", BookingStatusRules.ExplainRecord(BookingStatus.CheckedIn, BookingStatus.NoShow, Today, Today));

    [Theory]
    [InlineData(BookingStatus.Cancelled, "cancelled")]
    [InlineData(BookingStatus.Completed, "already been weighed")]
    [InlineData(BookingStatus.NoShow, "marked missed")]
    public void Finished_bookings_do_not_move_again(BookingStatus from, string reason) =>
        Assert.Contains(reason, BookingStatusRules.ExplainRecord(from, BookingStatus.CheckedIn, Today, Today));

    [Theory]
    [InlineData(BookingStatus.Booked)]
    [InlineData(BookingStatus.Cancelled)]
    public void Staff_record_only_check_in_weight_or_missed(BookingStatus to) =>
        Assert.Contains("not recorded at the centre", BookingStatusRules.ExplainRecord(BookingStatus.Booked, to, Today, Today));

    [Theory]
    [InlineData(null, "Enter the weight")]
    [InlineData(-1, "cannot be negative")]
    [InlineData(200_000, "Check the weight")]
    public void A_delivered_weight_must_be_plausible(int? kg, string reason) =>
        Assert.Contains(reason, BookingStatusRules.ExplainWeight(kg));

    [Theory]
    [InlineData(0)]
    [InlineData(387)]
    public void Zero_or_a_normal_weight_is_accepted(int kg) =>
        Assert.Null(BookingStatusRules.ExplainWeight(kg));
}
