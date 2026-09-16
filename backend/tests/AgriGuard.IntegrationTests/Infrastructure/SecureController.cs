using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Stand-ins for the real endpoints each policy will eventually guard, so authorization can be
/// tested before those features exist. Only registered by <see cref="AgriGuardApiFactory"/>.
/// </summary>
[ApiController]
[Route("_test/secure")]
public sealed class SecureController(ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet("authenticated")]
    [Authorize]
    public IActionResult AnyLoggedInUser() => Ok();

    [HttpGet("approve")]
    [Authorize(Policy = AuthPolicies.CanApprovePrescriptions)]
    public IActionResult Approve() => Ok();

    [HttpGet("inventory")]
    [Authorize(Policy = AuthPolicies.ManagesInventory)]
    public IActionResult Inventory() => Ok();

    [HttpGet("rules")]
    [Authorize(Policy = AuthPolicies.AdministersRules)]
    public IActionResult Rules() => Ok();

    [HttpGet("owns-farm")]
    [Authorize(Policy = AuthPolicies.OwnsFarm)]
    public IActionResult OwnsFarm() => Ok();

    /// <summary>What the API reads back out of the token — proves the claims survive the round trip.</summary>
    [HttpGet("claims")]
    [Authorize]
    public IActionResult Claims() => Ok(new
    {
        userId = currentUser.UserId,
        role = currentUser.Role?.ToString(),
        districtId = currentUser.DistrictId,
        email = currentUser.Email
    });
}
