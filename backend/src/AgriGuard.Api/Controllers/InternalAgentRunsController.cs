using AgriGuard.Api.Infrastructure;
using AgriGuard.Application.Agent;
using AgriGuard.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Callbacks from the agent service (agent/app/runner.py BackendReporter). The agent has no
/// database access; these two endpoints are the only way anything it does is written down.
/// </summary>
[ApiController]
[Route("internal/agent-runs")]
[Authorize(Policy = AuthPolicies.AgentService)]
public sealed class InternalAgentRunsController(IAgentCallbackService callbacks) : ControllerBase
{
    /// <summary>Appends one event to the run's timeline and moves the run and its steps along.</summary>
    [HttpPost("{id:guid}/events")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordEvent(Guid id, AgentEventRequest request, CancellationToken ct)
    {
        await callbacks.RecordEventAsync(id, request, CorrelationIdMiddleware.Get(HttpContext), ct);
        return NoContent();
    }

    /// <summary>
    /// The run's final result. A proposal reported as ready for approval is re-validated here before
    /// the run goes to PendingApproval. 409 if the run has already ended.
    /// </summary>
    [HttpPost("{id:guid}/result")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordResult(Guid id, AgentRunResultRequest request, CancellationToken ct)
    {
        await callbacks.RecordResultAsync(id, request, CorrelationIdMiddleware.Get(HttpContext), ct);
        return NoContent();
    }
}
