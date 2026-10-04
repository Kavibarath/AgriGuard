using AgriGuard.Application.Common;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Intelligence;
using AgriGuard.Domain.Intelligence;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Intelligence;

/// <summary>
/// GET /api/intelligence/outbreak-signal and the agent's get_regional_outbreak_signal tool: one
/// query for the cases in the window, then the pure <see cref="OutbreakSignal"/> calculation.
///
/// Open to every signed-in role, unscoped by district: the answer is an aggregate (counts, scores
/// and positions rounded to ~11 km), and warning the next district over is the point of it.
/// </summary>
public sealed class OutbreakSignalService(AgriGuardDbContext db, FarmCalendar calendar) : IOutbreakSignalService
{
    public async Task<OutbreakSignalDto> ComputeAsync(OutbreakSignalQuery query, CancellationToken ct = default)
    {
        var days = OutbreakSignal.ClampDays(query.Days);
        var today = calendar.Today;

        string? cropName = null;
        if (query.CropId is { } cropId)
            cropName = await db.Crops.Where(c => c.Id == cropId).Select(c => c.Name).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Crop", cropId);
        string? districtName = null;
        if (query.DistrictId is { } districtId)
            districtName = await db.Districts.Where(d => d.Id == districtId).Select(d => d.Name).FirstOrDefaultAsync(ct)
                           ?? throw new NotFoundException("District", districtId);

        // A day's slack either side of the window: local dates are worked out below, and the
        // calculation drops whatever falls outside.
        var since = today.AddDays(-days).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var cases = db.CropCases.AsNoTracking().Where(c => c.CreatedAt >= since);
        if (query.CropId is { } crop)
            cases = cases.Where(c => c.CropCycle.CropId == crop);

        // Every district's cases are read, even when one district is asked for: the per-district
        // breakdown (the map) always shows the neighbours.
        var rows = await cases
            .Select(c => new
            {
                c.DistrictId,
                c.CreatedAt,
                c.Severity,
                c.ReportedLatitude,
                c.ReportedLongitude,
                PathogenCode = c.ConfirmedPathogen == null ? null : c.ConfirmedPathogen.Code,
                PathogenName = c.ConfirmedPathogen == null ? null : c.ConfirmedPathogen.CommonName
            })
            .ToListAsync(ct);

        var observed = rows
            .Select(r => new ObservedCase(r.DistrictId, calendar.DateOf(r.CreatedAt), r.Severity, r.ReportedLatitude, r.ReportedLongitude,
                r.PathogenCode, r.PathogenName))
            .ToList();

        var all = OutbreakSignal.Assess(observed, today, days);
        var focus = query.DistrictId is { } only
            ? OutbreakSignal.Assess(observed.Where(o => o.DistrictId == only), today, days)
            : all;

        var districtNames = await db.Districts.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        return new OutbreakSignalDto(
            query.CropId, cropName, query.DistrictId, districtName,
            focus.From, focus.To, focus.WindowDays, focus.ReportedCases, focus.ConfirmedCases,
            focus.Score, focus.Index, focus.Level, focus.Trend,
            Summarise(focus, cropName, districtName),
            [.. focus.Pathogens.Select(p => new PathogenPressureDto(p.Code, p.Name, p.ConfirmedCases, p.Score, p.SharePercent, p.LastReportedOn))],
            [.. focus.Daily.Select(d => new DailyCaseCountDto(d.Date, d.ReportedCases, d.ConfirmedCases))],
            [.. all.Districts.Select(d => new DistrictPressureDto(
                d.DistrictId, districtNames.GetValueOrDefault(d.DistrictId, "Unknown district"), d.ReportedCases, d.ConfirmedCases,
                d.Score, d.Index, d.Level, d.TopPathogenCode, d.Latitude, d.Longitude))]);
    }

    /// <summary>
    /// "Late blight pressure is high for tomato in Nuwara Eliya (index 68/100, rising): 5 confirmed of
    /// 7 reported in 14 days. Also seen: Early blight (2)." The agent reads this line as it is.
    /// </summary>
    internal static string Summarise(OutbreakAssessment a, string? cropName, string? districtName)
    {
        var scope = (cropName, districtName) switch
        {
            ({ } crop, { } district) => $" for {crop.ToLowerInvariant()} in {district}",
            ({ } crop, null) => $" for {crop.ToLowerInvariant()} across all districts",
            (null, { } district) => $" in {district}",
            _ => " across all districts"
        };
        var counts = $"{a.ConfirmedCases} confirmed of {a.ReportedCases} reported in {a.WindowDays} days";

        if (a.Pathogens.Count == 0)
            return $"No confirmed outbreaks{scope} ({a.ReportedCases} case(s) reported in {a.WindowDays} days, none confirmed yet).";

        var top = a.Pathogens[0];
        var others = a.Pathogens.Skip(1).Take(2).Select(p => $"{p.Name} ({p.ConfirmedCases})").ToList();
        var trend = a.Trend == PressureTrend.Steady ? "" : $", {a.Trend.ToString().ToLowerInvariant()}";
        return $"{top.Name} pressure is {a.Level.ToString().ToLowerInvariant()}{scope} " +
               $"(index {a.Index}/100{trend}): {counts}." +
               (others.Count > 0 ? $" Also seen: {string.Join(", ", others)}." : "");
    }
}
