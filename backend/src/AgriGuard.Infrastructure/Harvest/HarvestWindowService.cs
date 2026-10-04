using System.Globalization;
using AgriGuard.Application.Common;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Harvest;
using AgriGuard.Application.Weather;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Harvest;

/// <summary>
/// GET /api/harvest-windows/{cropCycleId} (§5.1, non-CRUD): the crop's maturity, the chemical-safety
/// line drawn by its sprays' pre-harvest intervals, and the Open-Meteo forecast, combined by
/// <see cref="HarvestWindows"/> into ranked harvest days.
/// </summary>
public sealed class HarvestWindowService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    IWeatherService weather,
    FarmCalendar calendar) : IHarvestWindowService
{
    /// <summary>
    /// The best few days are recommended, if they score this well. A forecast-wet day scores at most
    /// 60 (100 − 40), so a wet day is never recommended, however close to maturity it is.
    /// </summary>
    private const int RecommendedCount = 3;
    private const int RecommendedMinimumScore = 65;

    public async Task<HarvestWindowDto> GetAsync(Guid cropCycleId, CancellationToken ct = default)
    {
        var cycle = await db.CropCycles.AsNoTracking().ScopedTo(currentUser)
            .Where(c => c.Id == cropCycleId)
            .Select(c => new
            {
                c.Id,
                c.PlotId,
                c.Plot.PlotCode,
                c.Plot.Latitude,
                c.Plot.Longitude,
                c.CropId,
                CropName = c.Crop.Name,
                Maturity = c.PlannedHarvestDate ?? c.ExpectedHarvestDate,
                c.Status
            })
            .FirstOrDefaultAsync(ct);
        RegistryScope.EnsureVisible(cycle, await db.CropCycles.AnyAsync(c => c.Id == cropCycleId, ct), "Crop cycle", cropCycleId);

        if (cycle!.Status != CropCycleStatus.Active)
            throw new BusinessRuleException("CYCLE_NOT_ACTIVE", $"This crop cycle is {cycle.Status}; there is no harvest left to plan.");

        var today = calendar.Today;
        var sprays = await HarvestScope.SpraysAsync(db, cycle.Id, cycle.CropId, ct);

        var forecast = await weather.ForecastAsync(cycle.Latitude, cycle.Longitude, ct);
        Dictionary<DateOnly, HarvestDayWeather> byDay = [];
        if (forecast is not null)
            for (var day = today; day <= today.AddDays(16); day = day.AddDays(1))
                if (HarvestWindows.WeatherFor(forecast.Hours, day) is { } w)
                    byDay[day] = w;

        var result = HarvestWindows.Rank(today, cycle.Maturity, sprays, byDay);
        var recommended = result.Days.Take(RecommendedCount).Where(d => d.Score >= RecommendedMinimumScore).Select(d => d.Date).ToHashSet();

        return new HarvestWindowDto(
            cycle.Id, cycle.PlotId, cycle.PlotCode, cycle.CropName, result.MaturityDate, result.SafeFromDate,
            result.LimitingSpray is { } s
                ? $"{s.ProductName} was sprayed on {s.SprayedOn:yyyy-MM-dd}; its {s.PreHarvestIntervalDays}-day pre-harvest interval clears on {s.ClearsOn:yyyy-MM-dd}."
                : null,
            forecast is not null,
            Summarise(result, recommended),
            [.. result.Days.Select(d => new HarvestDayDto(
                d.Date, d.Score, recommended.Contains(d.Date), d.Weather?.RainProbabilityPercent, d.Weather?.RainMm, d.Reasons))]);
    }

    private static string Summarise(HarvestWindowResult result, IReadOnlySet<DateOnly> recommended)
    {
        static string Day(DateOnly d) => d.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        var best = result.Days.FirstOrDefault();
        var safety = result.SafeFromDate is { } safe ? $" Not before {Day(safe)} (pre-harvest interval)." : "";
        return best is null
            ? $"No harvest day could be ranked.{safety}"
            : recommended.Count > 0
                ? $"Best: {string.Join(", ", result.Days.Where(d => recommended.Contains(d.Date)).Select(d => Day(d.Date)))}.{safety}"
                : $"No day scores well yet; the least bad is {Day(best.Date)} ({string.Join("; ", best.Reasons)}).{safety}";
    }
}
