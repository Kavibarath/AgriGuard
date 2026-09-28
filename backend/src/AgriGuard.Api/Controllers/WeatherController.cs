using AgriGuard.Application.Auth;
using AgriGuard.Application.Weather;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component D — weather at a plot, from Open-Meteo through the backend (§10). Clients never call
/// Open-Meteo themselves: the backend caches it, rounds the coordinates it sends, and judges each
/// day with the same rule the prescription validator applies (V8).
/// </summary>
[ApiController]
[Route("api/weather")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class WeatherController(ISprayWindowService sprayWindows) : ControllerBase
{
    /// <summary>
    /// The next <paramref name="days"/> days (1–14) on a plot the caller may see, each marked
    /// suitable or not for spraying, with the reasons. Naming a product uses its own rainfast time
    /// on the plot's crop. When the forecast is unavailable, <c>forecastAvailable</c> is false and
    /// no day is guessed.
    /// </summary>
    [HttpGet("spray-window")]
    [ProducesResponseType<SprayWindowDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<SprayWindowDto> SprayWindow(
        [FromQuery, BindRequired] Guid plotId,
        [FromQuery] int days = 7,
        [FromQuery] Guid? productId = null,
        CancellationToken ct = default) =>
        sprayWindows.ForPlotAsync(plotId, days, productId, ct);
}
