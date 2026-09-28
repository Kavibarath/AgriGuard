using AgriGuard.Application.Auth;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Domain.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Persistence.Seed;

/// <summary>
/// A second demo district in Sri Lanka's dry zone, so the §11 walkthrough can reach approval while
/// the hill country is in its wet season.
///
/// Since Open-Meteo drives rule V8, a real forecast can block spraying: in late September Nuwara
/// Eliya had one sprayable morning in fourteen. That is the system working, and it is worth showing
/// (V8 refusing a rainy spray). But the happy path needs a farm where a dry morning is likely, so
/// the demo farmer also farms tomato near Anuradhapura, with a dealer and an agronomist of that
/// district (an agronomist decides only for their own district).
///
/// Idempotent, and only runs where Seed:DemoUsers is true. ⚠ Academic sample data; the passwords
/// are the public demo ones.
/// </summary>
public static class DemoDryZoneSeeder
{
    public const string AgronomistEmail = "agronomist.anu@agriguard.demo";
    public const string DealerEmail = "dealer.anu@agriguard.demo";

    public static async Task SeedAsync(AgriGuardDbContext db, IPasswordHasher hasher, TimeProvider timeProvider, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.Email == DealerEmail, ct))
            return;

        var district = await db.Districts.FirstOrDefaultAsync(d => d.Code == "ANU", ct);
        var farmer = await db.Users.FirstOrDefaultAsync(u => u.Email == "farmer@agriguard.demo" && u.Role == UserRole.Farmer, ct);
        var tomato = await db.Crops.FirstOrDefaultAsync(c => c.Code == "TOM", ct);
        if (district is null || farmer is null || tomato is null)
            return;

        var passwordHash = hasher.Hash(DemoUserSeeder.Password);
        var agronomist = new User
        {
            Email = AgronomistEmail,
            FullName = "Mr. Chaminda Rathnayake",
            Role = UserRole.FieldAgronomist,
            DistrictId = district.Id,
            PasswordHash = passwordHash
        };
        var dealerUser = new User
        {
            Email = DealerEmail,
            FullName = "Rajarata Agro Centre",
            Role = UserRole.AgroDealer,
            DistrictId = district.Id,
            PasswordHash = passwordHash
        };
        db.Users.AddRange(agronomist, dealerUser);

        var dealer = new Dealer
        {
            User = dealerUser,
            ShopName = "Rajarata Agro Centre — Anuradhapura",
            Address = "45 Maithripala Senanayake Mawatha, Anuradhapura",
            DistrictId = district.Id,
            Latitude = 8.3114m,
            Longitude = 80.4037m
        };
        db.Dealers.Add(dealer);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var products = await db.Products.ToDictionaryAsync(p => p.Name, ct);
        foreach (var (name, batch, quantity, months) in new (string, string, decimal, int)[]
        {
            ("Mancozeb 80 WP", "MZ-A2601", 30m, 14),
            ("Chlorothalonil 75 WP", "CT-A2603", 15m, 18),
            ("Metalaxyl 25 WP", "MX-A2602", 8m, 12),
            ("Azoxystrobin 25 SC", "AZ-A2601", 4m, 16),
            ("Copper Hydroxide 77 WP", "CU-A2604", 12m, 20)
        })
        {
            if (products.TryGetValue(name, out var product))
                db.InventoryBatches.Add(new InventoryBatch
                {
                    Dealer = dealer,
                    ProductId = product.Id,
                    BatchNo = batch,
                    ExpiryDate = today.AddMonths(months),
                    QuantityOnHand = quantity,
                    UnitPrice = product.UnitPrice
                });
        }

        // The demo farmer's second farm: 0.8 ha of flowering tomato, harvest in about 50 days,
        // the same shape as the §11 plot so every calculation in the walkthrough carries over.
        var farm = new Farm { FarmerId = farmer.Id, Name = "Dry Zone Farm", Village = "Mihintale", DistrictId = district.Id };
        var plot = new Plot
        {
            Farm = farm,
            PlotCode = "A-01",
            Name = "Tank field",
            AreaHectares = 0.8m,
            Latitude = 8.3510m,
            Longitude = 80.5040m,
            SoilType = SoilType.SandyLoam
        };
        var sown = today.AddDays(-60);
        db.AddRange(farm, plot, new CropCycle
        {
            Plot = plot,
            CropId = tomato.Id,
            SownDate = sown,
            Stage = CropStage.Flowering,
            Status = CropCycleStatus.Active,
            ExpectedHarvestDate = sown.AddDays(tomato.MaturityDays)
        });

        await db.SaveChangesAsync(ct);
    }
}
