using AgriGuard.Domain.Cases;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// The state changes every path that ends a run shares — a failed dispatch, the agent's own
/// result, the timeout sweeper — so a run ends the same way whoever ends it.
/// </summary>
internal static class AgentRunLifecycle
{
    /// <summary>
    /// Rejected, failed or timed out: the run is over, open steps are closed as failed, and the case
    /// goes to a person (§9.6). Requires <c>run.Case</c> and <c>run.Steps</c> to be loaded.
    /// </summary>
    public static void End(AgentRun run, AgentRunStatus status, string reason, DateTime now)
    {
        run.Status = status;
        run.FailureReason = Truncate(reason, 1000);
        run.CompletedAt = now;

        foreach (var step in run.Steps.Where(s => s.Status is AgentStepStatus.Running or AgentStepStatus.Pending))
        {
            CompleteStep(step, AgentStepStatus.Failed, now);
            step.ErrorMessage = Truncate(reason, 2000);
        }

        // Only if the run still owns the case: a manual change made meanwhile is not overwritten.
        if (run.Case.Status == CaseStatus.AgentProcessing)
            run.Case.Status = CaseStatus.AwaitingManualReview;
    }

    public static void CompleteStep(AgentRunStep step, AgentStepStatus status, DateTime now)
    {
        step.Status = status;
        step.CompletedAt = now;
        if (step.StartedAt is { } started)
            step.DurationMs = (int)Math.Min(int.MaxValue, (now - started).TotalMilliseconds);
    }

    public static AgentRunEvent StatusChanged(
        Guid runId, AgentRunStatus? from, AgentRunStatus to, DateTime at, string? correlationId = null, object? detail = null) => new()
        {
            AgentRunId = runId,
            EventType = AgentEventType.StatusChanged,
            PayloadJson = AgentPayloads.ToStorable(new { from = from?.ToString(), to = to.ToString(), detail }),
            OccurredAt = at,
            CorrelationId = correlationId
        };

    public static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
