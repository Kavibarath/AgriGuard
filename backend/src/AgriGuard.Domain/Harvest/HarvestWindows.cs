namespace AgriGuard.Domain.Harvest;

/// <summary>A spray still counting down its pre-harvest interval on this crop.</summary>
public sealed record PhiConstraint(string ProductName, DateOnly SprayedOn, int PreHarvestIntervalDays)
{
    /// <summary>The first day this spray allows harvesting.</summary>
    public DateOnly ClearsOn => SprayedOn.AddDays(PreHarvestIntervalDays);
}

/// <summary>Whether the harvesting hours of a day are forecast dry.</summary>
public sealed record HarvestDayWeather(int RainProbabilityPercent, decimal RainMm)
{
    public bool Wet => RainProbabilityPercent >= HarvestWindows.WetRainProbabilityPercent && RainMm >= HarvestWindows.WetRainMm;
}

public sealed record RankedHarvestDay(
    DateOnly Date,
    int Score,
    HarvestDayWeather? Weather,
    IReadOnlyList<string> Reasons);

public sealed record HarvestWindowResult(
    DateOnly MaturityDate,
    // Harvest is refused before this day: the latest spray's pre-harvest interval. Null when nothing was sprayed.
    DateOnly? SafeFromDate,
    PhiConstraint? LimitingSpray,
    DateOnly FirstCandidate,
    DateOnly LastCandidate,
    // Best first. Days before SafeFromDate never appear.
    IReadOnlyList<RankedHarvestDay> Days);

/// <summary>
/// Component D's first non-CRUD operation (§5.1): when should this crop be harvested?
/// The answer intersects three things:
///
///   1. **Maturity.** The crop's planned (or expected) harvest date. Days from a few before to
///      two weeks after are candidates; the best are on or just after maturity.
///   2. **Chemical safety.** No day before every recorded spray has cleared its pre-harvest
///      interval (the rules table's PHI for that product on this crop). Such days are not
///      "worse" — they are excluded, because harvesting them breaks the rule V5 protects.
///   3. **Weather.** Harvesting in rain spoils produce and spreads disease, so a forecast-wet day
///      loses points. Beyond the 16-day forecast a day is "unknown", scored between dry and wet.
///
/// Scoring starts at 100: −15 per day before maturity, −4 per day after the third day past it,
/// −40 for a wet day, −10 for an unknown forecast. Pure: no clock, no database, no network.
/// </summary>
public static class HarvestWindows
{
    public const int DaysBeforeMaturity = 5;
    public const int DaysAfterMaturity = 14;
    public const int WetRainProbabilityPercent = 40;
    public const decimal WetRainMm = 0.5m;

    /// <summary>Harvesting hours: morning dew gone, before afternoon showers.</summary>
    public static readonly TimeOnly HarvestStart = new(7, 0);
    public static readonly TimeOnly HarvestEnd = new(15, 0);

    public static HarvestWindowResult Rank(
        DateOnly today,
        DateOnly maturityDate,
        IReadOnlyList<PhiConstraint> sprays,
        IReadOnlyDictionary<DateOnly, HarvestDayWeather> weather)
    {
        var limiting = sprays.MaxBy(s => s.ClearsOn);
        var safeFrom = limiting?.ClearsOn;

        var first = Max(today, maturityDate.AddDays(-DaysBeforeMaturity));
        if (safeFrom is { } clear)
            first = Max(first, clear);
        var last = Max(first, maturityDate.AddDays(DaysAfterMaturity));

        List<RankedHarvestDay> days = [];
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var score = 100;
            List<string> reasons = [];

            var offset = day.DayNumber - maturityDate.DayNumber;
            if (offset < 0)
            {
                score -= 15 * -offset;
                reasons.Add($"{-offset} day(s) before the crop is mature");
            }
            else if (offset > 3)
            {
                score -= 4 * (offset - 3);
                reasons.Add($"{offset} day(s) past maturity: quality starts to drop");
            }
            else
            {
                reasons.Add("at maturity");
            }

            weather.TryGetValue(day, out var w);
            if (w is null)
            {
                score -= 10;
                reasons.Add("beyond the weather forecast");
            }
            else if (w.Wet)
            {
                score -= 40;
                reasons.Add($"{w.RainProbabilityPercent}% chance of {w.RainMm:0.#} mm of rain while harvesting");
            }
            else
            {
                reasons.Add("forecast dry while harvesting");
            }

            days.Add(new RankedHarvestDay(day, Math.Max(score, 0), w, reasons));
        }

        return new HarvestWindowResult(
            maturityDate, safeFrom, limiting, first, last,
            [.. days.OrderByDescending(d => d.Score).ThenBy(d => d.Date)]);
    }

    /// <summary>The worst hour's rain chance and the total rain across the harvesting hours of a day; null when not covered.</summary>
    public static HarvestDayWeather? WeatherFor(IReadOnlyList<HourlyWeather> hours, DateOnly day)
    {
        var start = day.ToDateTime(HarvestStart);
        var end = day.ToDateTime(HarvestEnd);
        var window = hours.Where(h => h.LocalTime >= start && h.LocalTime < end).ToList();
        if (window.Count < (int)(end - start).TotalHours)
            return null;
        return new HarvestDayWeather(window.Max(h => h.RainProbabilityPercent), window.Sum(h => h.PrecipitationMm));
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
