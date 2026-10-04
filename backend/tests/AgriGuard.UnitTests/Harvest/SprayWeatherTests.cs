using AgriGuard.Domain.Harvest;
using AgriGuard.Infrastructure.Weather;

namespace AgriGuard.UnitTests.Harvest;

public sealed class SprayWeatherTests
{
    private static readonly DateOnly Day = new(2026, 9, 29);

    /// <summary>A whole day of calm weather, with some hours overridden. A likely hour (≥ 40%) brings 2 mm unless told otherwise.</summary>
    private static List<HourlyWeather> Calm(params (int Hour, int Rain, decimal Wind, decimal Temp)[] overrides)
    {
        var hours = Enumerable.Range(0, 24)
            .Select(h => new HourlyWeather(Day.ToDateTime(new TimeOnly(h, 0)), 10, 5m, 25m, 0m, 70))
            .ToList();
        foreach (var (hour, rain, wind, temp) in overrides)
            hours[hour] = hours[hour] with { RainProbabilityPercent = rain, WindSpeedKph = wind, TemperatureC = temp, PrecipitationMm = rain >= 40 ? 2m : 0m };
        return hours;
    }

    [Fact]
    public void A_calm_dry_morning_suits_spraying()
    {
        var day = SprayWeather.Assess(Calm(), Day, rainfastHours: 4)!;

        Assert.True(day.Suitable);
        Assert.Equal(new(10, 5m, 25m, 0m), day.ToRuleInput());
    }

    [Fact]
    public void Rain_before_the_spray_is_rainfast_counts_even_after_the_application_window()
    {
        // Application 06–10, rainfast 4 h: rain at 12:00 still washes it off.
        var day = SprayWeather.Assess(Calm((12, 70, 5m, 25m)), Day, rainfastHours: 4)!;

        Assert.False(day.Suitable);
        Assert.Equal(70, day.RainProbabilityPercent);
    }

    [Fact]
    public void A_likely_drizzle_too_light_to_wash_the_spray_off_does_not_count()
    {
        var hours = Calm((8, 60, 5m, 25m));
        hours[8] = hours[8] with { PrecipitationMm = 0.2m };

        var day = SprayWeather.Assess(hours, Day, rainfastHours: 4)!;

        Assert.True(day.Suitable);
        Assert.Equal(60, day.RainProbabilityPercent);
        Assert.Equal(0.2m, day.ToRuleInput().ExpectedRainMm);
    }

    [Fact]
    public void Rain_after_the_product_is_rainfast_does_not_count()
    {
        // Rainfast 1 h: the product is safe from 11:00, so rain at 15:00 is fine.
        var day = SprayWeather.Assess(Calm((15, 90, 5m, 25m)), Day, rainfastHours: 1)!;

        Assert.True(day.Suitable);
    }

    [Theory]
    [InlineData(7, 10, 15, 25, "drift")]  // wind at the limit
    [InlineData(9, 10, 5, 33, "too hot")] // heat during application
    public void Wind_and_heat_are_judged_over_the_application_window(int hour, int rain, int wind, int temp, string problem)
    {
        var day = SprayWeather.Assess(Calm((hour, rain, wind, temp)), Day, rainfastHours: 2)!;

        Assert.False(day.Suitable);
        Assert.Contains(day.Problems, p => p.Contains(problem, StringComparison.Ordinal));
    }

    [Fact]
    public void Afternoon_wind_outside_the_application_window_is_ignored() =>
        Assert.True(SprayWeather.Assess(Calm((15, 10, 30m, 25m)), Day, rainfastHours: 2)!.Suitable);

    [Fact]
    public void A_day_the_forecast_does_not_cover_is_not_guessed() =>
        Assert.Null(SprayWeather.Assess(Calm(), Day.AddDays(20), rainfastHours: 4));
}

public sealed class OpenMeteoClientTests
{
    // Trimmed from a real response for Nuwara Eliya (timezone=Asia/Colombo).
    private const string Recorded = """
        {"latitude":6.924429,"longitude":80.81787,"timezone":"Asia/Colombo",
         "hourly":{"time":["2026-09-29T06:00","2026-09-29T07:00","2026-09-29T08:00"],
                   "precipitation_probability":[12,null,55],
                   "precipitation":[0.0,0.1,1.4],
                   "wind_speed_10m":[4.3,5.0,7.94],
                   "temperature_2m":[16.2,17.0,18.46],
                   "relative_humidity_2m":[91,88,84]}}
        """;

    [Fact]
    public void Hours_are_read_in_local_time_and_rounded()
    {
        var hours = OpenMeteoClient.Parse(Recorded);

        Assert.Equal(new DateTime(2026, 9, 29, 6, 0, 0), hours[0].LocalTime);
        Assert.Equal(12, hours[0].RainProbabilityPercent);
        Assert.Equal(7.9m, hours[^1].WindSpeedKph);
        Assert.Equal(18.5m, hours[^1].TemperatureC);
        Assert.Equal(84, hours[^1].RelativeHumidityPercent);
    }

    [Fact]
    public void An_hour_without_a_rain_probability_is_skipped_never_read_as_dry()
    {
        var hours = OpenMeteoClient.Parse(Recorded);

        Assert.Equal(2, hours.Count);
        Assert.DoesNotContain(hours, h => h.LocalTime.Hour == 7);
    }

    [Fact]
    public void A_response_without_hourly_data_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => OpenMeteoClient.Parse("""{"latitude":6.9}"""));
}
