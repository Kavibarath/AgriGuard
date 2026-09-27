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
using Microsoft.Extensions.Logging;

namespace AgriGuard.Infrastructure.Inventory;

/// <summary>
/// A dealer's holds on their own stock (§5.1 non-CRUD): hold, then commit or release.
///
/// Each of the three is ONE serializable transaction in which the batch rows are locked with
/// SELECT … FOR UPDATE before anything is read from them (<see cref="StockLedger"/>). Two holds racing
/// for the last packs therefore queue on the lock: the second sees what the first took, and either
/// finds enough left or is refused. It can never oversell, and the CHECK constraint
/// quantity_reserved &lt;= quantity_on_hand is the backstop if the code were ever wrong.
///
/// A hold lasts <see cref="ReservationLimits.HoldFor"/> (24 h). <see cref="ReservationExpirySweeper"/>
/// releases the ones nobody resolved, so forgotten holds do not keep stock off sale.
/// </summary>
public sealed class ReservationService(
    AgriGuardDbContext db,
    StockLedger ledger,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<ReservationService> logger) : IReservationService
{
    private const string RaceMessage = "Someone changed this stock at the same moment. Reload and try again.";

    private static readonly Dictionary<string, Expression<Func<StockReservation, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["createdAt"] = r => r.CreatedAt,
        ["expiresAt"] = r => r.ExpiresAt,
        ["product"] = r => r.Product.Name,
        ["status"] = r => r.Status
    };

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<ReservationDto>> ListAsync(ReservationQuery query, CancellationToken ct = default)
    {
        if (await db.OwnShopIdAsync(currentUser, ct) is not { } shopId)
            return PagedResult<ReservationDto>.Empty(query.NormalisedPage, query.NormalisedPageSize);

        var reservations = db.StockReservations.AsNoTracking().Where(r => r.DealerId == shopId);
        if (query.Status is { } status)
            reservations = reservations.Where(r => r.Status == status);

        // Newest first unless asked otherwise: the hold just placed is the one being looked for.
        var ordered = query.SortBy is null
            ? reservations.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            : reservations.OrderByAllowed(query, Sortable, r => r.CreatedAt, r => r.Id);

        return await ordered.Select(Projection).ToPagedResultAsync(query, ct);
    }

    public async Task<ReservationDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var shopId = await db.OwnShopIdAsync(currentUser, ct);
        var reservation = await db.StockReservations.AsNoTracking()
            .Where(r => r.Id == id && r.DealerId == shopId)
            .Select(Projection)
            .FirstOrDefaultAsync(ct);

        DealerScope.EnsureVisible(reservation, await db.StockReservations.AnyAsync(r => r.Id == id, ct), "Reservation", id);
        return reservation!;
    }

    public async Task<ReservationDto> CreateAsync(CreateReservationRequest request, CancellationToken ct = default)
    {
        var shopId = await db.RequireOwnShopAsync(currentUser, ct);
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == request.ProductId)
            .Select(p => new { p.Name, p.PackSize, p.Unit, p.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        if (!product.IsActive)
            throw new BusinessRuleException("PRODUCT_WITHDRAWN", $"{product.Name} has been withdrawn from sale and cannot be held.");

        var today = DateOnly.FromDateTime(UtcNow);
        var usableOn = request.UsableOn ?? today;
        if (usableOn < today)
            throw new RequestValidationException(nameof(request.UsableOn), "The date the stock is needed cannot be in the past.");

        // Whole packs are what leave the shelf, so whole packs are what is held.
        var packs = StockAllocation.PacksFor(request.Quantity, product.PackSize);
        var quantity = packs * product.PackSize;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        var reservation = await SerializableTransaction.RunAsync(db, async () =>
        {
            var pick = await ledger.PickAsync(request.ProductId, quantity, shopId, districtId: null, usableOn, ct)
                ?? throw new BusinessRuleException("INSUFFICIENT_STOCK",
                    $"Not enough {product.Name} in date on {usableOn:yyyy-MM-dd} to hold {packs} pack(s) ({Num(quantity)} {product.Unit}). " +
                    $"Available: {Num(await AvailableAsync(shopId, request.ProductId, usableOn, ct))} {product.Unit}.");

            return ledger.Hold(pick, agentRunId: null, UtcNow + ReservationLimits.HoldFor, note);
        }, RaceMessage, ct);

        logger.LogInformation("Held {Quantity} of product {ProductId} at dealer {DealerId} as reservation {ReservationId}",
            quantity, request.ProductId, shopId, reservation.Id);
        return await GetAsync(reservation.Id, ct);
    }

    public Task<ReservationDto> CommitAsync(Guid id, CancellationToken ct = default) =>
        ResolveAsync(id, commit: true, ct);

    public Task<ReservationDto> ReleaseAsync(Guid id, CancellationToken ct = default) =>
        ResolveAsync(id, commit: false, ct);

    /// <summary>Commit or release, with the reservation row and its batches locked for the whole change.</summary>
    private async Task<ReservationDto> ResolveAsync(Guid id, bool commit, CancellationToken ct)
    {
        var shopId = await db.RequireOwnShopAsync(currentUser, ct);

        await SerializableTransaction.RunAsync(db, async () =>
        {
            var reservation = await ledger.LockReservationAsync(id, ct);
            DealerScope.EnsureVisible(reservation?.DealerId == shopId ? reservation : null, reservation is not null, "Reservation", id);

            if (reservation!.Status != ReservationStatus.Held)
                throw new ConflictException($"This hold is already {reservation.Status}.");

            // A hold made for an agent's proposal is the agronomist's to resolve, through the approval decision.
            if (reservation.AgentRunId is not null)
                throw new BusinessRuleException("HELD_FOR_APPROVAL",
                    "This stock is held for a prescription awaiting an agronomist's decision. It is committed or released by that decision.");

            var now = UtcNow;
            if (commit && reservation.ExpiresAt <= now)
                throw new BusinessRuleException("RESERVATION_EXPIRED",
                    $"This hold expired at {reservation.ExpiresAt:yyyy-MM-dd HH:mm} UTC and its stock is back on sale. Place a new hold.");

            if (commit)
                await ledger.CommitAsync(reservation, now, ct);
            else
                await ledger.ReleaseAsync(reservation, ReservationStatus.Released, now, ct);
            return reservation;
        }, RaceMessage, ct);

        logger.LogInformation("Reservation {ReservationId} {Outcome} by dealer {DealerId}", id, commit ? "committed" : "released", shopId);
        return await GetAsync(id, ct);
    }

    private Task<decimal> AvailableAsync(Guid shopId, Guid productId, DateOnly usableOn, CancellationToken ct) =>
        db.InventoryBatches.AsNoTracking()
            .Where(b => b.DealerId == shopId && b.ProductId == productId && b.ExpiryDate > usableOn)
            .SumAsync(b => b.QuantityOnHand - b.QuantityReserved, ct);

    private static Expression<Func<StockReservation, ReservationDto>> Projection => r => new ReservationDto(
        r.Id,
        r.DealerId,
        r.Dealer.ShopName,
        r.ProductId,
        r.Product.Name,
        r.Product.Unit,
        r.TotalQuantity,
        (int)Math.Ceiling(r.TotalQuantity / r.Product.PackSize),
        r.Status,
        r.CreatedAt,
        r.ExpiresAt,
        r.ResolvedAt,
        r.AgentRunId,
        r.Note,
        r.Lines
            .OrderBy(l => l.Batch.ExpiryDate)
            .Select(l => new ReservationLineDto(l.BatchId, l.Batch.BatchNo, l.Batch.ExpiryDate, l.Quantity))
            .ToList());

    private static string Num(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
