using AgriGuard.Application.Common;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Reports;
using AgriGuard.Domain.Cases;
using AgriGuard.Infrastructure.Cases;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Reports;

/// <summary>
/// GET /api/reports/case-throughput (Component B): how many cases came in, how many ended in a
/// prescription and how fast, and how the agent runs behind them finished. Scoped like the case
/// queue (farmer → own, agronomist → district, administrator → all).
/// </summary>
public sealed class CaseReportService(AgriGuardDbContext db, ICurrentUserAccessor currentUser, FarmCalendar calendar) : ICaseReportService
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;

    public async Task<CaseThroughputReport> CaseThroughputAsync(CaseThroughputQuery query, CancellationToken ct = default)
    {
        var to = query.To ?? calendar.Today;
        var from = query.From ?? to.AddDays(-(DefaultDays - 1));
        if (from > to)
            throw new RequestValidationException(nameof(query.From), "The start date must be on or before the end date.");
        if (to.DayNumber - from.DayNumber >= MaxDays)
            throw new RequestValidationException(nameof(query.From), $"A report covers at most {MaxDays} days.");

        // A day's slack either side in UTC; local report dates are worked out below.
        var since = from.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var until = to.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var cases = db.CropCases.AsNoTracking().ScopedTo(currentUser).Where(c => c.CreatedAt >= since && c.CreatedAt < until);
        if (query.DistrictId is { } districtId)
            cases = cases.Where(c => c.DistrictId == districtId);

        var rows = (await cases
                .Select(c => new
                {
                    c.Id,
                    c.CreatedAt,
                    c.Status,
                    FirstIssuedAt = db.Prescriptions.Where(p => p.CaseId == c.Id && p.IssuedAt != null).Min(p => p.IssuedAt),
                    Runs = c.AgentRuns.Select(r => r.Status).ToList()
                })
                .ToListAsync(ct))
            .Where(r => calendar.DateOf(r.CreatedAt) is var day && day >= from && day <= to)
            .ToList();

        var hours = rows
            .Where(r => r.FirstIssuedAt is not null)
            .Select(r => (r.FirstIssuedAt!.Value - r.CreatedAt).TotalHours)
            .Order()
            .ToList();
        var prescribed = hours.Count;

        var runs = rows.SelectMany(r => r.Runs).ToList();
        // A deliberate hand-off to an agronomist is a finished run, just not a prescription.
        var finished = runs.Count(s => s is AgentRunStatus.Completed or AgentRunStatus.Rejected or AgentRunStatus.Failed
            or AgentRunStatus.TimedOut or AgentRunStatus.Escalated);

        var daily = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => from.AddDays(i))
            .Select(day => new DailyThroughputDto(
                day,
                rows.Count(r => calendar.DateOf(r.CreatedAt) == day),
                rows.Count(r => r.FirstIssuedAt is { } issued && calendar.DateOf(issued) == day)))
            .ToList();

        return new CaseThroughputReport(
            from, to, query.DistrictId,
            rows.Count,
            prescribed,
            Percent(prescribed, rows.Count),
            prescribed == 0 ? null : Math.Round(Median(hours), 1),
            prescribed == 0 ? null : Math.Round(hours.Average(), 1),
            [.. Enum.GetValues<CaseStatus>().Select(s => new StatusCountDto(s.ToString(), rows.Count(r => r.Status == s)))],
            [.. Enum.GetValues<AgentRunStatus>().Select(s => new StatusCountDto(s.ToString(), runs.Count(r => r == s))).Where(s => s.Count > 0)],
            Percent(runs.Count(s => s == AgentRunStatus.Completed), finished),
            daily);
    }

    private static decimal? Percent(int part, int whole) =>
        whole == 0 ? null : Math.Round(100m * part / whole, 1, MidpointRounding.AwayFromZero);

    /// <summary>The middle value of a sorted list (the mean of the two middle values when even).</summary>
    private static double Median(IReadOnlyList<double> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
