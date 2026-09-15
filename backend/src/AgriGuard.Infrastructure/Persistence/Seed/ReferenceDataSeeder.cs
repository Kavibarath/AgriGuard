using AgriGuard.Domain.Inventory;
using AgriGuard.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds reference and regulatory data. Idempotent: skips if districts already exist.
///
/// ⚠ ACADEMIC SAMPLE DATA. Dose ranges, pre-harvest intervals and other limits are plausible
/// values chosen to exercise the validation rules. They are NOT authoritative regulatory limits
/// and must not be used for real agronomic advice. Product names are generic, not real brands.
///
/// Demo users, farms, dealers, stock and historical cases are seeded separately once
/// password hashing exists.
/// </summary>
public static class ReferenceDataSeeder
{
    public static async Task SeedAsync(AgriGuardDbContext db, CancellationToken ct = default)
    {
        if (await db.Districts.AnyAsync(ct))
            return;

        var districts = new[]
        {
            new District { Code = "NUW", Name = "Nuwara Eliya", Province = "Central" },
            new District { Code = "MTL", Name = "Matale", Province = "Central" },
            new District { Code = "BAD", Name = "Badulla", Province = "Uva" },
            new District { Code = "ANU", Name = "Anuradhapura", Province = "North Central" }
        };
        db.Districts.AddRange(districts);

        var crops = new[]
        {
            new Crop { Code = "TOM", Name = "Tomato", ScientificName = "Solanum lycopersicum", MaturityDays = 110 },
            new Crop { Code = "CHI", Name = "Chilli", ScientificName = "Capsicum annuum", MaturityDays = 150 },
            new Crop { Code = "RIC", Name = "Paddy (Rice)", ScientificName = "Oryza sativa", MaturityDays = 120 },
            new Crop { Code = "POT", Name = "Potato", ScientificName = "Solanum tuberosum", MaturityDays = 100 },
            new Crop { Code = "CAB", Name = "Cabbage", ScientificName = "Brassica oleracea var. capitata", MaturityDays = 90 },
            new Crop { Code = "BRI", Name = "Brinjal", ScientificName = "Solanum melongena", MaturityDays = 130 },
            new Crop { Code = "ONI", Name = "Big Onion", ScientificName = "Allium cepa", MaturityDays = 120 },
            new Crop { Code = "CAR", Name = "Carrot", ScientificName = "Daucus carota", MaturityDays = 95 },
            new Crop { Code = "BEA", Name = "Beans", ScientificName = "Phaseolus vulgaris", MaturityDays = 75 },
            new Crop { Code = "MAI", Name = "Maize", ScientificName = "Zea mays", MaturityDays = 110 },
            new Crop { Code = "CUC", Name = "Cucumber", ScientificName = "Cucumis sativus", MaturityDays = 70 },
            new Crop { Code = "OKR", Name = "Okra", ScientificName = "Abelmoschus esculentus", MaturityDays = 90 }
        };
        db.Crops.AddRange(crops);

        var pathogens = new Dictionary<string, Pathogen>
        {
            ["LATE_BLIGHT"] = P("LATE_BLIGHT", "Late blight", "Phytophthora infestans", PathogenType.FungalDisease,
                "leaf_water_soaked_lesions", "leaf_brown_patches", "leaf_white_mould_underside", "stem_dark_lesions"),
            ["EARLY_BLIGHT"] = P("EARLY_BLIGHT", "Early blight", "Alternaria solani", PathogenType.FungalDisease,
                "leaf_concentric_rings", "leaf_brown_spots", "leaf_yellowing"),
            ["POWDERY_MILDEW"] = P("POWDERY_MILDEW", "Powdery mildew", "Leveillula taurica", PathogenType.FungalDisease,
                "leaf_white_powder", "leaf_yellowing"),
            ["ANTHRACNOSE"] = P("ANTHRACNOSE", "Anthracnose", "Colletotrichum spp.", PathogenType.FungalDisease,
                "fruit_sunken_lesions", "leaf_brown_spots"),
            ["RICE_BLAST"] = P("RICE_BLAST", "Rice blast", "Magnaporthe oryzae", PathogenType.FungalDisease,
                "leaf_diamond_lesions", "panicle_neck_rot"),
            // No chemical cure — a correct diagnosis should route to manual review, not a prescription.
            ["BACTERIAL_WILT"] = P("BACTERIAL_WILT", "Bacterial wilt", "Ralstonia solanacearum", PathogenType.BacterialDisease,
                "plant_wilting", "stem_vascular_browning"),
            ["LEAF_CURL_VIRUS"] = P("LEAF_CURL_VIRUS", "Leaf curl virus", "Begomovirus spp.", PathogenType.ViralDisease,
                "leaf_curling", "plant_stunting", "leaf_yellowing"),
            ["WHITEFLY"] = P("WHITEFLY", "Whitefly", "Bemisia tabaci", PathogenType.InsectPest,
                "insects_white_underside", "leaf_sticky_honeydew", "leaf_yellowing"),
            ["APHID"] = P("APHID", "Aphids", "Aphis gossypii", PathogenType.InsectPest,
                "insects_clusters_on_shoots", "leaf_curling", "leaf_sticky_honeydew"),
            ["THRIPS"] = P("THRIPS", "Thrips", "Thrips palmi", PathogenType.InsectPest,
                "leaf_silvering", "leaf_curling", "flower_drop"),
            ["FRUIT_BORER"] = P("FRUIT_BORER", "Fruit borer", "Helicoverpa armigera", PathogenType.InsectPest,
                "fruit_bore_holes", "fruit_frass"),
            ["DIAMONDBACK_MOTH"] = P("DIAMONDBACK_MOTH", "Diamondback moth", "Plutella xylostella", PathogenType.InsectPest,
                "leaf_window_holes", "larvae_small_green"),
            ["BROWN_PLANTHOPPER"] = P("BROWN_PLANTHOPPER", "Brown planthopper", "Nilaparvata lugens", PathogenType.InsectPest,
                "plant_hopperburn", "insects_at_plant_base"),
            ["FALL_ARMYWORM"] = P("FALL_ARMYWORM", "Fall armyworm", "Spodoptera frugiperda", PathogenType.InsectPest,
                "leaf_ragged_holes", "whorl_frass"),
            ["SPIDER_MITE"] = P("SPIDER_MITE", "Two-spotted spider mite", "Tetranychus urticae", PathogenType.Mite,
                "leaf_stippling", "fine_webbing")
        };
        db.Pathogens.AddRange(pathogens.Values);

        // Active ingredients with FRAC (fungicide) / IRAC (insecticide) mode-of-action groups.
        var ingredients = new Dictionary<string, ActiveIngredient>
        {
            ["Mancozeb"] = AI("Mancozeb", "Dithiocarbamate", "FRAC M03"),
            ["Chlorothalonil"] = AI("Chlorothalonil", "Chloronitrile", "FRAC M05"),
            ["Metalaxyl"] = AI("Metalaxyl", "Phenylamide", "FRAC 4"),
            ["Azoxystrobin"] = AI("Azoxystrobin", "Strobilurin (QoI)", "FRAC 11"),
            ["Difenoconazole"] = AI("Difenoconazole", "Triazole (DMI)", "FRAC 3"),
            ["Copper hydroxide"] = AI("Copper hydroxide", "Inorganic copper", "FRAC M01"),
            ["Sulphur"] = AI("Sulphur", "Inorganic sulphur", "FRAC M02"),
            ["Tricyclazole"] = AI("Tricyclazole", "Melanin biosynthesis inhibitor", "FRAC 16.1"),
            ["Imidacloprid"] = AI("Imidacloprid", "Neonicotinoid", "IRAC 4A"),
            ["Thiamethoxam"] = AI("Thiamethoxam", "Neonicotinoid", "IRAC 4A"),
            ["Acetamiprid"] = AI("Acetamiprid", "Neonicotinoid", "IRAC 4A"),
            ["Abamectin"] = AI("Abamectin", "Avermectin", "IRAC 6"),
            ["Emamectin benzoate"] = AI("Emamectin benzoate", "Avermectin", "IRAC 6"),
            ["Chlorantraniliprole"] = AI("Chlorantraniliprole", "Diamide", "IRAC 28"),
            ["Spinosad"] = AI("Spinosad", "Spinosyn", "IRAC 5"),
            ["Lambda-cyhalothrin"] = AI("Lambda-cyhalothrin", "Pyrethroid", "IRAC 3A"),
            ["Buprofezin"] = AI("Buprofezin", "Chitin synthesis inhibitor", "IRAC 16"),
            ["Fipronil"] = AI("Fipronil", "Phenylpyrazole", "IRAC 2B"),
            ["Bacillus thuringiensis"] = AI("Bacillus thuringiensis var. kurstaki", "Microbial", "IRAC 11A"),
            ["Carbofuran"] = AI("Carbofuran", "Carbamate", "IRAC 1A")
        };
        db.ActiveIngredients.AddRange(ingredients.Values);

        // Product defaults shared by every crop approval of that product:
        // (key, name, ingredient, formulation, unit, packSize, priceLkr, reiHours, maxApps, minIntervalDays, rainfastHours, restricted)
        var productSpecs = new (string Key, string Name, string Ai, Formulation F, ProductUnit U, decimal Pack, decimal Price,
            int Rei, int MaxApps, int MinInterval, int Rainfast, bool Restricted)[]
        {
            ("MAN", "Mancozeb 80 WP", "Mancozeb", Formulation.WP, ProductUnit.Kilogram, 1.0m, 2400m, 24, 4, 7, 4, false),
            ("CHL", "Chlorothalonil 75 WP", "Chlorothalonil", Formulation.WP, ProductUnit.Kilogram, 0.5m, 2100m, 12, 4, 7, 2, false),
            ("MET", "Metalaxyl 25 WP", "Metalaxyl", Formulation.WP, ProductUnit.Kilogram, 0.5m, 3200m, 12, 3, 14, 2, false),
            ("AZO", "Azoxystrobin 25 SC", "Azoxystrobin", Formulation.SC, ProductUnit.Litre, 0.25m, 4800m, 4, 2, 10, 1, false),
            ("DIF", "Difenoconazole 25 EC", "Difenoconazole", Formulation.EC, ProductUnit.Litre, 0.25m, 3900m, 12, 3, 10, 2, false),
            ("COP", "Copper Hydroxide 77 WP", "Copper hydroxide", Formulation.WP, ProductUnit.Kilogram, 0.5m, 1900m, 24, 5, 7, 6, false),
            ("SUL", "Wettable Sulphur 80 WG", "Sulphur", Formulation.WG, ProductUnit.Kilogram, 1.0m, 1200m, 24, 6, 7, 6, false),
            ("TRI", "Tricyclazole 75 WP", "Tricyclazole", Formulation.WP, ProductUnit.Kilogram, 0.25m, 2600m, 12, 2, 14, 2, false),
            ("IMI", "Imidacloprid 17.8 SL", "Imidacloprid", Formulation.SL, ProductUnit.Litre, 0.25m, 2900m, 12, 2, 14, 2, false),
            ("THI", "Thiamethoxam 25 WG", "Thiamethoxam", Formulation.WG, ProductUnit.Kilogram, 0.1m, 3100m, 12, 2, 14, 1, false),
            ("ACE", "Acetamiprid 20 SL", "Acetamiprid", Formulation.SL, ProductUnit.Litre, 0.25m, 2300m, 12, 2, 14, 2, false),
            ("ABA", "Abamectin 1.8 EC", "Abamectin", Formulation.EC, ProductUnit.Litre, 0.25m, 2700m, 12, 3, 7, 2, false),
            ("EMA", "Emamectin Benzoate 5 WG", "Emamectin benzoate", Formulation.WG, ProductUnit.Kilogram, 0.1m, 3600m, 12, 3, 7, 2, false),
            ("CLA", "Chlorantraniliprole 18.5 SC", "Chlorantraniliprole", Formulation.SC, ProductUnit.Litre, 0.15m, 6200m, 4, 2, 10, 2, false),
            ("SPI", "Spinosad 45 SC", "Spinosad", Formulation.SC, ProductUnit.Litre, 0.1m, 5400m, 4, 3, 7, 1, false),
            ("LAM", "Lambda-Cyhalothrin 2.5 EC", "Lambda-cyhalothrin", Formulation.EC, ProductUnit.Litre, 0.5m, 1800m, 24, 3, 10, 2, false),
            ("BUP", "Buprofezin 25 SC", "Buprofezin", Formulation.SC, ProductUnit.Litre, 0.5m, 2500m, 12, 2, 14, 2, false),
            // Restricted: usable only under permit (rule V10).
            ("FIP", "Fipronil 5 SC", "Fipronil", Formulation.SC, ProductUnit.Litre, 0.5m, 3300m, 24, 2, 14, 2, true),
            ("BTK", "Bt kurstaki WP", "Bacillus thuringiensis", Formulation.WP, ProductUnit.Kilogram, 0.5m, 2200m, 4, 6, 5, 6, false),
            // Withdrawn: still in the catalogue (legacy stock), but every approval is inactive (rule V2 / golden case G5).
            ("CAR", "Carbofuran 3 GR", "Carbofuran", Formulation.GR, ProductUnit.Kilogram, 5.0m, 1500m, 48, 1, 30, 0, true)
        };

        var products = productSpecs.ToDictionary(s => s.Key, s => new Product
        {
            Name = s.Name,
            ActiveIngredient = ingredients[s.Ai],
            Formulation = s.F,
            Unit = s.U,
            PackSize = s.Pack,
            UnitPrice = s.Price
        });
        db.Products.AddRange(products.Values);

        var targets = new Dictionary<string, string[]>
        {
            ["MAN"] = ["LATE_BLIGHT", "EARLY_BLIGHT", "ANTHRACNOSE"],
            ["CHL"] = ["EARLY_BLIGHT", "LATE_BLIGHT", "ANTHRACNOSE"],
            ["MET"] = ["LATE_BLIGHT"],
            ["AZO"] = ["EARLY_BLIGHT", "LATE_BLIGHT", "POWDERY_MILDEW", "ANTHRACNOSE", "RICE_BLAST"],
            ["DIF"] = ["EARLY_BLIGHT", "POWDERY_MILDEW", "ANTHRACNOSE"],
            ["COP"] = ["LATE_BLIGHT", "ANTHRACNOSE"],
            ["SUL"] = ["POWDERY_MILDEW", "SPIDER_MITE"],
            ["TRI"] = ["RICE_BLAST"],
            ["IMI"] = ["WHITEFLY", "APHID", "THRIPS", "BROWN_PLANTHOPPER"],
            ["THI"] = ["WHITEFLY", "APHID", "BROWN_PLANTHOPPER"],
            ["ACE"] = ["WHITEFLY", "APHID"],
            ["ABA"] = ["SPIDER_MITE", "THRIPS"],
            ["EMA"] = ["FRUIT_BORER", "DIAMONDBACK_MOTH", "FALL_ARMYWORM"],
            ["CLA"] = ["FRUIT_BORER", "DIAMONDBACK_MOTH", "FALL_ARMYWORM"],
            ["SPI"] = ["THRIPS", "DIAMONDBACK_MOTH", "FRUIT_BORER"],
            ["LAM"] = ["FRUIT_BORER", "APHID", "FALL_ARMYWORM"],
            ["BUP"] = ["WHITEFLY", "BROWN_PLANTHOPPER"],
            ["FIP"] = ["THRIPS", "BROWN_PLANTHOPPER"],
            ["BTK"] = ["DIAMONDBACK_MOTH", "FRUIT_BORER"],
            ["CAR"] = ["BROWN_PLANTHOPPER", "FALL_ARMYWORM"]
        };
        foreach (var (productKey, pathogenCodes) in targets)
            foreach (var code in pathogenCodes)
                db.ProductTargets.Add(new ProductTarget { Product = products[productKey], Pathogen = pathogens[code] });

        // Crop-specific limits: (productKey, cropCode, minDose/ha, maxDose/ha, PHI days, active)
        var approvals = new (string P, string Crop, decimal Min, decimal Max, int Phi, bool Active)[]
        {
            // Tomato
            ("MAN", "TOM", 1.5m, 2.5m, 7, true), ("CHL", "TOM", 1.0m, 2.0m, 7, true), ("MET", "TOM", 0.8m, 1.2m, 14, true),
            ("AZO", "TOM", 0.4m, 0.8m, 3, true), ("DIF", "TOM", 0.3m, 0.5m, 7, true), ("COP", "TOM", 1.5m, 2.5m, 3, true),
            ("SUL", "TOM", 2.0m, 3.0m, 1, true), ("IMI", "TOM", 0.25m, 0.4m, 21, true), ("THI", "TOM", 0.1m, 0.2m, 14, true),
            ("ACE", "TOM", 0.1m, 0.2m, 7, true), ("ABA", "TOM", 0.4m, 0.6m, 7, true), ("EMA", "TOM", 0.15m, 0.25m, 7, true),
            ("CLA", "TOM", 0.1m, 0.15m, 3, true), ("SPI", "TOM", 0.15m, 0.25m, 3, true), ("LAM", "TOM", 0.3m, 0.5m, 5, true),
            ("BUP", "TOM", 0.8m, 1.2m, 14, true), ("BTK", "TOM", 0.5m, 1.0m, 0, true), ("CAR", "TOM", 20m, 33m, 60, false),
            // Chilli
            ("MAN", "CHI", 1.5m, 2.5m, 7, true), ("DIF", "CHI", 0.3m, 0.5m, 7, true), ("SUL", "CHI", 2.0m, 3.0m, 1, true),
            ("IMI", "CHI", 0.25m, 0.4m, 21, true), ("THI", "CHI", 0.1m, 0.2m, 14, true), ("ACE", "CHI", 0.1m, 0.2m, 7, true),
            ("ABA", "CHI", 0.4m, 0.6m, 7, true), ("EMA", "CHI", 0.15m, 0.25m, 7, true), ("SPI", "CHI", 0.15m, 0.25m, 3, true),
            ("FIP", "CHI", 0.8m, 1.0m, 14, true),
            // Paddy
            ("TRI", "RIC", 0.3m, 0.5m, 21, true), ("AZO", "RIC", 0.5m, 0.75m, 21, true), ("BUP", "RIC", 0.8m, 1.0m, 21, true),
            ("THI", "RIC", 0.1m, 0.15m, 21, true), ("IMI", "RIC", 0.1m, 0.15m, 21, true), ("CLA", "RIC", 0.1m, 0.15m, 30, true),
            ("FIP", "RIC", 1.0m, 1.5m, 30, true), ("CAR", "RIC", 20m, 33m, 60, false),
            // Potato
            ("MAN", "POT", 2.0m, 2.5m, 7, true), ("MET", "POT", 1.0m, 1.5m, 14, true), ("CHL", "POT", 1.5m, 2.0m, 7, true),
            ("AZO", "POT", 0.5m, 0.8m, 7, true), ("IMI", "POT", 0.3m, 0.4m, 21, true), ("LAM", "POT", 0.3m, 0.5m, 7, true),
            // Cabbage
            ("BTK", "CAB", 0.5m, 1.0m, 0, true), ("EMA", "CAB", 0.15m, 0.2m, 7, true), ("CLA", "CAB", 0.1m, 0.15m, 3, true),
            ("SPI", "CAB", 0.15m, 0.2m, 3, true), ("LAM", "CAB", 0.3m, 0.5m, 7, true), ("MAN", "CAB", 1.5m, 2.0m, 7, true),
            // Brinjal
            ("EMA", "BRI", 0.15m, 0.25m, 5, true), ("CLA", "BRI", 0.1m, 0.15m, 5, true), ("SPI", "BRI", 0.15m, 0.25m, 3, true),
            ("ABA", "BRI", 0.4m, 0.6m, 7, true), ("IMI", "BRI", 0.25m, 0.4m, 21, true), ("MAN", "BRI", 1.5m, 2.5m, 7, true),
            // Big onion
            ("MAN", "ONI", 2.0m, 2.5m, 7, true), ("DIF", "ONI", 0.3m, 0.5m, 14, true), ("SPI", "ONI", 0.15m, 0.2m, 7, true),
            ("CHL", "ONI", 1.5m, 2.0m, 14, true),
            // Carrot
            ("MAN", "CAR", 1.5m, 2.0m, 14, true), ("DIF", "CAR", 0.3m, 0.5m, 14, true), ("LAM", "CAR", 0.3m, 0.4m, 14, true),
            // Beans
            ("MAN", "BEA", 1.5m, 2.0m, 7, true), ("SUL", "BEA", 2.0m, 3.0m, 1, true), ("ACE", "BEA", 0.1m, 0.2m, 7, true),
            ("LAM", "BEA", 0.3m, 0.4m, 7, true), ("DIF", "BEA", 0.3m, 0.5m, 14, true),
            // Maize
            ("EMA", "MAI", 0.2m, 0.25m, 14, true), ("CLA", "MAI", 0.15m, 0.2m, 21, true), ("SPI", "MAI", 0.2m, 0.25m, 7, true),
            ("LAM", "MAI", 0.3m, 0.5m, 21, true), ("CAR", "MAI", 20m, 33m, 60, false),
            // Cucumber
            ("SUL", "CUC", 2.0m, 3.0m, 1, true), ("AZO", "CUC", 0.4m, 0.6m, 1, true), ("ACE", "CUC", 0.1m, 0.2m, 3, true),
            ("ABA", "CUC", 0.4m, 0.5m, 3, true), ("CHL", "CUC", 1.0m, 1.5m, 3, true),
            // Okra
            ("IMI", "OKR", 0.25m, 0.35m, 14, true), ("EMA", "OKR", 0.15m, 0.2m, 5, true), ("SUL", "OKR", 2.0m, 3.0m, 1, true),
            ("LAM", "OKR", 0.3m, 0.4m, 7, true)
        };

        var specByKey = productSpecs.ToDictionary(s => s.Key);
        var cropByCode = crops.ToDictionary(c => c.Code);
        foreach (var a in approvals)
        {
            var spec = specByKey[a.P];
            db.ProductCropApprovals.Add(new ProductCropApproval
            {
                Product = products[a.P],
                Crop = cropByCode[a.Crop],
                MinDosePerHectare = a.Min,
                MaxDosePerHectare = a.Max,
                PreHarvestIntervalDays = a.Phi,
                ReEntryIntervalHours = spec.Rei,
                MaxApplicationsPerCycle = spec.MaxApps,
                MinDaysBetweenApplications = spec.MinInterval,
                RainfastHours = spec.Rainfast,
                IsRestricted = spec.Restricted,
                IsActive = a.Active
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static Pathogen P(string code, string name, string scientific, PathogenType type, params string[] symptoms)
    {
        foreach (var s in symptoms)
            if (!SymptomCatalogue.IsValid(s))
                throw new InvalidOperationException($"Seed pathogen {code} references unknown symptom '{s}'.");

        return new Pathogen { Code = code, CommonName = name, ScientificName = scientific, Type = type, IndicativeSymptoms = symptoms.ToList() };
    }

    private static ActiveIngredient AI(string name, string chemicalClass, string group) =>
        new() { Name = name, ChemicalClass = chemicalClass, ResistanceGroup = group };
}
