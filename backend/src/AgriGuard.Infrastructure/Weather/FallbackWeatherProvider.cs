using AgriGuard.Application.Weather;
using AgriGuard.Domain.Harvest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Weather;

/// <summary>
/// Open-Meteo first; when it cannot answer (429 from a shared cloud address, a timeout, the circuit
/// open), MET Norway. Only when both fail does the caller get an exception, and WeatherService then
/// degrades as before (V8 "not evaluated", the spray window "forecast unavailable").
/// </summary>
public sealed class FallbackWeatherProvider(
    OpenMeteoClient primary,
    MetNorwayClient fallback,
    IOptions<MetNorwayOptions> options,
    ILogger<FallbackWeatherProvider> logger) : IWeatherProvider
{
    public async Task<IReadOnlyList<HourlyWeather>> FetchAsync(decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        try
        {
            return await primary.FetchAsync(latitude, longitude, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && options.Value.Enabled)
        {
            logger.LogInformation("Open-Meteo could not answer for {Latitude},{Longitude} ({Reason}); using MET Norway",
                latitude, longitude, ex.GetBaseException().Message);
            return await fallback.FetchAsync(latitude, longitude, ct);
        }
    }
}

/// <summary>What the spray-window screen names as its source. The cache does not record which provider answered.</summary>
public static class WeatherSources
{
    public const string Label = "Open-Meteo (MET Norway when Open-Meteo is unavailable)";
}
