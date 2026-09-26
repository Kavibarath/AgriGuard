using System.Text.Json;
using AgriGuard.Application.Agent;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Domain.Cases;
using AgriGuard.Infrastructure.Agent;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgriGuard.Infrastructure.Cases;

public sealed class AgentRunService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    IAgentDispatcher dispatcher,
    TimeProvider timeProvider,
    ILogger<AgentRunService> logger) : IAgentRunService
{
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<AgentRunStartedDto> StartAsync(Guid caseId, CancellationToken ct = default)
    {
        var cropCase = await db.CropCases.ScopedTo(currentUser).FirstOrDefaultAsync(c => c.Id == caseId, ct);
        CaseScope.EnsureVisible(cropCase, await db.CropCases.AnyAsync(c => c.Id == caseId, ct), "Case", caseId);

        if (await db.AgentRuns.WhereNotTerminal().AnyAsync(r => r.CaseId == caseId, ct))
            throw new ConflictException("This case already has an agent run in progress or awaiting approval.");

        if (!CaseStatusRules.CanStartAgentRun(cropCase!.Status))
            throw new BusinessRuleException("CASE_NOT_READY_FOR_AGENT", $"An agent run cannot start on a case that is {cropCase.Status}.");

        var now = UtcNow;
        var run = new AgentRun
        {
            CaseId = cropCase.Id,
            // The agent receives ids, not data: it reads the case through its tools. The objective
            // is the only text it is handed, and it is ours, never the farmer's.
            Objective = $"Resolve crop-health case {cropCase.ReferenceNo}: diagnose the reported problem and propose a compliant treatment prescription.",
            Status = AgentRunStatus.Planning,
            StartedAt = now
        };
        run.Events.Add(AgentRunLifecycle.StatusChanged(run.Id, null, AgentRunStatus.Planning, now,
            detail: new { requestedByRole = currentUser.Role?.ToString() }));

        cropCase.Status = CaseStatus.AgentProcessing;
        db.AgentRuns.Add(run);

        try
        {
            // The case's xmin guards against a double tap: two concurrent starts both read Submitted,
            // but only the first can commit the move to AgentProcessing.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("An agent run was started on this case at the same moment. Reload to see it.");
        }

        // Committed before dispatching, because the agent's first callback can arrive before this
        // method returns. From here on the request's token is ignored: a client that disconnects
        // must not leave a run half-dispatched.
        try
        {
            await dispatcher.DispatchAsync(new AgentDispatchRequest(run.Id, cropCase.Id, run.Objective, null), CancellationToken.None);
        }
        catch (AgentDispatchException ex)
        {
            // Safe failure (§9.6): recorded, visible, and the case goes to a human.
            logger.LogWarning(ex, "Agent run {RunId} could not be dispatched", run.Id);

            var failedAt = UtcNow;
            AgentRunLifecycle.End(run, AgentRunStatus.Failed, ex.Message, failedAt);
            db.AgentRunEvents.Add(new AgentRunEvent
            {
                AgentRunId = run.Id,
                EventType = AgentEventType.RunFailed,
                PayloadJson = AgentPayloads.ToStorable(new { reason = ex.Message }),
                OccurredAt = failedAt
            });
            db.AgentRunEvents.Add(AgentRunLifecycle.StatusChanged(run.Id, AgentRunStatus.Planning, AgentRunStatus.Failed, failedAt));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return new AgentRunStartedDto(run.Id, cropCase.Id, run.Status, run.FailureReason);
    }

    public async Task<AgentRunDto> GetAsync(Guid runId, CancellationToken ct = default)
    {
        var row = await db.AgentRuns.AsNoTracking().ScopedTo(currentUser)
            .Where(r => r.Id == runId)
            .Select(r => new
            {
                r.Id,
                r.CaseId,
                r.Case.ReferenceNo,
                r.Objective,
                r.Status,
                r.PlanJson,
                r.ProposalJson,
                r.VerdictJson,
                r.FinalOutcomeJson,
                r.FailureReason,
                r.RevisionCount,
                r.CreatedAt,
                r.StartedAt,
                r.CompletedAt,
                Steps = r.Steps.OrderBy(s => s.SequenceNo).ToList()
            })
            .FirstOrDefaultAsync(ct);

        CaseScope.EnsureVisible(row, await db.AgentRuns.AnyAsync(r => r.Id == runId, ct), "Agent run", runId);

        var proposal = AgentPayloads.ToElement(row!.ProposalJson);
        string? productName = null;
        if (proposal is { ValueKind: JsonValueKind.Object } p
            && p.TryGetProperty("product_id", out var id) && id.ValueKind == JsonValueKind.String
            && Guid.TryParse(id.GetString(), out var productId))
            productName = await db.Products.Where(x => x.Id == productId).Select(x => x.Name).FirstOrDefaultAsync(ct);

        return new AgentRunDto(
            row.Id,
            row.CaseId,
            row.ReferenceNo,
            row.Objective,
            row.Status,
            AgentPayloads.ToElement(row.PlanJson),
            proposal,
            AgentPayloads.ToElement(row.VerdictJson),
            AgentPayloads.ToElement(row.FinalOutcomeJson),
            row.FailureReason,
            row.RevisionCount,
            row.CreatedAt,
            row.StartedAt,
            row.CompletedAt,
            row.Steps.Select(s => new AgentRunStepDto(
                s.SequenceNo, s.AgentRole, s.Goal, s.Status, AgentPayloads.ToElement(s.OutputJson),
                s.ErrorMessage, s.RetryCount, s.StartedAt, s.CompletedAt, s.DurationMs)).ToList(),
            productName,
            row.Status == AgentRunStatus.Completed ? await IssuedPrescriptions.ForRunAsync(db, row.Id, ct) : null);
    }

    public async Task<PagedResult<AgentRunEventDto>> ListEventsAsync(Guid runId, AgentRunEventQuery query, CancellationToken ct = default)
    {
        if (!await db.AgentRuns.ScopedTo(currentUser).AnyAsync(r => r.Id == runId, ct))
            CaseScope.EnsureVisible<object>(null, await db.AgentRuns.AnyAsync(r => r.Id == runId, ct), "Agent run", runId);

        // Chronological. Ids are UUIDv7, so they break ties between events stamped in the same instant
        // in the order they were written.
        var page = await db.AgentRunEvents.AsNoTracking()
            .Where(e => e.AgentRunId == runId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.EventType, e.AgentRole, e.ToolName, e.PayloadJson, e.DurationMs, e.OccurredAt, e.CorrelationId })
            .ToPagedResultAsync(query, ct);

        return new PagedResult<AgentRunEventDto>(
            page.Items.Select(e => new AgentRunEventDto(
                e.Id, e.EventType, e.AgentRole, e.ToolName, AgentPayloads.ToElement(e.PayloadJson),
                e.DurationMs, e.OccurredAt, e.CorrelationId)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }
}
