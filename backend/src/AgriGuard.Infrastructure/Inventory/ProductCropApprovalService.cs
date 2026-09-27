using System.Linq.Expressions;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgriGuard.Infrastructure.Inventory;

/// <summary>
/// The regulatory rules table: one row per product-crop pair, holding the limits rules V2–V10 of
/// <c>PrescriptionSafetyValidator</c> judge against.
///
/// Nothing caches these rows. <c>PrescriptionValidationService</c> reads the row afresh on every
/// check, so a limit saved here decides the very next validation — the agent's own check, the
/// backend's re-check when the agent reports, and the approval transaction's re-check. That is the
/// viva's "modify a business rule" task: an edit in /rules, no code change, no restart.
/// </summary>
public sealed class ProductCropApprovalService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    ILogger<ProductCropApprovalService> logger) : IProductCropApprovalService
{
    private static readonly Dictionary<string, Expression<Func<ProductCropApproval, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["product"] = a => a.Product.Name,
        ["crop"] = a => a.Crop.Name,
        ["phi"] = a => a.PreHarvestIntervalDays,
        ["maxDose"] = a => a.MaxDosePerHectare,
        ["updatedAt"] = a => a.UpdatedAt
    };

    public async Task<PagedResult<ProductCropApprovalDto>> ListAsync(ApprovalQuery query, CancellationToken ct = default)
    {
        var rules = db.ProductCropApprovals.AsNoTracking();

        if (query.ProductId is { } productId)
            rules = rules.Where(a => a.ProductId == productId);
        if (query.CropId is { } cropId)
            rules = rules.Where(a => a.CropId == cropId);
        if (query.IsActive is { } isActive)
            rules = rules.Where(a => a.IsActive == isActive);
        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{search.Trim()}%";
            rules = rules.Where(a => EF.Functions.ILike(a.Product.Name, pattern) || EF.Functions.ILike(a.Product.ActiveIngredient.Name, pattern));
        }

        return await rules
            .OrderByAllowed(query, Sortable, a => a.Product.Name, a => a.Id)
            .Select(Projection)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<ProductCropApprovalDto> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.ProductCropApprovals.AsNoTracking().Where(a => a.Id == id).Select(Projection).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Rule", id);

    public async Task<ProductCropApprovalDto> CreateAsync(CreateApprovalRequest request, CancellationToken ct = default)
    {
        var product = await db.Products.Where(p => p.Id == request.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product", request.ProductId);
        var crop = await db.Crops.Where(c => c.Id == request.CropId).Select(c => c.Name).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Crop", request.CropId);

        // One row per pair (a unique index too): the validator must never choose between two limits.
        var duplicate = $"{product} already has a rule for {crop}. Edit that rule instead.";
        if (await db.ProductCropApprovals.AnyAsync(a => a.ProductId == request.ProductId && a.CropId == request.CropId, ct))
            throw new ConflictException(duplicate);

        var rule = new ProductCropApproval { ProductId = request.ProductId, CropId = request.CropId };
        Apply(rule, request);
        db.ProductCropApprovals.Add(rule);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException(duplicate);
        }

        logger.LogInformation("Rule {RuleId} created for {Product} on {Crop} by {UserId}: {Limits}",
            rule.Id, product, crop, currentUser.UserId, Snapshot(rule));
        return await GetAsync(rule.Id, ct);
    }

    public async Task<ProductCropApprovalDto> UpdateAsync(Guid id, ApprovalLimitsRequest request, CancellationToken ct = default)
    {
        var rule = await db.ProductCropApprovals.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Rule", id);

        var before = Snapshot(rule);
        Apply(rule, request);
        await db.SaveChangesAsync(ct);

        // The audit trail of a regulatory change: who, when, and every limit before and after.
        logger.LogInformation("Rule {RuleId} changed by {UserId}: {Before} → {After}", id, currentUser.UserId, before, Snapshot(rule));
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Removes the pair's approval entirely: the next proposal of this product on this crop fails V2.
    /// Setting IsActive = false does the same and keeps the limits for later; the editor offers that first.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await db.ProductCropApprovals.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Rule", id);
        var before = Snapshot(rule);
        db.ProductCropApprovals.Remove(rule);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Rule {RuleId} deleted by {UserId}: {Before}", id, currentUser.UserId, before);
    }

    private static void Apply(ProductCropApproval rule, IApprovalLimits limits)
    {
        rule.MinDosePerHectare = limits.MinDosePerHectare;
        rule.MaxDosePerHectare = limits.MaxDosePerHectare;
        rule.PreHarvestIntervalDays = limits.PreHarvestIntervalDays;
        rule.ReEntryIntervalHours = limits.ReEntryIntervalHours;
        rule.MaxApplicationsPerCycle = limits.MaxApplicationsPerCycle;
        rule.MinDaysBetweenApplications = limits.MinDaysBetweenApplications;
        rule.RainfastHours = limits.RainfastHours;
        rule.IsRestricted = limits.IsRestricted;
        rule.IsActive = limits.IsActive;
    }

    private static ApprovalLimitsRequest Snapshot(ProductCropApproval r) => new(
        r.MinDosePerHectare, r.MaxDosePerHectare, r.PreHarvestIntervalDays, r.ReEntryIntervalHours,
        r.MaxApplicationsPerCycle, r.MinDaysBetweenApplications, r.RainfastHours, r.IsRestricted, r.IsActive);

    private static Expression<Func<ProductCropApproval, ProductCropApprovalDto>> Projection => a => new ProductCropApprovalDto(
        a.Id,
        a.ProductId,
        a.Product.Name,
        a.Product.ActiveIngredient.Name,
        a.Product.Unit,
        a.CropId,
        a.Crop.Name,
        a.MinDosePerHectare,
        a.MaxDosePerHectare,
        a.PreHarvestIntervalDays,
        a.ReEntryIntervalHours,
        a.MaxApplicationsPerCycle,
        a.MinDaysBetweenApplications,
        a.RainfastHours,
        a.IsRestricted,
        a.IsActive,
        a.UpdatedAt);
}
