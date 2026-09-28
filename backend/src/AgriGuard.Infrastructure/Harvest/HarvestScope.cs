using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Harvest;

/// <summary>Row access and chemical-safety facts shared by the harvest and collection services.</summary>
internal static class HarvestScope
{
    /// <summary>The same rule as the registry: farmer → own, agronomist → district, administrator → all.</summary>
    public static IQueryable<HarvestForecast> ScopedTo(this IQueryable<HarvestForecast> forecasts, ICurrentUserAccessor user) => user.Role switch
    {
        UserRole.Farmer when user.UserId is { } farmerId => forecasts.Where(f => f.CropCycle.Plot.Farm.FarmerId == farmerId),
        UserRole.FieldAgronomist when user.DistrictId is { } districtId => forecasts.Where(f => f.CropCycle.Plot.Farm.DistrictId == districtId),
        UserRole.CoopAdministrator => forecasts,
        _ => forecasts.Where(_ => false)
    };

    public static IQueryable<CollectionBooking> ScopedTo(this IQueryable<CollectionBooking> bookings, ICurrentUserAccessor user) => user.Role switch
    {
        UserRole.Farmer when user.UserId is { } farmerId => bookings.Where(b => b.FarmerId == farmerId),
        UserRole.FieldAgronomist when user.DistrictId is { } districtId => bookings.Where(b => b.CropCycle.Plot.Farm.DistrictId == districtId),
        UserRole.CoopAdministrator => bookings,
        _ => bookings.Where(_ => false)
    };

    /// <summary>
    /// Every spray on the cycle that has not been cancelled, with the pre-harvest interval the rules
    /// table sets for that product on this crop today. The latest to clear decides when harvest is safe.
    /// </summary>
    public static async Task<List<PhiConstraint>> SpraysAsync(AgriGuardDbContext db, Guid cropCycleId, Guid cropId, CancellationToken ct)
    {
        var rows = await db.ChemicalApplications.AsNoTracking()
            .Where(a => a.CropCycleId == cropCycleId && a.Status != ApplicationStatus.Cancelled)
            .Select(a => new
            {
                a.Product.Name,
                a.ApplicationDate,
                Phi = db.ProductCropApprovals
                    .Where(r => r.ProductId == a.ProductId && r.CropId == cropId)
                    .Select(r => (int?)r.PreHarvestIntervalDays)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        // A spray whose rule was deleted has no interval to count; the validator would have refused it (V2).
        return [.. rows.Where(r => r.Phi is not null).Select(r => new PhiConstraint(r.Name, r.ApplicationDate, r.Phi!.Value))];
    }
}
