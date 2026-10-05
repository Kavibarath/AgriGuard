using System.Globalization;
using AgriGuard.Application.Common;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Weather;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Registry;
using AgriGuard.Domain.Validation;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Weather;

/// <summary>
/// Which of the coming days suit spraying on a plot (GET /api/weather/spray-window and the agent's
/// get_weather_forecast tool). Each day is judged by <see cref="SprayWeather"/>, the same code and
/// thresholds rule V8 applies to a proposal, so the screen and the rule can never disagree.
/// </summary>
public sealed class SprayWindowService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    IWeatherService weather,
    FarmCalendar calendar) : ISprayWindowService
{
    /// <summary>When no product is named: a typical contact fungicide needs about 4 dry hours.</summary>
    public const int DefaultRainfastHours = 4;

    public const int MaxDays = 14;

    public async Task<SprayWindowDto> ForPlotAsync(Guid plotId, int days, Guid? productId, CancellationToken ct = default)
    {
        var visible = await db.Plots.AsNoTracking().ScopedTo(currentUser).AnyAsync(p => p.Id == plotId, ct);
        RegistryScope.EnsureVisible(visible ? (object)true : null, await db.Plots.AnyAsync(p => p.Id == plotId, ct), "Plot", plotId);
        return await ComputeAsync(plotId, days, productId, ct);
    }

    public async Task<SprayWindowDto> ComputeAsync(Guid plotId, int days, Guid? productId, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxDays);

        var plot = await db.Plots.AsNoTracking()
            .Where(p => p.Id == plotId)
            .Select(p => new
            {
                p.PlotCode,
                p.Latitude,
                p.Longitude,
                CropId = p.CropCycles.Where(c => c.Status == CropCycleStatus.Active).Select(c => (Guid?)c.CropId).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new Application.Common.Exceptions.NotFoundException("Plot", plotId);

        // The named product's own rainfast time on this crop, from the rules table.
        var rainfastHours = DefaultRainfastHours;
        string? productName = null;
        if (productId is { } pid)
        {
            var product = await db.Products.AsNoTracking()
                .Where(p => p.Id == pid)
                .Select(p => new
                {
                    p.Name,
                    Rainfast = p.CropApprovals.Where(a => a.CropId == plot.CropId).Select(a => (int?)a.RainfastHours).FirstOrDefault()
                })
                .FirstOrDefaultAsync(ct)
                ?? throw new Application.Common.Exceptions.NotFoundException("Product", pid);
            productName = product.Name;
            rainfastHours = product.Rainfast ?? DefaultRainfastHours;
        }

        var thresholds = new SprayThresholdsDto(
            PrescriptionSafetyValidator.MaxRainProbabilityPercent,
            PrescriptionSafetyValidator.MaxWindSpeedKph,
            PrescriptionSafetyValidator.MaxTemperatureC);

        var forecast = await weather.ForecastAsync(plot.Latitude, plot.Longitude, ct);
        if (forecast is null)
            return new SprayWindowDto(plotId, plot.PlotCode, false, WeatherSources.Label, null, rainfastHours, productName,
                "The weather forecast is unavailable right now, so spray timing cannot be checked.", null, null, [], thresholds);

        var today = calendar.Today;
        var assessed = Enumerable.Range(0, days)
            .Select(offset => SprayWeather.Assess(forecast.Hours, today.AddDays(offset), rainfastHours))
            .OfType<SprayDayAssessment>()
            .ToList();

        var (recentRain, recentHumidity) = Recent(forecast.Hours, today);

        return new SprayWindowDto(
            plotId, plot.PlotCode, true, WeatherSources.Label, forecast.FetchedAt, rainfastHours, productName,
            Summarise(assessed), recentRain, recentHumidity,
            [.. assessed.Select(d => new SprayDayDto(d.Date, d.Suitable, d.RainProbabilityPercent, d.WindSpeedKph,
                d.TemperatureC, d.PrecipitationMm, d.Problems))],
            thresholds);
    }

    /// <summary>The 48 hours before today: rain that fell and how humid it was.</summary>
    private static (decimal? RainMm, int? HumidityPercent) Recent(IReadOnlyList<HourlyWeather> hours, DateOnly today)
    {
        var end = today.ToDateTime(TimeOnly.MinValue);
        var past = hours.Where(h => h.LocalTime >= end.AddHours(-48) && h.LocalTime < end).ToList();
        return past.Count == 0 ? (null, null) : (past.Sum(h => h.PrecipitationMm), (int)Math.Round(past.Average(h => h.RelativeHumidityPercent)));
    }

    /// <summary>"Suitable to spray on Tue 29 Sep, Wed 30 Sep. Not suitable: Thu 1 Oct (…)."</summary>
    private static string Summarise(IReadOnlyList<SprayDayAssessment> days)
    {
        if (days.Count == 0)
            return "The forecast does not cover the coming days.";

        static string Day(DateOnly d) => d.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        var good = days.Where(d => d.Suitable).Select(d => Day(d.Date)).ToList();
        var bad = days.Where(d => !d.Suitable).Select(d => $"{Day(d.Date)} ({string.Join("; ", d.Problems)})").ToList();

        var parts = new List<string>
        {
            good.Count > 0 ? $"Suitable to spray on {string.Join(", ", good)}." : "No day in this period suits spraying."
        };
        if (bad.Count > 0)
            parts.Add($"Not suitable: {string.Join(", ", bad)}.");
        return string.Join(" ", parts);
    }
}
