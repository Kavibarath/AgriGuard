using AgriGuard.Application.Common.Models;
using AgriGuard.Domain.Registry;

namespace AgriGuard.Application.Registry;

// ── Responses ────────────────────────────────────────────────────────────────
// DTOs, never entities: entities carry navigation properties and audit columns that would
// either over-share or serialise into cycles.

public sealed record FarmDto(
    Guid Id,
    string Name,
    string? Village,
    Guid DistrictId,
    string DistrictName,
    Guid FarmerId,
    string FarmerName,
    int PlotCount,
    decimal TotalAreaHectares,
    DateTime CreatedAt);

public sealed record PlotDto(
    Guid Id,
    Guid FarmId,
    string FarmName,
    string PlotCode,
    string? Name,
    decimal AreaHectares,
    decimal Latitude,
    decimal Longitude,
    SoilType SoilType,
    PlotStatus Status,
    CropCycleSummaryDto? ActiveCycle);

public sealed record CropCycleSummaryDto(
    Guid Id,
    Guid CropId,
    string CropName,
    DateOnly SownDate,
    CropStage Stage,
    CropCycleStatus Status,
    DateOnly ExpectedHarvestDate,
    DateOnly? PlannedHarvestDate);

public sealed record CropCycleDto(
    Guid Id,
    Guid PlotId,
    string PlotCode,
    Guid CropId,
    string CropName,
    int CropMaturityDays,
    DateOnly SownDate,
    CropStage Stage,
    CropCycleStatus Status,
    DateOnly ExpectedHarvestDate,
    DateOnly? PlannedHarvestDate,
    DateOnly? ActualHarvestDate,
    // The date PHI is checked against: planned if set, otherwise expected.
    DateOnly EffectiveHarvestDate,
    int DaysToHarvest,
    // Stages this cycle may move to next — empty once harvested. Drives the UI's button.
    IReadOnlyList<CropStage> AllowedNextStages,
    IReadOnlyList<StageTransitionDto> Transitions);

public sealed record StageTransitionDto(
    CropStage FromStage,
    CropStage ToStage,
    DateTime TransitionedAt,
    string? Note);

// ── Requests ─────────────────────────────────────────────────────────────────

public sealed record CreateFarmRequest(
    string Name,
    string? Village,
    Guid DistrictId,
    // Owner. Ignored for farmers (always themselves); required for administrators.
    Guid? FarmerId);

public sealed record UpdateFarmRequest(string Name, string? Village, Guid DistrictId);

public sealed record CreatePlotRequest(
    Guid FarmId,
    string PlotCode,
    string? Name,
    decimal AreaHectares,
    decimal Latitude,
    decimal Longitude,
    SoilType SoilType);

public sealed record UpdatePlotRequest(
    string PlotCode,
    string? Name,
    decimal AreaHectares,
    decimal Latitude,
    decimal Longitude,
    SoilType SoilType,
    PlotStatus Status);

public sealed record CreateCropCycleRequest(
    Guid PlotId,
    Guid CropId,
    DateOnly SownDate,
    DateOnly? PlannedHarvestDate);

public sealed record AdvanceStageRequest(
    CropStage ToStage,
    // When the stage was reached. Defaults to today; cannot be in the future or before sowing.
    DateOnly? ReachedOn,
    string? Note);

// ── Query options ────────────────────────────────────────────────────────────

public sealed record FarmQuery : PageRequest
{
    public string? Search { get; init; }
    public Guid? DistrictId { get; init; }
}

public sealed record PlotQuery : PageRequest
{
    public Guid? FarmId { get; init; }
    public Guid? CropId { get; init; }
    public PlotStatus? Status { get; init; }
    public string? Search { get; init; }
}

// ── Services ─────────────────────────────────────────────────────────────────

/// <summary>
/// Every method is scoped to the caller: a farmer sees only their own farms, an agronomist
/// their district, an administrator everything. Scoping happens in the query, not after it,
/// so a page of 20 is a page of 20 rows the caller may actually see.
/// </summary>
public interface IFarmService
{
    Task<PagedResult<FarmDto>> ListAsync(FarmQuery query, CancellationToken ct = default);
    Task<FarmDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<FarmDto> CreateAsync(CreateFarmRequest request, CancellationToken ct = default);
    Task<FarmDto> UpdateAsync(Guid id, UpdateFarmRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IPlotService
{
    Task<PagedResult<PlotDto>> ListAsync(PlotQuery query, CancellationToken ct = default);
    Task<PlotDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<PlotDto> CreateAsync(CreatePlotRequest request, CancellationToken ct = default);
    Task<PlotDto> UpdateAsync(Guid id, UpdatePlotRequest request, CancellationToken ct = default);
}

public interface ICropCycleService
{
    Task<CropCycleDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CropCycleDto> CreateAsync(CreateCropCycleRequest request, CancellationToken ct = default);

    /// <summary>The non-CRUD operation: validated stage change + harvest-date revision + audit row.</summary>
    Task<CropCycleDto> AdvanceStageAsync(Guid id, AdvanceStageRequest request, CancellationToken ct = default);
}
