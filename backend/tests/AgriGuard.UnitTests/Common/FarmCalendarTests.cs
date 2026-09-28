using AgriGuard.Application.Common;

namespace AgriGuard.UnitTests.Common;

public sealed class FarmCalendarTests
{
    private static FarmCalendar At(string utc) => new(
        new FixedClock(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)),
        TimeZoneInfo.FindSystemTimeZoneById(FarmCalendar.DefaultTimeZone));

    [Fact]
    public void Before_dawn_in_Sri_Lanka_it_is_already_the_next_day()
    {
        // 23:15 UTC on the 27th is 04:45 on the 28th in Colombo: the demo run that proposed a
        // spray "today" for a day that had already passed on the farm.
        Assert.Equal(new DateOnly(2026, 9, 28), At("2026-09-27T23:15:00Z").Today);
    }

    [Theory]
    [InlineData("2026-09-27T18:29:59Z", 27)] // 23:59:59 local
    [InlineData("2026-09-27T18:30:00Z", 28)] // midnight local
    public void The_day_turns_at_local_midnight_not_UTC_midnight(string utc, int day) =>
        Assert.Equal(new DateOnly(2026, 9, day), At(utc).Today);

    [Fact]
    public void A_stored_UTC_timestamp_maps_to_its_local_date() =>
        Assert.Equal(new DateOnly(2026, 9, 28), At("2026-09-01T00:00:00Z").DateOf(new DateTime(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc)));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
