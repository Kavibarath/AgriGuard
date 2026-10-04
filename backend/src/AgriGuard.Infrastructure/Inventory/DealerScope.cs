using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Inventory;

/// <summary>
/// Row-level access for Component C: a dealer sees and changes only their own shop's stock, holds
/// and orders. The policy has already limited the endpoint to the AgroDealer role; this limits the rows.
/// </summary>
internal static class DealerScope
{
    /// <summary>The signed-in dealer's shop, or null when this account has none yet.</summary>
    public static async Task<Guid?> OwnShopIdAsync(this AgriGuardDbContext db, ICurrentUserAccessor user, CancellationToken ct) =>
        user.UserId is { } userId
            ? await db.Dealers.Where(d => d.UserId == userId).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct)
            : null;

    public static async Task<Guid> RequireOwnShopAsync(this AgriGuardDbContext db, ICurrentUserAccessor user, CancellationToken ct) =>
        await db.OwnShopIdAsync(user, ct)
        ?? throw new BusinessRuleException("NO_SHOP", "No shop is registered for this account yet. Ask the co-op administrator to set one up.");

    /// <summary>
    /// A dealer may filter by their own shop only. Naming another shop is refused rather than quietly
    /// answered with an empty page, so a wrong link is noticed.
    /// </summary>
    public static void EnsureOwnShop(Guid? requested, Guid? ownShop)
    {
        if (requested is { } id && id != ownShop)
            throw new ForbiddenAccessException("You can only see your own shop.");
    }

    /// <summary>404 when the row does not exist, 403 when it belongs to another shop.</summary>
    public static void EnsureVisible<T>(T? scoped, bool existsUnscoped, string resource, Guid id)
    {
        if (scoped is not null) return;
        if (existsUnscoped) throw new ForbiddenAccessException($"This {resource.ToLowerInvariant()} belongs to another shop.");
        throw new NotFoundException(resource, id);
    }
}
