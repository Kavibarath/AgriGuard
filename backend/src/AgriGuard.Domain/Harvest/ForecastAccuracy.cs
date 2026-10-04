namespace AgriGuard.Domain.Harvest;

/// <summary>How far a group of harvest forecasts were from what was actually harvested.</summary>
public sealed record ForecastAccuracySummary(
    int Forecasts,
    decimal ForecastKg,
    decimal ActualKg,
    // (actual − forecast) / forecast over the whole group, in percent. Negative: the harvest fell short.
    decimal? VariancePercent,
    // Mean absolute percentage error: the typical size of a miss, whichever way. Lower is better.
    decimal? MeanAbsolutePercentError,
    // How many forecasts landed within ±ToleranceFraction of the actual.
    int WithinTolerance);

/// <summary>
/// Forecast-versus-actual arithmetic for GET /api/reports/harvest-forecast-vs-actual.
///
/// Two numbers, because they answer different questions. **Variance** (bias) says whether estimates
/// run high or low overall: +10% and −10% cancel out. **MAPE** says how wrong a typical estimate
/// is, whichever way: +10% and −10% make 10%. A co-op planning lorries and slots needs both.
///
/// Only forecasts with an actual yield count. Pure: no database.
/// </summary>
public static class ForecastAccuracy
{
    /// <summary>A forecast within 10% of the harvest is counted as accurate.</summary>
    public const decimal ToleranceFraction = 0.10m;

    /// <summary>(actual − forecast) / forecast × 100, or null when nothing was forecast.</summary>
    public static decimal? VariancePercent(decimal forecastKg, decimal actualKg) =>
        forecastKg > 0 ? Math.Round((actualKg - forecastKg) / forecastKg * 100, 1, MidpointRounding.AwayFromZero) : null;

    public static ForecastAccuracySummary Summarise(IEnumerable<(decimal ForecastKg, decimal ActualKg)> pairs)
    {
        var scored = pairs.Where(p => p.ForecastKg > 0).ToList();
        if (scored.Count == 0)
            return new ForecastAccuracySummary(0, 0, 0, null, null, 0);

        var forecast = scored.Sum(p => p.ForecastKg);
        var actual = scored.Sum(p => p.ActualKg);
        var mape = scored.Average(p => Math.Abs(p.ActualKg - p.ForecastKg) / p.ForecastKg) * 100;
        var within = scored.Count(p => Math.Abs(p.ActualKg - p.ForecastKg) <= ToleranceFraction * p.ForecastKg);

        return new ForecastAccuracySummary(scored.Count, forecast, actual, VariancePercent(forecast, actual),
            Math.Round(mape, 1, MidpointRounding.AwayFromZero), within);
    }
}
