using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgriGuard.Application.Common;
using AgriGuard.Application.Weather;
using AgriGuard.Domain.Harvest;

namespace AgriGuard.Infrastructure.Weather;

public sealed class MetNorwayOptions
{
    public const string SectionName = "MetNorway";

    /// <summary>Off switch: without it, a refusal from Open-Meteo simply means "no forecast".</summary>
    public bool Enabled { get; set; } = true;

    public Uri BaseUrl { get; set; } = new("https://api.met.no/");

    /// <summary>
    /// MET Norway's terms require every caller to identify itself with an application name and a
    /// contact; anonymous or generic user agents are refused (403).
    /// </summary>
    public string UserAgent { get; set; } = "AgriGuard/1.0 (SLIIT SE3090 academic project; https://github.com/Kavibarath/AgriGuard)";
}

/// <summary>
/// MET Norway's Locationforecast 2.0 (api.met.no): free, global, no key. It is the fallback for
/// Open-Meteo, which limits requests per IP address and refuses shared cloud addresses (Render's)
/// with 429. MET Norway identifies callers by user agent instead, so a shared address does not
/// matter. Only the rounded coordinates are sent, as with Open-Meteo.
///
/// Two differences from Open-Meteo, both handled in <see cref="Parse"/>:
/// - Outside the Nordic countries there is no rain probability, only the forecast amount. The
///   forecast is taken as stated: an hour with at least 0.1 mm is 100% wet, otherwise 0%. V8 also
///   needs 0.5 mm in the rainfast window before it objects, so drizzle alone never blocks a spray.
/// - Hours are given one by one for about 2½ days, then in 6-hour steps to about 9½ days; a 6-hour
///   step is spread evenly over its hours. Later spray dates are "not evaluated", as beyond
///   Open-Meteo's 16 days.
/// </summary>
public sealed class MetNorwayClient(HttpClient http, FarmCalendar calendar) : IWeatherProvider
{
    /// <summary>The smallest amount MET Norway reports as rain (mm in an hour).</summary>
    private const decimal WetHourMm = 0.1m;

    public async Task<IReadOnlyList<HourlyWeather>> FetchAsync(decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        // MET Norway asks for at most 4 decimals; the cache already rounds to 1.
        var url = string.Create(CultureInfo.InvariantCulture,
            $"weatherapi/locationforecast/2.0/complete?lat={Math.Round(latitude, 4)}&lon={Math.Round(longitude, 4)}");

        return Parse(await http.GetStringAsync(url, ct), calendar.Zone);
    }

    /// <summary>Reads a Locationforecast response into local-time hours. Public so a recorded response can be unit-tested.</summary>
    public static IReadOnlyList<HourlyWeather> Parse(string json, TimeZoneInfo zone)
    {
        var body = JsonSerializer.Deserialize<Response>(json)
                   ?? throw new InvalidOperationException("MET Norway returned an empty response.");
        var series = body.Properties?.Timeseries
                     ?? throw new InvalidOperationException("MET Norway returned no timeseries.");

        List<HourlyWeather> hours = [];
        for (var i = 0; i < series.Count; i++)
        {
            var step = series[i];
            var now = step.Data?.Instant?.Details;
            if (now?.AirTemperature is not { } temperature || now.WindSpeed is not { } windMs)
                continue;

            // Prefer the hourly amount; otherwise spread the 6-hour amount over the hours up to the
            // next step. A step with neither (the very last one) has no rain figure and is skipped,
            // never read as dry.
            int span;
            decimal amountPerHour;
            if (step.Data!.NextHour?.Details?.PrecipitationAmount is { } hourly)
                (span, amountPerHour) = (1, hourly);
            else if (step.Data.NextSixHours?.Details?.PrecipitationAmount is { } sixHourly)
                (span, amountPerHour) = (6, sixHourly / 6);
            else
                continue;

            if (i + 1 < series.Count)
                span = Math.Min(span, Math.Max(1, (int)(series[i + 1].Time - step.Time).TotalHours));

            var wet = amountPerHour * span >= WetHourMm;
            for (var h = 0; h < span; h++)
            {
                var utc = DateTime.SpecifyKind(step.Time.UtcDateTime.AddHours(h), DateTimeKind.Utc);
                hours.Add(new HourlyWeather(
                    DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, zone), DateTimeKind.Unspecified),
                    wet ? 100 : 0,
                    Math.Round(windMs * 3.6m, 1),
                    Math.Round(temperature, 1),
                    Math.Round(amountPerHour, 2),
                    (int)Math.Round(now.RelativeHumidity ?? 0m)));
            }
        }
        return hours;
    }

    private sealed record Response([property: JsonPropertyName("properties")] Properties? Properties);

    private sealed record Properties([property: JsonPropertyName("timeseries")] List<Step>? Timeseries);

    private sealed record Step(
        [property: JsonPropertyName("time")] DateTimeOffset Time,
        [property: JsonPropertyName("data")] StepData? Data);

    private sealed record StepData(
        [property: JsonPropertyName("instant")] Instant? Instant,
        [property: JsonPropertyName("next_1_hours")] Period? NextHour,
        [property: JsonPropertyName("next_6_hours")] Period? NextSixHours);

    private sealed record Instant([property: JsonPropertyName("details")] InstantDetails? Details);

    private sealed record InstantDetails(
        [property: JsonPropertyName("air_temperature")] decimal? AirTemperature,
        [property: JsonPropertyName("wind_speed")] decimal? WindSpeed,
        [property: JsonPropertyName("relative_humidity")] decimal? RelativeHumidity);

    private sealed record Period([property: JsonPropertyName("details")] PeriodDetails? Details);

    private sealed record PeriodDetails([property: JsonPropertyName("precipitation_amount")] decimal? PrecipitationAmount);
}

