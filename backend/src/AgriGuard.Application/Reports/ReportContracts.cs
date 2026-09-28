using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Inventory;
using AgriGuard.Domain.Registry;

namespace AgriGuard.Application.Reports;

// Reports (§5.1): read-only aggregates, one per component. Each is scoped like the data it reads,
// so a report never shows a caller rows the list endpoints would hide from them.

// ── Component D: harvest forecast vs actual ─────────────────────────────────

public sealed record HarvestReportQuery
{
    public Guid? DistrictId { get; init; }
    public Guid? CropId { get; init; }
    /// <summary>Forecast harvest dates from/to (inclusive).</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record ForecastAccuracyDto(
    int Forecasts,
    decimal ForecastKg,
    decimal ActualKg,
    decimal? VariancePercent,
    decimal? MeanAbsolutePercentError,
    int WithinTolerance);

public sealed record ForecastAccuracyGroupDto(string Key, string Label, ForecastAccuracyDto Accuracy);

public sealed record ForecastVsActualRowDto(
    Guid ForecastId,
    Guid CropCycleId,
    string PlotCode,
    string CropName,
    string FarmerName,
    string DistrictName,
    ForecastSource Source,
    DateOnly ForecastHarvestDate,
    DateOnly? ActualHarvestDate,
    decimal EstimatedYieldKg,
    decimal? ActualYieldKg,
    // Null until the actual yield is recorded.
    decimal? VariancePercent);

public sealed record HarvestForecastVsActualReport(
    DateOnly? From,
    DateOnly? To,
    int ForecastCount,
    // Forecasts still waiting for their actual yield.
    int PendingActuals,
    decimal TolerancePercent,
    ForecastAccuracyDto Overall,
    IReadOnlyList<ForecastAccuracyGroupDto> ByCrop,
    // Farmer vs agronomist estimates: whose forecasts can the co-op plan on?
    IReadOnlyList<ForecastAccuracyGroupDto> BySource,
    // Newest forecast first, at most MaxRows.
    IReadOnlyList<ForecastVsActualRowDto> Rows);

public interface IHarvestReportService
{
    Task<HarvestForecastVsActualReport> ForecastVsActualAsync(HarvestReportQuery query, CancellationToken ct = default);
}

// ── Component A: plot treatment history ─────────────────────────────────────

public sealed record PlotTreatmentQuery
{
    public Guid PlotId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record TreatmentRowDto(
    Guid ApplicationId,
    DateOnly ApplicationDate,
    string CropName,
    DateOnly CycleSownDate,
    string ProductName,
    string ActiveIngredient,
    string? ResistanceGroup,
    string Unit,
    decimal DosePerHectare,
    decimal TotalQuantity,
    ApplicationStatus Status,
    string? PrescriptionNo,
    // From the rules table today; null if the product's approval for this crop was removed.
    int? PreHarvestIntervalDays,
    DateOnly? SafeToHarvestFrom);

public sealed record ActiveIngredientUseDto(string ActiveIngredient, string? ResistanceGroup, int Applications, DateOnly LastApplied);

public sealed record PlotTreatmentHistoryReport(
    Guid PlotId,
    string PlotCode,
    string FarmName,
    decimal AreaHectares,
    DateOnly? From,
    DateOnly? To,
    int Applications,
    // Cancelled sprays are listed but not counted here.
    IReadOnlyList<ActiveIngredientUseDto> ByActiveIngredient,
    // Newest first.
    IReadOnlyList<TreatmentRowDto> Rows);

public interface IRegistryReportService
{
    Task<PlotTreatmentHistoryReport> PlotTreatmentHistoryAsync(PlotTreatmentQuery query, CancellationToken ct = default);
}

// ── Component B: case throughput ────────────────────────────────────────────

public sealed record CaseThroughputQuery
{
    public Guid? DistrictId { get; init; }
    /// <summary>Case report dates from/to (inclusive); the last 30 days when omitted.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record StatusCountDto(string Status, int Count);

public sealed record DailyThroughputDto(DateOnly Date, int Reported, int Prescribed);

public sealed record CaseThroughputReport(
    DateOnly From,
    DateOnly To,
    Guid? DistrictId,
    int Reported,
    int Prescribed,
    // Prescribed ÷ reported, in percent; null with no cases.
    decimal? PrescribedPercent,
    // Report → prescription issued, over prescribed cases.
    double? MedianHoursToPrescription,
    double? AverageHoursToPrescription,
    IReadOnlyList<StatusCountDto> CasesByStatus,
    IReadOnlyList<StatusCountDto> AgentRunsByStatus,
    // Share of finished agent runs that ended in a prescription (the rest were rejected, failed or timed out).
    decimal? AgentSuccessPercent,
    IReadOnlyList<DailyThroughputDto> Daily);

public interface ICaseReportService
{
    Task<CaseThroughputReport> CaseThroughputAsync(CaseThroughputQuery query, CancellationToken ct = default);
}

// ── Component C: stock valuation and low stock ──────────────────────────────

public sealed record StockReportQuery
{
    /// <summary>A dealer may name only their own shop; an administrator any, or none for the whole co-op.</summary>
    public Guid? DealerId { get; init; }
}

public sealed record LowStockQuery
{
    public Guid? DealerId { get; init; }
    /// <summary>Flag products with fewer sellable packs than this. Default StockLevel.DefaultMinPacks.</summary>
    public int? MinPacks { get; init; }
}

public sealed record StockValuationRowDto(
    Guid DealerId,
    string ShopName,
    Guid ProductId,
    string ProductName,
    string Unit,
    int Batches,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    // Value at each batch's own pack price.
    decimal Value,
    decimal HeldValue,
    decimal ExpiredValue);

public sealed record DealerValuationDto(Guid DealerId, string ShopName, decimal Value, decimal ExpiredValue);

public sealed record StockValuationReport(
    DateOnly AsOf,
    int Batches,
    decimal TotalValue,
    // Held for approved or pending prescriptions: on the shelf, but not for sale.
    decimal HeldValue,
    // On the shelf but unsellable (past expiry): a write-off unless returned.
    decimal ExpiredValue,
    // Expires within BatchExpiry.WarningDays.
    decimal ExpiringSoonValue,
    IReadOnlyList<DealerValuationDto> ByDealer,
    // Highest value first.
    IReadOnlyList<StockValuationRowDto> ByProduct);

public sealed record LowStockRowDto(
    Guid DealerId,
    string ShopName,
    Guid ProductId,
    string ProductName,
    string Unit,
    decimal PackSize,
    // Unheld, in-date stock.
    decimal AvailableQuantity,
    int SellablePacks,
    decimal HeldQuantity,
    decimal ExpiredQuantity,
    DateOnly? NextExpiry,
    StockLevelState State);

public sealed record LowStockReport(DateOnly AsOf, int MinPacks, int OutOfStock, int Low, IReadOnlyList<LowStockRowDto> Rows);

public interface IStockReportService
{
    Task<StockValuationReport> StockValuationAsync(StockReportQuery query, CancellationToken ct = default);
    Task<LowStockReport> LowStockAsync(LowStockQuery query, CancellationToken ct = default);
}
