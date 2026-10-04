using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Inventory;

/// <summary>
/// Batches a caller has locked, the dealer they belong to, and the first-expiry-first-out plan for
/// drawing <see cref="Quantity"/> from them.
/// </summary>
public sealed record StockPick(
    Guid DealerId,
    Guid ProductId,
    decimal Quantity,
    IReadOnlyDictionary<Guid, InventoryBatch> Batches,
    IReadOnlyList<BatchDraw> Plan)
{
    /// <summary>The dearest batch drawn: what the whole order is priced at.</summary>
    public decimal HighestUnitPrice => Plan.Max(d => Batches[d.BatchId].UnitPrice);
}

/// <summary>
/// The one place dealer stock moves. The approval transaction and the reservation endpoints both go
/// through here, so there is one locking rule and one FEFO rule, not two copies that drift apart.
///
/// Every method expects the caller's serializable transaction to be open, and changes only tracked
/// entities: the caller saves and commits, so either everything it did lands or nothing does.
///
/// Four movements, all recorded as a <see cref="StockReservation"/> with one line per batch:
///   Draw    — off the shelf now          (QuantityOnHand −)              → Committed
///   Hold    — on the shelf, off sale     (QuantityReserved +)            → Held, until ExpiresAt
///   Commit  — a hold leaves the shelf    (QuantityOnHand −, Reserved −)  Held → Committed
///   Release — a hold goes back on sale   (QuantityReserved −)            Held → Released / Expired
/// </summary>
public sealed class StockLedger(AgriGuardDbContext db)
{
    /// <summary>
    /// Locks the in-date batches holding unreserved stock of the product — at the named dealer, or at
    /// every dealer in the district — then picks the best-stocked dealer and plans the draw FEFO.
    /// Returns null when no single dealer can supply <paramref name="quantity"/>: nothing is split
    /// across shops, and nothing is drawn partially.
    /// </summary>
    public async Task<StockPick?> PickAsync(
        Guid productId, decimal quantity, Guid? dealerId, Guid? districtId, DateOnly usableOn, CancellationToken ct)
    {
        var batches = await LockAvailableAsync(productId, dealerId, districtId, usableOn, ct);

        var source = batches
            .GroupBy(b => b.DealerId)
            .OrderByDescending(g => g.Sum(b => b.QuantityAvailable))
            .FirstOrDefault();
        if (source is null)
            return null;

        var plan = StockAllocation.PlanFefo(
            source.Select(b => new BatchStock(b.Id, b.BatchNo, b.ExpiryDate, b.QuantityAvailable)), quantity);

        return plan is null ? null : new StockPick(source.Key, productId, quantity, source.ToDictionary(b => b.Id), plan);
    }

    /// <summary>Takes the picked stock off the shelf at once, recorded as an already-committed reservation.</summary>
    public StockReservation Draw(StockPick pick, Guid? agentRunId, DateTime now)
    {
        foreach (var draw in pick.Plan)
            pick.Batches[draw.BatchId].QuantityOnHand -= draw.Quantity;

        return Record(pick, ReservationStatus.Committed, expiresAt: now, resolvedAt: now, agentRunId, note: null);
    }

    /// <summary>Keeps the picked stock on the shelf but off sale until <paramref name="expiresAt"/>.</summary>
    public StockReservation Hold(StockPick pick, Guid? agentRunId, DateTime expiresAt, string? note)
    {
        foreach (var draw in pick.Plan)
            pick.Batches[draw.BatchId].QuantityReserved += draw.Quantity;

        return Record(pick, ReservationStatus.Held, expiresAt, resolvedAt: null, agentRunId, note);
    }

    /// <summary>
    /// Held → Committed: exactly the held quantities leave exactly the held batches. Returns those
    /// batches, locked, for pricing.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, InventoryBatch>> CommitAsync(StockReservation reservation, DateTime now, CancellationToken ct)
    {
        var batches = await LockHeldBatchesAsync(reservation, ct);
        foreach (var line in reservation.Lines)
        {
            batches[line.BatchId].QuantityOnHand -= line.Quantity;
            batches[line.BatchId].QuantityReserved -= line.Quantity;
        }

        reservation.Status = ReservationStatus.Committed;
        reservation.ResolvedAt = now;
        return batches;
    }

    /// <summary>Held → Released (someone let it go) or Expired (the sweeper did): the stock is back on sale.</summary>
    public async Task ReleaseAsync(StockReservation reservation, ReservationStatus outcome, DateTime now, CancellationToken ct)
    {
        if (outcome is not (ReservationStatus.Released or ReservationStatus.Expired))
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A hold ends Released or Expired.");

        var batches = await LockHeldBatchesAsync(reservation, ct);
        foreach (var line in reservation.Lines)
            batches[line.BatchId].QuantityReserved -= line.Quantity;

        reservation.Status = outcome;
        reservation.ResolvedAt = now;
    }

    /// <summary>
    /// Loads a reservation and its lines with the reservation row locked, so a commit, a release and
    /// the expiry sweeper can never act on the same hold at once.
    /// </summary>
    public async Task<StockReservation?> LockReservationAsync(Guid id, CancellationToken ct)
    {
        var reservation = await db.StockReservations
            .FromSql($"SELECT r.*, r.xmin FROM stock_reservations r WHERE r.id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (reservation is not null)
            await db.StockReservationLines.Where(l => l.ReservationId == id).LoadAsync(ct);
        return reservation;
    }

    private StockReservation Record(StockPick pick, ReservationStatus status, DateTime expiresAt, DateTime? resolvedAt, Guid? agentRunId, string? note)
    {
        var reservation = new StockReservation
        {
            AgentRunId = agentRunId,
            DealerId = pick.DealerId,
            ProductId = pick.ProductId,
            TotalQuantity = pick.Quantity,
            Status = status,
            ExpiresAt = expiresAt,
            ResolvedAt = resolvedAt,
            Note = note
        };
        db.StockReservations.Add(reservation);
        foreach (var draw in pick.Plan)
            db.StockReservationLines.Add(new StockReservationLine { Reservation = reservation, BatchId = draw.BatchId, Quantity = draw.Quantity });
        return reservation;
    }

    private async Task<Dictionary<Guid, InventoryBatch>> LockHeldBatchesAsync(StockReservation reservation, CancellationToken ct)
    {
        if (reservation.Status != ReservationStatus.Held)
            throw new InvalidOperationException($"Reservation {reservation.Id} is {reservation.Status}, not Held.");

        var ids = reservation.Lines.Select(l => l.BatchId).ToArray();
        var batches = await db.InventoryBatches
            .FromSql($"SELECT b.*, b.xmin FROM inventory_batches b WHERE b.id = ANY({ids}) ORDER BY b.id FOR UPDATE OF b")
            .ToListAsync(ct);
        return batches.ToDictionary(b => b.Id);
    }

    /// <summary>
    /// Locks the candidate batch rows until the transaction ends, so no other hold or approval can
    /// take the same stock in between. Raw SQL because LINQ has no FOR UPDATE; xmin is selected
    /// because EF maps it as the row version and needs it to track the rows. Rows are locked in id
    /// order, so two transactions wanting overlapping batches queue instead of deadlocking.
    /// </summary>
    private Task<List<InventoryBatch>> LockAvailableAsync(Guid productId, Guid? dealerId, Guid? districtId, DateOnly usableOn, CancellationToken ct) =>
        dealerId is { } id
            ? db.InventoryBatches.FromSql($"""
                SELECT b.*, b.xmin FROM inventory_batches b
                WHERE b.product_id = {productId} AND b.dealer_id = {id}
                  AND b.expiry_date > {usableOn} AND b.quantity_on_hand - b.quantity_reserved > 0
                ORDER BY b.id
                FOR UPDATE OF b
                """).ToListAsync(ct)
            : db.InventoryBatches.FromSql($"""
                SELECT b.*, b.xmin FROM inventory_batches b
                JOIN dealers d ON d.id = b.dealer_id
                WHERE b.product_id = {productId} AND d.district_id = {districtId ?? Guid.Empty}
                  AND b.expiry_date > {usableOn} AND b.quantity_on_hand - b.quantity_reserved > 0
                ORDER BY b.id
                FOR UPDATE OF b
                """).ToListAsync(ct);
}
