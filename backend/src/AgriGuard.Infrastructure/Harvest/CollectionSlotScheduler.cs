using AgriGuard.Application.Common;
using AgriGuard.Domain.Harvest;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Harvest;

public sealed class CollectionOptions
{
    public const string SectionName = "Collection";

    /// <summary>
    /// Keep every active centre's standard slots open this far ahead. Matches the phone's date picker,
    /// which offers collection days up to 60 days away.
    /// </summary>
    public int SlotHorizonDays { get; set; } = 60;

    /// <summary>How often the horizon is topped up. Once a day would do; this catches a restart.</summary>
    public TimeSpan TopUpInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Off in the integration tests, which plan their own slots.</summary>
    public bool OpenSlotsAutomatically { get; set; } = true;
}

/// <summary>A centre's ordinary day: three two-hour morning slots sharing its daily capacity evenly.</summary>
public static class StandardSlots
{
    public static readonly (TimeOnly Start, TimeOnly End)[] Times =
    [
        (new TimeOnly(7, 0), new TimeOnly(9, 0)),
        (new TimeOnly(9, 0), new TimeOnly(11, 0)),
        (new TimeOnly(11, 0), new TimeOnly(13, 0))
    ];

    public static IEnumerable<CollectionSlot> ForDay(Guid centreId, decimal dailyCapacityKg, DateOnly date) =>
        Times.Select((t, i) => new CollectionSlot
        {
            CentreId = centreId,
            SlotDate = date,
            SlotIndex = i + 1,
            StartTime = t.Start,
            EndTime = t.End,
            CapacityKg = Math.Round(dailyCapacityKg / Times.Length, 2)
        });
}

/// <summary>
/// Keeps collection slots open ahead of time. Without it, slots exist only as far as someone last
/// opened them, and every harvest after that date is refused "no room" although the centres are
/// open. Each active centre gets the standard slots on every day in the horizon that has no slots
/// yet. A day a planner has already set up (any slot at all) is left exactly as they made it.
///
/// Repeat-safe: the unique (centre, date, index) index means two runs, or two API instances, can
/// never open the same slot twice; the loser's insert is simply dropped.
/// </summary>
public sealed class CollectionSlotScheduler(
    IServiceScopeFactory scopes,
    IOptions<CollectionOptions> options,
    TimeProvider timeProvider,
    ILogger<CollectionSlotScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.OpenSlotsAutomatically)
            return;

        // Straight away at start-up, so a fresh or long-idle database is bookable at once.
        await TopUpSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(options.Value.TopUpInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await TopUpSafelyAsync(stoppingToken);
    }

    private async Task TopUpSafelyAsync(CancellationToken ct)
    {
        try
        {
            await TopUpAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database briefly unreachable must not stop the next top-up.
            logger.LogError(ex, "Collection slot top-up failed");
        }
    }

    /// <summary>
    /// The standard slots for every (centre, day) from today to today + horizon that has no slot
    /// yet. Pure, so the rule is tested without a database.
    /// </summary>
    public static List<CollectionSlot> SlotsToOpen(
        IEnumerable<(Guid Id, decimal DailyCapacityKg)> centres,
        IReadOnlySet<(Guid CentreId, DateOnly Date)> planned,
        DateOnly today,
        int horizonDays)
    {
        var slots = new List<CollectionSlot>();
        var last = today.AddDays(horizonDays);
        foreach (var (id, dailyCapacityKg) in centres)
            for (var day = today; day <= last; day = day.AddDays(1))
                if (!planned.Contains((id, day)))
                    slots.AddRange(StandardSlots.ForDay(id, dailyCapacityKg, day));
        return slots;
    }

    /// <summary>Opens the standard slots on every day in the horizon that has none. Returns how many slots it opened.</summary>
    public async Task<int> TopUpAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgriGuardDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<FarmCalendar>().Today;
        var last = today.AddDays(options.Value.SlotHorizonDays);

        var centres = await db.CollectionCentres.AsNoTracking()
            .Where(c => c.IsActive && c.DailyCapacityKg > 0)
            .Select(c => new { c.Id, c.DailyCapacityKg })
            .ToListAsync(ct);

        var planned = (await db.CollectionSlots.AsNoTracking()
                .Where(s => s.SlotDate >= today && s.SlotDate <= last)
                .Select(s => new { s.CentreId, s.SlotDate })
                .Distinct()
                .ToListAsync(ct))
            .Select(s => (s.CentreId, s.SlotDate))
            .ToHashSet();

        var toOpen = SlotsToOpen(centres.Select(c => (c.Id, c.DailyCapacityKg)), planned, today, options.Value.SlotHorizonDays);
        db.CollectionSlots.AddRange(toOpen);
        var opened = toOpen.Count;

        if (opened == 0)
            return 0;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            // Another instance opened some of these days a moment ago; the next top-up fills any gap.
            logger.LogInformation("Collection slots were opened concurrently; this top-up was dropped");
            return 0;
        }

        logger.LogInformation("Opened {Count} collection slots across {Centres} centres up to {Last}", opened, centres.Count, last);
        return opened;
    }
}
