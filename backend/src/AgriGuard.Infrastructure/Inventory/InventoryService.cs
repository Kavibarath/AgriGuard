using System.Globalization;
using System.Linq.Expressions;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Inventory;

/// <summary>A dealer's own shelf: batches received, counted and repriced. Holds are <see cref="ReservationService"/>.</summary>
public sealed class InventoryService(AgriGuardDbContext db, ICurrentUserAccessor currentUser, TimeProvider timeProvider) : IInventoryService
{
    private static readonly Dictionary<string, Expression<Func<InventoryBatch, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["expiryDate"] = b => b.ExpiryDate,
        ["product"] = b => b.Product.Name,
        ["batchNo"] = b => b.BatchNo,
        ["onHand"] = b => b.QuantityOnHand,
        ["available"] = b => b.QuantityOnHand - b.QuantityReserved
    };

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public async Task<PagedResult<InventoryBatchDto>> ListAsync(InventoryQuery query, CancellationToken ct = default)
    {
        var shopId = await db.OwnShopIdAsync(currentUser, ct);
        DealerScope.EnsureOwnShop(query.DealerId, shopId);
        if (shopId is null)
            return PagedResult<InventoryBatchDto>.Empty(query.NormalisedPage, query.NormalisedPageSize);

        var batches = db.InventoryBatches.AsNoTracking().Where(b => b.DealerId == shopId);

        if (!query.IncludeEmpty)
            batches = batches.Where(b => b.QuantityOnHand > 0);
        if (query.ProductId is { } productId)
            batches = batches.Where(b => b.ProductId == productId);
        if (query.ExpiringBefore is { } before)
            batches = batches.Where(b => b.ExpiryDate <= before);
        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{search.Trim()}%";
            batches = batches.Where(b => EF.Functions.ILike(b.Product.Name, pattern) || EF.Functions.ILike(b.BatchNo, pattern));
        }

        // Soonest expiry first by default: that is the stock that needs attention.
        var page = await batches
            .OrderByAllowed(query, Sortable, b => b.ExpiryDate, b => b.Id)
            .Select(Row)
            .ToPagedResultAsync(query, ct);

        var today = Today;
        return new PagedResult<InventoryBatchDto>([.. page.Items.Select(r => ToDto(r, today))], page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<InventoryBatchDto> CreateBatchAsync(CreateBatchRequest request, CancellationToken ct = default)
    {
        var shopId = await db.RequireOwnShopAsync(currentUser, ct);
        await EnsureSellableAsync(request.ProductId, ct);

        if (request.ExpiryDate <= Today)
            throw new RequestValidationException(nameof(request.ExpiryDate), "This batch has already expired. Do not put it on the shelf.");

        var batchNo = request.BatchNo.Trim();
        if (await db.InventoryBatches.AnyAsync(b => b.DealerId == shopId && b.ProductId == request.ProductId && b.BatchNo == batchNo, ct))
            throw new ConflictException($"Batch {batchNo} of this product is already on your shelf. Update its count instead.");

        var batch = new InventoryBatch
        {
            DealerId = shopId,
            ProductId = request.ProductId,
            BatchNo = batchNo,
            ExpiryDate = request.ExpiryDate,
            QuantityOnHand = request.QuantityOnHand,
            UnitPrice = request.UnitPrice
        };
        db.InventoryBatches.Add(batch);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            // The same delivery entered twice at the same instant.
            throw new ConflictException($"Batch {batchNo} of this product is already on your shelf. Update its count instead.");
        }

        return await GetAsync(batch.Id, ct);
    }

    public async Task<InventoryBatchDto> UpdateBatchAsync(Guid id, UpdateBatchRequest request, CancellationToken ct = default)
    {
        var shopId = await db.RequireOwnShopAsync(currentUser, ct);
        var batch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.Id == id && b.DealerId == shopId, ct);
        DealerScope.EnsureVisible(batch, await db.InventoryBatches.AnyAsync(b => b.Id == id, ct), "Batch", id);

        // Held stock is promised to someone. A recount may not make it disappear; release the hold first.
        if (request.QuantityOnHand < batch!.QuantityReserved)
            throw new BusinessRuleException("BELOW_RESERVED",
                $"{Num(batch.QuantityReserved)} of this batch is held for pending orders, so stock on hand cannot go below that. Release the hold first.");

        batch.ExpiryDate = request.ExpiryDate;
        batch.QuantityOnHand = request.QuantityOnHand;
        batch.UnitPrice = request.UnitPrice;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin moved: a hold or an approval drew on this batch while the count was being edited.
            throw new ConflictException("This batch changed while you were editing it (stock was held or sold). Reload and enter the count again.");
        }

        return await GetAsync(id, ct);
    }

    private async Task<InventoryBatchDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.InventoryBatches.AsNoTracking().Where(b => b.Id == id).Select(Row).FirstAsync(ct);
        return ToDto(row, Today);
    }

    private async Task EnsureSellableAsync(Guid productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Name, p.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product", productId);

        if (!product.IsActive)
            throw new BusinessRuleException("PRODUCT_WITHDRAWN", $"{product.Name} has been withdrawn from sale and cannot be stocked.");
    }

    /// <summary>What SQL returns; days to expiry and the warning are worked out against today afterwards.</summary>
    private sealed record BatchRow(
        Guid Id, Guid DealerId, string ShopName, Guid ProductId, string ProductName, ProductUnit Unit, decimal PackSize,
        string BatchNo, DateOnly ExpiryDate, decimal QuantityOnHand, decimal QuantityReserved, decimal UnitPrice);

    private static Expression<Func<InventoryBatch, BatchRow>> Row => b => new BatchRow(
        b.Id, b.DealerId, b.Dealer.ShopName, b.ProductId, b.Product.Name, b.Product.Unit, b.Product.PackSize,
        b.BatchNo, b.ExpiryDate, b.QuantityOnHand, b.QuantityReserved, b.UnitPrice);

    private static InventoryBatchDto ToDto(BatchRow r, DateOnly today) => new(
        r.Id, r.DealerId, r.ShopName, r.ProductId, r.ProductName, r.Unit, r.PackSize, r.BatchNo, r.ExpiryDate,
        r.QuantityOnHand, r.QuantityReserved, r.QuantityOnHand - r.QuantityReserved, r.UnitPrice,
        r.ExpiryDate.DayNumber - today.DayNumber,
        BatchExpiry.Classify(r.ExpiryDate, today));

    private static string Num(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
