using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Common;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Domain.Reference;
using AgriGuard.Domain.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AgriGuard.Infrastructure.Persistence;

public class AgriGuardDbContext(
    DbContextOptions<AgriGuardDbContext> options,
    TimeProvider? timeProvider = null,
    ICurrentUserAccessor? currentUser = null) : DbContext(options)
{
    // Identity & reference
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<Crop> Crops => Set<Crop>();
    public DbSet<Pathogen> Pathogens => Set<Pathogen>();

    // Component A — Registry
    public DbSet<Farm> Farms => Set<Farm>();
    public DbSet<Plot> Plots => Set<Plot>();
    public DbSet<CropCycle> CropCycles => Set<CropCycle>();
    public DbSet<CropStageTransition> CropStageTransitions => Set<CropStageTransition>();
    public DbSet<ChemicalApplication> ChemicalApplications => Set<ChemicalApplication>();

    // Component B — Cases & agent workflow
    public DbSet<CropCase> CropCases => Set<CropCase>();
    public DbSet<CaseAttachment> CaseAttachments => Set<CaseAttachment>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentRunStep> AgentRunSteps => Set<AgentRunStep>();
    public DbSet<AgentRunEvent> AgentRunEvents => Set<AgentRunEvent>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();

    // Component C — Catalogue, inventory & orders
    public DbSet<ActiveIngredient> ActiveIngredients => Set<ActiveIngredient>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCropApproval> ProductCropApprovals => Set<ProductCropApproval>();
    public DbSet<ProductTarget> ProductTargets => Set<ProductTarget>();
    public DbSet<Dealer> Dealers => Set<Dealer>();
    public DbSet<InventoryBatch> InventoryBatches => Set<InventoryBatch>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<StockReservationLine> StockReservationLines => Set<StockReservationLine>();
    public DbSet<InputOrder> InputOrders => Set<InputOrder>();
    public DbSet<InputOrderLine> InputOrderLines => Set<InputOrderLine>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();

    // Component D — Harvest, logistics & intelligence
    public DbSet<HarvestForecast> HarvestForecasts => Set<HarvestForecast>();
    public DbSet<CollectionCentre> CollectionCentres => Set<CollectionCentre>();
    public DbSet<CollectionSlot> CollectionSlots => Set<CollectionSlot>();
    public DbSet<CollectionBooking> CollectionBookings => Set<CollectionBooking>();
    public DbSet<WeatherSnapshot> WeatherSnapshots => Set<WeatherSnapshot>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Sensible default; money, coordinates and areas override this explicitly.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    /// <summary>Numbers case references (AG-2026-000142). A sequence, so concurrent submissions never collide.</summary>
    public const string CaseReferenceSequence = "case_reference_numbers";

    /// <summary>Numbers prescriptions (RX-2026-000001) and dealer orders (ORD-2026-000001).</summary>
    public const string PrescriptionSequence = "prescription_numbers";
    public const string InputOrderSequence = "input_order_numbers";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(CaseReferenceSequence);
        modelBuilder.HasSequence<long>(PrescriptionSequence);
        modelBuilder.HasSequence<long>(InputOrderSequence);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgriGuardDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Enums are stored as readable strings: safe to reorder, legible in psql.
            foreach (var property in entityType.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type.IsEnum)
                {
                    property.SetProviderClrType(typeof(string));
                    property.SetMaxLength(40);
                }
            }

            // Default every relationship to Restrict so history is never deleted by accident.
            // Configurations opt into Cascade explicitly where a child is meaningless without its parent.
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                if (foreignKey is IConventionForeignKey conventionKey
                    && conventionKey.GetDeleteBehaviorConfigurationSource() != ConfigurationSource.Explicit)
                {
                    foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampAuditFields()
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var userId = currentUser?.UserId;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.CreatedBy ??= userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    // Creation metadata is immutable once written.
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    break;
            }
        }
    }
}
