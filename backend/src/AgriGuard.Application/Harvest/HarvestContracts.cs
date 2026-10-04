using AgriGuard.Application.Common.Models;
using AgriGuard.Domain.Harvest;

namespace AgriGuard.Application.Harvest;

// ── Harvest windows ──────────────────────────────────────────────────────────

public sealed record HarvestDayDto(
    DateOnly Date,
    int Score,
    bool Recommended,
    int? RainProbabilityPercent,
    decimal? RainMm,
    IReadOnlyList<string> Reasons);

public sealed record HarvestWindowDto(
    Guid CropCycleId,
    Guid PlotId,
    string PlotCode,
    string CropName,
    DateOnly MaturityDate,
    // Harvest is refused before this day (the latest spray's pre-harvest interval); null when nothing was sprayed.
    DateOnly? SafeFromDate,
    string? SafetyReason,
    bool ForecastAvailable,
    // "Best: Thu 12 Nov (at maturity, forecast dry)": one line for a person.
    string Summary,
    // Best first.
    IReadOnlyList<HarvestDayDto> Days);

public interface IHarvestWindowService
{
    Task<HarvestWindowDto> GetAsync(Guid cropCycleId, CancellationToken ct = default);
}

// ── Harvest forecasts ────────────────────────────────────────────────────────

public sealed record HarvestForecastDto(
    Guid Id,
    Guid CropCycleId,
    string PlotCode,
    string CropName,
    string FarmerName,
    DateOnly ForecastHarvestDate,
    decimal EstimatedYieldKg,
    decimal? ActualYieldKg,
    ForecastSource Source,
    string? Notes,
    DateTime CreatedAt);

public sealed record CreateHarvestForecastRequest(Guid CropCycleId, DateOnly ForecastHarvestDate, decimal EstimatedYieldKg, string? Notes);

public sealed record RecordActualYieldRequest(decimal ActualYieldKg);

public sealed record HarvestForecastQuery : PageRequest
{
    public Guid? CropCycleId { get; init; }
    public Guid? DistrictId { get; init; }
    public Guid? CropId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public interface IHarvestForecastService
{
    Task<PagedResult<HarvestForecastDto>> ListAsync(HarvestForecastQuery query, CancellationToken ct = default);
    Task<HarvestForecastDto> CreateAsync(CreateHarvestForecastRequest request, CancellationToken ct = default);
    Task<HarvestForecastDto> RecordActualAsync(Guid id, RecordActualYieldRequest request, CancellationToken ct = default);
}

// ── Collection centres, slots and bookings ───────────────────────────────────

public sealed record CollectionCentreDto(Guid Id, string Name, Guid DistrictId, string DistrictName, decimal Latitude, decimal Longitude, decimal DailyCapacityKg);

public sealed record CollectionSlotDto(
    Guid Id,
    Guid CentreId,
    string CentreName,
    DateOnly SlotDate,
    int SlotIndex,
    TimeOnly StartTime,
    TimeOnly EndTime,
    decimal CapacityKg,
    decimal BookedKg,
    decimal RemainingKg);

public sealed record CreateCollectionSlotRequest(Guid CentreId, DateOnly SlotDate, int SlotIndex, TimeOnly StartTime, TimeOnly EndTime, decimal CapacityKg);

public sealed record CollectionSlotQuery : PageRequest
{
    public DateOnly? Date { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? CentreId { get; init; }
    public Guid? DistrictId { get; init; }
    /// <summary>Only slots with at least this much room left.</summary>
    public decimal? MinRemainingKg { get; init; }
}

public sealed record CollectionBookingDto(
    Guid Id,
    string BookingNo,
    BookingStatus Status,
    Guid SlotId,
    string CentreName,
    DateOnly SlotDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    Guid CropCycleId,
    string PlotCode,
    string CropName,
    string FarmerName,
    decimal QuantityKg,
    // Straight-line distance from the plot to the centre.
    double DistanceKm,
    DateTime CreatedAt);

public sealed record AllocateBookingRequest(
    Guid CropCycleId,
    decimal QuantityKg,
    DateOnly PreferredDate,
    // Limit the search to one centre; otherwise every active centre in the farm's district.
    Guid? CentreId);

public sealed record CollectionBookingQuery : PageRequest
{
    public BookingStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public Guid? CentreId { get; init; }
}

public interface ICollectionService
{
    Task<IReadOnlyList<CollectionCentreDto>> ListCentresAsync(Guid? districtId, CancellationToken ct = default);
    Task<PagedResult<CollectionSlotDto>> ListSlotsAsync(CollectionSlotQuery query, CancellationToken ct = default);
    Task<CollectionSlotDto> CreateSlotAsync(CreateCollectionSlotRequest request, CancellationToken ct = default);
    Task<PagedResult<CollectionBookingDto>> ListBookingsAsync(CollectionBookingQuery query, CancellationToken ct = default);

    /// <summary>The non-CRUD allocation: one serializable transaction, slots locked, never overbooked.</summary>
    Task<CollectionBookingDto> AllocateAsync(AllocateBookingRequest request, CancellationToken ct = default);

    Task<CollectionBookingDto> CancelAsync(Guid id, CancellationToken ct = default);
}
