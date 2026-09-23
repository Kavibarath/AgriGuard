using System.Linq.Expressions;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Reference;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Cases;

public sealed class CaseService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ICaseService
{
    private static readonly Dictionary<string, Expression<Func<CropCase, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["createdAt"] = c => c.CreatedAt,
        ["updatedAt"] = c => c.UpdatedAt,
        ["referenceNo"] = c => c.ReferenceNo,
        ["status"] = c => c.Status,
        ["severity"] = c => c.Severity
    };

    public async Task<PagedResult<CaseSummaryDto>> ListAsync(CaseQuery query, CancellationToken ct = default)
    {
        var cases = db.CropCases.AsNoTracking().ScopedTo(currentUser);

        if (query.Status is { } status) cases = cases.Where(c => c.Status == status);
        if (query.Severity is { } severity) cases = cases.Where(c => c.Severity == severity);
        if (query.DistrictId is { } districtId) cases = cases.Where(c => c.DistrictId == districtId);
        if (query.PlotId is { } plotId) cases = cases.Where(c => c.PlotId == plotId);
        if (query.CropId is { } cropId) cases = cases.Where(c => c.CropCycle.CropId == cropId);

        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{search.Trim()}%";
            cases = cases.Where(c => EF.Functions.ILike(c.ReferenceNo, pattern)
                                  || EF.Functions.ILike(c.Plot.PlotCode, pattern)
                                  || EF.Functions.ILike(c.Farmer.FullName, pattern));
        }

        // Default: oldest first — a triage queue is worked in the order problems were reported.
        return await cases
            .OrderByAllowed(query, Sortable, c => c.CreatedAt, c => c.Id)
            .Select(SummaryProjection)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<CaseDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.CropCases.AsNoTracking().ScopedTo(currentUser)
            .Where(c => c.Id == id)
            .Select(DetailProjection)
            .FirstOrDefaultAsync(ct);

        CaseScope.EnsureVisible(row, await db.CropCases.AnyAsync(c => c.Id == id, ct), "Case", id);
        return ToDetail(row!);
    }

    public async Task<CaseDetailDto> CreateAsync(CreateCaseRequest request, CancellationToken ct = default)
    {
        var plot = await db.Plots.ScopedTo(currentUser)
            .Include(p => p.Farm)
            .FirstOrDefaultAsync(p => p.Id == request.PlotId, ct);
        RegistryScope.EnsureVisible(plot, await db.Plots.AnyAsync(p => p.Id == request.PlotId, ct), "Plot", request.PlotId);

        // Reporting a problem is the farmer's act. An administrator may enter one on their behalf;
        // an agronomist advises on cases but does not raise them in someone else's name.
        if (!currentUser.CanWriteFarm(plot!.Farm))
            throw new ForbiddenAccessException("Only the farm's owner or a co-op administrator can report a case on this plot.");

        var cycle = await db.CropCycles.FirstOrDefaultAsync(c => c.Id == request.CropCycleId && c.PlotId == plot.Id, ct)
            ?? throw new RequestValidationException(nameof(request.CropCycleId), "That crop cycle is not on this plot.");

        if (cycle.Status != CropCycleStatus.Active)
            throw new BusinessRuleException("CYCLE_NOT_ACTIVE", $"This crop cycle is {cycle.Status}. Report problems against the crop currently growing.");

        var cropCase = new CropCase
        {
            ReferenceNo = await NextReferenceNoAsync(ct),
            FarmerId = plot.Farm.FarmerId,
            PlotId = plot.Id,
            CropCycleId = cycle.Id,
            // Denormalised so the agronomist queue can filter by district without joining to the farm.
            DistrictId = plot.Farm.DistrictId,
            Status = CaseStatus.Submitted,
            Severity = request.Severity ?? CaseSeverity.Medium,
            SymptomCodes = request.SymptomCodes.Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList(),
            FarmerNote = string.IsNullOrWhiteSpace(request.FarmerNote) ? null : request.FarmerNote.Trim(),
            ReportedLatitude = request.Latitude,
            ReportedLongitude = request.Longitude
        };

        db.CropCases.Add(cropCase);
        await db.SaveChangesAsync(ct);
        return await GetAsync(cropCase.Id, ct);
    }

    public async Task<CaseDetailDto> UpdateStatusAsync(Guid id, UpdateCaseStatusRequest request, CancellationToken ct = default)
    {
        var cropCase = await db.CropCases.ScopedTo(currentUser).FirstOrDefaultAsync(c => c.Id == id, ct);
        CaseScope.EnsureVisible(cropCase, await db.CropCases.AnyAsync(c => c.Id == id, ct), "Case", id);

        var role = currentUser.Role ?? throw new ForbiddenAccessException();
        if (CaseStatusRules.ExplainManualChange(cropCase!.Status, request.Status, role) is { } reason)
            throw new BusinessRuleException("ILLEGAL_CASE_STATUS_CHANGE", reason);

        cropCase.Status = request.Status;

        // The agronomist who takes a case into manual review owns it from then on.
        if (request.Status == CaseStatus.AwaitingManualReview && role == UserRole.FieldAgronomist)
            cropCase.AssignedAgronomistId = currentUser.UserId;

        try
        {
            // Version is xmin: a status change racing the agent's result is refused, not merged.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This case was changed while you were looking at it. Reload and try again.");
        }

        return await GetAsync(id, ct);
    }

    /// <summary>
    /// "AG-2026-000142". Numbered from a PostgreSQL sequence, which never hands the same value to
    /// two transactions — counting existing rows would, under concurrent submissions.
    /// </summary>
    private async Task<string> NextReferenceNoAsync(CancellationToken ct)
    {
        var next = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{AgriGuardDbContext.CaseReferenceSequence}') AS \"Value\"")
            .SingleAsync(ct);
        return $"AG-{timeProvider.GetUtcNow().Year}-{next:D6}";
    }

    private static readonly Expression<Func<CropCase, CaseSummaryDto>> SummaryProjection = c => new CaseSummaryDto(
        c.Id,
        c.ReferenceNo,
        c.Status,
        c.Severity,
        c.FarmerId,
        c.Farmer.FullName,
        c.PlotId,
        c.Plot.PlotCode,
        c.CropCycleId,
        c.CropCycle.CropId,
        c.CropCycle.Crop.Name,
        c.CropCycle.Stage,
        c.DistrictId,
        c.District.Name,
        c.SymptomCodes,
        c.AssignedAgronomistId,
        c.CreatedAt,
        c.UpdatedAt,
        c.AgentRuns.OrderByDescending(r => r.CreatedAt).Select(r => (Guid?)r.Id).FirstOrDefault(),
        c.AgentRuns.OrderByDescending(r => r.CreatedAt).Select(r => (AgentRunStatus?)r.Status).FirstOrDefault());

    private static readonly Expression<Func<CropCase, CaseRow>> DetailProjection = c => new CaseRow(
        c.Id,
        c.ReferenceNo,
        c.Status,
        c.Severity,
        c.FarmerId,
        c.Farmer.FullName,
        c.PlotId,
        c.Plot.PlotCode,
        c.Plot.AreaHectares,
        c.CropCycleId,
        c.CropCycle.CropId,
        c.CropCycle.Crop.Name,
        c.CropCycle.Stage,
        c.DistrictId,
        c.District.Name,
        c.SymptomCodes,
        c.FarmerNote,
        c.ReportedLatitude,
        c.ReportedLongitude,
        c.AssignedAgronomistId,
        c.AssignedAgronomist != null ? c.AssignedAgronomist.FullName : null,
        c.ConfirmedPathogen != null ? c.ConfirmedPathogen.Code : null,
        c.CreatedAt,
        c.UpdatedAt,
        c.AgentRuns
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new AgentRunSummaryDto(r.Id, r.Status, r.RevisionCount, r.FailureReason, r.CreatedAt, r.CompletedAt))
            .ToList());

    /// <summary>Symptom labels come from the in-code catalogue, so they are attached after the query.</summary>
    private static CaseDetailDto ToDetail(CaseRow r) => new(
        r.Id, r.ReferenceNo, r.Status, r.Severity, r.FarmerId, r.FarmerName,
        r.PlotId, r.PlotCode, r.PlotAreaHectares, r.CropCycleId, r.CropId, r.CropName, r.Stage,
        r.DistrictId, r.DistrictName,
        r.SymptomCodes.Select(code => new SymptomDto(code, SymptomCatalogue.All.GetValueOrDefault(code, code))).ToList(),
        r.FarmerNote, r.ReportedLatitude, r.ReportedLongitude,
        r.AssignedAgronomistId, r.AssignedAgronomistName, r.ConfirmedPathogenCode,
        r.CreatedAt, r.UpdatedAt, r.Runs);

    private sealed record CaseRow(
        Guid Id,
        string ReferenceNo,
        CaseStatus Status,
        CaseSeverity Severity,
        Guid FarmerId,
        string FarmerName,
        Guid PlotId,
        string PlotCode,
        decimal PlotAreaHectares,
        Guid CropCycleId,
        Guid CropId,
        string CropName,
        CropStage Stage,
        Guid DistrictId,
        string DistrictName,
        List<string> SymptomCodes,
        string? FarmerNote,
        decimal ReportedLatitude,
        decimal ReportedLongitude,
        Guid? AssignedAgronomistId,
        string? AssignedAgronomistName,
        string? ConfirmedPathogenCode,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        List<AgentRunSummaryDto> Runs);
}
