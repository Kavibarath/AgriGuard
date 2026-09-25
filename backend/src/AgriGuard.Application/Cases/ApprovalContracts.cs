using AgriGuard.Domain.Cases;

namespace AgriGuard.Application.Cases;

/// <summary>
/// An agronomist's decision on a run awaiting approval. A reason is required to reject or revise:
/// a rejection is audited, and a revision's reason is what the agent is told to fix.
/// </summary>
public sealed record DecideRequest(ApprovalDecisionType Decision, string? Reason);

public static class ApprovalLimits
{
    /// <summary>After this many "please revise" decisions a person should take over the case.</summary>
    public const int MaxHumanRevisions = 2;

    /// <summary>What the agent accepts as a reviewer note (agent/app/contracts.py ShortText).</summary>
    public const int MaxReviewerNoteLength = 300;

    /// <summary>Client-generated, e.g. a UUID. Plain tokens only, because the key is stored and logged.</summary>
    public const string IdempotencyKeyPattern = "^[A-Za-z0-9._:-]{8,100}$";
}

public sealed record DecisionResultDto(
    Guid DecisionId,
    Guid RunId,
    Guid CaseId,
    ApprovalDecisionType Decision,
    string? Reason,
    DateTime DecidedAt,
    AgentRunStatus RunStatus,
    CaseStatus CaseStatus,
    // Set only when the decision was Approve.
    IssuedPrescriptionDto? Prescription,
    // True when this answer is the stored result of an earlier request with the same Idempotency-Key.
    bool Replayed);

public sealed record IssuedPrescriptionDto(
    Guid Id,
    string PrescriptionNo,
    string ProductName,
    decimal DosePerHectare,
    decimal TotalQuantity,
    DateOnly SprayDate,
    // SprayDate + the pre-harvest interval: shown to the farmer as "do not harvest before".
    DateOnly EarliestSafeHarvestDate,
    string? Instructions,
    Guid OrderId,
    string OrderNo,
    string DealerName,
    int Packs,
    decimal OrderTotal);

/// <summary>
/// The human gate (§9.5). The only way a proposal becomes a prescription, and the only place stock
/// is committed. Field Agronomists only, and only for runs in their district.
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Approve issues the prescription and commits the stock in one serializable transaction;
    /// reject ends the run; revise sends it back to the agent with the reason as guidance.
    /// Idempotent on <paramref name="idempotencyKey"/>: a retried request returns the original
    /// result and changes nothing.
    /// </summary>
    Task<DecisionResultDto> DecideAsync(Guid runId, DecideRequest request, string idempotencyKey, CancellationToken ct = default);
}
