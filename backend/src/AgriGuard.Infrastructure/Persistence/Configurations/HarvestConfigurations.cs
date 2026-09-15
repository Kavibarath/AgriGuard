using AgriGuard.Domain.Harvest;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriGuard.Infrastructure.Persistence.Configurations;

internal sealed class HarvestForecastConfiguration : IEntityTypeConfiguration<HarvestForecast>
{
    public void Configure(EntityTypeBuilder<HarvestForecast> b)
    {
        b.Property(x => x.EstimatedYieldKg).HasPrecision(12, 2);
        b.Property(x => x.ActualYieldKg).HasPrecision(12, 2);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.CropCycle).WithMany().HasForeignKey(x => x.CropCycleId);
        b.HasIndex(x => new { x.CropCycleId, x.CreatedAt });

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_forecasts_estimated_yield", "estimated_yield_kg >= 0");
            t.HasCheckConstraint("ck_forecasts_actual_yield", "actual_yield_kg IS NULL OR actual_yield_kg >= 0");
        });
    }
}

internal sealed class CollectionCentreConfiguration : IEntityTypeConfiguration<CollectionCentre>
{
    public void Configure(EntityTypeBuilder<CollectionCentre> b)
    {
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.DailyCapacityKg).HasPrecision(12, 2);

        b.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.DistrictId);

        b.ToTable(t => t.HasCheckConstraint("ck_centres_capacity_positive", "daily_capacity_kg > 0"));
    }
}

internal sealed class CollectionSlotConfiguration : IEntityTypeConfiguration<CollectionSlot>
{
    public void Configure(EntityTypeBuilder<CollectionSlot> b)
    {
        b.Property(x => x.CapacityKg).HasPrecision(12, 2);
        b.Property(x => x.BookedKg).HasPrecision(12, 2);
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.Centre).WithMany(c => c.Slots).HasForeignKey(x => x.CentreId);
        b.HasIndex(x => new { x.CentreId, x.SlotDate, x.SlotIndex }).IsUnique();
        b.HasIndex(x => x.SlotDate);

        // Overbooking is impossible at the database level, whatever the allocation code does.
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_slots_capacity_positive", "capacity_kg > 0");
            t.HasCheckConstraint("ck_slots_booked_non_negative", "booked_kg >= 0");
            t.HasCheckConstraint("ck_slots_no_overbooking", "booked_kg <= capacity_kg");
            t.HasCheckConstraint("ck_slots_time_order", "end_time > start_time");
        });
    }
}

internal sealed class CollectionBookingConfiguration : IEntityTypeConfiguration<CollectionBooking>
{
    public void Configure(EntityTypeBuilder<CollectionBooking> b)
    {
        b.Property(x => x.BookingNo).HasMaxLength(20);
        b.Property(x => x.QuantityKg).HasPrecision(12, 2);
        b.Property(x => x.ActualQuantityKg).HasPrecision(12, 2);

        b.HasOne(x => x.Slot).WithMany(s => s.Bookings).HasForeignKey(x => x.SlotId);
        b.HasOne(x => x.CropCycle).WithMany().HasForeignKey(x => x.CropCycleId);
        b.HasOne(x => x.Farmer).WithMany().HasForeignKey(x => x.FarmerId);

        b.HasIndex(x => x.BookingNo).IsUnique();
        b.HasIndex(x => x.SlotId);
        b.HasIndex(x => x.FarmerId);

        b.ToTable(t => t.HasCheckConstraint("ck_bookings_quantity_positive", "quantity_kg > 0"));
    }
}

internal sealed class WeatherSnapshotConfiguration : IEntityTypeConfiguration<WeatherSnapshot>
{
    public void Configure(EntityTypeBuilder<WeatherSnapshot> b)
    {
        b.Property(x => x.LatitudeRounded).HasPrecision(5, 1);
        b.Property(x => x.LongitudeRounded).HasPrecision(5, 1);
        b.Property(x => x.ForecastJson).HasColumnType("jsonb");

        // One cached forecast per ~11 km grid cell, refreshed in place.
        b.HasIndex(x => new { x.LatitudeRounded, x.LongitudeRounded }).IsUnique();
        b.HasIndex(x => x.ExpiresAt);
    }
}
