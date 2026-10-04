using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Inventory;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    /// <summary>How often expired holds are looked for. A hold lasts 24 h, so a minute late is harmless.</summary>
    public TimeSpan ReservationSweepInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Releases holds whose 24 hours ran out (status Expired), putting their stock back on sale. Without
/// it a dealer who forgot a hold, or a phone order never collected, would keep stock off the shelf
/// for good.
///
/// Each hold is released in its own serializable transaction through the same
/// <see cref="StockLedger.ReleaseAsync"/> a dealer's release uses, with the reservation row locked, so
/// the sweeper and a dealer committing at the last second can never both act: whichever locks the
/// row first wins, and the other sees it is no longer Held.
/// </summary>
public sealed class ReservationExpirySweeper(
    IServiceScopeFactory scopes,
    IOptions<InventoryOptions> options,
    TimeProvider timeProvider,
    ILogger<ReservationExpirySweeper> logger) : BackgroundService
{
    /// <summary>Holds released per sweep; the rest wait a minute. Keeps one sweep's work bounded.</summary>
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ReservationSweepInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed sweep (database briefly unreachable) must not stop the next one.
                logger.LogError(ex, "Reservation expiry sweep failed");
            }
        }
    }

    /// <summary>Releases every hold past its deadline. Returns how many it released.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgriGuardDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<StockLedger>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expired = await db.StockReservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Held && r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Select(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var released = 0;
        foreach (var id in expired)
        {
            try
            {
                var done = await SerializableTransaction.RunAsync(db, async () =>
                {
                    // Re-read under the lock: it may have been committed or released since the scan.
                    var reservation = await ledger.LockReservationAsync(id, ct);
                    if (reservation is not { Status: ReservationStatus.Held } || reservation.ExpiresAt > now)
                        return false;

                    await ledger.ReleaseAsync(reservation, ReservationStatus.Expired, now, ct);

                    // A proposal's hold lapsing is part of that run's story: the console shows it,
                    // and an approval after this draws the stock afresh.
                    if (reservation.AgentRunId is { } runId)
                        db.AgentRunEvents.Add(ProposalStockHolds.Released(runId, reservation, "the 24-hour hold expired before a decision", now));
                    return true;
                }, "Reservation changed during the sweep.", ct);

                if (done) released++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad hold must not block the rest; it is retried on the next sweep.
                logger.LogWarning(ex, "Could not release expired reservation {ReservationId}", id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        if (released > 0)
            logger.LogInformation("Released {Count} expired stock reservation(s)", released);
        return released;
    }
}
