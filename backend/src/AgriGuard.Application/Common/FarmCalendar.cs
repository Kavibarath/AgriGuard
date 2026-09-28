namespace AgriGuard.Application.Common;

/// <summary>
/// The co-op's calendar: what "today" means for a spray date, a pre-harvest interval or a batch's
/// expiry. Farming days are local days. Timestamps are still stored in UTC; only calendar dates
/// come from here.
///
/// Before this existed each service took the UTC date. Between midnight and 05:30 in Sri Lanka
/// that is still yesterday, so a proposal made at 04:45 on 28 Sept was told to spray on the 27th.
/// </summary>
public sealed class FarmCalendar(TimeProvider timeProvider, TimeZoneInfo zone)
{
    /// <summary>Sri Lanka (UTC+05:30, no daylight saving). Configurable as Calendar:TimeZone.</summary>
    public const string DefaultTimeZone = "Asia/Colombo";

    public TimeZoneInfo Zone { get; } = zone;

    /// <summary>Today's date where the farms are.</summary>
    public DateOnly Today => DateOf(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>The local calendar date of a UTC timestamp.</summary>
    public DateOnly DateOf(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone));
}
