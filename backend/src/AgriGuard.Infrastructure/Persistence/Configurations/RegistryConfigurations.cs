using AgriGuard.Domain.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriGuard.Infrastructure.Persistence.Configurations;

internal sealed class FarmConfiguration : IEntityTypeConfiguration<Farm>
{
    public void Configure(EntityTypeBuilder<Farm> b)
    {
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Village).HasMaxLength(150);

        b.HasOne(x => x.Farmer).WithMany().HasForeignKey(x => x.FarmerId);
        b.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId);

        b.HasIndex(x => x.FarmerId);
        b.HasIndex(x => x.DistrictId);
    }
}

internal sealed class PlotConfiguration : IEntityTypeConfiguration<Plot>
{
    public void Configure(EntityTypeBuilder<Plot> b)
    {
        b.Property(x => x.PlotCode).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.AreaHectares).HasPrecision(10, 4);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);

        b.HasOne(x => x.Farm).WithMany(f => f.Plots).HasForeignKey(x => x.FarmId);
        b.HasIndex(x => new { x.FarmId, x.PlotCode }).IsUnique();

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_plots_area_positive", "area_hectares > 0");
            t.HasCheckConstraint("ck_plots_latitude", "latitude BETWEEN -90 AND 90");
            t.HasCheckConstraint("ck_plots_longitude", "longitude BETWEEN -180 AND 180");
        });
    }
}

internal sealed class CropCycleConfiguration : IEntityTypeConfiguration<CropCycle>
{
    public void Configure(EntityTypeBuilder<CropCycle> b)
    {
        b.HasOne(x => x.Plot).WithMany(p => p.CropCycles).HasForeignKey(x => x.PlotId);
        b.HasOne(x => x.Crop).WithMany().HasForeignKey(x => x.CropId);

        b.Property(x => x.Version).IsRowVersion();

        // A plot can have only one active crop cycle at a time.
        b.HasIndex(x => x.PlotId).IsUnique().HasFilter("status = 'Active'")
            .HasDatabaseName("ux_crop_cycles_one_active_per_plot");
        b.HasIndex(x => new { x.PlotId, x.Status });

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_crop_cycles_expected_after_sown", "expected_harvest_date >= sown_date");
            t.HasCheckConstraint("ck_crop_cycles_planned_after_sown", "planned_harvest_date IS NULL OR planned_harvest_date >= sown_date");
        });
    }
}

internal sealed class CropStageTransitionConfiguration : IEntityTypeConfiguration<CropStageTransition>
{
    public void Configure(EntityTypeBuilder<CropStageTransition> b)
    {
        b.Property(x => x.Note).HasMaxLength(500);

        b.HasOne(x => x.CropCycle).WithMany(c => c.StageTransitions)
            .HasForeignKey(x => x.CropCycleId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.CropCycleId, x.TransitionedAt });
    }
}

internal sealed class ChemicalApplicationConfiguration : IEntityTypeConfiguration<ChemicalApplication>
{
    public void Configure(EntityTypeBuilder<ChemicalApplication> b)
    {
        b.Property(x => x.DosePerHectare).HasPrecision(10, 4);
        b.Property(x => x.TotalQuantity).HasPrecision(12, 4);

        b.HasOne(x => x.CropCycle).WithMany(c => c.ChemicalApplications).HasForeignKey(x => x.CropCycleId);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Prescription).WithMany().HasForeignKey(x => x.PrescriptionId);

        // Serves the safety-profile query: applications per product within a cycle, by date.
        b.HasIndex(x => new { x.CropCycleId, x.ProductId, x.ApplicationDate });

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_chemical_applications_dose_positive", "dose_per_hectare > 0");
            t.HasCheckConstraint("ck_chemical_applications_quantity_positive", "total_quantity > 0");
        });
    }
}
