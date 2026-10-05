using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using AgriGuard.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>Component C — a dealer's input orders, created by approved prescriptions. Agro-Dealer only, own shop.</summary>
[ApiController]
[Route("api/orders")]
[Authorize(Policy = AuthPolicies.ManagesInventory)]
public sealed class OrdersController(IOrderService orders, IPaymentService payments) : ControllerBase
{
    /// <summary>Oldest first by default, filterable by status; each order says its next fulfilment step.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<OrderDto>> List([FromQuery] OrderQuery query, CancellationToken ct) =>
        orders.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<OrderDto> Get(Guid id, CancellationToken ct) => orders.GetAsync(id, ct);

    /// <summary>
    /// Non-CRUD (§5.1): one fulfilment step, Confirmed → Packed → Collected. The body names the target
    /// status, so repeating a request is harmless. 422 ILLEGAL_ORDER_TRANSITION for any other move.
    /// </summary>
    [HttpPost("{id:guid}/fulfil")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<OrderDto> Fulfil(Guid id, FulfilOrderRequest request, CancellationToken ct) =>
        orders.FulfilAsync(id, request, ct);

    /// <summary>
    /// Non-CRUD: records that the farmer paid the order in cash at the counter, which allows the
    /// hand-over. Closes any card checkout the farmer has open. Repeating it changes nothing;
    /// 422 ORDER_NOT_PAYABLE for an order that cannot be paid (collected, cancelled).
    /// </summary>
    [HttpPost("{id:guid}/payments/cash")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<OrderDto> RecordCash(Guid id, CancellationToken ct)
    {
        await payments.RecordCashPaymentAsync(id, ct);
        return await orders.GetAsync(id, ct);
    }
}
