using AgriGuard.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriGuard.Infrastructure.Persistence.Configurations;

internal sealed class ActiveIngredientConfiguration : IEntityTypeConfiguration<ActiveIngredient>
{
    public void Configure(EntityTypeBuilder<ActiveIngredient> b)
    {
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.ChemicalClass).HasMaxLength(100);
        b.Property(x => x.ResistanceGroup).HasMaxLength(20);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Manufacturer).HasMaxLength(150);
        b.Property(x => x.PackSize).HasPrecision(10, 3);
        b.Property(x => x.UnitPrice).HasPrecision(12, 2);

        b.HasOne(x => x.ActiveIngredient).WithMany().HasForeignKey(x => x.ActiveIngredientId);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.ActiveIngredientId);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_products_pack_size_positive", "pack_size > 0");
            t.HasCheckConstraint("ck_products_unit_price_non_negative", "unit_price >= 0");
        });
    }
}

internal sealed class ProductCropApprovalConfiguration : IEntityTypeConfiguration<ProductCropApproval>
{
    public void Configure(EntityTypeBuilder<ProductCropApproval> b)
    {
        b.Property(x => x.MinDosePerHectare).HasPrecision(10, 4);
        b.Property(x => x.MaxDosePerHectare).HasPrecision(10, 4);

        b.HasOne(x => x.Product).WithMany(p => p.CropApprovals).HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Crop).WithMany().HasForeignKey(x => x.CropId);

        // Exactly one rule row per product-crop pair: the validator never has to pick between conflicting limits.
        b.HasIndex(x => new { x.ProductId, x.CropId }).IsUnique();
        b.HasIndex(x => new { x.CropId, x.IsActive });

        // The database itself refuses a nonsensical rule, even if the admin UI or API has a bug.
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_approvals_min_dose_positive", "min_dose_per_hectare > 0");
            t.HasCheckConstraint("ck_approvals_dose_range", "min_dose_per_hectare <= max_dose_per_hectare");
            t.HasCheckConstraint("ck_approvals_phi_non_negative", "pre_harvest_interval_days >= 0");
            t.HasCheckConstraint("ck_approvals_rei_non_negative", "re_entry_interval_hours >= 0");
            t.HasCheckConstraint("ck_approvals_max_applications", "max_applications_per_cycle >= 1");
            t.HasCheckConstraint("ck_approvals_min_interval", "min_days_between_applications >= 0");
            t.HasCheckConstraint("ck_approvals_rainfast", "rainfast_hours >= 0");
        });
    }
}

internal sealed class ProductTargetConfiguration : IEntityTypeConfiguration<ProductTarget>
{
    public void Configure(EntityTypeBuilder<ProductTarget> b)
    {
        b.HasOne(x => x.Product).WithMany(p => p.Targets)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Pathogen).WithMany().HasForeignKey(x => x.PathogenId);

        b.HasIndex(x => new { x.ProductId, x.PathogenId }).IsUnique();
        b.HasIndex(x => x.PathogenId);
    }
}

internal sealed class DealerConfiguration : IEntityTypeConfiguration<Dealer>
{
    public void Configure(EntityTypeBuilder<Dealer> b)
    {
        b.Property(x => x.ShopName).HasMaxLength(150);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId);

        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => x.DistrictId);
    }
}

internal sealed class InventoryBatchConfiguration : IEntityTypeConfiguration<InventoryBatch>
{
    public void Configure(EntityTypeBuilder<InventoryBatch> b)
    {
        b.Property(x => x.BatchNo).HasMaxLength(50);
        b.Property(x => x.QuantityOnHand).HasPrecision(12, 4);
        b.Property(x => x.QuantityReserved).HasPrecision(12, 4);
        b.Property(x => x.UnitPrice).HasPrecision(12, 2);
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.Dealer).WithMany(d => d.Batches).HasForeignKey(x => x.DealerId);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);

        b.HasIndex(x => new { x.DealerId, x.ProductId, x.BatchNo }).IsUnique();

        // Stock-availability lookups only care about batches that still hold stock.
        b.HasIndex(x => new { x.ProductId, x.DealerId, x.ExpiryDate })
            .HasFilter("quantity_on_hand > 0")
            .HasDatabaseName("ix_inventory_batches_in_stock");

        // Last line of defence against overselling, beneath the transactional reservation logic.
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_batches_on_hand_non_negative", "quantity_on_hand >= 0");
            t.HasCheckConstraint("ck_batches_reserved_non_negative", "quantity_reserved >= 0");
            t.HasCheckConstraint("ck_batches_reserved_within_on_hand", "quantity_reserved <= quantity_on_hand");
        });
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> b)
    {
        b.Property(x => x.TotalQuantity).HasPrecision(12, 4);
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.AgentRun).WithMany().HasForeignKey(x => x.AgentRunId);
        b.HasOne(x => x.Dealer).WithMany().HasForeignKey(x => x.DealerId);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);

        // The expiry sweeper scans held reservations past their deadline.
        b.HasIndex(x => new { x.Status, x.ExpiresAt });
        b.HasIndex(x => x.AgentRunId);

        b.ToTable(t => t.HasCheckConstraint("ck_reservations_quantity_positive", "total_quantity > 0"));
    }
}

internal sealed class StockReservationLineConfiguration : IEntityTypeConfiguration<StockReservationLine>
{
    public void Configure(EntityTypeBuilder<StockReservationLine> b)
    {
        b.Property(x => x.Quantity).HasPrecision(12, 4);

        b.HasOne(x => x.Reservation).WithMany(r => r.Lines)
            .HasForeignKey(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId);

        b.HasIndex(x => new { x.ReservationId, x.BatchId }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("ck_reservation_lines_quantity_positive", "quantity > 0"));
    }
}

internal sealed class InputOrderConfiguration : IEntityTypeConfiguration<InputOrder>
{
    public void Configure(EntityTypeBuilder<InputOrder> b)
    {
        b.Property(x => x.OrderNo).HasMaxLength(20);
        b.Property(x => x.TotalAmount).HasPrecision(14, 2);
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.Farmer).WithMany().HasForeignKey(x => x.FarmerId);
        b.HasOne(x => x.Dealer).WithMany().HasForeignKey(x => x.DealerId);
        b.HasOne(x => x.Prescription).WithMany().HasForeignKey(x => x.PrescriptionId);

        b.HasIndex(x => x.OrderNo).IsUnique();
        b.HasIndex(x => new { x.DealerId, x.Status });
        b.HasIndex(x => x.FarmerId);

        b.ToTable(t => t.HasCheckConstraint("ck_orders_total_non_negative", "total_amount >= 0"));
    }
}

internal sealed class InputOrderLineConfiguration : IEntityTypeConfiguration<InputOrderLine>
{
    public void Configure(EntityTypeBuilder<InputOrderLine> b)
    {
        b.Property(x => x.Quantity).HasPrecision(12, 4);
        b.Property(x => x.UnitPrice).HasPrecision(12, 2);
        b.Property(x => x.LineTotal).HasPrecision(14, 2);

        b.HasOne(x => x.Order).WithMany(o => o.Lines)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_order_lines_packs_positive", "packs > 0");
            t.HasCheckConstraint("ck_order_lines_quantity_positive", "quantity > 0");
        });
    }
}

internal sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> b)
    {
        b.Property(x => x.PrescriptionNo).HasMaxLength(20);
        b.Property(x => x.DosePerHectare).HasPrecision(10, 4);
        b.Property(x => x.TotalQuantity).HasPrecision(12, 4);
        b.Property(x => x.Instructions).HasMaxLength(2000);

        b.HasOne(x => x.Case).WithMany().HasForeignKey(x => x.CaseId);
        b.HasOne(x => x.AgentRun).WithMany().HasForeignKey(x => x.AgentRunId);
        b.HasOne(x => x.CropCycle).WithMany().HasForeignKey(x => x.CropCycleId);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.DiagnosedPathogen).WithMany().HasForeignKey(x => x.DiagnosedPathogenId);
        b.HasOne(x => x.IssuedBy).WithMany().HasForeignKey(x => x.IssuedByUserId);

        b.HasIndex(x => x.PrescriptionNo).IsUnique();
        b.HasIndex(x => x.CaseId);

        // One prescription per agent run: a replayed approval cannot issue a second one (G12).
        b.HasIndex(x => x.AgentRunId).IsUnique().HasFilter("agent_run_id IS NOT NULL");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_prescriptions_dose_positive", "dose_per_hectare > 0");
            t.HasCheckConstraint("ck_prescriptions_quantity_positive", "total_quantity > 0");
            t.HasCheckConstraint("ck_prescriptions_safe_harvest_after_spray", "earliest_safe_harvest_date >= spray_date");
        });
    }
}
