using AgriGuard.Application.Prescriptions;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Prescriptions;

/// <summary>
/// Runs <see cref="PrescriptionSafetyValidator"/> against live data: checks the proposal's shape
/// (V1), loads everything the other rules read, then validates. All the I/O is here, so the
/// validator stays pure.
///
/// Used twice per run: by the agent's validate_prescription tool, and again by the backend when the
/// agent reports a proposal as ready for approval — the backend never takes the agent's word for it.
/// </summary>
public sealed class PrescriptionSafetyChecker(AgriGuardDbContext db, TimeProvider timeProvider)
{
    public async Task<PrescriptionVerdict> CheckAsync(PrescriptionProposalInput input, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var (proposal, shape) = PrescriptionSafetyValidator.CheckShape(input, today);
        if (proposal is null)
            return PrescriptionSafetyValidator.Malformed(shape);

        return PrescriptionSafetyValidator.Validate(proposal, await LoadContextAsync(proposal, today, ct));
    }

    private async Task<PrescriptionSafetyContext> LoadContextAsync(ParsedProposal p, DateOnly today, CancellationToken ct)
    {
        var cycle = await db.CropCycles.AsNoTracking()
            .Where(c => c.Id == p.CropCycleId)
            .Select(c => new
            {
                Facts = new CycleFacts(
                    c.Id,
                    c.Status == CropCycleStatus.Active,
                    c.CropId,
                    c.Plot.AreaHectares,
                    c.PlannedHarvestDate ?? c.ExpectedHarvestDate,
                    c.Plot.Farm.FarmerId),
                c.Plot.Farm.DistrictId
            })
            .FirstOrDefaultAsync(ct);

        var product = await db.Products.AsNoTracking()
            .Where(x => x.Id == p.ProductId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.IsActive,
                x.ActiveIngredientId,
                ActiveIngredient = x.ActiveIngredient.Name,
                x.Unit,
                x.PackSize,
                x.UnitPrice
            })
            .FirstOrDefaultAsync(ct);

        // V2–V8: the rules-table row for this product on this crop.
        var approval = cycle is null ? null : await db.ProductCropApprovals.AsNoTracking()
            .Where(a => a.ProductId == p.ProductId && a.CropId == cycle.Facts.CropId)
            .Select(a => new ApprovalFacts(
                a.IsActive, a.MinDosePerHectare, a.MaxDosePerHectare, a.PreHarvestIntervalDays,
                a.MaxApplicationsPerCycle, a.MinDaysBetweenApplications, a.RainfastHours, a.IsRestricted))
            .FirstOrDefaultAsync(ct);

        // V6–V7 count the active ingredient, not the product: two brands of mancozeb are one chemical
        // to the pathogen, and to the resistance it builds.
        var history = ApplicationHistory.None;
        if (cycle is not null && product is not null)
        {
            var dates = await db.ChemicalApplications.AsNoTracking()
                .Where(a => a.CropCycleId == cycle.Facts.Id
                            && a.Status != ApplicationStatus.Cancelled
                            && a.Product.ActiveIngredientId == product.ActiveIngredientId)
                .Select(a => a.ApplicationDate)
                .ToListAsync(ct);
            history = new ApplicationHistory(dates.Count, dates.Count == 0 ? null : dates.Max());
        }

        // V10: the run making the proposal, and the case it is resolving.
        var run = await db.AgentRuns.AsNoTracking()
            .Where(r => r.Id == p.RunId)
            .Select(r => new RunFacts(
                r.Id,
                r.Case.CropCycleId,
                r.Case.FarmerId,
                r.Status == AgentRunStatus.Completed || r.Status == AgentRunStatus.Rejected
                    || r.Status == AgentRunStatus.Failed || r.Status == AgentRunStatus.TimedOut))
            .FirstOrDefaultAsync(ct);

        var stock = product is null ? null : await LoadStockAsync(p, cycle?.DistrictId, ct);

        // V11
        var creditLimit = cycle is null ? null : await db.Users.AsNoTracking()
            .Where(u => u.Id == cycle.Facts.PlotOwnerId)
            .Select(u => u.CreditLimit)
            .FirstOrDefaultAsync(ct);

        return new PrescriptionSafetyContext(
            today,
            cycle?.Facts,
            product is null ? null : new ProductFacts(
                product.Id, product.Name, product.IsActive, product.ActiveIngredient,
                product.Unit.ToString(), product.PackSize, product.UnitPrice),
            approval,
            history,
            run,
            stock,
            // V8 reads a spray-window forecast once Component D's Open-Meteo client exists. Until then
            // the rule reports "not checked", which is the documented degradation (§10).
            Weather: null,
            creditLimit);
    }

    /// <summary>
    /// V9: the named dealer if there is one, otherwise the best-stocked dealer in the farm's
    /// district. Only batches still in date on the spray date count, and only what is not already
    /// reserved for another approval.
    /// </summary>
    private async Task<DealerStock?> LoadStockAsync(ParsedProposal p, Guid? districtId, CancellationToken ct)
    {
        var batches = db.InventoryBatches.AsNoTracking()
            .Where(b => b.ProductId == p.ProductId
                        && b.ExpiryDate > p.SprayDate
                        && b.QuantityOnHand - b.QuantityReserved > 0);

        if (p.DealerId is { } dealerId)
            batches = batches.Where(b => b.DealerId == dealerId);
        else if (districtId is { } district)
            batches = batches.Where(b => b.Dealer.DistrictId == district);
        else
            return null;

        var rows = await batches
            .Select(b => new { b.DealerId, b.Dealer.ShopName, Available = b.QuantityOnHand - b.QuantityReserved, b.UnitPrice })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => (r.DealerId, r.ShopName))
            // The highest batch price, so the credit check (V11) is never optimistic.
            .Select(g => new DealerStock(g.Key.DealerId, g.Key.ShopName, g.Sum(r => r.Available), g.Max(r => r.UnitPrice)))
            .OrderByDescending(s => s.AvailableQuantity)
            .FirstOrDefault();
    }
}
