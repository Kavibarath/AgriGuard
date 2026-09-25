using AgriGuard.Api.Infrastructure;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriGuard.Api.Controllers;

/// <summary>Component B — starting agent runs and following them.</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class AgentRunsController(IAgentRunService runs) : ControllerBase
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

    /// <summary>The auditable timeline, oldest first: tool calls, timings, retries, verdicts, status changes.</summary>
    [HttpGet("agent-runs/{id:guid}/events")]
    [ProducesResponseType<PagedResult<AgentRunEventDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AgentRunEventDto>> Events(Guid id, [FromQuery] AgentRunEventQuery query, CancellationToken ct) =>
        runs.ListEventsAsync(id, query, ct);
}
