// Component C — Dealer inventory, reservations, orders and issued prescriptions
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Common;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Reference;
using AgriGuard.Domain.Registry;

namespace AgriGuard.Domain.Inventory;

public enum ReservationStatus { Held, Committed, Released, Expired }

public enum OrderStatus { Draft, Confirmed, Packed, Collected, Cancelled }

public enum PrescriptionStatus { Draft, Issued, Applied, Revoked }

public class Dealer : AuditableEntity
{
    /// <summary>The AgroDealer login that manages this shop.</summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string ShopName { get; set; } = string.Empty;
    public string? Address { get; set; }

    public Guid DistrictId { get; set; }
    public District District { get; set; } = null!;

    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    public ICollection<InventoryBatch> Batches { get; set; } = new List<InventoryBatch>();
}

public class InventoryBatch : AuditableEntity
{
    public Guid DealerId { get; set; }
    public Dealer Dealer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string BatchNo { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }

    /// <summary>Physical stock, in the product's Unit.</summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>Held for pending approvals; not yet removed from the shelf.</summary>
    public decimal QuantityReserved { get; set; }

    public decimal UnitPrice { get; set; }

    public uint Version { get; set; }

    public decimal QuantityAvailable => QuantityOnHand - QuantityReserved;
}

/// <summary>
/// A hold on stock across one or more batches (FEFO: first-expiry-first-out).
/// Created when a proposal passes validation; committed on approval, released on reject/revise/expiry.
/// </summary>
public class StockReservation : AuditableEntity
{
    public Guid? AgentRunId { get; set; }
    public AgentRun? AgentRun { get; set; }

    public Guid DealerId { get; set; }
    public Dealer Dealer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal TotalQuantity { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Held;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public uint Version { get; set; }

    public ICollection<StockReservationLine> Lines { get; set; } = new List<StockReservationLine>();
}

public class StockReservationLine : Entity
{
    public Guid ReservationId { get; set; }
    public StockReservation Reservation { get; set; } = null!;

    public Guid BatchId { get; set; }
    public InventoryBatch Batch { get; set; } = null!;

    public decimal Quantity { get; set; }
}

public class InputOrder : AuditableEntity
{
    public string OrderNo { get; set; } = string.Empty;

    public Guid FarmerId { get; set; }
    public User Farmer { get; set; } = null!;

    public Guid DealerId { get; set; }
    public Dealer Dealer { get; set; } = null!;

    public Guid? PrescriptionId { get; set; }
    public Prescription? Prescription { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public decimal TotalAmount { get; set; }

    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CollectedAt { get; set; }

    public uint Version { get; set; }

    public ICollection<InputOrderLine> Lines { get; set; } = new List<InputOrderLine>();
}

public class InputOrderLine : Entity
{
    public Guid OrderId { get; set; }
    public InputOrder Order { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? BatchId { get; set; }
    public InventoryBatch? Batch { get; set; }

    public int Packs { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>The high-impact artefact. Only ever created Issued by the approval transaction.</summary>
public class Prescription : AuditableEntity
{
    public string PrescriptionNo { get; set; } = string.Empty;

    public Guid CaseId { get; set; }
    public CropCase Case { get; set; } = null!;

    public Guid? AgentRunId { get; set; }
    public AgentRun? AgentRun { get; set; }

    public Guid CropCycleId { get; set; }
    public CropCycle CropCycle { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? DiagnosedPathogenId { get; set; }
    public Pathogen? DiagnosedPathogen { get; set; }

    public decimal DosePerHectare { get; set; }
    public decimal TotalQuantity { get; set; }
    public DateOnly SprayDate { get; set; }

    /// <summary>SprayDate + PHI: the earliest safe harvest date, shown prominently to the farmer.</summary>
    public DateOnly EarliestSafeHarvestDate { get; set; }

    public string? Instructions { get; set; }
    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.Draft;

    public Guid? IssuedByUserId { get; set; }
    public User? IssuedBy { get; set; }
    public DateTime? IssuedAt { get; set; }
}
