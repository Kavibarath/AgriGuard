// Component C — Agro-Input Catalogue and the regulatory rules table
using AgriGuard.Domain.Common;
using AgriGuard.Domain.Reference;

namespace AgriGuard.Domain.Inventory;

public enum ProductUnit { Litre, Kilogram }

/// <summary>EC emulsifiable concentrate · SC suspension concentrate · WP wettable powder ·
/// WG water-dispersible granule · SL soluble liquid · GR granule.</summary>
public enum Formulation { EC, SC, WP, WG, SL, GR }

public class ActiveIngredient : Entity
{
    public string Name { get; set; } = string.Empty;
    public string ChemicalClass { get; set; } = string.Empty;

    /// <summary>FRAC/IRAC mode-of-action group, used for resistance management (rule V7).</summary>
    public string? ResistanceGroup { get; set; }
}

public class Product : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }

    public Guid ActiveIngredientId { get; set; }
    public ActiveIngredient ActiveIngredient { get; set; } = null!;

    public Formulation Formulation { get; set; }
    public ProductUnit Unit { get; set; }

    /// <summary>Size of one sellable pack, in Unit (e.g. 0.5 L).</summary>
    public decimal PackSize { get; set; }

    /// <summary>Reference price per pack in LKR; dealers may hold batch-specific prices.</summary>
    public decimal UnitPrice { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ProductCropApproval> CropApprovals { get; set; } = new List<ProductCropApproval>();
    public ICollection<ProductTarget> Targets { get; set; } = new List<ProductTarget>();
}

/// <summary>
/// THE REGULATORY RULES TABLE. One row = "product P may be used on crop C within these limits".
/// The deterministic validator reads these values; nothing is hard-coded. Editing a row in the
/// admin screen changes system behaviour — this is the viva "modify a business rule" task.
/// </summary>
public class ProductCropApproval : AuditableEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid CropId { get; set; }
    public Crop Crop { get; set; } = null!;

    public decimal MinDosePerHectare { get; set; }      // V3
    public decimal MaxDosePerHectare { get; set; }      // V3
    public int PreHarvestIntervalDays { get; set; }     // V5
    public int ReEntryIntervalHours { get; set; }
    public int MaxApplicationsPerCycle { get; set; }    // V6
    public int MinDaysBetweenApplications { get; set; } // V7
    public int RainfastHours { get; set; }              // V8
    public bool IsRestricted { get; set; }              // V10
    public bool IsActive { get; set; } = true;          // V2
}

/// <summary>Which pathogens a product is labelled to control.</summary>
public class ProductTarget : Entity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid PathogenId { get; set; }
    public Pathogen Pathogen { get; set; } = null!;
}
