using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component C — a dealer's own stock and holds. Agro-Dealer only (policy ManagesInventory), and
/// every row is limited to the signed-in dealer's shop in the services.
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize(Policy = AuthPolicies.ManagesInventory)]
public sealed class InventoryController(IInventoryService inventory, IReservationService reservations) : ControllerBase
{
    /// <summary>The shop's batches, soonest expiry first, each flagged InDate / ExpiringSoon / Expired.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<InventoryBatchDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<InventoryBatchDto>> List([FromQuery] InventoryQuery query, CancellationToken ct) =>
        inventory.ListAsync(query, ct);

    /// <summary>A delivery received. 409 if the batch number is already on the shelf for this product.</summary>
    [HttpPost("batches")]
    [ProducesResponseType<InventoryBatchDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<InventoryBatchDto>> CreateBatch(CreateBatchRequest request, CancellationToken ct)
    {
        var batch = await inventory.CreateBatchAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, batch);
    }

    /// <summary>A recount, reprice or expiry correction. 422 if the count would fall below what is held.</summary>
    [HttpPut("batches/{id:guid}")]
    [ProducesResponseType<InventoryBatchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<InventoryBatchDto> UpdateBatch(Guid id, UpdateBatchRequest request, CancellationToken ct) =>
        inventory.UpdateBatchAsync(id, request, ct);

    [HttpGet("reservations")]
    [ProducesResponseType<PagedResult<ReservationDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ReservationDto>> Reservations([FromQuery] ReservationQuery query, CancellationToken ct) =>
        reservations.ListAsync(query, ct);

    [HttpGet("reservations/{id:guid}")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ReservationDto> Reservation(Guid id, CancellationToken ct) => reservations.GetAsync(id, ct);

    /// <summary>
    /// Non-CRUD (§5.1): holds stock for 24 hours. One serializable transaction: the batch rows are
    /// locked (SELECT … FOR UPDATE), drawn first-expiry-first-out in whole packs, and a Held
    /// reservation records which batches. 422 INSUFFICIENT_STOCK if the shop cannot cover it.
    /// </summary>
    [HttpPost("reservations")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReservationDto>> Reserve(CreateReservationRequest request, CancellationToken ct)
    {
        var reservation = await reservations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Reservation), new { id = reservation.Id }, reservation);
    }

    /// <summary>Non-CRUD: the held stock leaves the shelf. 409 if the hold is no longer Held; 422 if it expired.</summary>
    [HttpPost("reservations/{id:guid}/commit")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ReservationDto> Commit(Guid id, CancellationToken ct) => reservations.CommitAsync(id, ct);

    /// <summary>Non-CRUD: the held stock goes back on sale. 409 if the hold is no longer Held.</summary>
    [HttpPost("reservations/{id:guid}/release")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ReservationDto> Release(Guid id, CancellationToken ct) => reservations.ReleaseAsync(id, ct);
}
