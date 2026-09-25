using AgriGuard.Domain.Registry;

namespace AgriGuard.Application.Registry;

public sealed record IngredientUsageDto(
    Guid ActiveIngredientId,
    string Name,
    string? ResistanceGroup,
    int ApplicationCount,
    DateOnly? LastAppliedOn,
    int? DaysSinceLastApplication);

public sealed record ProductWindowDto(
    Guid ProductId,
    string ProductName,
    Guid ActiveIngredientId,
    string ActiveIngredientName,
    string? ResistanceGroup,
    bool IsRestricted,
    int PreHarvestIntervalDays,
    int ApplicationsUsed,
    int MaxApplicationsPerCycle,
    int ApplicationsRemaining,
    DateOnly? LastAppliedOn,
    DateOnly LastSafeSprayDate,
    DateOnly? EarliestNextApplication,
    bool CanSprayToday,
    SprayBlock BlockedReason,
    // Farmer-facing sentence for BlockedReason; null when the product can be sprayed today.
    string? BlockedExplanation);

public sealed record AppliedTreatmentDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ActiveIngredientName,
    DateOnly ApplicationDate,
    decimal DosePerHectare,
    decimal TotalQuantity,
    ApplicationStatus Status);

/// <summary>
/// A plot's chemical-safety position (§5.1, Student A's second non-CRUD operation).
/// <c>Cycle</c> fields are null when nothing is growing — there is no harvest date to measure
/// pre-harvest intervals against, so every window is meaningless and the lists come back empty.
/// </summary>
public sealed record PlotSafetyProfileDto(
    Guid PlotId,
    string PlotCode,
    string FarmName,
    decimal AreaHectares,
    Guid? CropCycleId,
    string? CropName,
    CropStage? Stage,
    DateOnly? SownDate,
    DateOnly? HarvestDate,
    int? DaysToHarvest,
    IReadOnlyList<AppliedTreatmentDto> Applications,
    IReadOnlyList<IngredientUsageDto> IngredientUsage,
    IReadOnlyList<ProductWindowDto> ProductWindows,
    IReadOnlyList<DateOnly> PhiBlockedSprayDates,
    DateTime? ReEntryClearAtUtc,
    DateTime GeneratedAtUtc);

public interface ISafetyProfileService
{
    Task<PlotSafetyProfileDto> GetAsync(Guid plotId, CancellationToken ct = default);
}
