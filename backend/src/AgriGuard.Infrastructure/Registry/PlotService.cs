using System.Linq.Expressions;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Registry;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Registry;

public sealed class PlotService(AgriGuardDbContext db, ICurrentUserAccessor currentUser) : IPlotService
{
    private static readonly Dictionary<string, Expression<Func<Plot, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["plotCode"] = p => p.PlotCode,
        ["name"] = p => p.Name!,
        ["area"] = p => p.AreaHectares,
        ["status"] = p => p.Status,
        ["createdAt"] = p => p.CreatedAt
    };

    public async Task<PagedResult<PlotDto>> ListAsync(PlotQuery query, CancellationToken ct = default)
    {
        var plots = db.Plots.AsNoTracking().ScopedTo(currentUser);

        if (query.FarmId is { } farmId) plots = plots.Where(p => p.FarmId == farmId);
        if (query.Status is { } status) plots = plots.Where(p => p.Status == status);

        // "Growing tomato right now" — filters on the active cycle, not on history.
        if (query.CropId is { } cropId)
            plots = plots.Where(p => p.CropCycles.Any(c => c.CropId == cropId && c.Status == CropCycleStatus.Active));

        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{search.Trim()}%";
            plots = plots.Where(p => EF.Functions.ILike(p.PlotCode, pattern)
                                  || (p.Name != null && EF.Functions.ILike(p.Name, pattern)));
        }

        return await plots
            .OrderByAllowed(query, Sortable, p => p.PlotCode, p => p.Id)
            .Select(Projection)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<PlotDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var plot = await db.Plots.AsNoTracking().ScopedTo(currentUser)
            .Where(p => p.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(ct);

        RegistryScope.EnsureVisible(plot, await db.Plots.AnyAsync(p => p.Id == id, ct), "Plot", id);
        return plot!;
    }

    public async Task<PlotDto> CreateAsync(CreatePlotRequest request, CancellationToken ct = default)
    {
        await EnsureCanWriteFarmAsync(request.FarmId, ct);

        var code = request.PlotCode.Trim();
        // Matches the unique index (FarmId, PlotCode): caught here to answer with a readable
        // 409 instead of letting PostgreSQL raise a constraint violation as a 500.
        if (await db.Plots.AnyAsync(p => p.FarmId == request.FarmId && p.PlotCode == code, ct))
            throw new ConflictException($"Plot code '{code}' is already used on this farm.");

        var plot = new Plot
        {
            FarmId = request.FarmId,
            PlotCode = code,
            Name = request.Name?.Trim(),
            AreaHectares = request.AreaHectares,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            SoilType = request.SoilType
        };

        db.Plots.Add(plot);
        await db.SaveChangesAsync(ct);
        return await GetAsync(plot.Id, ct);
    }

    public async Task<PlotDto> UpdateAsync(Guid id, UpdatePlotRequest request, CancellationToken ct = default)
    {
        var plot = await db.Plots.ScopedTo(currentUser).FirstOrDefaultAsync(p => p.Id == id, ct);
        RegistryScope.EnsureVisible(plot, await db.Plots.AnyAsync(p => p.Id == id, ct), "Plot", id);
        await EnsureCanWriteFarmAsync(plot!.FarmId, ct);

        var code = request.PlotCode.Trim();
        if (await db.Plots.AnyAsync(p => p.FarmId == plot.FarmId && p.PlotCode == code && p.Id != id, ct))
            throw new ConflictException($"Plot code '{code}' is already used on this farm.");

        // Area drives every dose calculation (rule V4), so changing it mid-cycle would silently
        // invalidate an outstanding prescription's total quantity.
        if (request.AreaHectares != plot.AreaHectares
            && await db.CropCycles.AnyAsync(c => c.PlotId == id && c.Status == CropCycleStatus.Active, ct))
            throw new BusinessRuleException(
                "PLOT_AREA_LOCKED_DURING_CYCLE",
                "The plot area cannot change while a crop cycle is active, because treatment quantities are calculated from it. End the cycle first.");

        if (request.Status == PlotStatus.Retired
            && await db.CropCycles.AnyAsync(c => c.PlotId == id && c.Status == CropCycleStatus.Active, ct))
            throw new BusinessRuleException(
                "PLOT_HAS_ACTIVE_CYCLE",
                "Harvest or abandon the active crop cycle before retiring this plot.");

        plot.PlotCode = code;
        plot.Name = request.Name?.Trim();
        plot.AreaHectares = request.AreaHectares;
        plot.Latitude = request.Latitude;
        plot.Longitude = request.Longitude;
        plot.SoilType = request.SoilType;
        plot.Status = request.Status;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task EnsureCanWriteFarmAsync(Guid farmId, CancellationToken ct)
    {
        var farm = await db.Farms.ScopedTo(currentUser).FirstOrDefaultAsync(f => f.Id == farmId, ct);
        RegistryScope.EnsureVisible(farm, await db.Farms.AnyAsync(f => f.Id == farmId, ct), "Farm", farmId);

        if (!currentUser.CanWriteFarm(farm!))
            throw new ForbiddenAccessException("Only the farm's owner or a co-op administrator can change its plots.");
    }

    private static Expression<Func<Plot, PlotDto>> Projection => p => new PlotDto(
        p.Id,
        p.FarmId,
        p.Farm.Name,
        p.PlotCode,
        p.Name,
        p.AreaHectares,
        p.Latitude,
        p.Longitude,
        p.SoilType,
        p.Status,
        // At most one active cycle per plot (enforced by a filtered unique index).
        p.CropCycles
            .Where(c => c.Status == CropCycleStatus.Active)
            .Select(c => new CropCycleSummaryDto(
                c.Id, c.CropId, c.Crop.Name, c.SownDate, c.Stage, c.Status, c.ExpectedHarvestDate, c.PlannedHarvestDate))
            .FirstOrDefault());
}
