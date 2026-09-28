using AgriGuard.Application.Auth;
using AgriGuard.Application.Common;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Persistence.Seed;

/// <summary>
/// History for Component D's intelligence and reports (plan §6.2: "30 historical cases so the
/// outbreak signal has something to aggregate"): four neighbouring farmers, one per district, whose
/// past three weeks of cases were confirmed by an agronomist, and last season's harvests with the
/// forecast made for each.
///
/// The story it tells: late blight is rising on tomato and potato in the wet hill country (Nuwara
/// Eliya, Badulla), while the dry zone (Anuradhapura) sees sap-suckers and a virus instead.
///
/// The cases are kept current: they are dated relative to the day they were seeded, and on every
/// later start they are moved forward by whole days so the newest is from today. Otherwise a
/// 14-day signal seeded before the viva would be empty during it.
///
/// Idempotent, and only runs where Seed:DemoUsers is true. ⚠ Academic sample data.
/// </summary>
public static class DemoHistorySeeder
{
    public const string ReferencePrefix = "AG-DEMO-";

    private sealed record Neighbour(string District, string Email, string Name, string Village, decimal Latitude, decimal Longitude, string[] Crops);

    private static readonly Neighbour[] Neighbours =
    [
        new("NUW", "farmer.kandapola@agriguard.demo", "Ranjith Bandara", "Kandapola", 6.9870m, 80.8160m, ["TOM", "POT"]),
        new("ANU", "farmer.mihintale@agriguard.demo", "Kumari Dissanayake", "Mihintale", 8.3590m, 80.5150m, ["TOM", "ONI"]),
        new("MTL", "farmer.dambulla@agriguard.demo", "Asanka Herath", "Dambulla", 7.8600m, 80.6500m, ["TOM", "CHI"]),
        new("BAD", "farmer.welimada@agriguard.demo", "Nirosha Wijesinghe", "Welimada", 6.9050m, 80.9130m, ["POT", "CAB"])
    ];

    // (district, crop, pathogen or null for a report not yet confirmed, days ago, severity)
    private static readonly (string District, string Crop, string? Pathogen, int DaysAgo, CaseSeverity Severity)[] Cases =
    [
        // Nuwara Eliya: a late-blight surge on tomato, growing in the last week.
        ("NUW", "TOM", "LATE_BLIGHT", 1, CaseSeverity.High),
        ("NUW", "TOM", "LATE_BLIGHT", 2, CaseSeverity.High),
        ("NUW", "TOM", "LATE_BLIGHT", 2, CaseSeverity.Medium),
        ("NUW", "TOM", "LATE_BLIGHT", 3, CaseSeverity.Critical),
        ("NUW", "TOM", "LATE_BLIGHT", 5, CaseSeverity.Medium),
        ("NUW", "TOM", "LATE_BLIGHT", 9, CaseSeverity.Medium),
        ("NUW", "POT", "LATE_BLIGHT", 4, CaseSeverity.High),
        ("NUW", "POT", "LATE_BLIGHT", 8, CaseSeverity.Medium),
        ("NUW", "TOM", "EARLY_BLIGHT", 10, CaseSeverity.Low),
        ("NUW", "TOM", "EARLY_BLIGHT", 12, CaseSeverity.Medium),
        ("NUW", "TOM", "POWDERY_MILDEW", 16, CaseSeverity.Low),
        ("NUW", "TOM", null, 0, CaseSeverity.High),
        ("NUW", "TOM", null, 1, CaseSeverity.Medium),
        // Anuradhapura: dry weather, so insects and the virus they carry.
        ("ANU", "TOM", "WHITEFLY", 3, CaseSeverity.Medium),
        ("ANU", "TOM", "WHITEFLY", 11, CaseSeverity.Low),
        ("ANU", "TOM", "LEAF_CURL_VIRUS", 6, CaseSeverity.High),
        ("ANU", "TOM", "EARLY_BLIGHT", 7, CaseSeverity.Low),
        ("ANU", "ONI", "THRIPS", 4, CaseSeverity.Medium),
        ("ANU", "ONI", "THRIPS", 9, CaseSeverity.Medium),
        ("ANU", "TOM", null, 2, CaseSeverity.Low),
        // Matale.
        ("MTL", "TOM", "ANTHRACNOSE", 8, CaseSeverity.Medium),
        ("MTL", "CHI", "THRIPS", 2, CaseSeverity.Medium),
        ("MTL", "CHI", "THRIPS", 12, CaseSeverity.Low),
        ("MTL", "CHI", "ANTHRACNOSE", 18, CaseSeverity.Medium),
        // Badulla: potato late blight spreading from the hills.
        ("BAD", "POT", "LATE_BLIGHT", 1, CaseSeverity.High),
        ("BAD", "POT", "LATE_BLIGHT", 4, CaseSeverity.Medium),
        ("BAD", "POT", "LATE_BLIGHT", 11, CaseSeverity.Medium),
        ("BAD", "CAB", "DIAMONDBACK_MOTH", 6, CaseSeverity.Medium),
        ("BAD", "CAB", "DIAMONDBACK_MOTH", 15, CaseSeverity.Low),
        ("BAD", "POT", null, 0, CaseSeverity.Medium)
    ];

    // Last season: (district, crop, days ago harvested, forecast kg, actual kg, who forecast)
    private static readonly (string District, string Crop, int HarvestedDaysAgo, decimal ForecastKg, decimal ActualKg, ForecastSource Source)[] Harvests =
    [
        ("NUW", "TOM", 40, 2400m, 2150m, ForecastSource.Farmer),
        ("NUW", "POT", 55, 5200m, 5350m, ForecastSource.Agronomist),
        ("ANU", "TOM", 35, 1800m, 1980m, ForecastSource.Farmer),
        ("ANU", "ONI", 62, 4200m, 3600m, ForecastSource.Farmer),
        ("MTL", "TOM", 48, 2000m, 1940m, ForecastSource.Agronomist),
        ("MTL", "CHI", 70, 1500m, 1210m, ForecastSource.Farmer),
        ("BAD", "POT", 44, 6000m, 6150m, ForecastSource.Agronomist),
        ("BAD", "CAB", 30, 3500m, 3380m, ForecastSource.Agronomist)
    ];

    public static async Task SeedAsync(AgriGuardDbContext db, IPasswordHasher hasher, TimeProvider timeProvider, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Farm days, not UTC days: between midnight and 05:30 in Sri Lanka the UTC date is yesterday.
        var calendar = new FarmCalendar(timeProvider, TimeZoneInfo.FindSystemTimeZoneById(FarmCalendar.DefaultTimeZone));
        if (await db.CropCases.AnyAsync(c => c.ReferenceNo.StartsWith(ReferencePrefix), ct))
        {
            await KeepCurrentAsync(db, calendar, ct);
            await ForecastGrowingCropsAsync(db, ct);
            return;
        }

        var districts = await db.Districts.ToDictionaryAsync(d => d.Code, ct);
        var crops = await db.Crops.ToDictionaryAsync(c => c.Code, ct);
        var pathogens = await db.Pathogens.ToDictionaryAsync(p => p.Code, ct);
        if (Neighbours.Any(n => !districts.ContainsKey(n.District)) || await db.Users.AnyAsync(u => u.Email == Neighbours[0].Email, ct))
            return;

        var today = calendar.Today;
        var passwordHash = hasher.Hash(DemoUserSeeder.Password);
        var growing = new Dictionary<(string District, string Crop), (User Farmer, Plot Plot, CropCycle Cycle)>();
        var harvested = new List<(HarvestForecast Forecast, int DaysAgo)>();

        foreach (var n in Neighbours)
        {
            var district = districts[n.District];
            var farmer = new User { Email = n.Email, FullName = n.Name, Role = UserRole.Farmer, DistrictId = district.Id, PasswordHash = passwordHash };
            var farm = new Farm { Farmer = farmer, Name = $"{n.Village} Farm", Village = n.Village, DistrictId = district.Id };
            db.AddRange(farmer, farm);

            for (var i = 0; i < n.Crops.Length; i++)
            {
                var crop = crops[n.Crops[i]];
                var plot = new Plot
                {
                    Farm = farm,
                    PlotCode = $"H-{i + 1:00}",
                    Name = $"{crop.Name} field",
                    AreaHectares = 0.6m + 0.2m * i,
                    Latitude = n.Latitude + 0.004m * i,
                    Longitude = n.Longitude + 0.004m * i,
                    SoilType = SoilType.Loam
                };
                var sown = today.AddDays(-(crop.MaturityDays / 2));
                var cycle = new CropCycle
                {
                    Plot = plot,
                    CropId = crop.Id,
                    SownDate = sown,
                    Stage = CropStage.Vegetative,
                    Status = CropCycleStatus.Active,
                    ExpectedHarvestDate = sown.AddDays(crop.MaturityDays)
                };
                db.AddRange(plot, cycle);
                growing[(n.District, n.Crops[i])] = (farmer, plot, cycle);

                // The same field's previous crop, harvested, with the forecast made for it.
                foreach (var h in Harvests.Where(h => h.District == n.District && h.Crop == n.Crops[i]))
                {
                    var harvestedOn = today.AddDays(-h.HarvestedDaysAgo);
                    var past = new CropCycle
                    {
                        Plot = plot,
                        CropId = crop.Id,
                        SownDate = harvestedOn.AddDays(-crop.MaturityDays),
                        Stage = CropStage.Harvested,
                        Status = CropCycleStatus.Harvested,
                        ExpectedHarvestDate = harvestedOn,
                        ActualHarvestDate = harvestedOn
                    };
                    var forecast = new HarvestForecast
                    {
                        CropCycle = past,
                        ForecastHarvestDate = harvestedOn,
                        EstimatedYieldKg = h.ForecastKg,
                        ActualYieldKg = h.ActualKg,
                        Source = h.Source
                    };
                    db.AddRange(past, forecast);
                    harvested.Add((forecast, h.HarvestedDaysAgo + 14));
                }
            }
        }

        var reported = new List<(CropCase Case, int DaysAgo)>();
        for (var i = 0; i < Cases.Length; i++)
        {
            var (districtCode, cropCode, pathogenCode, daysAgo, severity) = Cases[i];
            var (farmer, plot, cycle) = growing[(districtCode, cropCode)];
            var pathogen = pathogenCode is null ? null : pathogens.GetValueOrDefault(pathogenCode);
            var c = new CropCase
            {
                ReferenceNo = $"{ReferencePrefix}{i + 1:0000}",
                Farmer = farmer,
                Plot = plot,
                CropCycle = cycle,
                DistrictId = districts[districtCode].Id,
                // Historical cases are resolved; the unconfirmed recent ones are still in the queue.
                Status = pathogen is null ? CaseStatus.Submitted : CaseStatus.Closed,
                Severity = severity,
                SymptomCodes = pathogen is null ? ["leaf_brown_spots"] : [.. pathogen.IndicativeSymptoms.Take(2)],
                FarmerNote = "Seeded history (academic sample data).",
                ReportedLatitude = plot.Latitude + 0.001m * (i % 5),
                ReportedLongitude = plot.Longitude - 0.001m * (i % 3),
                ConfirmedPathogen = pathogen
            };
            db.CropCases.Add(c);
            reported.Add((c, daysAgo));
        }

        await db.SaveChangesAsync(ct);

        // The audit stamp says "now"; history needs its own dates. Mid-morning in Sri Lanka (09:30 = 04:00 UTC).
        var morning = today.ToDateTime(new TimeOnly(4, 0), DateTimeKind.Utc);
        foreach (var (c, daysAgo) in reported)
        {
            // Never in the future: seeded before 09:30 local, today's cases are from "now".
            var at = Min(morning.AddDays(-daysAgo), now);
            await db.CropCases.Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, at).SetProperty(x => x.UpdatedAt, at), ct);
        }
        foreach (var (f, daysAgo) in harvested)
        {
            var at = morning.AddDays(-daysAgo);
            await db.HarvestForecasts.Where(x => x.Id == f.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, at), ct);
        }

        await ForecastGrowingCropsAsync(db, ct);
    }

    /// <summary>Rough yields for the forecasts, in kg per hectare. ⚠ Academic sample data.</summary>
    private static readonly Dictionary<string, decimal> YieldPerHectare = new()
    {
        ["TOM"] = 15_000m,
        ["POT"] = 18_000m,
        ["ONI"] = 16_000m,
        ["CHI"] = 8_000m,
        ["CAB"] = 25_000m
    };

    /// <summary>
    /// A farmer's forecast for each neighbour's crop still in the ground, so "coming harvests" has
    /// rows. Adds only what is missing, so it also fills a database seeded before it existed.
    /// </summary>
    private static async Task ForecastGrowingCropsAsync(AgriGuardDbContext db, CancellationToken ct)
    {
        var emails = Neighbours.Select(n => n.Email).ToArray();
        var cycles = await db.CropCycles
            .Where(c => c.Status == CropCycleStatus.Active && emails.Contains(c.Plot.Farm.Farmer.Email)
                        && !db.HarvestForecasts.Any(f => f.CropCycleId == c.Id))
            .Select(c => new { c.Id, c.ExpectedHarvestDate, c.Plot.AreaHectares, CropCode = c.Crop.Code })
            .ToListAsync(ct);
        if (cycles.Count == 0)
            return;

        db.HarvestForecasts.AddRange(cycles.Select(c => new HarvestForecast
        {
            CropCycleId = c.Id,
            ForecastHarvestDate = c.ExpectedHarvestDate,
            EstimatedYieldKg = Math.Round(c.AreaHectares * YieldPerHectare.GetValueOrDefault(c.CropCode, 10_000m) / 10) * 10,
            Source = ForecastSource.Farmer,
            Notes = "Seeded forecast (academic sample data)."
        }));
        await db.SaveChangesAsync(ct);
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>Moves the demo cases forward by whole days, so the newest is from today again.</summary>
    private static async Task KeepCurrentAsync(AgriGuardDbContext db, FarmCalendar calendar, CancellationToken ct)
    {
        var newest = await db.CropCases.Where(c => c.ReferenceNo.StartsWith(ReferencePrefix)).MaxAsync(c => c.CreatedAt, ct);
        var days = calendar.Today.DayNumber - calendar.DateOf(newest).DayNumber;
        if (days < 1)
            return;

        await db.CropCases
            .Where(c => c.ReferenceNo.StartsWith(ReferencePrefix))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.CreatedAt, c => c.CreatedAt.AddDays(days))
                .SetProperty(c => c.UpdatedAt, c => c.UpdatedAt.AddDays(days)), ct);
    }
}
