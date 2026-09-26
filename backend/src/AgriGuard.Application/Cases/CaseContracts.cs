using System.Text.Json;
using AgriGuard.Application.Common.Models;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Registry;

namespace AgriGuard.Application.Cases;

// ── Responses ────────────────────────────────────────────────────────────────

/// <summary>One row of the case queue. The farmer's note is left out: it is long, and the list does not need it.</summary>
public sealed record CaseSummaryDto(
    Guid Id,
    string ReferenceNo,
    CaseStatus Status,
    CaseSeverity Severity,
    Guid FarmerId,
    string FarmerName,
    Guid PlotId,
    string PlotCode,
    Guid CropCycleId,
    Guid CropId,
    string CropName,
    CropStage Stage,
    Guid DistrictId,
    string DistrictName,
    IReadOnlyList<string> SymptomCodes,
    Guid? AssignedAgronomistId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // The most recent run, so the queue can show "agent working" / "awaiting approval" at a glance.
    Guid? LatestRunId,
    AgentRunStatus? LatestRunStatus);

public sealed record CaseDetailDto(
    Guid Id,
    string ReferenceNo,
    CaseStatus Status,
    CaseSeverity Severity,
    Guid FarmerId,
    string FarmerName,
    Guid PlotId,
    string PlotCode,
    decimal PlotAreaHectares,
    Guid CropCycleId,
    Guid CropId,
    string CropName,
    CropStage Stage,
    Guid DistrictId,
    string DistrictName,
    IReadOnlyList<SymptomDto> Symptoms,
    // Untrusted free text, returned exactly as the farmer typed it; clients must render it as text.
    string? FarmerNote,
    decimal ReportedLatitude,
    decimal ReportedLongitude,
    Guid? AssignedAgronomistId,
    string? AssignedAgronomistName,
    string? ConfirmedPathogenCode,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<AgentRunSummaryDto> AgentRuns);

public sealed record SymptomDto(string Code, string Label);

public sealed record AgentRunSummaryDto(
    Guid Id,
    AgentRunStatus Status,
    int RevisionCount,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record AgentRunDto(
    Guid Id,
    Guid CaseId,
    string CaseReferenceNo,
    string Objective,
    AgentRunStatus Status,
    // Stored as jsonb; passed through as JSON so the console can render them without a second schema.
    JsonElement? Plan,
    JsonElement? Proposal,
    JsonElement? Verdict,
    JsonElement? FinalOutcome,
    string? FailureReason,
    int RevisionCount,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<AgentRunStepDto> Steps,
    // The stored proposal carries only the product's id; its name is resolved for display.
    string? ProposedProductName,
    // Set once the run was approved.
    IssuedPrescriptionDto? Prescription);

public sealed record AgentRunStepDto(
    int SequenceNo,
    AgentRole AgentRole,
    string Goal,
    AgentStepStatus Status,
    JsonElement? Output,
    string? ErrorMessage,
    int RetryCount,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int? DurationMs);

public sealed record AgentRunEventDto(
    Guid Id,
    AgentEventType EventType,
    AgentRole? AgentRole,
    string? ToolName,
    JsonElement? Payload,
    int? DurationMs,
    DateTime OccurredAt,
    string? CorrelationId);

/// <summary>What POST /api/cases/{id}/agent-runs answers with. Poll GET /api/agent-runs/{runId} for progress.</summary>
public sealed record AgentRunStartedDto(Guid RunId, Guid CaseId, AgentRunStatus Status, string? FailureReason);

// ── Requests ─────────────────────────────────────────────────────────────────

public sealed record CreateCaseRequest(
    Guid PlotId,
    Guid CropCycleId,
    // Codes from the closed symptom catalogue (GET-able from the apps' checklist), never free text.
    IReadOnlyList<string> SymptomCodes,
    string? FarmerNote,
    // Where the phone was when the report was made; may differ from the plot's centroid.
    decimal Latitude,
    decimal Longitude,
    // The farmer's own sense of urgency. Defaults to Medium.
    CaseSeverity? Severity);

public sealed record UpdateCaseStatusRequest(CaseStatus Status);

// ── Query options ────────────────────────────────────────────────────────────

public sealed record CaseQuery : PageRequest
{
    public CaseStatus? Status { get; init; }
    public Guid? CropId { get; init; }
    public Guid? DistrictId { get; init; }
    public CaseSeverity? Severity { get; init; }
    public Guid? PlotId { get; init; }

    /// <summary>Matches the reference number, plot code or farmer name.</summary>
    public string? Search { get; init; }
}

public sealed record AgentRunEventQuery : PageRequest;

// ── Services ─────────────────────────────────────────────────────────────────

/// <summary>
/// Component B — crop-health cases. Scoped like the registry: a farmer sees their own cases, an
/// agronomist their district's, an administrator all. Dealers see none.
/// </summary>
public interface ICaseService
{
    Task<PagedResult<CaseSummaryDto>> ListAsync(CaseQuery query, CancellationToken ct = default);
    Task<CaseDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CaseDetailDto> CreateAsync(CreateCaseRequest request, CancellationToken ct = default);
    Task<CaseDetailDto> UpdateStatusAsync(Guid id, UpdateCaseStatusRequest request, CancellationToken ct = default);
}

/// <summary>Component B — the user-facing side of agent runs.</summary>
public interface IAgentRunService
{
    /// <summary>
    /// The non-CRUD operation (§5.1): creates the run, moves the case to AgentProcessing, and hands
    /// the run to the agent service. Returns as soon as the agent has accepted it — the run itself
    /// takes about a minute and reports back through the internal callbacks.
    /// </summary>
    Task<AgentRunStartedDto> StartAsync(Guid caseId, CancellationToken ct = default);

    Task<AgentRunDto> GetAsync(Guid runId, CancellationToken ct = default);

    /// <summary>The auditable timeline, oldest first.</summary>
    Task<PagedResult<AgentRunEventDto>> ListEventsAsync(Guid runId, AgentRunEventQuery query, CancellationToken ct = default);
}
