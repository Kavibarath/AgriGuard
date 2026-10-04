using AgriGuard.Domain.Harvest;

namespace AgriGuard.UnitTests.Harvest;

public sealed class HarvestWindowsTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private static readonly DateOnly Maturity = Today.AddDays(4);

    private static Dictionary<DateOnly, HarvestDayWeather> Dry(int days = 16) =>
        Enumerable.Range(0, days).ToDictionary(i => Today.AddDays(i), _ => new HarvestDayWeather(10, 0m));

    [Fact]
    public void With_dry_weather_the_best_day_is_maturity()
    {
        var result = HarvestWindows.Rank(Today, Maturity, [], Dry());

        Assert.Equal(Maturity, result.Days[0].Date);
        Assert.Equal(100, result.Days[0].Score);
        Assert.Null(result.SafeFromDate);
    }

    [Fact]
    public void No_day_before_the_latest_spray_clears_its_pre_harvest_interval_is_offered()
    {
        // Sprayed yesterday with a 7-day interval: nothing before Today + 6.
        var spray = new PhiConstraint("Mancozeb 80 WP", Today.AddDays(-1), 7);

        var result = HarvestWindows.Rank(Today, Maturity, [spray], Dry());

        Assert.Equal(Today.AddDays(6), result.SafeFromDate);
        Assert.All(result.Days, d => Assert.True(d.Date >= Today.AddDays(6)));
        Assert.Equal(spray, result.LimitingSpray);
    }

    [Fact]
    public void The_spray_that_clears_last_decides()
    {
        var early = new PhiConstraint("A", Today.AddDays(-10), 3);
        var late = new PhiConstraint("B", Today.AddDays(-2), 14);

        Assert.Equal(late, HarvestWindows.Rank(Today, Maturity, [early, late], Dry()).LimitingSpray);
    }

    [Fact]
    public void A_wet_maturity_day_loses_to_a_dry_one_just_after()
    {
        var weather = Dry();
        weather[Maturity] = new HarvestDayWeather(80, 6m);

        var result = HarvestWindows.Rank(Today, Maturity, [], weather);

        Assert.NotEqual(Maturity, result.Days[0].Date);
        var wet = result.Days.Single(d => d.Date == Maturity);
        Assert.Equal(60, wet.Score);
        Assert.Contains(wet.Reasons, r => r.Contains("mm of rain", StringComparison.Ordinal));
    }

    [Fact]
    public void Light_drizzle_is_not_wet()
    {
        Assert.False(new HarvestDayWeather(90, 0.2m).Wet);
        Assert.True(new HarvestDayWeather(40, 0.5m).Wet);
    }

    [Fact]
    public void Days_before_maturity_score_lower_and_beyond_the_forecast_is_uncertain()
    {
        var result = HarvestWindows.Rank(Today, Maturity, [], Dry(days: 6));

        Assert.Equal(70, result.Days.Single(d => d.Date == Maturity.AddDays(-2)).Score);
        Assert.Equal(90, result.Days.Single(d => d.Date == Maturity.AddDays(2)).Score);
    }

    [Fact]
    public void Harvest_hours_need_the_full_window_covered()
    {
        var hours = Enumerable.Range(7, 7) // 07:00–13:00 only: 14:00 missing
            .Select(h => new HourlyWeather(Today.ToDateTime(new TimeOnly(h, 0)), 20, 5m, 25m, 0.1m, 70))
            .ToList();

        Assert.Null(HarvestWindows.WeatherFor(hours, Today));
    }
}

public sealed class SlotAllocationTests
{
    private static readonly DateOnly Day = new(2026, 10, 5);
    private const decimal PlotLat = 8.35m, PlotLon = 80.50m;

    private static SlotOption Slot(DateOnly date, decimal remaining, decimal lat = 8.36m, decimal lon = 80.50m, int index = 1, Guid? centre = null) =>
        new(Guid.NewGuid(), centre ?? Guid.NewGuid(), date, index, remaining, lat, lon);

    [Fact]
    public void The_preferred_day_comes_first_even_if_a_later_centre_is_nearer()
    {
        var preferred = Slot(Day, 500, lat: 8.45m);
        var nearerLater = Slot(Day.AddDays(1), 500, lat: 8.351m);

        Assert.Equal(preferred, SlotAllocation.Choose([nearerLater, preferred], 100, Day, PlotLat, PlotLon));
    }

    [Fact]
    public void On_the_same_day_the_nearest_centre_wins()
    {
        var far = Slot(Day, 500, lat: 8.60m);
        var near = Slot(Day, 500, lat: 8.36m);

        Assert.Equal(near, SlotAllocation.Choose([far, near], 100, Day, PlotLat, PlotLon));
    }

    [Fact]
    public void A_harvest_is_never_split_across_slots()
    {
        // 300 + 300 would hold 500, but no single slot does.
        Assert.Null(SlotAllocation.Choose([Slot(Day, 300), Slot(Day, 300, index: 2)], 500, Day, PlotLat, PlotLon));
    }

    [Fact]
    public void A_full_preferred_day_moves_to_the_next_day_with_room_but_never_earlier_or_too_late()
    {
        var earlier = Slot(Day.AddDays(-1), 900);
        var full = Slot(Day, 50);
        var next = Slot(Day.AddDays(1), 900);
        var tooLate = Slot(Day.AddDays(SlotAllocation.MaxDaysLater + 1), 900);

        Assert.Equal(next, SlotAllocation.Choose([earlier, full, tooLate, next], 100, Day, PlotLat, PlotLon));
        Assert.Null(SlotAllocation.Choose([earlier, full, tooLate], 100, Day, PlotLat, PlotLon));
    }

    [Fact]
    public void Distance_is_great_circle_kilometres() =>
        // Anuradhapura to Mihintale: about 11 km.
        Assert.InRange(SlotAllocation.DistanceKm(8.3114m, 80.4037m, 8.3590m, 80.5050m), 11, 13);
}
