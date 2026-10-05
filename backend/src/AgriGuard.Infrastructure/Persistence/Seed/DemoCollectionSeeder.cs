using AgriGuard.Domain.Harvest;
using AgriGuard.Infrastructure.Harvest;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Persistence.Seed;

/// <summary>
/// Collection centres in the two demo districts, each with three morning slots a day for the next
/// four weeks, so harvest bookings can be demonstrated. After that, CollectionSlotScheduler keeps
/// the standard slots open ahead, and planners open extra ones through POST /api/collection-slots. Idempotent, and only runs where Seed:DemoUsers is true.
///
/// ⚠ Academic sample data: names and coordinates approximate real economic centres.
/// </summary>
public static class DemoCollectionSeeder
{
    public const int Days = 28;

    public static async Task SeedAsync(AgriGuardDbContext db, TimeProvider timeProvider, CancellationToken ct = default)
    {
        await SeedCentresAsync(db, timeProvider, ct);
        await SeedHarvestReadyCropAsync(db, timeProvider, ct);
    }

    /// <summary>
    /// A crop about to be harvested, so the harvest-window and booking walkthrough has real
    /// forecast days and open slots to work with: Big Onion on the Dry Zone Farm, maturing in 4 days.
    /// (Plot A-01's tomato is the flowering crop the treatment walkthrough uses.)
    /// </summary>
    private static async Task SeedHarvestReadyCropAsync(AgriGuardDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Name == "Dry Zone Farm", ct);
        var onion = await db.Crops.FirstOrDefaultAsync(c => c.Code == "ONI", ct);
        if (farm is null || onion is null || await db.Plots.AnyAsync(p => p.FarmId == farm.Id && p.PlotCode == "A-02", ct))
            return;

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var sown = today.AddDays(-(onion.MaturityDays - 4));
        var plot = new Domain.Registry.Plot
        {
            FarmId = farm.Id,
            PlotCode = "A-02",
            Name = "Onion bed",
            AreaHectares = 0.5m,
            Latitude = 8.3530m,
            Longitude = 80.5010m,
            SoilType = Domain.Registry.SoilType.SandyLoam
        };
        db.Plots.Add(plot);
        db.CropCycles.Add(new Domain.Registry.CropCycle
        {
            Plot = plot,
            CropId = onion.Id,
            SownDate = sown,
            Stage = Domain.Registry.CropStage.PreHarvest,
            Status = Domain.Registry.CropCycleStatus.Active,
            ExpectedHarvestDate = sown.AddDays(onion.MaturityDays)
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCentresAsync(AgriGuardDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (await db.CollectionCentres.AnyAsync(ct))
            return;

        var districts = await db.Districts.ToDictionaryAsync(d => d.Code, ct);
        var centres = new List<CollectionCentre>();
        foreach (var (code, name, lat, lon, daily) in new (string, string, decimal, decimal, decimal)[]
        {
            ("NUW", "Nuwara Eliya Economic Centre", 6.9660m, 80.7700m, 4500m),
            ("NUW", "Welimada Collection Point", 6.9050m, 80.9130m, 3000m),
            ("ANU", "Anuradhapura Economic Centre", 8.3350m, 80.4100m, 4500m),
            ("ANU", "Mihintale Collection Point", 8.3590m, 80.5050m, 3000m)
        })
        {
            if (!districts.TryGetValue(code, out var district))
                continue;
            centres.Add(new CollectionCentre { Name = name, DistrictId = district.Id, Latitude = lat, Longitude = lon, DailyCapacityKg = daily });
        }
        db.CollectionCentres.AddRange(centres);

        await db.SaveChangesAsync(ct);

        // The same three morning slots a day the scheduler keeps open (CollectionSlotScheduler),
        // so a freshly seeded database is bookable before the scheduler's first run.
        var start = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        foreach (var centre in centres)
            for (var day = 0; day < Days; day++)
                db.CollectionSlots.AddRange(StandardSlots.ForDay(centre.Id, centre.DailyCapacityKg, start.AddDays(day)));
        await db.SaveChangesAsync(ct);
    }
}
