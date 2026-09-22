using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;

namespace AgriGuard.Infrastructure.Registry;

/// <summary>
/// Row-level access for registry data — the half of <c>OwnsFarm</c> that a policy cannot do.
///
/// The policy answers "may this ROLE call this endpoint"; this answers "WHICH rows may this
/// caller see", which needs the rows. Applied as a filter inside the query, never as a check
/// after loading: filtering afterwards would leak row counts through pagination and would let
/// a forgotten check return someone else's farm.
/// </summary>
internal static class RegistryScope
{
    /// <summary>
    /// Farmer → own farms. Agronomist → farms in their district. Administrator → all.
    /// Anyone else (dealer, no role) → nothing; the endpoint policy already refuses them,
    /// this is the second lock on the same door.
    /// </summary>
    public static IQueryable<Farm> ScopedTo(this IQueryable<Farm> farms, ICurrentUserAccessor user) => user.Role switch
    {
        UserRole.Farmer when user.UserId is { } farmerId => farms.Where(f => f.FarmerId == farmerId),
        UserRole.FieldAgronomist when user.DistrictId is { } districtId => farms.Where(f => f.DistrictId == districtId),
        UserRole.CoopAdministrator => farms,
        _ => farms.Where(_ => false)
    };

    public static IQueryable<Plot> ScopedTo(this IQueryable<Plot> plots, ICurrentUserAccessor user) => user.Role switch
    {
        UserRole.Farmer when user.UserId is { } farmerId => plots.Where(p => p.Farm.FarmerId == farmerId),
        UserRole.FieldAgronomist when user.DistrictId is { } districtId => plots.Where(p => p.Farm.DistrictId == districtId),
        UserRole.CoopAdministrator => plots,
        _ => plots.Where(_ => false)
    };

    public static IQueryable<CropCycle> ScopedTo(this IQueryable<CropCycle> cycles, ICurrentUserAccessor user) => user.Role switch
    {
        UserRole.Farmer when user.UserId is { } farmerId => cycles.Where(c => c.Plot.Farm.FarmerId == farmerId),
        UserRole.FieldAgronomist when user.DistrictId is { } districtId => cycles.Where(c => c.Plot.Farm.DistrictId == districtId),
        UserRole.CoopAdministrator => cycles,
        _ => cycles.Where(_ => false)
    };

    /// <summary>
    /// Whether the caller may write to a farm they can see. Agronomists advise and read; they do
    /// not edit someone's registry. Administrators may correct anything.
    /// </summary>
    public static bool CanWriteFarm(this ICurrentUserAccessor user, Farm farm) => user.Role switch
    {
        UserRole.Farmer => farm.FarmerId == user.UserId,
        UserRole.CoopAdministrator => true,
        _ => false
    };

    /// <summary>
    /// Distinguishes "does not exist" (404) from "exists but not yours" (403), which §12's test
    /// matrix requires. It does reveal that an id exists — acceptable here because ids are
    /// UUIDv7 and cannot be enumerated, and because a farmer who is told "not found" for their
    /// own farm after a district transfer would file a support ticket instead of understanding.
    /// </summary>
    public static void EnsureVisible<T>(T? scoped, bool existsUnscoped, string resource, Guid id)
    {
        if (scoped is not null) return;
        if (existsUnscoped) throw new ForbiddenAccessException($"This {resource.ToLowerInvariant()} belongs to another farm.");
        throw new NotFoundException(resource, id);
    }
}
