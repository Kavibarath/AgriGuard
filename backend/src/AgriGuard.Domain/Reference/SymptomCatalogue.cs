namespace AgriGuard.Domain.Reference;

/// <summary>
/// The closed allow-list of symptom codes a farmer can tick in the Flutter app.
/// Cases may only carry codes from this list, so the agent receives structured,
/// validated symptoms rather than relying on free text alone.
/// </summary>
public static class SymptomCatalogue
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["leaf_water_soaked_lesions"] = "Water-soaked patches on leaves",
        ["leaf_brown_patches"] = "Large brown or black patches on leaves",
        ["leaf_white_mould_underside"] = "White fuzzy mould under leaves",
        ["stem_dark_lesions"] = "Dark lesions on stems",
        ["leaf_concentric_rings"] = "Brown spots with target-like rings",
        ["leaf_yellowing"] = "Yellowing leaves",
        ["leaf_brown_spots"] = "Small brown spots on leaves",
        ["leaf_white_powder"] = "White powdery coating on leaves",
        ["plant_wilting"] = "Whole plant wilting",
        ["stem_vascular_browning"] = "Brown streaks inside cut stem",
        ["leaf_curling"] = "Leaves curling or puckering",
        ["plant_stunting"] = "Stunted plant growth",
        ["insects_white_underside"] = "Tiny white flying insects under leaves",
        ["leaf_sticky_honeydew"] = "Sticky residue on leaves",
        ["insects_clusters_on_shoots"] = "Clusters of small soft insects on shoots",
        ["leaf_silvering"] = "Silvery streaks or scars on leaves",
        ["flower_drop"] = "Flowers dropping early",
        ["fruit_bore_holes"] = "Holes bored into fruit",
        ["fruit_frass"] = "Insect droppings on or in fruit",
        ["leaf_window_holes"] = "Transparent windows or small holes in leaves",
        ["larvae_small_green"] = "Small green caterpillars",
        ["leaf_diamond_lesions"] = "Diamond-shaped grey lesions (paddy)",
        ["panicle_neck_rot"] = "Rotting at the base of the grain head",
        ["plant_hopperburn"] = "Circular patches of dried, burnt-looking plants",
        ["insects_at_plant_base"] = "Insects clustered at the plant base",
        ["leaf_ragged_holes"] = "Large ragged holes in leaves",
        ["whorl_frass"] = "Sawdust-like droppings in the leaf whorl",
        ["leaf_stippling"] = "Fine pale speckling on leaves",
        ["fine_webbing"] = "Fine webbing on leaves or stems",
        ["fruit_sunken_lesions"] = "Sunken dark lesions on fruit"
    };

    public static bool IsValid(string code) => All.ContainsKey(code);
}
