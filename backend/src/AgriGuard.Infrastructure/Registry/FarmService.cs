using System.Linq.Expressions;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Registry;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Registry;

public sealed class FarmService(AgriGuardDbContext db, ICurrentUserAccessor currentUser) : IFarmService
{
    private static readonly Dictionary<string, Expression<Func<Farm, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = f => f.Name,
        ["village"] = f => f.Village!,
        ["district"] = f => f.District.Name,
        ["createdAt"] = f => f.CreatedAt
    };

    public async Task<PagedResult<FarmDto>> ListAsync(FarmQuery query, CancellationToken ct = default)
    {
        var farms = db.Farms.AsNoTracking().ScopedTo(currentUser);

        if (query.DistrictId is { } districtId)
            farms = farms.Where(f => f.DistrictId == districtId);

        if (query.Search is { Length: > 0 } search)
        {
            // ILike: case-insensitive on PostgreSQL without pulling rows into memory.
            var pattern = $"%{search.Trim()}%";
            farms = farms.Where(f => EF.Functions.ILike(f.Name, pattern)
                                  || (f.Village != null && EF.Functions.ILike(f.Village, pattern)));
        }

        return await farms
            .OrderByAllowed(query, Sortable, f => f.Name, f => f.Id)
            .Select(Projection)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<FarmDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var farm = await db.Farms.AsNoTracking().ScopedTo(currentUser)
            .Where(f => f.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(ct);

        RegistryScope.EnsureVisible(farm, await db.Farms.AnyAsync(f => f.Id == id, ct), "Farm", id);
        return farm!;
    }

    public async Task<FarmDto> CreateAsync(CreateFarmRequest request, CancellationToken ct = default)
    {
        // A farmer always registers their own farm; only an administrator may name another owner.
        var farmerId = currentUser.Role == UserRole.CoopAdministrator
            ? request.FarmerId ?? throw new RequestValidationException(nameof(request.FarmerId), "Choose the farmer who owns this farm.")
            : currentUser.UserId ?? throw new ForbiddenAccessException();

        await EnsureDistrictExists(request.DistrictId, ct);

        if (currentUser.Role == UserRole.CoopAdministrator)
        {
            var farmerExists = await db.Users.AnyAsync(u => u.Id == farmerId && u.Role == UserRole.Farmer, ct);
            if (!farmerExists) throw new NotFoundException("Farmer", farmerId);
        }

        // One farmer cannot have two farms with the same name — a duplicate is almost always a
        // double-submitted form, and two "Home Field"s make every later plot ambiguous.
        var duplicate = await db.Farms.AnyAsync(f => f.FarmerId == farmerId && f.Name == request.Name.Trim(), ct);
        if (duplicate) throw new ConflictException($"You already have a farm called '{request.Name.Trim()}'.");

        var farm = new Farm
        {
            FarmerId = farmerId,
            Name = request.Name.Trim(),
            Village = request.Village?.Trim(),
            DistrictId = request.DistrictId
        };

        db.Farms.Add(farm);
        await db.SaveChangesAsync(ct);
        return await GetAsync(farm.Id, ct);
    }

    public async Task<FarmDto> UpdateAsync(Guid id, UpdateFarmRequest request, CancellationToken ct = default)
    {
        var farm = await LoadForWriteAsync(id, ct);
        await EnsureDistrictExists(request.DistrictId, ct);

        var name = request.Name.Trim();
        var duplicate = await db.Farms.AnyAsync(f => f.FarmerId == farm.FarmerId && f.Name == name && f.Id != id, ct);
        if (duplicate) throw new ConflictException($"You already have a farm called '{name}'.");

        farm.Name = name;
        farm.Village = request.Village?.Trim();
        farm.DistrictId = request.DistrictId;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var farm = await LoadForWriteAsync(id, ct);

        // Plots (and through them crop cycles, cases and prescriptions) are history, not clutter:
        // the FK is Restrict by design. Refuse with an explanation rather than cascading it away.
        var plotCount = await db.Plots.CountAsync(p => p.FarmId == id, ct);
        if (plotCount > 0)
            throw new ConflictException(
                $"This farm still has {plotCount} plot(s). Retire or move them before deleting the farm.");

        db.Farms.Remove(farm);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Farm> LoadForWriteAsync(Guid id, CancellationToken ct)
    {
        var farm = await db.Farms.ScopedTo(currentUser).FirstOrDefaultAsync(f => f.Id == id, ct);
        RegistryScope.EnsureVisible(farm, await db.Farms.AnyAsync(f => f.Id == id, ct), "Farm", id);

        if (!currentUser.CanWriteFarm(farm!))
            throw new ForbiddenAccessException("Only the farm's owner or a co-op administrator can change it.");

        return farm!;
    }

    private async Task EnsureDistrictExists(Guid districtId, CancellationToken ct)
    {
        if (!await db.Districts.AnyAsync(d => d.Id == districtId, ct))
            throw new NotFoundException("District", districtId);
    }

    /// <summary>
    /// Projected in SQL: plot count and total area are aggregates, not loaded collections,
    /// so listing 20 farms is one query rather than twenty-one.
    /// </summary>
    private static Expression<Func<Farm, FarmDto>> Projection => f => new FarmDto(
        f.Id,
        f.Name,
        f.Village,
        f.DistrictId,
        f.District.Name,
        f.FarmerId,
        f.Farmer.FullName,
        f.Plots.Count,
        f.Plots.Sum(p => (decimal?)p.AreaHectares) ?? 0m,
        f.CreatedAt);
}
