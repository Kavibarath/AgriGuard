using AgriGuard.Domain.Validation;

namespace AgriGuard.Application.Validation;

/// <summary>
/// A prescription proposal to check. Identified by crop cycle rather than plot: the cycle is what
/// carries the harvest date and the application history the rules are measured against.
/// </summary>
public sealed record ValidatePrescriptionRequest(
    Guid CropCycleId,
    Guid ProductId,
    decimal DosePerHectare,
    decimal TotalQuantity,
    DateOnly SprayDate,
    Guid? DealerId = null,
    // Forecast for the spray date, when the caller has one. Absent leaves V8 unevaluated.
    WeatherInput? Weather = null,
    // Dealer stock for the product. Absent leaves V9 unevaluated.
    StockInput? Stock = null);

public sealed record WeatherInput(int RainProbabilityPercent, decimal WindSpeedKph, decimal TemperatureC);

public sealed record StockInput(decimal AvailableQuantity, DateOnly? EarliestBatchExpiry);

public sealed record RuleResultDto(
    string Code,
    string Name,
    RuleStatus Status,
    RuleSeverity Severity,
    string Message,
    string? Evidence);

public sealed record ValidationVerdictDto(
    ValidationOutcome Outcome,
    string Summary,
    IReadOnlyList<RuleResultDto> Results,
    // What the verdict was decided against, echoed so the console and the audit trail can show it.
    Guid CropCycleId,
    Guid PlotId,
    string PlotCode,
    string CropName,
    decimal AreaHectares,
    DateOnly HarvestDate,
    string? ProductName,
    decimal? EstimatedCost,
    DateTime ValidatedAtUtc);

public interface IPrescriptionValidationService
{
    Task<ValidationVerdictDto> ValidateAsync(ValidatePrescriptionRequest request, CancellationToken ct = default);
}
