using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Validation;
using AgriGuard.Domain.Registry;
using AgriGuard.Domain.Validation;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgriGuard.Infrastructure.Validation;

/// <summary>
/// Gathers the facts a proposal is judged against and hands them to
/// <see cref="PrescriptionSafetyValidator"/>.
///
/// Deliberately not access-scoped to the caller: this runs on behalf of an agent acting for a
/// farmer, and V10 checks ownership itself using the plot's real owner. Reaching it still
/// requires authentication at the endpoint.
/// </summary>
public sealed class PrescriptionValidationService(
    AgriGuardDbContext db,
    TimeProvider timeProvider,
    ILogger<PrescriptionValidationService> logger) : IPrescriptionValidationService
{
    public async Task<ValidationVerdictDto> ValidateAsync(ValidatePrescriptionRequest request, CancellationToken ct = default)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(nowUtc);

        var cycle = await db.CropCycles.AsNoTracking()
            .Where(c => c.Id == request.CropCycleId)
            .Select(c => new
            {
                c.Id,
                c.PlotId,
                c.Plot.PlotCode,
                c.Plot.AreaHectares,
                PlotOwnerId = c.Plot.Farm.FarmerId,
                OwnerCreditLimit = c.Plot.Farm.Farmer.CreditLimit,
                c.CropId,
                CropName = c.Crop.Name,
                c.ExpectedHarvestDate,
                c.PlannedHarvestDate,
                c.Status
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Crop cycle", request.CropCycleId);

        if (cycle.Status != CropCycleStatus.Active)
            throw new BusinessRuleException("CYCLE_NOT_ACTIVE",
                $"This crop cycle is {cycle.Status}; there is nothing left to treat.");

        // The rules row for exactly this product on this crop. Missing or inactive is V2's job
        // to report, not an error here — a rejected proposal is a valid outcome.
        var approval = await db.ProductCropApprovals.AsNoTracking()
            .Where(a => a.ProductId == request.ProductId && a.CropId == cycle.CropId)
            .Select(a => new ProductApprovalSnapshot(
                a.ProductId,
                a.Product.Name,
                a.Product.ActiveIngredientId,
                a.Product.ActiveIngredient.Name,
                a.MinDosePerHectare,
                a.MaxDosePerHectare,
                a.PreHarvestIntervalDays,
                a.MaxApplicationsPerCycle,
                a.MinDaysBetweenApplications,
                a.RainfastHours,
                a.IsRestricted,
                a.IsActive))
            .FirstOrDefaultAsync(ct);

        var applications = await db.ChemicalApplications.AsNoTracking()
            .Where(a => a.CropCycleId == cycle.Id && a.Status != ApplicationStatus.Cancelled)
            .Select(a => new AppliedTreatmentFact(a.ProductId, a.Product.ActiveIngredientId, a.ApplicationDate))
            .ToListAsync(ct);

        var estimatedCost = await EstimateCostAsync(request, ct);

        var context = new PrescriptionValidationContext(
            cycle.PlotId,
            cycle.PlotOwnerId,
            cycle.AreaHectares,
            cycle.CropId,
            cycle.CropName,
            cycle.PlannedHarvestDate ?? cycle.ExpectedHarvestDate,
            // The proposal is made for the farmer who owns the plot; V10 fails if a run ever
            // drifts onto land belonging to someone else.
            cycle.PlotOwnerId,
            approval,
            applications,
            // Restricted-use permits are not modelled yet, so a restricted product always fails
            // V10. Failing closed is the right default for a permit we cannot verify.
            HasRestrictedUsePermit: false,
            cycle.OwnerCreditLimit,
            estimatedCost,
            request.Weather is { } w ? new WeatherAssessment(w.RainProbabilityPercent, w.WindSpeedKph, w.TemperatureC) : null,
            request.Stock is { } s ? new StockAssessment(s.AvailableQuantity, s.EarliestBatchExpiry) : null);

        var proposal = new PrescriptionProposal(
            request.ProductId, request.DosePerHectare, request.TotalQuantity, request.SprayDate, request.DealerId);

        var verdict = PrescriptionSafetyValidator.Validate(proposal, context, today);

        logger.LogInformation(
            "Validated prescription for cycle {CropCycleId}: {Outcome} ({Summary})",
            cycle.Id, verdict.Outcome, verdict.Summary);

        return new ValidationVerdictDto(
            verdict.Outcome,
            verdict.Summary,
            [.. verdict.Results.Select(r => new RuleResultDto(r.Code, r.Name, r.Status, r.Severity, r.Message, r.Evidence))],
            cycle.Id,
            cycle.PlotId,
            cycle.PlotCode,
            cycle.CropName,
            cycle.AreaHectares,
            context.HarvestDate,
            approval?.ProductName,
            estimatedCost,
            nowUtc);
    }

    /// <summary>
    /// Prices the order in whole packs — a farmer cannot buy 1.6 litres of a product sold in
    /// 1-litre packs, and the credit check (V11) has to reflect what they will actually be charged.
    /// </summary>
    private async Task<decimal?> EstimateCostAsync(ValidatePrescriptionRequest request, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == request.ProductId)
            .Select(p => new { p.PackSize, p.UnitPrice })
            .FirstOrDefaultAsync(ct);

        if (product is null || product.PackSize <= 0 || request.TotalQuantity <= 0) return null;

        var packs = Math.Ceiling(request.TotalQuantity / product.PackSize);
        return packs * product.UnitPrice;
    }
}
