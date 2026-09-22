namespace AgriGuard.Domain.Registry;

/// <summary>
/// The legal-transition matrix for a crop cycle's stage (§5.1, Student A's non-CRUD operation).
///
/// Growth runs one stage at a time and never backwards: a farmer cannot mark a plot
/// PreHarvest while it is still Sown, and cannot "un-harvest" it. Skipping matters because
/// every downstream rule — pre-harvest interval, harvest windows, the agent's diagnosis
/// context — reads the stage and assumes it reflects the crop in the field.
///
/// Pure domain logic with no EF or HTTP dependency, so it is unit-tested directly.
/// </summary>
public static class CropStageRules
{
    /// <summary>Stages in growth order. The enum's numeric order is the sequence.</summary>
    public static readonly IReadOnlyList<CropStage> Sequence =
        Enum.GetValues<CropStage>().OrderBy(s => (int)s).ToArray();

    /// <summary>The stage that may follow <paramref name="current"/>, or null at the end of the sequence.</summary>
    public static CropStage? Next(CropStage current) =>
        current == CropStage.Harvested ? null : current + 1;

    public static bool CanAdvance(CropStage from, CropStage to) => Next(from) == to;

    /// <summary>
    /// Explains why a transition is illegal, or null when it is allowed. The message reaches the
    /// farmer, so it says what to do rather than restating the enum.
    /// </summary>
    public static string? Explain(CropStage from, CropStage to)
    {
        if (CanAdvance(from, to)) return null;

        if (from == CropStage.Harvested)
            return "This crop cycle is already harvested; start a new cycle for the next planting.";

        if (to == from)
            return $"The crop cycle is already at {from}.";

        if ((int)to < (int)from)
            return $"A crop cycle cannot go back from {from} to {to}.";

        return $"A crop cycle moves one stage at a time: {from} must advance to {Next(from)} before {to}.";
    }

    /// <summary>
    /// Expected harvest date = sowing date + the crop's maturity days. Recomputed on every
    /// advance because a cycle that reaches a stage early or late shifts the whole schedule;
    /// harvest windows and the PHI check read this date.
    /// </summary>
    public static DateOnly ExpectedHarvestDate(DateOnly sownDate, int cropMaturityDays) =>
        sownDate.AddDays(cropMaturityDays);

    /// <summary>
    /// Fraction of the growing period a stage is normally reached at. Used to re-estimate the
    /// harvest date from observed progress: a cycle that reaches Flowering (≈45% of the way)
    /// ten days late will be harvested about ten days late.
    ///
    /// Academic approximation, not agronomic fact — it is deliberately coarse and the farmer's
    /// PlannedHarvestDate always wins where one is set.
    /// </summary>
    public static double ProgressFraction(CropStage stage) => stage switch
    {
        CropStage.Sown => 0.0,
        CropStage.Vegetative => 0.25,
        CropStage.Flowering => 0.45,
        CropStage.FruitSet => 0.65,
        CropStage.PreHarvest => 0.90,
        CropStage.Harvested => 1.0,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown crop stage.")
    };

    /// <summary>
    /// Re-estimates the harvest date when <paramref name="reached"/> is reached on
    /// <paramref name="reachedOn"/>. Returns the unchanged baseline for Sown (no progress yet)
    /// and for Harvested (the date is now history, not a forecast).
    /// </summary>
    public static DateOnly ReviseExpectedHarvestDate(
        DateOnly sownDate,
        int cropMaturityDays,
        CropStage reached,
        DateOnly reachedOn)
    {
        if (reached is CropStage.Sown or CropStage.Harvested)
            return ExpectedHarvestDate(sownDate, cropMaturityDays);

        var fraction = ProgressFraction(reached);
        var expectedDaysToHere = cropMaturityDays * fraction;
        var actualDaysToHere = (reachedOn.DayNumber - sownDate.DayNumber);

        // Scale the whole cycle by how far ahead or behind this stage arrived.
        var scale = actualDaysToHere / expectedDaysToHere;
        var revisedTotal = (int)Math.Round(cropMaturityDays * scale, MidpointRounding.AwayFromZero);

        // Never predict a harvest before the stage was even reached, and never let a wild
        // outlier (a mis-dated transition) push the estimate years out. The lower bound wins
        // when the two conflict: a stage recorded beyond twice the maturity period is already
        // nonsense, and "the day after it was reached" is the only defensible answer left.
        var lowerBound = Math.Max(reachedOn.DayNumber - sownDate.DayNumber + 1, 1);
        var upperBound = Math.Max(cropMaturityDays * 2, lowerBound);
        revisedTotal = Math.Clamp(revisedTotal, lowerBound, upperBound);

        return sownDate.AddDays(revisedTotal);
    }
}
