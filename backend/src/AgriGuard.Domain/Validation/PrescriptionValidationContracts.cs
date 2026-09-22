namespace AgriGuard.Domain.Validation;

/// <summary>
/// What a failed rule costs. Reject is terminal; Revise sends the proposal back to the Action
/// agent for another attempt (at most twice — §9.4).
/// </summary>
public enum RuleSeverity { Reject, Revise }

public enum RuleStatus
{
    Passed,
    Failed,

    /// <summary>
    /// The rule could not be checked because its input was unavailable (no weather service, no
    /// stock figures, or an earlier rule already made the check meaningless). Reported honestly
    /// rather than being counted as a pass — a validator that silently skips a rule is worse
    /// than one that admits it.
    /// </summary>
    NotEvaluated
}

public enum ValidationOutcome
{
    /// <summary>Every evaluated rule passed. The proposal may go to a human for approval.</summary>
    Approved,

    /// <summary>At least one Revise-severity rule failed; the agent may propose again.</summary>
    Revise,

    /// <summary>At least one Reject-severity rule failed. Terminal — no retry.</summary>
    Rejected
}

/// <summary>
/// One rule's finding. <paramref name="Message"/> is written for the agronomist reading the
/// approval console; <paramref name="Evidence"/> carries the numbers it was decided on, so the
/// verdict can be audited without re-running anything.
/// </summary>
public sealed record RuleResult(
    string Code,
    string Name,
    RuleStatus Status,
    RuleSeverity Severity,
    string Message,
    string? Evidence = null)
{
    public bool IsFailure => Status == RuleStatus.Failed;
}

/// <summary>What the Action agent proposes. It can only ever propose — nothing here is applied.</summary>
public sealed record PrescriptionProposal(
    Guid ProductId,
    decimal DosePerHectare,
    decimal TotalQuantity,
    DateOnly SprayDate,
    Guid? DealerId = null);

/// <summary>One row of the rules table, as it stood when the proposal was validated.</summary>
public sealed record ProductApprovalSnapshot(
    Guid ProductId,
    string ProductName,
    Guid ActiveIngredientId,
    string ActiveIngredientName,
    decimal MinDosePerHectare,
    decimal MaxDosePerHectare,
    int PreHarvestIntervalDays,
    int MaxApplicationsPerCycle,
    int MinDaysBetweenApplications,
    int RainfastHours,
    bool IsRestricted,
    bool IsActive);

/// <summary>An application already recorded against the crop cycle.</summary>
public sealed record AppliedTreatmentFact(Guid ProductId, Guid ActiveIngredientId, DateOnly AppliedOn);

/// <summary>Forecast for the spray date, within the product's rainfast window (V8).</summary>
public sealed record WeatherAssessment(int RainProbabilityPercent, decimal WindSpeedKph, decimal TemperatureC);

/// <summary>Dealer stock for the proposed product (V9).</summary>
public sealed record StockAssessment(decimal AvailableQuantity, DateOnly? EarliestBatchExpiry);

/// <summary>
/// Everything the validator is allowed to know, gathered before it runs. Passing a snapshot
/// rather than a DbContext is deliberate: the rules cannot make extra queries, cannot depend on
/// ambient state, and can be unit-tested exhaustively.
/// </summary>
public sealed record PrescriptionValidationContext(
    Guid PlotId,
    Guid PlotOwnerId,
    decimal AreaHectares,
    Guid CropId,
    string CropName,
    DateOnly HarvestDate,
    Guid RequestedForUserId,
    ProductApprovalSnapshot? Approval,
    IReadOnlyList<AppliedTreatmentFact> Applications,
    bool HasRestrictedUsePermit = false,
    decimal? CreditLimit = null,
    decimal? EstimatedCost = null,
    WeatherAssessment? Weather = null,
    StockAssessment? Stock = null);

public sealed record ValidationVerdict(ValidationOutcome Outcome, IReadOnlyList<RuleResult> Results)
{
    public IEnumerable<RuleResult> Failures => Results.Where(r => r.IsFailure);
    public IEnumerable<RuleResult> NotEvaluated => Results.Where(r => r.Status == RuleStatus.NotEvaluated);

    /// <summary>One-line summary for the run timeline and the case status.</summary>
    public string Summary => Outcome switch
    {
        ValidationOutcome.Approved => $"All {Results.Count(r => r.Status == RuleStatus.Passed)} checked rules passed.",
        ValidationOutcome.Revise => $"Needs revision: {string.Join("; ", Failures.Select(f => f.Code))}.",
        _ => $"Rejected: {string.Join("; ", Failures.Where(f => f.Severity == RuleSeverity.Reject).Select(f => f.Code))}."
    };
}
