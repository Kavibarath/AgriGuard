using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Echoes the scheme and client address the API believes it is serving, so the forwarded-headers
/// set-up can be tested end to end. Only registered by <see cref="AgriGuardApiFactory"/>.
/// </summary>
[ApiController]
[Route("_test/connection")]
[AllowAnonymous]
public sealed class ConnectionInfoController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        scheme = Request.Scheme,
        remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString()
    });
}
