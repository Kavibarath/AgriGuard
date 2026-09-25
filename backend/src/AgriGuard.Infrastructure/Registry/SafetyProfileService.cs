using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Registry;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Registry;

/// <summary>
/// Assembles the inputs for <see cref="SafetyProfileCalculator"/> from the database and maps the
/// result to DTOs. All the judgement lives in the calculator; this class only fetches and maps.
/// </summary>
public sealed class SafetyProfileService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ISafetyProfileService
{
    public async Task<PlotSafetyProfileDto> GetAsync(Guid plotId, CancellationToken ct = default)
    {
        var visible = await db.Plots.AsNoTracking().ScopedTo(currentUser).AnyAsync(p => p.Id == plotId, ct);
        if (!visible)
            RegistryScope.EnsureVisible<object>(null, await db.Plots.AnyAsync(p => p.Id == plotId, ct), "Plot", plotId);

        return await ComputeAsync(plotId, ct);
    }

    /// <summary>
    /// The profile without the caller check. For the agent's plot-safety-profile tool, which has no
    /// user and is authorised by its service key instead; users always come through <see cref="GetAsync"/>.
    /// </summary>
    internal async Task<PlotSafetyProfileDto> ComputeAsync(Guid plotId, CancellationToken ct = default)
    {
        var plot = await db.Plots.AsNoTracking()
            .Where(p => p.Id == plotId)
            .Select(p => new { p.Id, p.PlotCode, p.AreaHectares, FarmName = p.Farm.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Plot", plotId);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(nowUtc);

        var cycle = await db.CropCycles.AsNoTracking()
            .Where(c => c.PlotId == plotId && c.Status == CropCycleStatus.Active)
            .Select(c => new
            {
                c.Id,
                c.CropId,
                CropName = c.Crop.Name,
                c.SownDate,
                c.Stage,
                c.ExpectedHarvestDate,
                c.PlannedHarvestDate
            })
            .FirstOrDefaultAsync(ct);

        if (cycle is null)
        {
            // Nothing growing: no harvest date, so no interval to measure against.
            return new PlotSafetyProfileDto(
                plot!.Id, plot.PlotCode, plot.FarmName, plot.AreaHectares,
                null, null, null, null, null, null,
                [], [], [], [], null, nowUtc);
        }

        var harvestDate = cycle.PlannedHarvestDate ?? cycle.ExpectedHarvestDate;

        // Cancelled applications never touched the crop, so they must not consume an allowance.
        var applications = await db.ChemicalApplications.AsNoTracking()
            .Where(a => a.CropCycleId == cycle.Id && a.Status != ApplicationStatus.Cancelled)
            .OrderBy(a => a.ApplicationDate)
            .Select(a => new
            {
                a.Id,
                a.ProductId,
                ProductName = a.Product.Name,
                a.Product.ActiveIngredientId,
                IngredientName = a.Product.ActiveIngredient.Name,
                a.ApplicationDate,
                a.DosePerHectare,
                a.TotalQuantity,
                a.Status
            })
            .ToListAsync(ct);

        // The rules table for this crop — nothing here is hard-coded; editing an approval row
        // in the admin screen changes what this endpoint reports.
        var approvals = await db.ProductCropApprovals.AsNoTracking()
            .Where(a => a.CropId == cycle.CropId && a.IsActive)
            .Select(a => new ProductRule(
                a.ProductId,
                a.Product.Name,
                a.Product.ActiveIngredientId,
                a.Product.ActiveIngredient.Name,
                a.Product.ActiveIngredient.ResistanceGroup,
                a.PreHarvestIntervalDays,
                a.ReEntryIntervalHours,
                a.MaxApplicationsPerCycle,
                a.MinDaysBetweenApplications,
                a.IsRestricted))
            .ToListAsync(ct);

        var reEntryHoursByProduct = approvals.ToDictionary(a => a.ProductId, a => a.ReEntryIntervalHours);

        var treatments = applications
            .Select(a => new AppliedTreatment(
                a.ProductId,
                a.ActiveIngredientId,
                a.ApplicationDate,
                reEntryHoursByProduct.GetValueOrDefault(a.ProductId)))
            .ToList();

        var profile = SafetyProfileCalculator.Compute(today, nowUtc, harvestDate, treatments, approvals);

        return new PlotSafetyProfileDto(
            plot!.Id,
            plot.PlotCode,
            plot.FarmName,
            plot.AreaHectares,
            cycle.Id,
            cycle.CropName,
            cycle.Stage,
            cycle.SownDate,
            profile.HarvestDate,
            profile.DaysToHarvest,
            [.. applications.Select(a => new AppliedTreatmentDto(
                a.Id, a.ProductId, a.ProductName, a.IngredientName,
                a.ApplicationDate, a.DosePerHectare, a.TotalQuantity, a.Status))],
            [.. profile.IngredientUsage.Select(u => new IngredientUsageDto(
                u.ActiveIngredientId, u.Name, u.ResistanceGroup, u.ApplicationCount,
                u.LastAppliedOn, u.DaysSinceLastApplication))],
            [.. profile.ProductWindows.Select(w => new ProductWindowDto(
                w.ProductId, w.ProductName, w.ActiveIngredientId, w.ActiveIngredientName, w.ResistanceGroup,
                w.IsRestricted, w.PreHarvestIntervalDays, w.ApplicationsUsed, w.MaxApplicationsPerCycle,
                w.ApplicationsRemaining, w.LastAppliedOn, w.LastSafeSprayDate, w.EarliestNextApplication,
                w.CanSprayToday, w.BlockedReason, Explain(w)))],
            profile.PhiBlockedSprayDates,
            profile.ReEntryClearAtUtc,
            nowUtc);
    }

    /// <summary>Turns the machine-readable block into something a farmer can act on.</summary>
    private static string? Explain(ProductWindow window) => window.BlockedReason switch
    {
        SprayBlock.PreHarvestInterval =>
            $"Too close to harvest: {window.ProductName} needs {window.PreHarvestIntervalDays} days before picking, so the last safe spray was {window.LastSafeSprayDate:yyyy-MM-dd}.",
        SprayBlock.MaxApplicationsReached =>
            $"{window.ProductName} has already been used {window.ApplicationsUsed} of {window.MaxApplicationsPerCycle} times allowed this season.",
        SprayBlock.MinimumInterval =>
            $"{window.ActiveIngredientName} was applied on {window.LastAppliedOn:yyyy-MM-dd}; the next application is allowed from {window.EarliestNextApplication:yyyy-MM-dd}.",
        _ => null
    };
}
