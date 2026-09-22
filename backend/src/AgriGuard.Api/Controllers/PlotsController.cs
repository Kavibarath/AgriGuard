using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>Component A — plots. Plots are where area, coordinates and crop cycles live.</summary>
[ApiController]
[Route("api/plots")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class PlotsController(IPlotService plots) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<PlotDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<PlotDto>> List([FromQuery] PlotQuery query, CancellationToken ct) =>
        plots.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PlotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<PlotDto> Get(Guid id, CancellationToken ct) => plots.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<PlotDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlotDto>> Create(CreatePlotRequest request, CancellationToken ct)
    {
        var plot = await plots.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = plot.Id }, plot);
    }

    /// <summary>422 if the change would invalidate an active cycle (area is locked while growing).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<PlotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<PlotDto> Update(Guid id, UpdatePlotRequest request, CancellationToken ct) =>
        plots.UpdateAsync(id, request, ct);
}
