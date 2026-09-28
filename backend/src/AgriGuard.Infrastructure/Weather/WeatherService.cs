using System.Text.Json;
using AgriGuard.Application.Weather;
using AgriGuard.Domain.Harvest;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Weather;

/// <summary>
/// Forecasts through the WeatherSnapshot cache (§10): coordinates are rounded to 0.1° (~11 km), so
/// neighbouring plots share one fetch, and a fetched forecast is reused for 3 hours.
///
/// The cache has its own database scope on purpose. This service is called from inside the
/// approval's and the callback's serializable transactions; writing the cache on their connection
/// would tie a weather refresh to their commit, and a cache race (unique index) would abort them.
///
/// When the provider cannot be reached — timeout, retries spent, circuit open — this returns null
/// and the callers degrade: V8 is reported "not evaluated" and the spray-window screen says the
/// forecast is unavailable. Nothing fails because the weather service is down.
/// </summary>
public sealed class WeatherService(
    IServiceScopeFactory scopes,
    IWeatherProvider provider,
    TimeProvider timeProvider,
    IOptions<OpenMeteoOptions> options,
    ILogger<WeatherService> logger) : IWeatherService
{
    public async Task<WeatherForecast?> ForecastAsync(decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        var lat = Math.Round(latitude, 1, MidpointRounding.AwayFromZero);
        var lon = Math.Round(longitude, 1, MidpointRounding.AwayFromZero);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        using var scope = scopes.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<AgriGuardDbContext>();

        var cached = await cache.WeatherSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LatitudeRounded == lat && s.LongitudeRounded == lon, ct);
        if (cached is not null && cached.ExpiresAt > now)
            return new WeatherForecast(lat, lon, cached.FetchedAt, Read(cached.ForecastJson));

        IReadOnlyList<HourlyWeather> hours;
        try
        {
            hours = await provider.FetchAsync(lat, lon, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Weather forecast unavailable for cell {Latitude},{Longitude}; weather checks are skipped", lat, lon);
            return null;
        }

        await StoreAsync(cache, lat, lon, now, hours, ct);
        return new WeatherForecast(lat, lon, now, hours);
    }

    private async Task StoreAsync(AgriGuardDbContext cache, decimal lat, decimal lon, DateTime now, IReadOnlyList<HourlyWeather> hours, CancellationToken ct)
    {
        try
        {
            await cache.WeatherSnapshots.Where(s => s.LatitudeRounded == lat && s.LongitudeRounded == lon).ExecuteDeleteAsync(ct);
            cache.WeatherSnapshots.Add(new WeatherSnapshot
            {
                LatitudeRounded = lat,
                LongitudeRounded = lon,
                FetchedAt = now,
                ExpiresAt = now + options.Value.CacheFor,
                ForecastJson = JsonSerializer.Serialize(hours)
            });
            await cache.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            // Another request cached the same cell a moment ago; its copy is as good as ours.
        }
    }

    private static IReadOnlyList<HourlyWeather> Read(string json) =>
        JsonSerializer.Deserialize<List<HourlyWeather>>(json) ?? [];
}
