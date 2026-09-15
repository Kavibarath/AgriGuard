using AgriGuard.Domain.Common;

namespace AgriGuard.Domain.Reference;

public class District : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
}

public class Crop : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ScientificName { get; set; }

    /// <summary>Typical sowing-to-harvest days; drives ExpectedHarvestDate.</summary>
    public int MaturityDays { get; set; }
}

public enum PathogenType
{
    FungalDisease,
    BacterialDisease,
    ViralDisease,
    InsectPest,
    Mite,
    Nematode
}

/// <summary>A pest or disease the Diagnosis agent can name and a product can target.</summary>
public class Pathogen : Entity
{
    public string Code { get; set; } = string.Empty;
    public string CommonName { get; set; } = string.Empty;
    public string? ScientificName { get; set; }
    public PathogenType Type { get; set; }

    /// <summary>Symptom codes that suggest this pathogen (matched against case symptoms).</summary>
    public List<string> IndicativeSymptoms { get; set; } = new();
}
