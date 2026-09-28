using System.Linq.Expressions;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Harvest;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Harvest;

/// <summary>
/// Expected harvests (date and yield), recorded by the farmer or their agronomist, and the actual
/// yield once harvested — the data the forecast-vs-actual report compares.
/// </summary>
public sealed class HarvestForecastService(AgriGuardDbContext db, ICurrentUserAccessor currentUser) : IHarvestForecastService
{
    private static readonly Dictionary<string, Expression<Func<HarvestForecast, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["forecastHarvestDate"] = f => f.ForecastHarvestDate,
        ["estimatedYieldKg"] = f => f.EstimatedYieldKg,
        ["createdAt"] = f => f.CreatedAt
    };

    public async Task<PagedResult<HarvestForecastDto>> ListAsync(HarvestForecastQuery query, CancellationToken ct = default)
    {
        var forecasts = db.HarvestForecasts.AsNoTracking().ScopedTo(currentUser);
        if (query.CropCycleId is { } cycleId)
            forecasts = forecasts.Where(f => f.CropCycleId == cycleId);
        if (query.DistrictId is { } districtId)
            forecasts = forecasts.Where(f => f.CropCycle.Plot.Farm.DistrictId == districtId);
        if (query.From is { } from)
            forecasts = forecasts.Where(f => f.ForecastHarvestDate >= from);
        if (query.To is { } to)
            forecasts = forecasts.Where(f => f.ForecastHarvestDate <= to);

        return await forecasts
            .OrderByAllowed(query, Sortable, f => f.ForecastHarvestDate, f => f.Id)
            .Select(Projection)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<HarvestForecastDto> CreateAsync(CreateHarvestForecastRequest request, CancellationToken ct = default)
    {
        var cycle = await db.CropCycles.AsNoTracking().ScopedTo(currentUser)
            .Where(c => c.Id == request.CropCycleId)
            .Select(c => new { c.SownDate, c.Status })
            .FirstOrDefaultAsync(ct);
        RegistryScope.EnsureVisible(cycle, await db.CropCycles.AnyAsync(c => c.Id == request.CropCycleId, ct), "Crop cycle", request.CropCycleId);

        if (cycle!.Status != CropCycleStatus.Active)
            throw new BusinessRuleException("CYCLE_NOT_ACTIVE", "Forecasts are for crops still in the ground.");
        if (request.ForecastHarvestDate <= cycle.SownDate)
            throw new RequestValidationException(nameof(request.ForecastHarvestDate), "The harvest date must be after sowing.");

        var forecast = new HarvestForecast
        {
            CropCycleId = request.CropCycleId,
            ForecastHarvestDate = request.ForecastHarvestDate,
            EstimatedYieldKg = request.EstimatedYieldKg,
            // Who made the estimate matters when judging its accuracy later.
            Source = currentUser.Role == UserRole.Farmer ? ForecastSource.Farmer : ForecastSource.Agronomist,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        db.HarvestForecasts.Add(forecast);
        await db.SaveChangesAsync(ct);
        return await GetAsync(forecast.Id, ct);
    }

    public async Task<HarvestForecastDto> RecordActualAsync(Guid id, RecordActualYieldRequest request, CancellationToken ct = default)
    {
        var forecast = await db.HarvestForecasts.ScopedTo(currentUser).FirstOrDefaultAsync(f => f.Id == id, ct);
        RegistryScope.EnsureVisible(forecast, await db.HarvestForecasts.AnyAsync(f => f.Id == id, ct), "Harvest forecast", id);

        forecast!.ActualYieldKg = request.ActualYieldKg;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private Task<HarvestForecastDto> GetAsync(Guid id, CancellationToken ct) =>
        db.HarvestForecasts.AsNoTracking().Where(f => f.Id == id).Select(Projection).FirstAsync(ct);

    private static Expression<Func<HarvestForecast, HarvestForecastDto>> Projection => f => new HarvestForecastDto(
        f.Id,
        f.CropCycleId,
        f.CropCycle.Plot.PlotCode,
        f.CropCycle.Crop.Name,
        f.CropCycle.Plot.Farm.Farmer.FullName,
        f.ForecastHarvestDate,
        f.EstimatedYieldKg,
        f.ActualYieldKg,
        f.Source,
        f.Notes,
        f.CreatedAt);
}
