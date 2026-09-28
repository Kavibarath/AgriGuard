using AgriGuard.Application.Intelligence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component D — regional intelligence. Open to every signed-in role: the answer is an aggregate
/// with no farmer, plot or case in it, and a dealer stocking up before an outbreak is as much the
/// point as a farmer spraying before one.
/// </summary>
[ApiController]
[Route("api/intelligence")]
[Authorize]
public sealed class IntelligenceController(IOutbreakSignalService outbreakSignals) : ControllerBase
{
    /// <summary>
    /// Non-CRUD (§5.1): disease pressure from confirmed cases over the last <c>days</c> (1–90,
    /// default 14), for a crop and/or a district, or everywhere. Returns a 0–100 pressure index with
    /// its level and trend, the pathogens behind it, a day-by-day series, and every district's
    /// pressure for the map. The same calculation answers the agent's outbreak tool.
    /// </summary>
    [HttpGet("outbreak-signal")]
    [ProducesResponseType<OutbreakSignalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<OutbreakSignalDto> OutbreakSignal([FromQuery] OutbreakSignalQuery query, CancellationToken ct) =>
        outbreakSignals.ComputeAsync(query, ct);
}
