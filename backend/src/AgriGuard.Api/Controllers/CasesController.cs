using AgriGuard.Api.Infrastructure;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component B — crop-health cases. The policy admits farmers, agronomists and administrators;
/// which cases each sees, and who may change what, is decided per row in the service.
/// </summary>
[ApiController]
[Route("api/cases")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class CasesController(ICaseService cases) : ControllerBase
{
    /// <summary>The case queue: filter by status, crop, district, severity or plot; search by reference, plot or farmer.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CaseSummaryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CaseSummaryDto>> List([FromQuery] CaseQuery query, CancellationToken ct) =>
        cases.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CaseDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<CaseDetailDto> Get(Guid id, CancellationToken ct) => cases.GetAsync(id, ct);

    /// <summary>Reports a problem on a plot's current crop. Symptom codes come from the closed catalogue.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Cases)]
    [ProducesResponseType<CaseDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CaseDetailDto>> Create(CreateCaseRequest request, CancellationToken ct)
    {
        var created = await cases.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>
    /// Closes a case, or sends it to manual review. Every other status is set by the agent workflow;
    /// asking for one here gives 422 with code ILLEGAL_CASE_STATUS_CHANGE.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<CaseDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<CaseDetailDto> UpdateStatus(Guid id, UpdateCaseStatusRequest request, CancellationToken ct) =>
        cases.UpdateStatusAsync(id, request, ct);
}
