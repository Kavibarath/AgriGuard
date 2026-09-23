namespace AgriGuard.Application.Prescriptions;

// Inputs and outputs of PrescriptionSafetyValidator. The verdict's JSON shape is read by the
// agent (agent/app/contracts.py Verdict): outcome, summary, results[code, name, status, severity,
// message, evidence]. Enums travel as their names.

public enum VerdictOutcome { Approved, Revise, Rejected }

public enum RuleStatus { Passed, Failed, Skipped }

/// <summary>Reject ends the run; Revise sends the proposal back to the Action agent to fix.</summary>
public enum RuleSeverity { Reject, Revise }

public sealed record RuleResult(
    string Code,
    string Name,
    RuleStatus Status,
    RuleSeverity Severity,
    // Written for the Action agent and the agronomist: what is wrong and what would be right.
    string Message,
    // The numbers the decision was made on, e.g. "dose=3.0; range=1.5–2.5".
    string? Evidence);

public sealed record PrescriptionVerdict(VerdictOutcome Outcome, string Summary, IReadOnlyList<RuleResult> Results);

/// <summary>
/// A proposal exactly as it arrives — every field loosely typed, because checking the shape is
/// itself a rule (V1). A malformed id or an unparseable date becomes a recorded V1 failure in the
/// verdict, rather than a 400 the agent could only retry blindly.
/// </summary>
public sealed record PrescriptionProposalInput(
    string? RunId,
    string? CropCycleId,
    string? ProductId,
    decimal? DosePerHectare,
    decimal? TotalQuantity,
    string? SprayDate,
    string? DealerId);

/// <summary>A proposal that passed V1: every field present and of the right type.</summary>
public sealed record ParsedProposal(
    Guid RunId,
    Guid CropCycleId,
    Guid ProductId,
    decimal DosePerHectare,
    decimal TotalQuantity,
    DateOnly SprayDate,
    Guid? DealerId);

/// <summary>
/// Everything the rules read, loaded from the database before validation so the validator itself
/// is pure: same inputs, same verdict, no I/O. A null member means "not found".
/// </summary>
public sealed record PrescriptionSafetyContext(
    DateOnly Today,
    CycleFacts? Cycle,
    ProductFacts? Product,
    ApprovalFacts? Approval,
    ApplicationHistory History,
    RunFacts? Run,
    DealerStock? Stock,
    SprayWeather? Weather,
    decimal? FarmerCreditLimit);

public sealed record CycleFacts(
    Guid Id,
    bool IsActive,
    Guid CropId,
    decimal AreaHectares,
    // Planned harvest date if the farmer set one, otherwise the expected one.
    DateOnly HarvestDate,
    Guid PlotOwnerId);

public sealed record ProductFacts(
    Guid Id,
    string Name,
    bool IsActive,
    string ActiveIngredient,
    string Unit,
    decimal PackSize,
    decimal UnitPrice);

/// <summary>The ProductCropApproval row for (product, crop) — the regulatory rules table.</summary>
public sealed record ApprovalFacts(
    bool IsActive,
    decimal MinDosePerHectare,
    decimal MaxDosePerHectare,
    int PreHarvestIntervalDays,
    int MaxApplicationsPerCycle,
    int MinDaysBetweenApplications,
    int RainfastHours,
    bool IsRestricted);

/// <summary>Scheduled or applied (not cancelled) uses of the same active ingredient in this cycle.</summary>
public sealed record ApplicationHistory(int CountThisCycle, DateOnly? LastApplicationDate)
{
    public static readonly ApplicationHistory None = new(0, null);
}

public sealed record RunFacts(Guid RunId, Guid CaseCropCycleId, Guid CaseFarmerId, bool IsTerminal);

/// <summary>
/// The dealer the proposal would be filled from: the named one if the proposal names a dealer,
/// otherwise the best-stocked dealer in the farm's district. Only batches still in date on the
/// spray date count.
/// </summary>
public sealed record DealerStock(Guid DealerId, string ShopName, decimal AvailableQuantity, decimal PackPrice);

/// <summary>Worst forecast values within the product's rainfast window after the spray.</summary>
public sealed record SprayWeather(decimal MaxRainProbabilityPercent, decimal MaxWindKmh, decimal MaxTemperatureC);
