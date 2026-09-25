using System.Text.RegularExpressions;
using AgriGuard.Api.Infrastructure;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriGuard.Api.Controllers;

/// <summary>Component B — starting agent runs and following them.</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed partial class AgentRunsController(IAgentRunService runs, IApprovalService approvals) : ControllerBase
{
    /// <summary>
    /// Non-CRUD (§5.1): creates an AgentRun, moves the case to AgentProcessing and hands the run to
    /// the agent service. Answers 202 at once — the run takes about a minute; poll the Location.
    /// If the agent service cannot be reached the run is recorded as Failed (still 202, with the
    /// reason) and the case goes to manual review. 409 if a run is already in flight.
    /// </summary>
    [HttpPost("cases/{caseId:guid}/agent-runs")]
    [EnableRateLimiting(RateLimitPolicies.Cases)]
    [ProducesResponseType<AgentRunStartedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AgentRunStartedDto>> Start(Guid caseId, CancellationToken ct)
    {
        var started = await runs.StartAsync(caseId, ct);
        return AcceptedAtAction(nameof(Get), new { id = started.RunId }, started);
    }

    /// <summary>Status, plan, per-step outcomes, the proposal and the validation verdict.</summary>
    [HttpGet("agent-runs/{id:guid}")]
    [ProducesResponseType<AgentRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<AgentRunDto> Get(Guid id, CancellationToken ct) => runs.GetAsync(id, ct);

    /// <summary>
    /// Non-CRUD (§5.1): the human gate. Approve issues the prescription, confirms the dealer order and
    /// draws the stock in one serializable transaction; reject ends the run; revise sends it back to
    /// the agent with the reason. Field Agronomists only, in their own district.
    ///
    /// Requires an <c>Idempotency-Key</c> header (e.g. a UUID per decision). Repeating a request with
    /// the same key returns the original result with <c>Idempotent-Replayed: true</c> and changes
    /// nothing. 409 if the run was already decided; 422 if the proposal no longer passes the rules
    /// or the stock is gone.
    /// </summary>
    [HttpPost("agent-runs/{id:guid}/decision")]
    [Authorize(Policy = AuthPolicies.CanApprovePrescriptions)]
    [ProducesResponseType<DecisionResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DecisionResultDto>> Decide(
        Guid id,
        DecideRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (idempotencyKey is null || !IdempotencyKeyFormat().IsMatch(idempotencyKey))
            throw new RequestValidationException("Idempotency-Key",
                "Send an Idempotency-Key header: 8–100 letters, digits or . _ : - (a new UUID per decision).");

        var result = await approvals.DecideAsync(id, request, idempotencyKey, ct);
        if (result.Replayed)
            Response.Headers["Idempotent-Replayed"] = "true";
        return result;
    }

    [GeneratedRegex(ApprovalLimits.IdempotencyKeyPattern)]
    private static partial Regex IdempotencyKeyFormat();

    /// <summary>The auditable timeline, oldest first: tool calls, timings, retries, verdicts, status changes.</summary>
    [HttpGet("agent-runs/{id:guid}/events")]
    [ProducesResponseType<PagedResult<AgentRunEventDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AgentRunEventDto>> Events(Guid id, [FromQuery] AgentRunEventQuery query, CancellationToken ct) =>
        runs.ListEventsAsync(id, query, ct);
}
