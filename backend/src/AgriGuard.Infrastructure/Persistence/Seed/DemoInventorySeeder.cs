using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Persistence.Seed;

/// <summary>
/// A shop and shelf stock for the demo dealer, so a demo run can pass the stock rule (V9) end to
/// end. Separate from <see cref="DemoUserSeeder"/> so databases seeded before it existed get stock
/// too. Idempotent, and only runs where Seed:DemoUsers is true.
///
/// ⚠ ACADEMIC SAMPLE DATA, like the rest of the seed.
/// </summary>
public static class DemoInventorySeeder
{
    public static async Task SeedAsync(AgriGuardDbContext db, TimeProvider timeProvider, CancellationToken ct = default)
    {
        if (await db.Dealers.AnyAsync(ct))
            return;

        var dealerUser = await db.Users.FirstOrDefaultAsync(u => u.Email == "dealer@agriguard.demo" && u.Role == UserRole.AgroDealer, ct);
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Code == "NUW", ct);
        if (dealerUser is null || district is null)
            return;

        var dealer = new Dealer
        {
            UserId = dealerUser.Id,
            ShopName = "Kandy Agro Supplies — Nuwara Eliya",
            Address = "12 Lawson Street, Nuwara Eliya",
            DistrictId = district.Id,
            Latitude = 6.9708m,
            Longitude = 80.7829m
        };
        db.Dealers.Add(dealer);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var products = await db.Products.ToDictionaryAsync(p => p.Name, ct);

        // (product, batch, quantity on hand, months to expiry). Covers the tomato blight and pest
        // products the demo case needs, plus one batch close to expiry to show the in-date check.
        var stock = new (string Product, string Batch, decimal Quantity, int Months)[]
        {
            ("Mancozeb 80 WP", "MZ-2601", 40m, 14),
            ("Mancozeb 80 WP", "MZ-2512", 3m, 0),
            ("Chlorothalonil 75 WP", "CT-2603", 20m, 18),
            ("Metalaxyl 25 WP", "MX-2602", 10m, 12),
            ("Copper Hydroxide 77 WP", "CU-2604", 15m, 20),
            ("Azoxystrobin 25 SC", "AZ-2601", 5m, 16),
            ("Difenoconazole 25 EC", "DF-2602", 5m, 15),
            ("Emamectin Benzoate 5 WG", "EM-2603", 2m, 12),
            ("Imidacloprid 17.8 SL", "IM-2601", 5m, 10)
        };

        foreach (var (name, batch, quantity, months) in stock)
        {
            if (!products.TryGetValue(name, out var product))
                continue;

            db.InventoryBatches.Add(new InventoryBatch
            {
                Dealer = dealer,
                ProductId = product.Id,
                BatchNo = batch,
                // Months = 0: expires in ten days, so it cannot fill a spray planned later than that.
                ExpiryDate = months == 0 ? today.AddDays(10) : today.AddMonths(months),
                QuantityOnHand = quantity,
                UnitPrice = product.UnitPrice
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
