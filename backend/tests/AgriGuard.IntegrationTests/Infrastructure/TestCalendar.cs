using AgriGuard.Application.Common;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>
/// "Today" as the API sees it: the farms' local date, not the UTC one. Tests that build dates from
/// the UTC date fail every evening between 18:30 and midnight UTC, when Sri Lanka is already on
/// the next day.
/// </summary>
public static class TestCalendar
{
    private static readonly FarmCalendar Calendar =
        new(TimeProvider.System, TimeZoneInfo.FindSystemTimeZoneById(FarmCalendar.DefaultTimeZone));

    public static DateOnly Today => Calendar.Today;
}
