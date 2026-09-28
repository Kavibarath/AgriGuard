using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Harvest;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>Component D — when to harvest, and how much is expected. Rows are scoped like the registry.</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class HarvestController(IHarvestWindowService windows, IHarvestForecastService forecasts) : ControllerBase
{
    /// <summary>
    /// Non-CRUD (§5.1): ranked harvest days for a growing crop — crop maturity, intersected with the
    /// pre-harvest intervals of every spray on it (days before those clear are excluded), and scored
    /// against the Open-Meteo forecast for the harvesting hours.
    /// </summary>
    [HttpGet("harvest-windows/{cropCycleId:guid}")]
    [ProducesResponseType<HarvestWindowDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<HarvestWindowDto> Window(Guid cropCycleId, CancellationToken ct) => windows.GetAsync(cropCycleId, ct);

    [HttpGet("harvest-forecasts")]
    [ProducesResponseType<PagedResult<HarvestForecastDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<HarvestForecastDto>> Forecasts([FromQuery] HarvestForecastQuery query, CancellationToken ct) =>
        forecasts.ListAsync(query, ct);

    [HttpPost("harvest-forecasts")]
    [ProducesResponseType<HarvestForecastDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HarvestForecastDto>> CreateForecast(CreateHarvestForecastRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await forecasts.CreateAsync(request, ct));

    /// <summary>Records what was actually harvested, for the forecast-vs-actual report.</summary>
    [HttpPut("harvest-forecasts/{id:guid}/actual")]
    [ProducesResponseType<HarvestForecastDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<HarvestForecastDto> RecordActual(Guid id, RecordActualYieldRequest request, CancellationToken ct) =>
        forecasts.RecordActualAsync(id, request, ct);
}
