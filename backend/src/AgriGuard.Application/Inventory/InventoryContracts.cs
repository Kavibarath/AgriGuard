using AgriGuard.Application.Common.Models;
using AgriGuard.Domain.Inventory;

namespace AgriGuard.Application.Inventory;

// ── Responses ────────────────────────────────────────────────────────────────

public sealed record ProductDto(
    Guid Id,
    string Name,
    string? Manufacturer,
    Guid ActiveIngredientId,
    string ActiveIngredientName,
    string? ResistanceGroup,
    Formulation Formulation,
    ProductUnit Unit,
    decimal PackSize,
    decimal UnitPrice,
    bool IsActive,
    // Crops the product has an active rule for: what it may legally be sprayed on today.
    int ApprovedCropCount);

public sealed record ActiveIngredientDto(Guid Id, string Name, string ChemicalClass, string? ResistanceGroup);

/// <summary>One row of the regulatory rules table, with the names the editor shows.</summary>
public sealed record ProductCropApprovalDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ActiveIngredientName,
    ProductUnit Unit,
    Guid CropId,
    string CropName,
    decimal MinDosePerHectare,
    decimal MaxDosePerHectare,
    int PreHarvestIntervalDays,
    int ReEntryIntervalHours,
    int MaxApplicationsPerCycle,
    int MinDaysBetweenApplications,
    int RainfastHours,
    bool IsRestricted,
    bool IsActive,
    DateTime UpdatedAt);

public sealed record InventoryBatchDto(
    Guid Id,
    Guid DealerId,
    string ShopName,
    Guid ProductId,
    string ProductName,
    ProductUnit Unit,
    decimal PackSize,
    string BatchNo,
    DateOnly ExpiryDate,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal QuantityAvailable,
    decimal UnitPrice,
    int DaysToExpiry,
    ExpiryState ExpiryState);

public sealed record ReservationLineDto(Guid BatchId, string BatchNo, DateOnly ExpiryDate, decimal Quantity);

public sealed record ReservationDto(
    Guid Id,
    Guid DealerId,
    string ShopName,
    Guid ProductId,
    string ProductName,
    ProductUnit Unit,
    decimal TotalQuantity,
    int Packs,
    ReservationStatus Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? ResolvedAt,
    // Set when the hold belongs to an agent run awaiting an agronomist's decision.
    Guid? AgentRunId,
    string? Note,
    IReadOnlyList<ReservationLineDto> Lines);

public sealed record OrderLineDto(Guid ProductId, string ProductName, ProductUnit Unit, int Packs, decimal Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderDto(
    Guid Id,
    string OrderNo,
    OrderStatus Status,
    // What the dealer's one button does next; null once the order is finished.
    OrderStatus? NextStatus,
    Guid DealerId,
    string ShopName,
    Guid FarmerId,
    string FarmerName,
    string? FarmerPhone,
    string? PrescriptionNo,
    DateOnly? SprayDate,
    decimal TotalAmount,
    OrderPaymentStatus PaymentStatus,
    DateTime? PaidAt,
    // How it was paid, and the card's brand and last four digits for a card payment.
    PaymentMethod? PaidBy,
    string? CardBrand,
    string? CardLast4,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? PackedAt,
    DateTime? CollectedAt,
    IReadOnlyList<OrderLineDto> Lines);

/// <summary>
/// An order as its farmer sees it on the phone: where to collect it, what is in it, how far the
/// dealer has got, and the pickup code (until it is collected).
/// </summary>
public sealed record FarmerOrderDto(
    Guid Id,
    string OrderNo,
    OrderStatus Status,
    string ShopName,
    string? ShopAddress,
    decimal ShopLatitude,
    decimal ShopLongitude,
    string? ShopPhone,
    string? PrescriptionNo,
    DateOnly? SprayDate,
    decimal TotalAmount,
    OrderPaymentStatus PaymentStatus,
    DateTime? PaidAt,
    PaymentMethod? PaidBy,
    string? CardBrand,
    string? CardLast4,
    // A card checkout still open for this order: the app checks it when the farmer comes back.
    Guid? OpenPaymentId,
    // Whether the farmer can pay by card right now (unpaid, payable, and card payments switched on).
    bool CanPayByCard,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? PackedAt,
    DateTime? CollectedAt,
    // Shown to the dealer at the counter. Null once the order is collected or cancelled.
    string? PickupCode,
    IReadOnlyList<OrderLineDto> Lines);

// ── Requests ─────────────────────────────────────────────────────────────────

public sealed record ProductRequest(
    string Name,
    string? Manufacturer,
    Guid ActiveIngredientId,
    Formulation Formulation,
    ProductUnit Unit,
    decimal PackSize,
    decimal UnitPrice,
    bool IsActive = true);

/// <summary>The limits one rule sets, shared by create and update so their checks cannot drift apart.</summary>
public interface IApprovalLimits
{
    decimal MinDosePerHectare { get; }
    decimal MaxDosePerHectare { get; }
    int PreHarvestIntervalDays { get; }
    int ReEntryIntervalHours { get; }
    int MaxApplicationsPerCycle { get; }
    int MinDaysBetweenApplications { get; }
    int RainfastHours { get; }
    bool IsRestricted { get; }
    bool IsActive { get; }
}

/// <summary>New limits for an existing rule. Its product and crop never change: delete and re-create instead.</summary>
public sealed record ApprovalLimitsRequest(
    decimal MinDosePerHectare,
    decimal MaxDosePerHectare,
    int PreHarvestIntervalDays,
    int ReEntryIntervalHours,
    int MaxApplicationsPerCycle,
    int MinDaysBetweenApplications,
    int RainfastHours,
    bool IsRestricted,
    bool IsActive) : IApprovalLimits;

public sealed record CreateApprovalRequest(
    Guid ProductId,
    Guid CropId,
    decimal MinDosePerHectare,
    decimal MaxDosePerHectare,
    int PreHarvestIntervalDays,
    int ReEntryIntervalHours,
    int MaxApplicationsPerCycle,
    int MinDaysBetweenApplications,
    int RainfastHours,
    bool IsRestricted,
    bool IsActive = true) : IApprovalLimits;

public sealed record CreateBatchRequest(Guid ProductId, string BatchNo, DateOnly ExpiryDate, decimal QuantityOnHand, decimal UnitPrice);

/// <summary>A stock count, price or expiry correction. Stock on hand can never drop below what is held.</summary>
public sealed record UpdateBatchRequest(DateOnly ExpiryDate, decimal QuantityOnHand, decimal UnitPrice);

public sealed record CreateReservationRequest(
    Guid ProductId,
    // In the product's unit; rounded up to whole packs, which is what leaves the shelf.
    decimal Quantity,
    // Only batches still in date on this day count. Defaults to today.
    DateOnly? UsableOn,
    string? Note);

/// <summary>To mark an order Collected, the dealer types the farmer's pickup code.</summary>
public sealed record FulfilOrderRequest(OrderStatus Status, string? PickupCode = null);

// ── Query options ────────────────────────────────────────────────────────────

public sealed record ProductQuery : PageRequest
{
    public string? Search { get; init; }
    /// <summary>Only products with an active rule for this crop.</summary>
    public Guid? CropId { get; init; }
    public Guid? ActiveIngredientId { get; init; }
    public bool IncludeInactive { get; init; }
}

public sealed record ApprovalQuery : PageRequest
{
    /// <summary>Product or active-ingredient name.</summary>
    public string? Search { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? CropId { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record InventoryQuery : PageRequest
{
    public Guid? DealerId { get; init; }
    public Guid? ProductId { get; init; }
    /// <summary>Batches expiring on or before this date, expired ones included.</summary>
    public DateOnly? ExpiringBefore { get; init; }
    /// <summary>Product name or batch number.</summary>
    public string? Search { get; init; }
    /// <summary>Batches sold down to zero are hidden unless asked for.</summary>
    public bool IncludeEmpty { get; init; }
}

public sealed record ReservationQuery : PageRequest
{
    public ReservationStatus? Status { get; init; }
}

public sealed record OrderQuery : PageRequest
{
    public OrderStatus? Status { get; init; }
    public Guid? DealerId { get; init; }
    /// <summary>Order number, prescription number or farmer name.</summary>
    public string? Search { get; init; }
}

// ── Services ─────────────────────────────────────────────────────────────────

/// <summary>The catalogue. Anyone signed in reads it; only a Co-op Administrator changes it.</summary>
public interface IProductService
{
    Task<PagedResult<ProductDto>> ListAsync(ProductQuery query, CancellationToken ct = default);
    Task<ProductDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(ProductRequest request, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(Guid id, ProductRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveIngredientDto>> ListActiveIngredientsAsync(CancellationToken ct = default);
}

/// <summary>
/// The regulatory rules table (ProductCropApproval). The validator reads it on every check, so a
/// saved change applies to the very next proposal with no code change or restart.
/// </summary>
public interface IProductCropApprovalService
{
    Task<PagedResult<ProductCropApprovalDto>> ListAsync(ApprovalQuery query, CancellationToken ct = default);
    Task<ProductCropApprovalDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ProductCropApprovalDto> CreateAsync(CreateApprovalRequest request, CancellationToken ct = default);
    Task<ProductCropApprovalDto> UpdateAsync(Guid id, ApprovalLimitsRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>A dealer's own shelf. Every method is limited to the signed-in dealer's shop.</summary>
public interface IInventoryService
{
    Task<PagedResult<InventoryBatchDto>> ListAsync(InventoryQuery query, CancellationToken ct = default);
    Task<InventoryBatchDto> CreateBatchAsync(CreateBatchRequest request, CancellationToken ct = default);
    Task<InventoryBatchDto> UpdateBatchAsync(Guid id, UpdateBatchRequest request, CancellationToken ct = default);
}

/// <summary>
/// Holds on stock: created Held for 24 hours, then committed (the stock leaves the shelf) or
/// released (it goes back on sale). Each is one serializable transaction with the batch rows locked.
/// </summary>
public interface IReservationService
{
    Task<PagedResult<ReservationDto>> ListAsync(ReservationQuery query, CancellationToken ct = default);
    Task<ReservationDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ReservationDto> CreateAsync(CreateReservationRequest request, CancellationToken ct = default);
    Task<ReservationDto> CommitAsync(Guid id, CancellationToken ct = default);
    Task<ReservationDto> ReleaseAsync(Guid id, CancellationToken ct = default);
}

public interface IOrderService
{
    Task<PagedResult<OrderDto>> ListAsync(OrderQuery query, CancellationToken ct = default);
    Task<OrderDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<OrderDto> FulfilAsync(Guid id, FulfilOrderRequest request, CancellationToken ct = default);

    /// <summary>The signed-in farmer's own orders, newest first. Farmers only.</summary>
    Task<PagedResult<FarmerOrderDto>> ListMineAsync(PageRequest query, CancellationToken ct = default);
}

public static class ReservationLimits
{
    /// <summary>How long a hold keeps stock off sale before the sweeper releases it (§6.1, §11 step 5).</summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromHours(24);

    public const int MaxNoteLength = 300;
}
