using AgriGuard.Domain.Intelligence;

namespace AgriGuard.Application.Intelligence;

public sealed record OutbreakSignalQuery
{
    public Guid? CropId { get; init; }
    public Guid? DistrictId { get; init; }
    public int Days { get; init; } = OutbreakSignal.DefaultDays;
}

public sealed record PathogenPressureDto(
    string Code,
    string Name,
    int ConfirmedCases,
    decimal Score,
    int SharePercent,
    DateOnly LastReportedOn);

public sealed record DailyCaseCountDto(DateOnly Date, int ReportedCases, int ConfirmedCases);

public sealed record DistrictPressureDto(
    Guid DistrictId,
    string DistrictName,
    int ReportedCases,
    int ConfirmedCases,
    decimal Score,
    int PressureIndex,
    PressureLevel Level,
    string? TopPathogenCode,
    // Rounded to 0.1°: where to put the district's marker, not where anyone farms.
    decimal Latitude,
    decimal Longitude);

public sealed record OutbreakSignalDto(
    Guid? CropId,
    string? CropName,
    Guid? DistrictId,
    string? DistrictName,
    DateOnly From,
    DateOnly To,
    int WindowDays,
    int ReportedCases,
    int ConfirmedCases,
    decimal Score,
    // 0–100; see OutbreakSignal for the formula.
    int PressureIndex,
    PressureLevel Level,
    PressureTrend Trend,
    // One line for a person or an agent: "Late blight pressure high (index 68/100), rising: …".
    string Summary,
    // Highest pressure first.
    IReadOnlyList<PathogenPressureDto> TopPathogens,
    IReadOnlyList<DailyCaseCountDto> Daily,
    IReadOnlyList<DistrictPressureDto> Districts);

public interface IOutbreakSignalService
{
    /// <summary>
    /// Disease pressure from confirmed cases over the last <c>Days</c> days, for one crop and/or one
    /// district, or everything. Aggregate only: no case, farmer or plot is identifiable in the answer.
    /// </summary>
    Task<OutbreakSignalDto> ComputeAsync(OutbreakSignalQuery query, CancellationToken ct = default);
}
