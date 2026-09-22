using System.Linq.Expressions;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Registry;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Registry;

public sealed class CropCycleService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ICropCycleService
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public async Task<CropCycleDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.CropCycles.AsNoTracking().ScopedTo(currentUser)
            .Where(c => c.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(ct);

        RegistryScope.EnsureVisible(row, await db.CropCycles.AnyAsync(c => c.Id == id, ct), "Crop cycle", id);
        return ToDto(row!, Today);
    }

    public async Task<CropCycleDto> CreateAsync(CreateCropCycleRequest request, CancellationToken ct = default)
    {
        var plot = await db.Plots.ScopedTo(currentUser)
            .Include(p => p.Farm)
            .FirstOrDefaultAsync(p => p.Id == request.PlotId, ct);
        RegistryScope.EnsureVisible(plot, await db.Plots.AnyAsync(p => p.Id == request.PlotId, ct), "Plot", request.PlotId);

        if (!currentUser.CanWriteFarm(plot!.Farm))
            throw new ForbiddenAccessException("Only the farm's owner or a co-op administrator can start a crop cycle.");

        if (plot.Status != PlotStatus.Active)
            throw new BusinessRuleException("PLOT_NOT_ACTIVE", $"Plot {plot.PlotCode} is {plot.Status} and cannot be sown.");

        var crop = await db.Crops.FirstOrDefaultAsync(c => c.Id == request.CropId, ct)
            ?? throw new NotFoundException("Crop", request.CropId);

        // Mirrors the filtered unique index ux_crop_cycles_one_active_per_plot. Checked here so
        // the farmer gets an explanation instead of a database constraint error.
        if (await db.CropCycles.AnyAsync(c => c.PlotId == plot.Id && c.Status == CropCycleStatus.Active, ct))
            throw new ConflictException($"Plot {plot.PlotCode} already has an active crop cycle. Harvest it before sowing again.");

        if (request.SownDate > Today)
            throw new RequestValidationException(nameof(request.SownDate), "The sowing date cannot be in the future.");

        var expected = CropStageRules.ExpectedHarvestDate(request.SownDate, crop.MaturityDays);

        if (request.PlannedHarvestDate is { } planned && planned < request.SownDate)
            throw new RequestValidationException(nameof(request.PlannedHarvestDate), "The planned harvest date cannot be before sowing.");

        var cycle = new CropCycle
        {
            PlotId = plot.Id,
            CropId = crop.Id,
            SownDate = request.SownDate,
            Stage = CropStage.Sown,
            Status = CropCycleStatus.Active,
            ExpectedHarvestDate = expected,
            PlannedHarvestDate = request.PlannedHarvestDate
        };

        db.CropCycles.Add(cycle);
        await db.SaveChangesAsync(ct);
        return await GetAsync(cycle.Id, ct);
    }

    /// <summary>
    /// The non-CRUD operation (§5.1). Four things happen together, or none do:
    ///   1. the transition is checked against the legal-transition matrix,
    ///   2. the stage moves,
    ///   3. the expected harvest date is re-estimated from how early or late this stage arrived,
    ///   4. an append-only CropStageTransition row records who moved it and when.
    /// Reaching Harvested also closes the cycle, which frees the plot for the next sowing.
    /// </summary>
    public async Task<CropCycleDto> AdvanceStageAsync(Guid id, AdvanceStageRequest request, CancellationToken ct = default)
    {
        var cycle = await db.CropCycles.ScopedTo(currentUser)
            .Include(c => c.Crop)
            .Include(c => c.Plot).ThenInclude(p => p.Farm)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        RegistryScope.EnsureVisible(cycle, await db.CropCycles.AnyAsync(c => c.Id == id, ct), "Crop cycle", id);

        if (!currentUser.CanWriteFarm(cycle!.Plot.Farm))
            throw new ForbiddenAccessException("Only the farm's owner or a co-op administrator can advance a crop cycle.");

        if (cycle.Status != CropCycleStatus.Active)
            throw new BusinessRuleException("CYCLE_NOT_ACTIVE", $"This crop cycle is {cycle.Status} and can no longer change stage.");

        if (CropStageRules.Explain(cycle.Stage, request.ToStage) is { } reason)
            throw new BusinessRuleException("ILLEGAL_STAGE_TRANSITION", reason);

        var reachedOn = request.ReachedOn ?? Today;
        if (reachedOn > Today)
            throw new RequestValidationException(nameof(request.ReachedOn), "A stage cannot be reached in the future.");
        if (reachedOn < cycle.SownDate)
            throw new RequestValidationException(nameof(request.ReachedOn), $"The crop was sown on {cycle.SownDate:yyyy-MM-dd}; a stage cannot be reached before that.");

        var lastTransition = await db.CropStageTransitions
            .Where(t => t.CropCycleId == id)
            .OrderByDescending(t => t.TransitionedAt)
            .FirstOrDefaultAsync(ct);
        if (lastTransition is not null && reachedOn < DateOnly.FromDateTime(lastTransition.TransitionedAt))
            throw new RequestValidationException(nameof(request.ReachedOn),
                $"{cycle.Stage} was recorded on {lastTransition.TransitionedAt:yyyy-MM-dd}; the next stage cannot be earlier.");

        var from = cycle.Stage;
        cycle.Stage = request.ToStage;
        cycle.ExpectedHarvestDate = CropStageRules.ReviseExpectedHarvestDate(
            cycle.SownDate, cycle.Crop.MaturityDays, request.ToStage, reachedOn);

        if (request.ToStage == CropStage.Harvested)
        {
            cycle.Status = CropCycleStatus.Harvested;
            cycle.ActualHarvestDate = reachedOn;
        }

        db.CropStageTransitions.Add(new CropStageTransition
        {
            CropCycleId = cycle.Id,
            FromStage = from,
            ToStage = request.ToStage,
            TransitionedAt = reachedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            TransitionedByUserId = currentUser.UserId ?? Guid.Empty,
            Note = request.Note?.Trim()
        });

        try
        {
            // One SaveChanges: stage, revised date, closure and audit row commit together.
            // Version is the PostgreSQL xmin row token — two farmers advancing the same cycle
            // at once means the second gets a 409, not a lost update.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This crop cycle was changed by someone else. Reload and try again.");
        }

        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Straight column reads only. The derived fields — days to harvest, which stage may come
    /// next — are computed in <see cref="ToDto"/>, because enums are stored as strings (so
    /// `stage + 1` is not valid SQL) and because the transition matrix must have exactly one
    /// home: <see cref="CropStageRules"/>.
    /// </summary>
    private static Expression<Func<CropCycle, CycleRow>> Projection => c => new CycleRow(
        c.Id,
        c.PlotId,
        c.Plot.PlotCode,
        c.CropId,
        c.Crop.Name,
        c.Crop.MaturityDays,
        c.SownDate,
        c.Stage,
        c.Status,
        c.ExpectedHarvestDate,
        c.PlannedHarvestDate,
        c.ActualHarvestDate,
        c.StageTransitions
            .OrderBy(t => t.TransitionedAt)
            .Select(t => new StageTransitionDto(t.FromStage, t.ToStage, t.TransitionedAt, t.Note))
            .ToList());

    private static CropCycleDto ToDto(CycleRow row, DateOnly today)
    {
        var effective = row.PlannedHarvestDate ?? row.ExpectedHarvestDate;
        var next = row.Status == CropCycleStatus.Active ? CropStageRules.Next(row.Stage) : null;

        return new CropCycleDto(
            row.Id,
            row.PlotId,
            row.PlotCode,
            row.CropId,
            row.CropName,
            row.CropMaturityDays,
            row.SownDate,
            row.Stage,
            row.Status,
            row.ExpectedHarvestDate,
            row.PlannedHarvestDate,
            row.ActualHarvestDate,
            effective,
            effective.DayNumber - today.DayNumber,
            next is { } stage ? [stage] : [],
            row.Transitions);
    }

    private sealed record CycleRow(
        Guid Id,
        Guid PlotId,
        string PlotCode,
        Guid CropId,
        string CropName,
        int CropMaturityDays,
        DateOnly SownDate,
        CropStage Stage,
        CropCycleStatus Status,
        DateOnly ExpectedHarvestDate,
        DateOnly? PlannedHarvestDate,
        DateOnly? ActualHarvestDate,
        IReadOnlyList<StageTransitionDto> Transitions);
}
