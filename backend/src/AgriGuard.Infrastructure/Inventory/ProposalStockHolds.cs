using System.Globalization;
using System.Text.Json;
using AgriGuard.Application.Inventory;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Agent;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Inventory;

/// <summary>
/// The stock hold under a proposal that waits for an agronomist (§9.5, §11 step 5).
///
///   agent reports a valid proposal → <see cref="HoldAsync"/>: the packs are Held for 24 h
///   agronomist approves             → the approval transaction commits that hold (<see cref="LockHeldAsync"/>)
///   agronomist rejects or revises   → <see cref="ReleaseAsync"/>: back on sale
///   nobody decides within 24 h      → the expiry sweeper releases it; approval then draws afresh
///
/// Every method runs inside the caller's serializable transaction and goes through
/// <see cref="StockLedger"/>, the same locking and FEFO code a dealer's own holds use.
/// </summary>
public sealed class ProposalStockHolds(AgriGuardDbContext db, StockLedger ledger)
{
    /// <summary>
    /// Holds the whole packs the proposal needs, at the dealer the validator judged (the named one,
    /// or the best-stocked in the farm's district). Returns null when held, or why it could not be:
    /// the stock V9 saw a moment ago was taken by another transaction in between.
    /// </summary>
    public async Task<string?> HoldAsync(AgentRun run, JsonElement proposal, DateTime now, CancellationToken ct)
    {
        // A revised proposal replaces the previous one. Revise already released its hold; this is a
        // backstop. Flushed at once (still inside the transaction) so the pick below sees that stock.
        if (await ReleaseAsync(run.Id, "replaced by a new proposal", now, ct))
            await db.SaveChangesAsync(ct);

        // Only called after the backend's own check passed, so V1 guarantees these fields are well-formed.
        var input = AgentPrescriptionGate.FromStoredProposal(proposal, run.Id, run.Case.CropCycleId);
        var productId = Guid.Parse(input.ProductId!);
        var sprayDate = DateOnly.ParseExact(input.SprayDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Guid? namedDealer = Guid.TryParse(input.DealerId, out var dealer) ? dealer : null;

        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Name, p.PackSize })
            .FirstAsync(ct);
        var packs = StockAllocation.PacksFor(input.TotalQuantity!.Value, product.PackSize);
        var quantity = packs * product.PackSize;

        var pick = await ledger.PickAsync(productId, quantity, namedDealer, run.Case.DistrictId, sprayDate, ct);
        if (pick is null)
            return $"The {packs} pack(s) of {product.Name} the check found were taken by another order before they could be held. Run the agent again.";

        var reservation = ledger.Hold(pick, run.Id, now + ReservationLimits.HoldFor, note: null);
        db.AgentRunEvents.Add(new AgentRunEvent
        {
            AgentRunId = run.Id,
            EventType = AgentEventType.StockHeld,
            PayloadJson = AgentPayloads.ToStorable(new
            {
                reservationId = reservation.Id,
                dealerId = pick.DealerId,
                packs,
                quantity,
                expiresAt = reservation.ExpiresAt,
                batches = pick.Plan.Select(d => pick.Batches[d.BatchId].BatchNo)
            }),
            OccurredAt = now
        });
        return null;
    }

    /// <summary>The run's live hold with its row locked, or null when it has none (never held, or already released or expired).</summary>
    public async Task<StockReservation?> LockHeldAsync(Guid runId, CancellationToken ct)
    {
        var id = await db.StockReservations
            .Where(r => r.AgentRunId == runId && r.Status == ReservationStatus.Held)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (id is null)
            return null;

        // Re-checked under the lock: the sweeper may have released it since the lookup.
        return await ledger.LockReservationAsync(id.Value, ct) is { Status: ReservationStatus.Held } held ? held : null;
    }

    /// <summary>Puts the run's held stock back on sale (reject, revise, or a replaced proposal). False when nothing was held.</summary>
    public async Task<bool> ReleaseAsync(Guid runId, string reason, DateTime now, CancellationToken ct)
    {
        if (await LockHeldAsync(runId, ct) is not { } held)
            return false;

        await ledger.ReleaseAsync(held, ReservationStatus.Released, now, ct);
        db.AgentRunEvents.Add(Released(runId, held, reason, now));
        return true;
    }

    /// <summary>The timeline entry for a hold that ended without being committed.</summary>
    public static AgentRunEvent Released(Guid runId, StockReservation hold, string reason, DateTime now) => new()
    {
        AgentRunId = runId,
        EventType = AgentEventType.StockReleased,
        PayloadJson = AgentPayloads.ToStorable(new { reservationId = hold.Id, quantity = hold.TotalQuantity, reason }),
        OccurredAt = now
    };
}
