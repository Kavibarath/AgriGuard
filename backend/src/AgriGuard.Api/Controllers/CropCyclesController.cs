using AgriGuard.Application.Auth;
using AgriGuard.Application.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>Component A — crop cycles, including the stage-advance operation.</summary>
[ApiController]
[Route("api/crop-cycles")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class CropCyclesController(ICropCycleService cycles) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CropCycleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<CropCycleDto> Get(Guid id, CancellationToken ct) => cycles.GetAsync(id, ct);

    /// <summary>Sows a plot. 409 if the plot already has an active cycle.</summary>
    [HttpPost]
    [ProducesResponseType<CropCycleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CropCycleDto>> Create(CreateCropCycleRequest request, CancellationToken ct)
    {
        var cycle = await cycles.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = cycle.Id }, cycle);
    }

    /// <summary>
    /// Non-CRUD (§5.1): moves the cycle one stage along the legal-transition matrix, re-estimates
    /// the expected harvest date from how early or late the stage arrived, and writes an audit row.
    /// Returns 422 with code ILLEGAL_STAGE_TRANSITION when the move is not allowed.
    /// </summary>
    [HttpPost("{id:guid}/advance-stage")]
    [ProducesResponseType<CropCycleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<CropCycleDto> AdvanceStage(Guid id, AdvanceStageRequest request, CancellationToken ct) =>
        cycles.AdvanceStageAsync(id, request, ct);
}
