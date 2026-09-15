// Component A — Farm, Plot & Crop-Cycle Registry
using AgriGuard.Domain.Common;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Domain.Reference;

namespace AgriGuard.Domain.Registry;

public enum SoilType { Clay, Loam, SandyLoam, SiltLoam, Laterite, Peat }

public enum PlotStatus { Active, Fallow, Retired }

/// <summary>Ordered: the stage-transition rules rely on the numeric order.</summary>
public enum CropStage { Sown, Vegetative, Flowering, FruitSet, PreHarvest, Harvested }

public enum CropCycleStatus { Active, Harvested, Abandoned }

public enum ApplicationStatus { Scheduled, Applied, Cancelled }

public class Farm : AuditableEntity
{
    public Guid FarmerId { get; set; }
    public User Farmer { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Village { get; set; }

    public Guid DistrictId { get; set; }
    public District District { get; set; } = null!;

    public ICollection<Plot> Plots { get; set; } = new List<Plot>();
}

public class Plot : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Farm Farm { get; set; } = null!;

    /// <summary>Farmer-facing short code, unique within a farm (e.g. "P-07").</summary>
    public string PlotCode { get; set; } = string.Empty;
    public string? Name { get; set; }

    /// <summary>Drives every dose calculation: total quantity = dose/ha × area (rule V4).</summary>
    public decimal AreaHectares { get; set; }

    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public SoilType SoilType { get; set; }
    public PlotStatus Status { get; set; } = PlotStatus.Active;

    public ICollection<CropCycle> CropCycles { get; set; } = new List<CropCycle>();
}

public class CropCycle : AuditableEntity
{
    public Guid PlotId { get; set; }
    public Plot Plot { get; set; } = null!;

    public Guid CropId { get; set; }
    public Crop Crop { get; set; } = null!;

    public DateOnly SownDate { get; set; }
    public CropStage Stage { get; set; } = CropStage.Sown;
    public CropCycleStatus Status { get; set; } = CropCycleStatus.Active;

    /// <summary>Computed: SownDate + Crop.MaturityDays.</summary>
    public DateOnly ExpectedHarvestDate { get; set; }

    /// <summary>Farmer's intended harvest date; the pre-harvest-interval check (V5) uses this.</summary>
    public DateOnly? PlannedHarvestDate { get; set; }
    public DateOnly? ActualHarvestDate { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; set; }

    public ICollection<CropStageTransition> StageTransitions { get; set; } = new List<CropStageTransition>();
    public ICollection<ChemicalApplication> ChemicalApplications { get; set; } = new List<ChemicalApplication>();

    /// <summary>The date the PHI rule should be checked against.</summary>
    public DateOnly EffectiveHarvestDate => PlannedHarvestDate ?? ExpectedHarvestDate;
}

/// <summary>Append-only audit of stage changes.</summary>
public class CropStageTransition : Entity
{
    public Guid CropCycleId { get; set; }
    public CropCycle CropCycle { get; set; } = null!;

    public CropStage FromStage { get; set; }
    public CropStage ToStage { get; set; }
    public DateTime TransitionedAt { get; set; }
    public Guid TransitionedByUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>A scheduled or completed chemical application — the history the safety profile reads.</summary>
public class ChemicalApplication : AuditableEntity
{
    public Guid CropCycleId { get; set; }
    public CropCycle CropCycle { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? PrescriptionId { get; set; }
    public Prescription? Prescription { get; set; }

    public DateOnly ApplicationDate { get; set; }
    public decimal DosePerHectare { get; set; }
    public decimal TotalQuantity { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Scheduled;
}
