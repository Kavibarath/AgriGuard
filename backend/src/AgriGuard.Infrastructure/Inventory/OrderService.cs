using System.Linq.Expressions;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using AgriGuard.Application.Payments;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Payments;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgriGuard.Infrastructure.Inventory;

/// <summary>
/// The dealer's side of an approved prescription. The approval transaction creates the order
/// Confirmed, with its stock already drawn; the dealer packs it and hands it over
/// (<see cref="OrderStatusRules"/>). Handing over needs the farmer's pickup code
/// (<see cref="PickupCodes"/>), which only the farmer sees, on the phone (<see cref="ListMineAsync"/>),
/// and only once the order is paid (<see cref="PaymentRules.ExplainHandOver"/>).
/// </summary>
public sealed class OrderService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    IPaymentGateway paymentGateway,
    TimeProvider timeProvider,
    ILogger<OrderService> logger) : IOrderService
{
    private static readonly Dictionary<string, Expression<Func<InputOrder, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orderNo"] = o => o.OrderNo,
        ["createdAt"] = o => o.CreatedAt,
        ["farmer"] = o => o.Farmer.FullName,
        ["status"] = o => o.Status,
        ["total"] = o => o.TotalAmount
    };

    public async Task<PagedResult<OrderDto>> ListAsync(OrderQuery query, CancellationToken ct = default)
    {
        var shopId = await db.OwnShopIdAsync(currentUser, ct);
        DealerScope.EnsureOwnShop(query.DealerId, shopId);
        if (shopId is null)
            return PagedResult<OrderDto>.Empty(query.NormalisedPage, query.NormalisedPageSize);

        var orders = db.InputOrders.AsNoTracking().Where(o => o.DealerId == shopId);
        if (query.Status is { } status)
            orders = orders.Where(o => o.Status == status);
        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{search.Trim()}%";
            orders = orders.Where(o => EF.Functions.ILike(o.OrderNo, pattern)
                                    || EF.Functions.ILike(o.Farmer.FullName, pattern)
                                    || (o.Prescription != null && EF.Functions.ILike(o.Prescription.PrescriptionNo, pattern)));
        }

        // Oldest first by default: the farmer who has waited longest is served first.
        var page = await orders
            .OrderByAllowed(query, Sortable, o => o.CreatedAt, o => o.Id)
            .Select(Projection)
            .ToPagedResultAsync(query, ct);
        return new PagedResult<OrderDto>([.. page.Items.Select(WithNextStep)], page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var shopId = await db.OwnShopIdAsync(currentUser, ct);
        var order = await db.InputOrders.AsNoTracking()
            .Where(o => o.Id == id && o.DealerId == shopId)
            .Select(Projection)
            .FirstOrDefaultAsync(ct);

        DealerScope.EnsureVisible(order, await db.InputOrders.AnyAsync(o => o.Id == id, ct), "Order", id);
        return WithNextStep(order!);
    }

    /// <summary>
    /// Non-CRUD (§5.1): moves the order one fulfilment step, to the status the dealer asked for.
    /// Naming the target (not "advance") makes a repeated click harmless: asking for Packed when it
    /// is already Packed changes nothing, instead of skipping on to Collected.
    /// </summary>
    public async Task<OrderDto> FulfilAsync(Guid id, FulfilOrderRequest request, CancellationToken ct = default)
    {
        var shopId = await db.RequireOwnShopAsync(currentUser, ct);
        var order = await db.InputOrders.FirstOrDefaultAsync(o => o.Id == id && o.DealerId == shopId, ct);
        DealerScope.EnsureVisible(order, await db.InputOrders.AnyAsync(o => o.Id == id, ct), "Order", id);

        if (order!.Status == request.Status)
            return await GetAsync(id, ct);

        if (OrderStatusRules.ExplainFulfilment(order.Status, request.Status) is { } refusal)
            throw new BusinessRuleException("ILLEGAL_ORDER_TRANSITION", refusal);

        // Nothing leaves the shop unpaid: cash at the counter, or the farmer's card in the app.
        if (request.Status == OrderStatus.Collected && PaymentRules.ExplainHandOver(order.PaymentStatus) is { } unpaid)
            throw new BusinessRuleException("ORDER_NOT_PAID", unpaid);

        // The packs go to the farmer the prescription was written for: they show a code only they have.
        if (request.Status == OrderStatus.Collected && order.PickupCode is { } code)
        {
            if (string.IsNullOrWhiteSpace(request.PickupCode))
                throw new BusinessRuleException("PICKUP_CODE_REQUIRED", "Ask the farmer for the six-digit pickup code on their phone.");
            if (!PickupCodes.Matches(code, request.PickupCode))
            {
                logger.LogWarning("Wrong pickup code for order {OrderNo} at dealer {DealerId}", order.OrderNo, shopId);
                throw new BusinessRuleException("WRONG_PICKUP_CODE", "That is not this order's pickup code. Check it with the farmer; nothing was handed over.");
            }
        }

        var from = order.Status;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        order.Status = request.Status;
        switch (request.Status)
        {
            case OrderStatus.Confirmed: order.ConfirmedAt = now; break;
            case OrderStatus.Packed: order.PackedAt = now; break;
            case OrderStatus.Collected: order.CollectedAt = now; break;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This order was updated at the same moment, perhaps from another screen. Reload to see where it is.");
        }

        logger.LogInformation("Order {OrderNo} {From} → {To} by dealer {DealerId}", order.OrderNo, from, request.Status, shopId);
        return await GetAsync(id, ct);
    }

    public async Task<PagedResult<FarmerOrderDto>> ListMineAsync(PageRequest query, CancellationToken ct = default)
    {
        if (currentUser.Role != UserRole.Farmer || currentUser.UserId is not { } farmerId)
            throw new ForbiddenAccessException("Only a farmer has orders of their own. Dealers see their shop's orders in /api/orders.");

        var page = await db.InputOrders.AsNoTracking()
            .Where(o => o.FarmerId == farmerId)
            .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
            .Select(o => new FarmerOrderDto(
                o.Id,
                o.OrderNo,
                o.Status,
                o.Dealer.ShopName,
                o.Dealer.Address,
                o.Dealer.Latitude,
                o.Dealer.Longitude,
                o.Dealer.User.PhoneNumber,
                o.Prescription != null ? o.Prescription.PrescriptionNo : null,
                o.Prescription != null ? (DateOnly?)o.Prescription.SprayDate : null,
                o.TotalAmount,
                o.PaymentStatus,
                o.PaidAt,
                o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => (PaymentMethod?)p.Method).FirstOrDefault(),
                o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => p.CardBrand).FirstOrDefault(),
                o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => p.CardLast4).FirstOrDefault(),
                o.Payments.Where(p => p.Method == PaymentMethod.Card && p.Status == PaymentAttemptStatus.Pending).Select(p => (Guid?)p.Id).FirstOrDefault(),
                false,
                o.CreatedAt,
                o.ConfirmedAt,
                o.PackedAt,
                o.CollectedAt,
                o.PickupCode,
                o.Lines
                    .Select(l => new OrderLineDto(l.ProductId, l.Product.Name, l.Product.Unit, l.Packs, l.Quantity, l.UnitPrice, l.LineTotal))
                    .ToList()))
            .ToPagedResultAsync(query, ct);

        // A used or void code is not worth showing: it can no longer hand anything over.
        return new PagedResult<FarmerOrderDto>(
            [.. page.Items.Select(o => o with
            {
                PickupCode = o.Status is OrderStatus.Collected or OrderStatus.Cancelled ? null : o.PickupCode,
                CanPayByCard = paymentGateway.IsConfigured && PaymentRules.ExplainCannotPay(o.Status, o.PaymentStatus, o.TotalAmount) is null
            })],
            page.Page, page.PageSize, page.TotalCount);
    }

    private static OrderDto WithNextStep(OrderDto order) => order with { NextStatus = OrderStatusRules.NextFulfilmentStep(order.Status) };

    private static Expression<Func<InputOrder, OrderDto>> Projection => o => new OrderDto(
        o.Id,
        o.OrderNo,
        o.Status,
        null,
        o.DealerId,
        o.Dealer.ShopName,
        o.FarmerId,
        o.Farmer.FullName,
        o.Farmer.PhoneNumber,
        o.Prescription != null ? o.Prescription.PrescriptionNo : null,
        o.Prescription != null ? (DateOnly?)o.Prescription.SprayDate : null,
        o.TotalAmount,
        o.PaymentStatus,
        o.PaidAt,
        o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => (PaymentMethod?)p.Method).FirstOrDefault(),
        o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => p.CardBrand).FirstOrDefault(),
        o.Payments.AsQueryable().Where(PaymentQueries.Settled).OrderBy(p => p.CompletedAt).Select(p => p.CardLast4).FirstOrDefault(),
        o.CreatedAt,
        o.ConfirmedAt,
        o.PackedAt,
        o.CollectedAt,
        o.Lines
            .Select(l => new OrderLineDto(l.ProductId, l.Product.Name, l.Product.Unit, l.Packs, l.Quantity, l.UnitPrice, l.LineTotal))
            .ToList());
}
