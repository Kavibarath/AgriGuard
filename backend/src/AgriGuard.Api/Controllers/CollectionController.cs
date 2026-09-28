using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Harvest;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>Component D — collection centres, their daily slots, and harvest bookings.</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class CollectionController(ICollectionService collection) : ControllerBase
{
    [HttpGet("collection-centres")]
    [ProducesResponseType<IReadOnlyList<CollectionCentreDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CollectionCentreDto>> Centres([FromQuery] Guid? districtId, CancellationToken ct) =>
        collection.ListCentresAsync(districtId, ct);

    /// <summary>Slots by date, centre or district, with the room left in each.</summary>
    [HttpGet("collection-slots")]
    [ProducesResponseType<PagedResult<CollectionSlotDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CollectionSlotDto>> Slots([FromQuery] CollectionSlotQuery query, CancellationToken ct) =>
        collection.ListSlotsAsync(query, ct);

    /// <summary>Opens a slot. Co-op Administrator only; a centre's slots on one day may not exceed its daily capacity.</summary>
    [HttpPost("collection-slots")]
    [Authorize(Policy = AuthPolicies.AdministersRules)]
    [ProducesResponseType<CollectionSlotDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CollectionSlotDto>> CreateSlot(CreateCollectionSlotRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await collection.CreateSlotAsync(request, ct));

    [HttpGet("collection-bookings")]
    [ProducesResponseType<PagedResult<CollectionBookingDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CollectionBookingDto>> Bookings([FromQuery] CollectionBookingQuery query, CancellationToken ct) =>
        collection.ListBookingsAsync(query, ct);

    /// <summary>
    /// Non-CRUD (§5.1): books a harvest into a slot with room for all of it — the preferred day
    /// first, then up to two days later; on a day, the nearest centre. One serializable
    /// transaction with the slots locked, so capacity is never overbooked. 422 HARVEST_BEFORE_PHI
    /// before the crop is safe to harvest, 422 NO_CAPACITY when nothing has room.
    /// </summary>
    [HttpPost("collection-bookings/allocate")]
    [ProducesResponseType<CollectionBookingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CollectionBookingDto>> Allocate(AllocateBookingRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await collection.AllocateAsync(request, ct));

    /// <summary>Cancels a booking and gives its room back to the slot. Not after the collection day.</summary>
    [HttpPost("collection-bookings/{id:guid}/cancel")]
    [ProducesResponseType<CollectionBookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<CollectionBookingDto> Cancel(Guid id, CancellationToken ct) => collection.CancelAsync(id, ct);
}
