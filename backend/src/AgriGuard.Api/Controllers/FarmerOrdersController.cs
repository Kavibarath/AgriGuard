using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component C, the farmer's side: the input orders their approved prescriptions created, with
/// the dealer to collect from and the pickup code. Only the farmer sees the code.
/// </summary>
[ApiController]
[Route("api/orders/mine")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class FarmerOrdersController(IOrderService orders) : ControllerBase
{
    /// <summary>The signed-in farmer's orders, newest first. 403 for any other role.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<FarmerOrderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<FarmerOrderDto>> Mine([FromQuery] PageRequest query, CancellationToken ct) =>
        orders.ListMineAsync(query, ct);
}
