using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgriGuard.Application.Common;
using AgriGuard.Application.Weather;
using AgriGuard.Domain.Harvest;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Weather;

public sealed class OpenMeteoOptions
{
    public const string SectionName = "OpenMeteo";

    /// <summary>Configurable so tests and a self-hosted mirror can replace it; never hard-coded in the client.</summary>
    public Uri BaseUrl { get; set; } = new("https://api.open-meteo.com/");

    /// <summary>How long a fetched forecast is reused for the same ~11 km cell.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromHours(3);

    /// <summary>Open-Meteo's maximum; a spray date further out is "not evaluated".</summary>
    public int ForecastDays { get; set; } = 16;

    /// <summary>Past days included, for the Diagnosis agent's "recent weather".</summary>
    public int PastDays { get; set; } = 2;
}

/// <summary>
/// Open-Meteo forecast API (§10): free, no key, no account. Only the rounded coordinates are sent —
/// no farmer, plot or case identifiers (§11 data minimisation). The resilience pipeline (timeout,
/// retries with jitter, circuit breaker) is attached where the client is registered.
/// </summary>
public sealed class OpenMeteoClient(HttpClient http, FarmCalendar calendar, IOptions<OpenMeteoOptions> options) : IWeatherProvider
{
    private const string Hourly = "precipitation_probability,precipitation,wind_speed_10m,temperature_2m,relative_humidity_2m";

    public async Task<IReadOnlyList<HourlyWeather>> FetchAsync(decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        var o = options.Value;
        var url = string.Create(CultureInfo.InvariantCulture,
            $"v1/forecast?latitude={latitude}&longitude={longitude}&hourly={Hourly}&wind_speed_unit=kmh" +
            $"&timezone={Uri.EscapeDataString(TimeZoneId(calendar.Zone))}&forecast_days={o.ForecastDays}&past_days={o.PastDays}");

        return Parse(await http.GetStringAsync(url, ct));
    }

    /// <summary>Reads an Open-Meteo response. Public so a recorded response can be unit-tested.</summary>
    public static IReadOnlyList<HourlyWeather> Parse(string json)
    {
        var body = JsonSerializer.Deserialize<Response>(json)
                   ?? throw new InvalidOperationException("Open-Meteo returned an empty response.");
        var h = body.Hourly ?? throw new InvalidOperationException("Open-Meteo returned no hourly data.");
        List<HourlyWeather> hours = [];
        for (var i = 0; i < h.Time.Count; i++)
        {
            // Hours the model has no value for are skipped rather than read as zero: a missing rain
            // probability must never look like a dry forecast.
            if (At(h.PrecipitationProbability, i) is not { } rain || At(h.WindSpeed, i) is not { } wind
                || At(h.Temperature, i) is not { } temperature)
                continue;

            hours.Add(new HourlyWeather(
                DateTime.ParseExact(h.Time[i], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
                (int)Math.Round(rain),
                Math.Round(wind, 1),
                Math.Round(temperature, 1),
                At(h.Precipitation, i) ?? 0m,
                (int)Math.Round(At(h.Humidity, i) ?? 0m)));
        }
        return hours;
    }

    private static decimal? At(List<decimal?>? values, int i) => values is not null && i < values.Count ? values[i] : null;

    /// <summary>Open-Meteo wants an IANA name ("Asia/Colombo"), which Windows may hold as its own id.</summary>
    private static string TimeZoneId(TimeZoneInfo zone) =>
        zone.HasIanaId ? zone.Id
        : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana
        : "auto";

    private sealed record Response([property: JsonPropertyName("hourly")] HourlyBlock? Hourly);

    private sealed record HourlyBlock(
        [property: JsonPropertyName("time")] List<string> Time,
        [property: JsonPropertyName("precipitation_probability")] List<decimal?>? PrecipitationProbability,
        [property: JsonPropertyName("precipitation")] List<decimal?>? Precipitation,
        [property: JsonPropertyName("wind_speed_10m")] List<decimal?>? WindSpeed,
        [property: JsonPropertyName("temperature_2m")] List<decimal?>? Temperature,
        [property: JsonPropertyName("relative_humidity_2m")] List<decimal?>? Humidity);
}
