using AgriGuard.Application.Common;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Reports;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Inventory;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Reports;

/// <summary>
/// GET /api/reports/stock-valuation and /api/reports/low-stock (Component C). A dealer sees their
/// own shop; the co-op administrator sees every shop, or one. Values are at each batch's own pack
/// price (InventoryBatch.UnitPrice is per pack), so a price change on a new delivery does not
/// silently revalue the old one.
/// </summary>
public sealed class StockReportService(AgriGuardDbContext db, ICurrentUserAccessor currentUser, FarmCalendar calendar) : IStockReportService
{
    public async Task<StockValuationReport> StockValuationAsync(StockReportQuery query, CancellationToken ct = default)
    {
        var today = calendar.Today;
        var batches = await LoadAsync(query.DealerId, onlyWithStock: true, ct);

        var rows = batches.Select(b => new
        {
            Batch = b,
            // Pack price × how many packs' worth is on the shelf.
            Value = Money(b.QuantityOnHand / b.PackSize * b.UnitPrice),
            HeldValue = Money(b.QuantityReserved / b.PackSize * b.UnitPrice),
            State = BatchExpiry.Classify(b.ExpiryDate, today)
        }).ToList();

        return new StockValuationReport(
            today,
            rows.Count,
            rows.Sum(r => r.Value),
            rows.Sum(r => r.HeldValue),
            rows.Where(r => r.State == ExpiryState.Expired).Sum(r => r.Value),
            rows.Where(r => r.State == ExpiryState.ExpiringSoon).Sum(r => r.Value),
            [.. rows.GroupBy(r => (r.Batch.DealerId, r.Batch.ShopName))
                .Select(g => new DealerValuationDto(g.Key.DealerId, g.Key.ShopName, g.Sum(r => r.Value),
                    g.Where(r => r.State == ExpiryState.Expired).Sum(r => r.Value)))
                .OrderByDescending(d => d.Value)],
            [.. rows.GroupBy(r => (r.Batch.DealerId, r.Batch.ShopName, r.Batch.ProductId, r.Batch.ProductName, r.Batch.Unit))
                .Select(g => new StockValuationRowDto(
                    g.Key.DealerId, g.Key.ShopName, g.Key.ProductId, g.Key.ProductName, g.Key.Unit.ToString(),
                    g.Count(), g.Sum(r => r.Batch.QuantityOnHand), g.Sum(r => r.Batch.QuantityReserved),
                    g.Sum(r => r.Value), g.Sum(r => r.HeldValue), g.Where(r => r.State == ExpiryState.Expired).Sum(r => r.Value)))
                .OrderByDescending(r => r.Value).ThenBy(r => r.ProductName, StringComparer.Ordinal)]);
    }

    public async Task<LowStockReport> LowStockAsync(LowStockQuery query, CancellationToken ct = default)
    {
        var minPacks = query.MinPacks ?? StockLevel.DefaultMinPacks;
        if (minPacks is < 1 or > 1000)
            throw new RequestValidationException(nameof(query.MinPacks), "Choose between 1 and 1000 packs.");

        var today = calendar.Today;
        // Sold-out batches are included: a product the shop stocked and has run out of is the
        // most urgent line in this report.
        var batches = await LoadAsync(query.DealerId, onlyWithStock: false, ct);

        var rows = batches
            .GroupBy(b => (b.DealerId, b.ShopName, b.ProductId, b.ProductName, b.Unit, b.PackSize))
            .Select(g =>
            {
                var inDate = g.Where(b => b.ExpiryDate > today).ToList();
                var available = inDate.Sum(b => b.QuantityOnHand - b.QuantityReserved);
                var packs = StockLevel.SellablePacks(available, g.Key.PackSize);
                return new LowStockRowDto(
                    g.Key.DealerId, g.Key.ShopName, g.Key.ProductId, g.Key.ProductName, g.Key.Unit.ToString(), g.Key.PackSize,
                    available, packs,
                    g.Sum(b => b.QuantityReserved),
                    g.Where(b => b.ExpiryDate <= today).Sum(b => b.QuantityOnHand),
                    inDate.Where(b => b.QuantityOnHand - b.QuantityReserved > 0).Select(b => (DateOnly?)b.ExpiryDate).Min(),
                    StockLevel.Classify(packs, minPacks));
            })
            .Where(r => r.State != StockLevelState.Sufficient)
            // Out of stock first, then the fewest packs left.
            .OrderBy(r => r.State).ThenBy(r => r.SellablePacks).ThenBy(r => r.ProductName, StringComparer.Ordinal)
            .ToList();

        return new LowStockReport(today, minPacks,
            rows.Count(r => r.State == StockLevelState.OutOfStock), rows.Count(r => r.State == StockLevelState.Low), rows);
    }

    private sealed record BatchRow(
        Guid DealerId, string ShopName, Guid ProductId, string ProductName, ProductUnit Unit, decimal PackSize,
        DateOnly ExpiryDate, decimal QuantityOnHand, decimal QuantityReserved, decimal UnitPrice);

    /// <summary>The batches this caller may report on: their own shop, or, for an administrator, all or one.</summary>
    private async Task<List<BatchRow>> LoadAsync(Guid? dealerId, bool onlyWithStock, CancellationToken ct)
    {
        var batches = db.InventoryBatches.AsNoTracking();
        if (currentUser.Role == UserRole.CoopAdministrator)
        {
            if (dealerId is { } requested)
            {
                if (!await db.Dealers.AnyAsync(d => d.Id == requested, ct))
                    throw new NotFoundException("Dealer", requested);
                batches = batches.Where(b => b.DealerId == requested);
            }
        }
        else
        {
            var shopId = await db.OwnShopIdAsync(currentUser, ct);
            DealerScope.EnsureOwnShop(dealerId, shopId);
            if (shopId is null)
                return [];
            batches = batches.Where(b => b.DealerId == shopId);
        }

        if (onlyWithStock)
            batches = batches.Where(b => b.QuantityOnHand > 0);

        return await batches
            .Select(b => new BatchRow(b.DealerId, b.Dealer.ShopName, b.ProductId, b.Product.Name, b.Product.Unit, b.Product.PackSize,
                b.ExpiryDate, b.QuantityOnHand, b.QuantityReserved, b.UnitPrice))
            .ToListAsync(ct);
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
