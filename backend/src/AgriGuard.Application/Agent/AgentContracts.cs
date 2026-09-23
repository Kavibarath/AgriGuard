using System.Text.Json;
using System.Text.Json.Serialization;
using AgriGuard.Application.Prescriptions;
using AgriGuard.Domain.Cases;

namespace AgriGuard.Application.Agent;

// The contract with the Python agent service (agent/app). Field names here are the ones the agent
// reads and writes — change them only together with agent/app/graph.py and agent/app/runner.py.

// ── Dispatch: backend → agent ────────────────────────────────────────────────

/// <summary>Ids only: the agent fetches everything else through the tool endpoints.</summary>
public sealed record AgentDispatchRequest(Guid RunId, Guid CaseId, string Objective, string? ReviewerNote);

/// <summary>Hands a run to the agent service. Throws <see cref="AgentDispatchException"/> if the service did not accept it.</summary>
public interface IAgentDispatcher
{
    Task DispatchAsync(AgentDispatchRequest request, CancellationToken ct = default);
}

public sealed class AgentDispatchException(string message, Exception? inner = null) : Exception(message, inner);

// ── Callbacks: agent → backend ───────────────────────────────────────────────

/// <summary>
/// One timeline entry, as posted by agent/app/runner.py's BackendReporter. Which fields are set
/// depends on the event: StepStarted carries sequenceNo + goal, ToolCalled carries toolName +
/// durationMs, most carry a payload.
/// </summary>
public sealed record AgentEventRequest(
    AgentEventType EventType,
    AgentRole? AgentRole,
    string? ToolName,
    int? DurationMs,
    int? SequenceNo,
    string? Goal,
    JsonElement? Payload);

/// <summary>
/// The run's final result — agent/app/contracts.py RunResult, dumped by Pydantic in snake_case.
/// The nested objects are kept as raw JSON: the backend stores them, and only the proposal is
/// read back (to re-validate it).
/// </summary>
public sealed record AgentRunResultRequest(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("proposal")] JsonElement? Proposal,
    [property: JsonPropertyName("verdict")] JsonElement? Verdict,
    [property: JsonPropertyName("diagnosis")] JsonElement? Diagnosis,
    [property: JsonPropertyName("plan")] JsonElement? Plan,
    [property: JsonPropertyName("failure_reason")] string? FailureReason,
    [property: JsonPropertyName("revisions")] int Revisions);

/// <summary>Where agent progress lands. The agent service holds no database access; this is its only write path.</summary>
public interface IAgentCallbackService
{
    Task RecordEventAsync(Guid runId, AgentEventRequest request, string? correlationId, CancellationToken ct = default);
    Task RecordResultAsync(Guid runId, AgentRunResultRequest request, string? correlationId, CancellationToken ct = default);
}

// ── Tools: agent → backend (read-only, except that validation writes nothing either) ──

public sealed record CaseDetailTool(
    Guid CaseId,
    string ReferenceNo,
    Guid CropCycleId,
    Guid PlotId,
    Guid CropId,
    Guid DistrictId,
    string CropName,
    string Stage,
    DateOnly SownDate,
    decimal AreaHectares,
    IReadOnlyList<string> SymptomCodes,
    string? FarmerNote,
    // Pathogens whose indicative symptoms overlap the reported ones, best match first. The
    // Diagnosis agent is told to use these codes only, so it cannot invent a pathogen.
    IReadOnlyList<CandidatePathogenTool> CandidatePathogens);

public sealed record CandidatePathogenTool(string Code, string CommonName, string Type, IReadOnlyList<string> MatchedSymptoms);

public sealed record CropHistoryTool(Guid CropCycleId, string Summary, IReadOnlyList<ApplicationTool> Applications);

public sealed record ApplicationTool(
    string ProductName,
    string ActiveIngredient,
    DateOnly ApplicationDate,
    decimal DosePerHectare,
    string Status);

public sealed record OutbreakSignalTool(
    Guid CropId,
    Guid DistrictId,
    int WindowDays,
    int CasesInWindow,
    // Low | Moderate | High, from confirmed diagnoses in the window.
    string Pressure,
    string Summary,
    IReadOnlyList<ConfirmedPathogenCountTool> ConfirmedPathogens);

public sealed record ConfirmedPathogenCountTool(string Code, string CommonName, int Cases);

public sealed record ApprovedProductsTool(IReadOnlyList<ApprovedProductTool> Products);

public sealed record ApprovedProductTool(
    Guid ProductId,
    string ProductName,
    string ActiveIngredient,
    string? ResistanceGroup,
    string Unit,
    decimal PackSize,
    decimal MinDosePerHectare,
    decimal MaxDosePerHectare,
    int PreHarvestIntervalDays,
    int MaxApplicationsPerCycle,
    int MinDaysBetweenApplications);

public sealed record PlotSafetyProfileTool(
    Guid PlotId,
    Guid CropCycleId,
    string CropName,
    decimal AreaHectares,
    DateOnly HarvestDate,
    int DaysToHarvest,
    IReadOnlyList<ProductWindowTool> ProductWindows);

public sealed record ProductWindowTool(
    Guid ProductId,
    string ProductName,
    int PreHarvestIntervalDays,
    // Spraying after this date breaks the pre-harvest interval (rule V5).
    DateOnly LastSafeSprayDate,
    int ApplicationsThisCycle,
    int MaxApplicationsPerCycle,
    DateOnly? LastApplicationDate,
    // Earliest date the same active ingredient may be sprayed again (rule V7), if it was used before.
    DateOnly? NextAllowedSprayDate,
    // Neither PHI nor the per-cycle limit blocks a spray today (the hard, Reject-level rules).
    bool CanSprayToday);

public sealed record StockAvailabilityTool(
    Guid ProductId,
    // Across the dealers returned, in the product's unit.
    decimal AvailableQuantity,
    string Unit,
    IReadOnlyList<DealerStockTool> Dealers);

public sealed record DealerStockTool(Guid DealerId, string ShopName, Guid DistrictId, decimal AvailableQuantity, DateOnly EarliestExpiry);

public sealed record ProductPricingTool(
    Guid ProductId,
    string ProductName,
    string Unit,
    decimal PackSize,
    // Reference price per pack, LKR.
    decimal UnitPrice,
    int? PacksNeeded,
    decimal? EstimatedCost);

/// <summary>
/// The agent's read-only view of the system (§9.3). Every method answers one allow-listed tool in
/// agent/app/tools.py. Nothing here writes: reserving stock and issuing prescriptions happen only
/// after a human approves, and are deliberately not tools.
/// </summary>
public interface IAgentToolService
{
    Task<CaseDetailTool> GetCaseDetailAsync(Guid caseId, CancellationToken ct = default);
    Task<CropHistoryTool> GetCropHistoryAsync(Guid cropCycleId, CancellationToken ct = default);
    Task<OutbreakSignalTool> GetOutbreakSignalAsync(Guid cropId, Guid districtId, int days, CancellationToken ct = default);
    Task<ApprovedProductsTool> SearchApprovedProductsAsync(Guid cropId, string pathogenCode, CancellationToken ct = default);
    Task<PlotSafetyProfileTool> GetPlotSafetyProfileAsync(Guid plotId, CancellationToken ct = default);
    Task<StockAvailabilityTool> CheckStockAvailabilityAsync(Guid productId, Guid? districtId, DateOnly? usableOn, CancellationToken ct = default);
    Task<ProductPricingTool> GetProductPricingAsync(Guid productId, decimal? quantity, CancellationToken ct = default);
    Task<PrescriptionVerdict> ValidatePrescriptionAsync(PrescriptionProposalInput proposal, CancellationToken ct = default);
}
