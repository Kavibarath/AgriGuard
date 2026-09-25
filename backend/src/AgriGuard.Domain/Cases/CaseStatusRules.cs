using AgriGuard.Domain.Identity;

namespace AgriGuard.Domain.Cases;

/// <summary>
/// Who may move a case where, by hand.
///
/// Most of a case's life is driven by the agent workflow — AgentProcessing, PendingApproval,
/// Prescribed and Rejected are set by the run and the approval decision, never typed in. A person
/// may only close a case or send it to manual review, and only from a state where nothing is in
/// flight: closing a case while the agent is still drafting would orphan the run.
///
/// Pure domain logic with no EF or HTTP dependency, so it is unit-tested directly.
/// </summary>
public static class CaseStatusRules
{
    /// <summary>States a case can be moved to by hand. Everything else belongs to the workflow.</summary>
    public static readonly IReadOnlySet<CaseStatus> ManuallySettable =
        new HashSet<CaseStatus> { CaseStatus.AwaitingManualReview, CaseStatus.Closed };

    /// <summary>
    /// Explains why <paramref name="role"/> may not move a case from <paramref name="from"/> to
    /// <paramref name="to"/>, or returns null when the change is allowed. The message reaches the
    /// user, so it says what to do instead.
    /// </summary>
    public static string? ExplainManualChange(CaseStatus from, CaseStatus to, UserRole role)
    {
        if (from == to)
            return $"The case is already {to}.";

        if (!ManuallySettable.Contains(to))
            return $"{to} is set by the agent workflow and the approval decision, not by hand.";

        if (from == CaseStatus.Closed)
            return "A closed case cannot be reopened. Report a new case instead.";

        if (from is CaseStatus.AgentProcessing or CaseStatus.PendingApproval)
            return from == CaseStatus.AgentProcessing
                ? "The agent is still working on this case. Wait for the run to finish."
                : "This case has a proposal awaiting an agronomist's decision. Approve, reject or revise it instead.";

        return to switch
        {
            // Triage is an agronomist's judgement; a farmer asking for help does that by starting a run.
            CaseStatus.AwaitingManualReview when role is not (UserRole.FieldAgronomist or UserRole.CoopAdministrator) =>
                "Only an agronomist or administrator can send a case to manual review.",
            CaseStatus.AwaitingManualReview when from is not (CaseStatus.Submitted or CaseStatus.Rejected) =>
                $"A {from} case cannot go to manual review.",
            _ => null
        };
    }

    /// <summary>A run may start on a fresh case, or again after a failed run sent it to manual review.</summary>
    public static bool CanStartAgentRun(CaseStatus status) =>
        status is CaseStatus.Submitted or CaseStatus.AwaitingManualReview;

    /// <summary>Run statuses after which nothing more will happen to the run.</summary>
    public static readonly IReadOnlySet<AgentRunStatus> TerminalRunStatuses = new HashSet<AgentRunStatus>
    {
        AgentRunStatus.Completed, AgentRunStatus.Rejected, AgentRunStatus.Failed, AgentRunStatus.TimedOut
    };

    /// <summary>
    /// Run statuses in which the agent service itself is still working, as opposed to waiting on a
    /// human (PendingApproval) or on the approval transaction (Approved, Executing). Only these
    /// accept progress callbacks and can time out.
    /// </summary>
    public static readonly IReadOnlySet<AgentRunStatus> AgentActiveRunStatuses = new HashSet<AgentRunStatus>
    {
        AgentRunStatus.Planning, AgentRunStatus.Diagnosing, AgentRunStatus.Drafting,
        AgentRunStatus.Validating, AgentRunStatus.RevisionRequested
    };
}
