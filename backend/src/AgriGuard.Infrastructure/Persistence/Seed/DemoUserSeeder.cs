using AgriGuard.Application.Auth;
using AgriGuard.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Persistence.Seed;

/// <summary>
/// One demo account per role, for the viva walkthrough and for logging in from React/Flutter
/// before any registration screen exists. Idempotent, and only runs where Seed:DemoUsers is true.
///
/// ⚠ These passwords are public knowledge (they are in this file and in the report). Never enable
/// this seeder on a database holding anything real.
/// </summary>
public static class DemoUserSeeder
{
    public const string Password = "AgriGuard!Demo1";

    public static async Task SeedAsync(AgriGuardDbContext db, IPasswordHasher hasher, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
            return;

        // Nuwara Eliya: the district used throughout the §11 end-to-end demo.
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Code == "NUW", ct);
        var passwordHash = hasher.Hash(Password);

        db.Users.AddRange(
            new User
            {
                Email = "farmer@agriguard.demo",
                FullName = "Sunil Perera",
                PhoneNumber = "+94771234567",
                Role = UserRole.Farmer,
                DistrictId = district?.Id,
                CreditLimit = 50_000m,
                PasswordHash = passwordHash
            },
            new User
            {
                Email = "agronomist@agriguard.demo",
                FullName = "Dr. Nimali Fernando",
                Role = UserRole.FieldAgronomist,
                DistrictId = district?.Id,
                PasswordHash = passwordHash
            },
            new User
            {
                Email = "dealer@agriguard.demo",
                FullName = "Kandy Agro Supplies",
                Role = UserRole.AgroDealer,
                DistrictId = district?.Id,
                PasswordHash = passwordHash
            },
            new User
            {
                Email = "admin@agriguard.demo",
                FullName = "Co-op Administrator",
                Role = UserRole.CoopAdministrator,
                PasswordHash = passwordHash
            });

        await db.SaveChangesAsync(ct);
    }
}
