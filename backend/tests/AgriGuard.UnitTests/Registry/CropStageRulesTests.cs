using AgriGuard.Domain.Registry;

namespace AgriGuard.UnitTests.Registry;

/// <summary>
/// The legal-transition matrix for Component A's non-CRUD operation. Pure functions, so every
/// rule is tested here rather than through HTTP.
/// </summary>
public sealed class CropStageRulesTests
{
    [Theory]
    [InlineData(CropStage.Sown, CropStage.Vegetative)]
    [InlineData(CropStage.Vegetative, CropStage.Flowering)]
    [InlineData(CropStage.Flowering, CropStage.FruitSet)]
    [InlineData(CropStage.FruitSet, CropStage.PreHarvest)]
    [InlineData(CropStage.PreHarvest, CropStage.Harvested)]
    public void Allows_the_next_stage_in_sequence(CropStage from, CropStage to)
    {
        Assert.True(CropStageRules.CanAdvance(from, to));
        Assert.Null(CropStageRules.Explain(from, to));
    }

    [Theory]
    [InlineData(CropStage.Sown, CropStage.Flowering)]      // skipping ahead
    [InlineData(CropStage.Sown, CropStage.Harvested)]      // straight to the end
    [InlineData(CropStage.Flowering, CropStage.Sown)]      // going backwards
    [InlineData(CropStage.FruitSet, CropStage.Flowering)]  // one step back
    [InlineData(CropStage.Flowering, CropStage.Flowering)] // no movement
    public void Refuses_anything_but_one_step_forward(CropStage from, CropStage to)
    {
        Assert.False(CropStageRules.CanAdvance(from, to));
        Assert.NotNull(CropStageRules.Explain(from, to));
    }

    [Fact]
    public void A_harvested_cycle_is_the_end_of_the_line()
    {
        Assert.Null(CropStageRules.Next(CropStage.Harvested));

        foreach (var stage in CropStageRules.Sequence)
            Assert.False(CropStageRules.CanAdvance(CropStage.Harvested, stage));

        Assert.Contains("already harvested", CropStageRules.Explain(CropStage.Harvested, CropStage.Sown));
    }

    [Fact]
    public void Every_stage_except_the_last_has_exactly_one_successor()
    {
        foreach (var stage in CropStageRules.Sequence)
        {
            var successors = CropStageRules.Sequence.Where(s => CropStageRules.CanAdvance(stage, s)).ToList();
            Assert.Equal(stage == CropStage.Harvested ? 0 : 1, successors.Count);
        }
    }

    [Fact]
    public void Explanations_name_the_stage_that_must_come_next()
    {
        var message = CropStageRules.Explain(CropStage.Sown, CropStage.PreHarvest);

        Assert.Contains("Vegetative", message);
    }

    public sealed class HarvestDateEstimation
    {
        private static readonly DateOnly Sown = new(2026, 3, 1);
        private const int MaturityDays = 100;

        [Fact]
        public void Baseline_is_sowing_date_plus_maturity_days()
        {
            Assert.Equal(new DateOnly(2026, 6, 9), CropStageRules.ExpectedHarvestDate(Sown, MaturityDays));
        }

        [Fact]
        public void On_schedule_stage_keeps_the_original_estimate()
        {
            // Flowering is 45% of the way: 45 days into a 100-day crop.
            var onSchedule = Sown.AddDays(45);

            var revised = CropStageRules.ReviseExpectedHarvestDate(Sown, MaturityDays, CropStage.Flowering, onSchedule);

            Assert.Equal(CropStageRules.ExpectedHarvestDate(Sown, MaturityDays), revised);
        }

        [Fact]
        public void A_late_stage_pushes_the_harvest_later()
        {
            // Flowering ten days late → the whole cycle stretches by about 22%.
            var late = Sown.AddDays(55);

            var revised = CropStageRules.ReviseExpectedHarvestDate(Sown, MaturityDays, CropStage.Flowering, late);

            Assert.True(revised > CropStageRules.ExpectedHarvestDate(Sown, MaturityDays));
            Assert.Equal(Sown.AddDays(122), revised);
        }

        [Fact]
        public void An_early_stage_pulls_the_harvest_forward()
        {
            var early = Sown.AddDays(35);

            var revised = CropStageRules.ReviseExpectedHarvestDate(Sown, MaturityDays, CropStage.Flowering, early);

            Assert.True(revised < CropStageRules.ExpectedHarvestDate(Sown, MaturityDays));
        }

        [Fact]
        public void Sown_and_Harvested_fall_back_to_the_baseline()
        {
            // Sown has no elapsed progress to scale by; Harvested is history, not a forecast.
            Assert.Equal(
                CropStageRules.ExpectedHarvestDate(Sown, MaturityDays),
                CropStageRules.ReviseExpectedHarvestDate(Sown, MaturityDays, CropStage.Sown, Sown));

            Assert.Equal(
                CropStageRules.ExpectedHarvestDate(Sown, MaturityDays),
                CropStageRules.ReviseExpectedHarvestDate(Sown, MaturityDays, CropStage.Harvested, Sown.AddDays(130)));
        }

        [Fact]
        public void Never_predicts_a_harvest_before_the_stage_was_reached()
        {
            // A wildly early PreHarvest (day 3 of a 100-day crop) would otherwise scale the
            // estimate back to before the transition itself.
            var absurdlyEarly = Sown.AddDays(3);

            var revised = CropStageRules.ReviseExpectedHarvestDate(Sown, MaturityDays, CropStage.PreHarvest, absurdlyEarly);

            Assert.True(revised > absurdlyEarly);
        }

        [Fact]
        public void Caps_a_late_transition_at_twice_the_maturity_period()
        {
            // Vegetative (25%) reached on day 60 of a 100-day crop scales to 240 days; capped at 200.
            var late = Sown.AddDays(60);

            var revised = CropStageRules.ReviseExpectedHarvestDate(Sown, MaturityDays, CropStage.Vegetative, late);

            Assert.Equal(Sown.AddDays(MaturityDays * 2), revised);
        }

        [Fact]
        public void A_transition_dated_beyond_the_cap_still_lands_after_it()
        {
            // Day 400 of a 100-day crop is a mis-dated entry: the cap (200) is below the
            // transition itself, so the "never before it happened" bound has to win.
            var absurdlyLate = Sown.AddDays(400);

            var revised = CropStageRules.ReviseExpectedHarvestDate(Sown, MaturityDays, CropStage.Vegetative, absurdlyLate);

            Assert.Equal(Sown.AddDays(401), revised);
        }

        [Fact]
        public void Progress_fractions_increase_with_the_stage()
        {
            var fractions = CropStageRules.Sequence.Select(CropStageRules.ProgressFraction).ToList();

            Assert.Equal(fractions.OrderBy(f => f), fractions);
            Assert.Equal(0.0, fractions.First());
            Assert.Equal(1.0, fractions.Last());
        }
    }
}
