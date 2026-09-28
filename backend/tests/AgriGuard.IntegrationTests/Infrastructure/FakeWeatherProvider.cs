using AgriGuard.Application.Weather;
using AgriGuard.Domain.Harvest;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Open-Meteo: tests never call the real service. By default every hour is calm and
/// dry (V8 passes); a test can make one day rainy, windy or hot, or make the provider fail.
///
/// Forecasts are cached per ~11 km cell for 3 hours, so a test that changes the weather must also
/// clear the cache (<see cref="WeatherFixtures.ChangeWeatherAsync"/> does both, and resets after).
/// </summary>
public sealed class FakeWeatherProvider : IWeatherProvider
{
    private readonly Dictionary<DateOnly, (int Rain, decimal Wind, decimal Temperature)> _days = [];

    public bool Unavailable { get; set; }

    public int Calls { get; private set; }

    public void Set(DateOnly date, int rain = 5, decimal wind = 6m, decimal temperature = 24m) =>
        _days[date] = (rain, wind, temperature);

    public void Reset()
    {
        _days.Clear();
        Unavailable = false;
    }

    public Task<IReadOnlyList<HourlyWeather>> FetchAsync(decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        Calls++;
        if (Unavailable)
            throw new HttpRequestException("Open-Meteo is down (fake).");

        List<HourlyWeather> hours = [];
        var today = TestCalendar.Today;
        for (var day = today.AddDays(-2); day <= today.AddDays(15); day = day.AddDays(1))
        {
            var (rain, wind, temperature) = _days.TryGetValue(day, out var set) ? set : (5, 6m, 24m);
            for (var hour = 0; hour < 24; hour++)
                hours.Add(new HourlyWeather(day.ToDateTime(new TimeOnly(hour, 0)), rain, wind, temperature, rain >= 40 ? 2m : 0m, 80));
        }
        return Task.FromResult<IReadOnlyList<HourlyWeather>>(hours);
    }
}
