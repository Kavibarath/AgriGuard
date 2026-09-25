using System.Text.Json;
using AgriGuard.Application.Agent;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Validation;
using AgriGuard.Infrastructure.Cases;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// Turns the agent's reports into durable state. The AgentRun tables are the system of record
/// (§6): the agent service keeps nothing, so what is written here is the whole story of a run.
///
/// The agent is trusted to report, not to decide. Its events move the run through its statuses,
/// but a result claiming "ready for approval" is re-checked against the safety rules here before
/// any human is asked to approve it.
/// </summary>
public sealed class AgentCallbackService(
    AgriGuardDbContext db,
    AgentPrescriptionGate prescriptionGate,
    TimeProvider timeProvider,
    ILogger<AgentCallbackService> logger) : IAgentCallbackService
{
    /// <summary>A callback can race the timeout sweeper or another callback on the run's xmin; it reloads and reapplies.</summary>
    private const int MaxAttempts = 3;

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task RecordEventAsync(Guid runId, AgentEventRequest request, string? correlationId, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var run = await db.AgentRuns.Include(r => r.Steps).FirstOrDefaultAsync(r => r.Id == runId, ct)
                ?? throw new NotFoundException("Agent run", runId);
            var now = UtcNow;

            db.AgentRunEvents.Add(new AgentRunEvent
            {
                AgentRunId = run.Id,
                EventType = request.EventType,
                AgentRole = request.AgentRole,
                ToolName = Truncate(request.ToolName, 100),
                PayloadJson = AgentPayloads.ToStorable(request.Payload)
                              ?? StepPayload(request),
                DurationMs = request.DurationMs,
                OccurredAt = now,
                CorrelationId = correlationId
            });

            // The timeline is append-only, so a late event (after a timeout, say) is still recorded,
            // but it no longer moves a run that has already ended or is waiting on a human.
            if (CaseStatusRules.AgentActiveRunStatuses.Contains(run.Status))
                Apply(run, request, now);

            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task RecordResultAsync(Guid runId, AgentRunResultRequest request, string? correlationId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(request.RunId, out var bodyRunId) || bodyRunId != runId)
            throw new RequestValidationException("run_id", "run_id does not match the run in the URL.");

        // Matched exactly: Enum.TryParse would also accept "0" or "1".
        var outcome = request.Outcome switch
        {
            "PendingApproval" => ReportedOutcome.PendingApproval,
            "Rejected" => ReportedOutcome.Rejected,
            "Failed" => ReportedOutcome.Failed,
            _ => throw new RequestValidationException("outcome", "outcome must be PendingApproval, Rejected or Failed.")
        };

        for (var attempt = 1; ; attempt++)
        {
            var run = await db.AgentRuns
                .Include(r => r.Case)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == runId, ct)
                ?? throw new NotFoundException("Agent run", runId);

            if (!CaseStatusRules.AgentActiveRunStatuses.Contains(run.Status))
                throw new ConflictException($"This run is already {run.Status}; its result cannot be recorded again.");

            var now = UtcNow;
            var from = run.Status;

            run.PlanJson = AgentPayloads.ToStorable(request.Plan) ?? run.PlanJson;
            run.ProposalJson = AgentPayloads.ToStorable(request.Proposal) ?? run.ProposalJson;
            run.VerdictJson = AgentPayloads.ToStorable(request.Verdict) ?? run.VerdictJson;
            run.FinalOutcomeJson = AgentPayloads.ToStorable(request);
            run.RevisionCount = Math.Clamp(request.Revisions, 0, 2);

            switch (outcome)
            {
                case ReportedOutcome.PendingApproval:
                    await AcceptProposalAsync(run, request.Proposal, now, ct);
                    break;

                case ReportedOutcome.Rejected:
                    // A Reject-level rule failed: revising cannot fix it, so a person has to look.
                    AgentRunLifecycle.End(run, AgentRunStatus.Rejected, request.FailureReason ?? "The proposal broke a safety rule that cannot be revised.", now);
                    break;

                case ReportedOutcome.Failed:
                    AgentRunLifecycle.End(run, AgentRunStatus.Failed, request.FailureReason ?? "The agent reported a failure without a reason.", now);
                    break;
            }

            db.AgentRunEvents.Add(AgentRunLifecycle.StatusChanged(run.Id, from, run.Status, now, correlationId));

            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Agent run {RunId} reported {Outcome}; recorded as {Status}", run.Id, outcome, run.Status);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// Defence in depth: the proposal is validated again, here, with the backend's own data, before
    /// the run is put in front of an agronomist. A compromised or buggy agent that reports
    /// "approved" for something the rules refuse gets a failed run, not a pending approval.
    /// </summary>
    private async Task AcceptProposalAsync(AgentRun run, JsonElement? proposal, DateTime now, CancellationToken ct)
    {
        if (proposal is not { ValueKind: JsonValueKind.Object } body)
        {
            AgentRunLifecycle.End(run, AgentRunStatus.Failed, "The agent reported a proposal for approval but did not send one.", now);
            return;
        }

        AgentVerdictTool verdict;
        try
        {
            // Built from the run's own case, not from anything the agent says about where it applies.
            verdict = await prescriptionGate.CheckAsync(new AgentProposalInput(
                run.Id.ToString(),
                run.Case.CropCycleId.ToString(),
                ReadString(body, "product_id"),
                ReadDecimal(body, "dose_per_hectare"),
                ReadDecimal(body, "total_quantity"),
                ReadString(body, "spray_date"),
                ReadString(body, "dealer_id")), ct);
        }
        catch (AppException ex)
        {
            // E.g. the crop cycle ended while the agent was working. Still a recorded, safe failure.
            AgentRunLifecycle.End(run, AgentRunStatus.Failed, $"The proposal could not be checked: {ex.Message}", now);
            return;
        }

        // The backend's verdict is the one stored and shown: it is the one that was actually enforced.
        run.VerdictJson = AgentPayloads.ToStorable(verdict);

        if (verdict.Outcome != ValidationOutcome.Approved)
        {
            logger.LogWarning("Agent run {RunId} reported an approvable proposal that failed the backend check: {Summary}", run.Id, verdict.Summary);
            AgentRunLifecycle.End(run, AgentRunStatus.Failed, $"The backend's check of the proposal did not pass ({verdict.Summary}), although the agent reported it as valid.", now);
            return;
        }

        run.Status = AgentRunStatus.PendingApproval;
        foreach (var step in run.Steps.Where(s => s.Status == AgentStepStatus.Running))
            AgentRunLifecycle.CompleteStep(step, AgentStepStatus.Succeeded, now);

        if (run.Case.Status == CaseStatus.AgentProcessing)
            run.Case.Status = CaseStatus.PendingApproval;
    }

    /// <summary>How one progress event moves the run and its steps.</summary>
    private void Apply(AgentRun run, AgentEventRequest e, DateTime now)
    {
        switch (e.EventType)
        {
            case AgentEventType.StepStarted when e.AgentRole is { } role && e.SequenceNo is { } seq:
                run.Status = role switch
                {
                    AgentRole.Coordinator => AgentRunStatus.Planning,
                    AgentRole.Diagnosis => AgentRunStatus.Diagnosing,
                    AgentRole.Action => AgentRunStatus.Drafting,
                    _ => AgentRunStatus.Validating
                };

                // A revision re-enters the Action and Validation steps: the same row is reused
                // (one row per sequence number), and the timeline keeps each attempt.
                var started = FindOrAddStep(run, seq, role, e.Goal);
                started.Status = AgentStepStatus.Running;
                started.StartedAt = now;
                started.CompletedAt = null;
                started.DurationMs = null;
                started.ErrorMessage = null;
                break;

            case AgentEventType.StepCompleted when e.AgentRole is { } role && e.SequenceNo is { } seq:
                var completed = FindOrAddStep(run, seq, role, e.Goal);
                completed.OutputJson = AgentPayloads.ToStorable(e.Payload);
                AgentRunLifecycle.CompleteStep(completed, AgentStepStatus.Succeeded, now);

                if (role == AgentRole.Action
                    && e.Payload is { ValueKind: JsonValueKind.Object } actionOutput
                    && actionOutput.TryGetProperty("proposal", out var proposal))
                    run.ProposalJson = AgentPayloads.ToStorable(proposal);
                break;

            case AgentEventType.PlanCreated:
                run.PlanJson = AgentPayloads.ToStorable(e.Payload);
                break;

            case AgentEventType.ValidationResult:
                run.VerdictJson = AgentPayloads.ToStorable(e.Payload);

                // The graph reports the verdict rather than a StepCompleted for the Validation step.
                if (run.Steps.FirstOrDefault(s => s.AgentRole == AgentRole.Validation && s.Status == AgentStepStatus.Running) is { } validation)
                {
                    validation.OutputJson = run.VerdictJson;
                    AgentRunLifecycle.CompleteStep(validation, AgentStepStatus.Succeeded, now);
                }

                if (e.Payload is { ValueKind: JsonValueKind.Object } verdict
                    && verdict.TryGetProperty("outcome", out var verdictOutcome)
                    && verdictOutcome.ValueKind == JsonValueKind.String
                    && verdictOutcome.GetString() == nameof(ValidationOutcome.Revise))
                    run.Status = AgentRunStatus.RevisionRequested;
                break;

            case AgentEventType.ToolFailed when e.AgentRole is { } role:
                // Each ToolFailed is one failed attempt; the tool client retries twice before giving up.
                if (run.Steps.FirstOrDefault(s => s.AgentRole == role && s.Status == AgentStepStatus.Running) is { } step)
                    step.RetryCount++;
                break;
        }
    }

    private AgentRunStep FindOrAddStep(AgentRun run, int sequenceNo, AgentRole role, string? goal)
    {
        if (run.Steps.FirstOrDefault(s => s.SequenceNo == sequenceNo) is { } existing)
            return existing;

        var step = new AgentRunStep
        {
            AgentRunId = run.Id,
            SequenceNo = sequenceNo,
            AgentRole = role,
            Goal = Truncate(goal, 500) ?? role.ToString(),
            Status = AgentStepStatus.Pending
        };
        // Added explicitly: a new entity with a client-generated key, found only through the
        // navigation, would be taken for an existing row and updated instead of inserted.
        db.AgentRunSteps.Add(step);
        return step;
    }

    /// <summary>StepStarted carries its detail in top-level fields rather than a payload; keep them on the timeline.</summary>
    private static string? StepPayload(AgentEventRequest e) =>
        e.SequenceNo is null && e.Goal is null
            ? null
            : AgentPayloads.ToStorable(new { sequenceNo = e.SequenceNo, goal = Truncate(e.Goal, 500) });

    private static string? ReadString(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? ReadDecimal(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;

    private static string? Truncate(string? value, int max) => AgentRunLifecycle.Truncate(value, max);

    /// <summary>agent/app/contracts.py RunOutcome.</summary>
    private enum ReportedOutcome { PendingApproval, Rejected, Failed }
}
