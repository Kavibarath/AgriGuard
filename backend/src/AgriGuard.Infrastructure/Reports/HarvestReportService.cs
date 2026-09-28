using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Reports;
using AgriGuard.Domain.Harvest;
using AgriGuard.Infrastructure.Harvest;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Reports;

/// <summary>
/// GET /api/reports/harvest-forecast-vs-actual (Component D): how close harvest forecasts came to
/// the yield actually recorded, overall, per crop and per forecaster. Rows are scoped like the
/// forecasts themselves (farmer → own, agronomist → district, administrator → all).
/// </summary>
public sealed class HarvestReportService(AgriGuardDbContext db, ICurrentUserAccessor currentUser) : IHarvestReportService
{
    public const int MaxRows = 200;

    public async Task<HarvestForecastVsActualReport> ForecastVsActualAsync(HarvestReportQuery query, CancellationToken ct = default)
    {
        var forecasts = db.HarvestForecasts.AsNoTracking().ScopedTo(currentUser);
        if (query.DistrictId is { } districtId)
            forecasts = forecasts.Where(f => f.CropCycle.Plot.Farm.DistrictId == districtId);
        if (query.CropId is { } cropId)
            forecasts = forecasts.Where(f => f.CropCycle.CropId == cropId);
        if (query.From is { } from)
            forecasts = forecasts.Where(f => f.ForecastHarvestDate >= from);
        if (query.To is { } to)
            forecasts = forecasts.Where(f => f.ForecastHarvestDate <= to);

        // A co-op's forecasts for a season are hundreds of rows, not millions: aggregated in memory,
        // where the accuracy arithmetic is the tested Domain code rather than a second copy in SQL.
        var rows = await forecasts
            .OrderByDescending(f => f.ForecastHarvestDate).ThenByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id,
                f.CropCycleId,
                f.CropCycle.Plot.PlotCode,
                CropName = f.CropCycle.Crop.Name,
                FarmerName = f.CropCycle.Plot.Farm.Farmer.FullName,
                DistrictName = f.CropCycle.Plot.Farm.District.Name,
                f.Source,
                f.ForecastHarvestDate,
                f.CropCycle.ActualHarvestDate,
                f.EstimatedYieldKg,
                f.ActualYieldKg
            })
            .ToListAsync(ct);

        var completed = rows.Where(r => r.ActualYieldKg is not null).ToList();

        static ForecastAccuracyDto Accuracy(IEnumerable<(decimal, decimal)> pairs)
        {
            var s = ForecastAccuracy.Summarise(pairs);
            return new ForecastAccuracyDto(s.Forecasts, s.ForecastKg, s.ActualKg, s.VariancePercent, s.MeanAbsolutePercentError, s.WithinTolerance);
        }

        return new HarvestForecastVsActualReport(
            query.From, query.To,
            rows.Count,
            rows.Count - completed.Count,
            ForecastAccuracy.ToleranceFraction * 100,
            Accuracy(completed.Select(r => (r.EstimatedYieldKg, r.ActualYieldKg!.Value))),
            [.. completed.GroupBy(r => r.CropName).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ForecastAccuracyGroupDto(g.Key, g.Key, Accuracy(g.Select(r => (r.EstimatedYieldKg, r.ActualYieldKg!.Value)))))],
            [.. completed.GroupBy(r => r.Source).OrderBy(g => g.Key)
                .Select(g => new ForecastAccuracyGroupDto(g.Key.ToString(), $"{g.Key} forecasts", Accuracy(g.Select(r => (r.EstimatedYieldKg, r.ActualYieldKg!.Value)))))],
            [.. rows.Take(MaxRows).Select(r => new ForecastVsActualRowDto(
                r.Id, r.CropCycleId, r.PlotCode, r.CropName, r.FarmerName, r.DistrictName, r.Source,
                r.ForecastHarvestDate, r.ActualHarvestDate, r.EstimatedYieldKg, r.ActualYieldKg,
                r.ActualYieldKg is { } actual ? ForecastAccuracy.VariancePercent(r.EstimatedYieldKg, actual) : null))]);
    }
}
