using System.Globalization;
using AgriGuard.Application.Agent;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// Answers the agent's tool calls. Read-only by construction: no method here adds, changes or
/// removes a row. The agent can look, and it can ask the validator; everything that changes state
/// happens elsewhere, after a human decides.
/// </summary>
public sealed class AgentToolService(
    AgriGuardDbContext db,
    AgentPrescriptionGate prescriptionGate,
    SafetyProfileService safetyProfiles,
    TimeProvider timeProvider) : IAgentToolService
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public async Task<CaseDetailTool> GetCaseDetailAsync(Guid caseId, CancellationToken ct = default)
    {
        var c = await db.CropCases.AsNoTracking()
            .Where(x => x.Id == caseId)
            .Select(x => new
            {
                x.Id,
                x.ReferenceNo,
                x.CropCycleId,
                x.PlotId,
                x.CropCycle.CropId,
                x.DistrictId,
                CropName = x.CropCycle.Crop.Name,
                x.CropCycle.Stage,
                x.CropCycle.SownDate,
                x.Plot.AreaHectares,
                x.SymptomCodes,
                x.FarmerNote
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Case", caseId);

        // Candidate pathogens: those whose indicative symptoms overlap the reported ones. Matched in
        // memory — fifteen pathogens — so the ranking stays readable.
        var pathogens = await db.Pathogens.AsNoTracking()
            .Select(p => new { p.Code, p.CommonName, p.Type, p.IndicativeSymptoms })
            .ToListAsync(ct);

        var candidates = pathogens
            .Select(p => new { p, Matched = p.IndicativeSymptoms.Intersect(c.SymptomCodes).ToList() })
            .Where(x => x.Matched.Count > 0)
            .OrderByDescending(x => x.Matched.Count)
            .ThenBy(x => x.p.Code, StringComparer.Ordinal)
            .Select(x => new CandidatePathogenTool(x.p.Code, x.p.CommonName, x.p.Type.ToString(), x.Matched))
            .ToList();

        return new CaseDetailTool(
            c.Id, c.ReferenceNo, c.CropCycleId, c.PlotId, c.CropId, c.DistrictId, c.CropName,
            c.Stage.ToString(), c.SownDate, c.AreaHectares, c.SymptomCodes,
            // Passed through untouched: the agent fences and flags it as untrusted (agent/app/prompts.py).
            c.FarmerNote,
            candidates);
    }

    public async Task<CropHistoryTool> GetCropHistoryAsync(Guid cropCycleId, CancellationToken ct = default)
    {
        if (!await db.CropCycles.AnyAsync(c => c.Id == cropCycleId, ct))
            throw new NotFoundException("Crop cycle", cropCycleId);

        var applications = await db.ChemicalApplications.AsNoTracking()
            .Where(a => a.CropCycleId == cropCycleId)
            .OrderBy(a => a.ApplicationDate)
            .Select(a => new
            {
                ProductName = a.Product.Name,
                ActiveIngredient = a.Product.ActiveIngredient.Name,
                a.ApplicationDate,
                a.DosePerHectare,
                a.Status
            })
            .ToListAsync(ct);

        var items = applications
            .Select(a => new ApplicationTool(a.ProductName, a.ActiveIngredient, a.ApplicationDate, a.DosePerHectare, a.Status.ToString()))
            .ToList();

        var summary = items.Count == 0
            ? "No chemical applications recorded this cycle."
            : $"{items.Count} application(s) this cycle: " + string.Join("; ", items.Select(a =>
                $"{a.ProductName} ({a.ActiveIngredient}) on {Iso(a.ApplicationDate)}, {Num(a.DosePerHectare)}/ha, {a.Status}")) + ".";

        return new CropHistoryTool(cropCycleId, summary, items);
    }

    /// <summary>
    /// Disease pressure for a crop in a district over a recent window, from cases an agronomist has
    /// confirmed. A deliberately simple index; Component D's intelligence endpoint refines it.
    /// </summary>
    public async Task<OutbreakSignalTool> GetOutbreakSignalAsync(Guid cropId, Guid districtId, int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var since = timeProvider.GetUtcNow().UtcDateTime.AddDays(-days);

        var recent = db.CropCases.AsNoTracking()
            .Where(c => c.DistrictId == districtId && c.CropCycle.CropId == cropId && c.CreatedAt >= since);

        var total = await recent.CountAsync(ct);
        var confirmed = await recent
            .Where(c => c.ConfirmedPathogenId != null)
            .GroupBy(c => new { c.ConfirmedPathogen!.Code, c.ConfirmedPathogen.CommonName })
            .Select(g => new ConfirmedPathogenCountTool(g.Key.Code, g.Key.CommonName, g.Count()))
            .ToListAsync(ct);
        confirmed = [.. confirmed.OrderByDescending(p => p.Cases).ThenBy(p => p.Code, StringComparer.Ordinal)];

        var top = confirmed.FirstOrDefault();
        var pressure = top?.Cases switch
        {
            >= 5 => "High",
            >= 2 => "Moderate",
            _ => "Low"
        };

        var summary = top is null
            ? $"No confirmed outbreaks of this crop in the district in the last {days} days ({total} case(s) reported)."
            : $"{top.CommonName} pressure {pressure.ToLowerInvariant()} in this district ({top.Cases} confirmed case(s) in {days} days; {total} reported in total).";

        return new OutbreakSignalTool(cropId, districtId, days, total, pressure, summary, confirmed);
    }

    /// <summary>
    /// Products labelled for the pathogen and approved for the crop. Withdrawn and restricted products
    /// are left out: the model should not be choosing between options the rules already refuse. An
    /// unknown pathogen code gives an empty list rather than an error, so a bad diagnosis ends in a
    /// recorded "no product" failure instead of three blind retries.
    /// </summary>
    public async Task<ApprovedProductsTool> SearchApprovedProductsAsync(Guid cropId, string pathogenCode, CancellationToken ct = default)
    {
        var code = pathogenCode.Trim().ToUpperInvariant();

        var products = await db.ProductCropApprovals.AsNoTracking()
            .Where(a => a.CropId == cropId && a.IsActive && !a.IsRestricted && a.Product.IsActive
                        && a.Product.Targets.Any(t => t.Pathogen.Code == code))
            .OrderBy(a => a.Product.Name)
            .Select(a => new
            {
                a.ProductId,
                ProductName = a.Product.Name,
                ActiveIngredient = a.Product.ActiveIngredient.Name,
                a.Product.ActiveIngredient.ResistanceGroup,
                a.Product.Unit,
                a.Product.PackSize,
                a.MinDosePerHectare,
                a.MaxDosePerHectare,
                a.PreHarvestIntervalDays,
                a.MaxApplicationsPerCycle,
                a.MinDaysBetweenApplications
            })
            .ToListAsync(ct);

        return new ApprovedProductsTool(products.Select(p => new ApprovedProductTool(
            p.ProductId, p.ProductName, p.ActiveIngredient, p.ResistanceGroup, p.Unit.ToString(), p.PackSize,
            p.MinDosePerHectare, p.MaxDosePerHectare, p.PreHarvestIntervalDays,
            p.MaxApplicationsPerCycle, p.MinDaysBetweenApplications)).ToList());
    }

    /// <summary>
    /// Component A's safety profile (the same calculation behind GET /api/plots/{id}/safety-profile),
    /// trimmed to what the Action agent reads: per product, the last PHI-safe spray date and whether
    /// anything blocks a spray today. Offered up front so the agent proposes something that passes.
    /// </summary>
    public async Task<PlotSafetyProfileTool> GetPlotSafetyProfileAsync(Guid plotId, CancellationToken ct = default)
    {
        var profile = await safetyProfiles.ComputeAsync(plotId, ct);

        if (profile is not { CropCycleId: { } cycleId, CropName: { } cropName, HarvestDate: { } harvestDate, DaysToHarvest: { } daysToHarvest })
            throw new BusinessRuleException("NO_ACTIVE_CYCLE", "This plot has no active crop cycle.");

        return new PlotSafetyProfileTool(
            profile.PlotId, cycleId, cropName, profile.AreaHectares, harvestDate, daysToHarvest,
            [.. profile.ProductWindows.Select(w => new ProductWindowTool(
                w.ProductId, w.ProductName, w.PreHarvestIntervalDays, w.LastSafeSprayDate,
                w.ApplicationsUsed, w.MaxApplicationsPerCycle, w.LastAppliedOn, w.EarliestNextApplication,
                w.CanSprayToday, w.BlockedExplanation))]);
    }

    public async Task<StockAvailabilityTool> CheckStockAvailabilityAsync(Guid productId, Guid? districtId, DateOnly? usableOn, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Unit })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product", productId);

        // A batch that expires before the spray date is not stock for this purpose.
        var inDateOn = usableOn ?? Today;
        var batches = db.InventoryBatches.AsNoTracking()
            .Where(b => b.ProductId == productId && b.ExpiryDate > inDateOn && b.QuantityOnHand - b.QuantityReserved > 0);
        if (districtId is { } district)
            batches = batches.Where(b => b.Dealer.DistrictId == district);

        var rows = await batches
            .Select(b => new { b.DealerId, b.Dealer.ShopName, b.Dealer.DistrictId, Available = b.QuantityOnHand - b.QuantityReserved, b.ExpiryDate })
            .ToListAsync(ct);

        var dealers = rows
            .GroupBy(r => (r.DealerId, r.ShopName, r.DistrictId))
            .Select(g => new DealerStockTool(g.Key.DealerId, g.Key.ShopName, g.Key.DistrictId, g.Sum(r => r.Available), g.Min(r => r.ExpiryDate)))
            .OrderByDescending(d => d.AvailableQuantity)
            .ToList();

        return new StockAvailabilityTool(productId, dealers.Sum(d => d.AvailableQuantity), product.Unit.ToString(), dealers);
    }

    public async Task<ProductPricingTool> GetProductPricingAsync(Guid productId, decimal? quantity, CancellationToken ct = default)
    {
        var p = await db.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => new { x.Id, x.Name, x.Unit, x.PackSize, x.UnitPrice })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product", productId);

        int? packs = quantity is > 0 ? (int)Math.Ceiling(quantity.Value / p.PackSize) : null;
        return new ProductPricingTool(p.Id, p.Name, p.Unit.ToString(), p.PackSize, p.UnitPrice, packs, packs * p.UnitPrice);
    }

    public Task<AgentVerdictTool> ValidatePrescriptionAsync(AgentProposalInput proposal, CancellationToken ct = default) =>
        prescriptionGate.CheckAsync(proposal, ct);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Num(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
