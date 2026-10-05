using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using AgriGuard.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component C, the farmer's side: the input orders their approved prescriptions created, with
/// the dealer to collect from and the pickup code, and paying for them by card. Only the farmer
/// sees the code, and only the farmer pays in the app.
/// </summary>
[ApiController]
[Route("api/orders/mine")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class FarmerOrdersController(IOrderService orders, IPaymentService payments) : ControllerBase
{
    /// <summary>The signed-in farmer's orders, newest first. 403 for any other role.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<FarmerOrderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<FarmerOrderDto>> Mine([FromQuery] PageRequest query, CancellationToken ct) =>
        orders.ListMineAsync(query, ct);

    /// <summary>
    /// Non-CRUD: opens (or reopens) a card checkout for one of the farmer's orders and returns the
    /// provider's payment page to send them to. The order is not paid by this call; it is paid when
    /// the provider says so. 422 ORDER_NOT_PAYABLE when there is nothing to pay; 503
    /// CARD_PAYMENTS_UNAVAILABLE / PAYMENT_PROVIDER_UNAVAILABLE when card payments cannot be taken now.
    /// </summary>
    [HttpPost("{orderId:guid}/payments/card")]
    [ProducesResponseType<CardCheckoutDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<CardCheckoutDto> PayByCard(Guid orderId, CancellationToken ct) =>
        payments.StartCardPaymentAsync(orderId, ct);

    /// <summary>
    /// Asks the provider how a card attempt stands, records it, and returns the order's payment
    /// state. The app calls this when the farmer comes back from the payment page; repeating it is harmless.
    /// </summary>
    [HttpPost("{orderId:guid}/payments/{paymentId:guid}/sync")]
    [ProducesResponseType<OrderPaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<OrderPaymentDto> SyncCardPayment(Guid orderId, Guid paymentId, CancellationToken ct) =>
        payments.SyncCardPaymentAsync(orderId, paymentId, ct);
}
