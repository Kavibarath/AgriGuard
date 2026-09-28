// Component D — Harvest Windows, Collection Logistics & Regional Intelligence
using AgriGuard.Domain.Cases;

namespace AgriGuard.Domain.Intelligence;

/// <summary>
/// A reported case as the outbreak signal sees it: where (district, rough position), when, how bad,
/// and, once an agronomist has confirmed it, what it was. No farmer, no plot, no note.
/// </summary>
public sealed record ObservedCase(
    Guid DistrictId,
    DateOnly ReportedOn,
    CaseSeverity Severity,
    decimal Latitude,
    decimal Longitude,
    // Null until an agronomist confirms the diagnosis; unconfirmed cases are counted, never scored.
    string? PathogenCode = null,
    string? PathogenName = null)
{
    public bool Confirmed => PathogenCode is not null;
}

public enum PressureLevel { None, Low, Moderate, High, Severe }

public enum PressureTrend { Rising, Steady, Falling }

public sealed record PathogenPressure(
    string Code,
    string Name,
    int ConfirmedCases,
    decimal Score,
    // This pathogen's part of the total score, 0–100.
    int SharePercent,
    DateOnly LastReportedOn);

public sealed record DailyCaseCount(DateOnly Date, int ReportedCases, int ConfirmedCases);

public sealed record DistrictPressure(
    Guid DistrictId,
    int ReportedCases,
    int ConfirmedCases,
    decimal Score,
    int Index,
    PressureLevel Level,
    string? TopPathogenCode,
    // Mean position of the district's reports, rounded to 0.1° (~11 km): a map marker, not a farm.
    decimal Latitude,
    decimal Longitude);

public sealed record OutbreakAssessment(
    DateOnly From,
    DateOnly To,
    int WindowDays,
    int ReportedCases,
    int ConfirmedCases,
    decimal Score,
    int Index,
    PressureLevel Level,
    PressureTrend Trend,
    // Highest score first.
    IReadOnlyList<PathogenPressure> Pathogens,
    // One row per day of the window, oldest first, including empty days (a chart needs the gaps).
    IReadOnlyList<DailyCaseCount> Daily,
    // Highest score first.
    IReadOnlyList<DistrictPressure> Districts);

/// <summary>
/// Component D's regional disease pressure (§5.1 non-CRUD): a spatio-temporal aggregation of the
/// cases agronomists have confirmed, turned into one number a farmer or an agent can act on.
///
/// Each confirmed case in the window contributes a weight:
///
///     weight = severity × recency
///     severity: Low 0.5, Medium 1, High 1.5, Critical 2
///     recency:  0.5 ^ (age in days / 7)     — a case a week old counts half, two weeks a quarter
///
/// The **score** is the sum of weights (per pathogen, per district, and overall). It is mapped to a
/// 0–100 **index** with 100 × (1 − e^(−score / 4)), which rises steeply for the first few cases and
/// then saturates, so one bad week does not read as "infinitely worse" than a merely bad one.
/// Levels are bands of the index: 0 None, below 25 Low, below 50 Moderate, below 75 High, else Severe.
///
/// The **trend** compares confirmed cases in the newer half of the window with the older half:
/// rising when the newer half has at least 2 and 1.5× as many, falling the other way round.
///
/// Only confirmed cases are scored: a farmer's report is a suspicion, and scoring suspicions would
/// let one noisy village raise the alarm for a whole district. Reported cases are still counted, so
/// a surge of unconfirmed reports shows up on the chart for an agronomist to look at.
///
/// Pure: no clock, no database. The caller passes the cases and "today".
/// </summary>
public static class OutbreakSignal
{
    public const int DefaultDays = 14;
    public const int MaxDays = 90;
    public const double HalfLifeDays = 7;
    public const double IndexScale = 4;

    public static int ClampDays(int days) => Math.Clamp(days, 1, MaxDays);

    public static decimal SeverityWeight(CaseSeverity severity) => severity switch
    {
        CaseSeverity.Low => 0.5m,
        CaseSeverity.Medium => 1m,
        CaseSeverity.High => 1.5m,
        CaseSeverity.Critical => 2m,
        _ => 1m
    };

    /// <summary>Halves every <see cref="HalfLifeDays"/> days: today 1, a week ago 0.5.</summary>
    public static decimal RecencyWeight(int ageDays) => (decimal)Math.Pow(0.5, Math.Max(ageDays, 0) / HalfLifeDays);

    public static decimal Weight(ObservedCase c, DateOnly today) =>
        SeverityWeight(c.Severity) * RecencyWeight(today.DayNumber - c.ReportedOn.DayNumber);

    public static int ToIndex(decimal score) =>
        score <= 0 ? 0 : (int)Math.Round(100 * (1 - Math.Exp(-(double)score / IndexScale)), MidpointRounding.AwayFromZero);

    public static PressureLevel LevelFor(int index) => index switch
    {
        <= 0 => PressureLevel.None,
        < 25 => PressureLevel.Low,
        < 50 => PressureLevel.Moderate,
        < 75 => PressureLevel.High,
        _ => PressureLevel.Severe
    };

    public static PressureTrend TrendOf(int olderHalf, int newerHalf) =>
        newerHalf >= 2 && newerHalf >= 1.5 * olderHalf ? PressureTrend.Rising
        : olderHalf >= 2 && olderHalf >= 1.5 * newerHalf ? PressureTrend.Falling
        : PressureTrend.Steady;

    /// <summary>
    /// The pressure over the <paramref name="days"/> days ending <paramref name="today"/> (inclusive).
    /// Cases outside the window are ignored, so the caller may over-fetch at the edges.
    /// </summary>
    public static OutbreakAssessment Assess(IEnumerable<ObservedCase> cases, DateOnly today, int days)
    {
        days = ClampDays(days);
        var from = today.AddDays(-(days - 1));
        var inWindow = cases.Where(c => c.ReportedOn >= from && c.ReportedOn <= today).ToList();
        var confirmed = inWindow.Where(c => c.Confirmed).ToList();

        var score = Round(confirmed.Sum(c => Weight(c, today)));
        var index = ToIndex(score);

        // The newer half is the more recent days/2 days; with an odd window the older half gets the extra day.
        var newerFrom = today.AddDays(-(days / 2 - 1));
        var newer = days >= 2 ? confirmed.Count(c => c.ReportedOn >= newerFrom) : confirmed.Count;
        var trend = days >= 2 ? TrendOf(confirmed.Count - newer, newer) : PressureTrend.Steady;

        var pathogens = confirmed
            .GroupBy(c => c.PathogenCode!)
            .Select(g => new
            {
                Code = g.Key,
                Name = g.First().PathogenName ?? g.Key,
                Cases = g.Count(),
                Score = Round(g.Sum(c => Weight(c, today))),
                Last = g.Max(c => c.ReportedOn)
            })
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.Cases)
            .ThenBy(p => p.Code, StringComparer.Ordinal)
            .Select(p => new PathogenPressure(p.Code, p.Name, p.Cases, p.Score,
                score > 0 ? (int)Math.Round(100 * p.Score / score, MidpointRounding.AwayFromZero) : 0, p.Last))
            .ToList();

        var daily = Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => new DailyCaseCount(d, inWindow.Count(c => c.ReportedOn == d), confirmed.Count(c => c.ReportedOn == d)))
            .ToList();

        var districts = inWindow
            .GroupBy(c => c.DistrictId)
            .Select(g =>
            {
                var districtConfirmed = g.Where(c => c.Confirmed).ToList();
                var districtScore = Round(districtConfirmed.Sum(c => Weight(c, today)));
                var districtIndex = ToIndex(districtScore);
                var top = districtConfirmed
                    .GroupBy(c => c.PathogenCode!)
                    .OrderByDescending(p => p.Sum(c => Weight(c, today)))
                    .ThenBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => p.Key)
                    .FirstOrDefault();
                return new DistrictPressure(
                    g.Key, g.Count(), districtConfirmed.Count, districtScore, districtIndex, LevelFor(districtIndex), top,
                    Math.Round(g.Average(c => c.Latitude), 1, MidpointRounding.AwayFromZero),
                    Math.Round(g.Average(c => c.Longitude), 1, MidpointRounding.AwayFromZero));
            })
            .OrderByDescending(d => d.Score)
            .ThenByDescending(d => d.ReportedCases)
            .ThenBy(d => d.DistrictId)
            .ToList();

        return new OutbreakAssessment(from, today, days, inWindow.Count, confirmed.Count, score, index, LevelFor(index), trend,
            pathogens, daily, districts);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
