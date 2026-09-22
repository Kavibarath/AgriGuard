using AgriGuard.Domain.Registry;

namespace AgriGuard.UnitTests.Registry;

/// <summary>
/// The facts behind validator rules V5 (pre-harvest interval), V6 (applications per cycle) and
/// V7 (resistance interval). Pure functions, so every case is tested here rather than over HTTP.
/// </summary>
public sealed class SafetyProfileCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 22);
    private static readonly DateTime NowUtc = new(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Mancozeb = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Imidacloprid = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid MancozebProduct = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid OtherMancozebProduct = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid ImidaclopridProduct = Guid.Parse("00000000-0000-0000-0000-0000000000b3");

    private static ProductRule Rule(
        Guid productId,
        Guid ingredientId,
        string name = "Mancozeb 80 WP",
        int phi = 7,
        int maxApplications = 4,
        int minInterval = 7,
        int reEntryHours = 24,
        bool restricted = false) =>
        new(productId, name, ingredientId, "Mancozeb", "FRAC M03", phi, reEntryHours, maxApplications, minInterval, restricted);

    private static AppliedTreatment Applied(Guid productId, Guid ingredientId, DateOnly on, int reEntryHours = 24) =>
        new(productId, ingredientId, on, reEntryHours);

    private static SafetyProfile Compute(
        DateOnly harvest,
        IReadOnlyList<AppliedTreatment>? applications = null,
        IReadOnlyList<ProductRule>? rules = null) =>
        SafetyProfileCalculator.Compute(
            Today, NowUtc, harvest,
            applications ?? [],
            rules ?? [Rule(MancozebProduct, Mancozeb)]);

    [Fact]
    public void Reports_days_to_harvest_from_the_harvest_date()
    {
        var profile = Compute(Today.AddDays(30));

        Assert.Equal(30, profile.DaysToHarvest);
        Assert.Equal(Today.AddDays(30), profile.HarvestDate);
    }

    [Fact]
    public void A_harvest_already_past_reports_negative_days()
    {
        // An overdue cycle is a real state — the farmer has not recorded the harvest yet.
        Assert.Equal(-3, Compute(Today.AddDays(-3)).DaysToHarvest);
    }

    public sealed class PreHarvestInterval
    {
        [Fact]
        public void Last_safe_spray_date_is_harvest_minus_the_interval()
        {
            var harvest = Today.AddDays(30);

            var window = Compute(harvest, rules: [Rule(MancozebProduct, Mancozeb, phi: 7)]).ProductWindows.Single();

            Assert.Equal(harvest.AddDays(-7), window.LastSafeSprayDate);
            Assert.True(window.CanSprayToday);
        }

        [Fact]
        public void Spraying_is_allowed_on_the_last_safe_day_itself()
        {
            // Boundary: spraying exactly PHI days before harvest satisfies the interval.
            var harvest = Today.AddDays(7);

            var window = Compute(harvest, rules: [Rule(MancozebProduct, Mancozeb, phi: 7)]).ProductWindows.Single();

            Assert.Equal(Today, window.LastSafeSprayDate);
            Assert.True(window.CanSprayToday);
        }

        [Fact]
        public void One_day_later_is_blocked()
        {
            var harvest = Today.AddDays(6);

            var window = Compute(harvest, rules: [Rule(MancozebProduct, Mancozeb, phi: 7)]).ProductWindows.Single();

            Assert.False(window.CanSprayToday);
            Assert.Equal(SprayBlock.PreHarvestInterval, window.BlockedReason);
        }

        [Fact]
        public void A_zero_day_interval_allows_spraying_up_to_harvest()
        {
            // Bt kurstaki and similar biologicals have PHI 0 in the seeded rules.
            var window = Compute(Today, rules: [Rule(MancozebProduct, Mancozeb, name: "Bt kurstaki WP", phi: 0)]).ProductWindows.Single();

            Assert.True(window.CanSprayToday);
        }
    }

    public sealed class ApplicationAllowance
    {
        [Fact]
        public void Counts_applications_of_that_product_and_reports_what_is_left()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [
                    Applied(MancozebProduct, Mancozeb, Today.AddDays(-30)),
                    Applied(MancozebProduct, Mancozeb, Today.AddDays(-20)),
                ],
                rules: [Rule(MancozebProduct, Mancozeb, maxApplications: 4, minInterval: 7)]);

            var window = profile.ProductWindows.Single();
            Assert.Equal(2, window.ApplicationsUsed);
            Assert.Equal(2, window.ApplicationsRemaining);
            Assert.Equal(Today.AddDays(-20), window.LastAppliedOn);
            Assert.True(window.CanSprayToday);
        }

        [Fact]
        public void Blocks_once_the_seasonal_allowance_is_used_up()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [
                    Applied(MancozebProduct, Mancozeb, Today.AddDays(-40)),
                    Applied(MancozebProduct, Mancozeb, Today.AddDays(-30)),
                ],
                rules: [Rule(MancozebProduct, Mancozeb, maxApplications: 2, minInterval: 7)]);

            var window = profile.ProductWindows.Single();
            Assert.Equal(0, window.ApplicationsRemaining);
            Assert.Equal(SprayBlock.MaxApplicationsReached, window.BlockedReason);
        }

        [Fact]
        public void Remaining_never_goes_negative_when_the_rule_was_tightened_mid_season()
        {
            // An administrator can lower MaxApplicationsPerCycle after sprays already happened.
            var profile = Compute(
                Today.AddDays(40),
                applications: [
                    Applied(MancozebProduct, Mancozeb, Today.AddDays(-40)),
                    Applied(MancozebProduct, Mancozeb, Today.AddDays(-30)),
                    Applied(MancozebProduct, Mancozeb, Today.AddDays(-20)),
                ],
                rules: [Rule(MancozebProduct, Mancozeb, maxApplications: 2)]);

            Assert.Equal(0, profile.ProductWindows.Single().ApplicationsRemaining);
        }

        [Fact]
        public void An_exhausted_allowance_outranks_a_pre_harvest_block()
        {
            // Both apply; the farmer is told the harder "no", because waiting will not fix it.
            var profile = Compute(
                Today.AddDays(1),
                applications: [Applied(MancozebProduct, Mancozeb, Today.AddDays(-30))],
                rules: [Rule(MancozebProduct, Mancozeb, phi: 7, maxApplications: 1)]);

            Assert.Equal(SprayBlock.MaxApplicationsReached, profile.ProductWindows.Single().BlockedReason);
        }
    }

    public sealed class ResistanceInterval
    {
        [Fact]
        public void Blocks_until_the_minimum_gap_since_the_last_application_has_passed()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [Applied(MancozebProduct, Mancozeb, Today.AddDays(-3))],
                rules: [Rule(MancozebProduct, Mancozeb, minInterval: 7)]);

            var window = profile.ProductWindows.Single();
            Assert.Equal(SprayBlock.MinimumInterval, window.BlockedReason);
            Assert.Equal(Today.AddDays(4), window.EarliestNextApplication);
        }

        [Fact]
        public void Allows_spraying_on_the_day_the_gap_expires()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [Applied(MancozebProduct, Mancozeb, Today.AddDays(-7))],
                rules: [Rule(MancozebProduct, Mancozeb, minInterval: 7)]);

            Assert.True(profile.ProductWindows.Single().CanSprayToday);
        }

        [Fact]
        public void The_gap_follows_the_active_ingredient_not_the_product_label()
        {
            // Two brands, one chemical: spraying the second does not reset resistance pressure,
            // which is the whole point of rule V7.
            var profile = Compute(
                Today.AddDays(40),
                applications: [Applied(MancozebProduct, Mancozeb, Today.AddDays(-2))],
                rules: [
                    Rule(MancozebProduct, Mancozeb, name: "Mancozeb 80 WP", minInterval: 7),
                    Rule(OtherMancozebProduct, Mancozeb, name: "Dithane M-45", minInterval: 7),
                ]);

            var otherBrand = profile.ProductWindows.Single(w => w.ProductName == "Dithane M-45");
            Assert.Equal(SprayBlock.MinimumInterval, otherBrand.BlockedReason);
            Assert.Equal(0, otherBrand.ApplicationsUsed); // never sprayed under this label
        }

        [Fact]
        public void A_different_ingredient_is_unaffected()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [Applied(MancozebProduct, Mancozeb, Today.AddDays(-2))],
                rules: [
                    Rule(MancozebProduct, Mancozeb, minInterval: 7),
                    Rule(ImidaclopridProduct, Imidacloprid, name: "Imidacloprid 17.8 SL", minInterval: 14),
                ]);

            Assert.True(profile.ProductWindows.Single(w => w.ProductId == ImidaclopridProduct).CanSprayToday);
        }
    }

    public sealed class IngredientSummary
    {
        [Fact]
        public void Groups_by_ingredient_across_brands_and_dates_the_most_recent_use()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [
                    Applied(MancozebProduct, Mancozeb, Today.AddDays(-20)),
                    Applied(OtherMancozebProduct, Mancozeb, Today.AddDays(-5)),
                    Applied(ImidaclopridProduct, Imidacloprid, Today.AddDays(-10)),
                ],
                rules: [
                    Rule(MancozebProduct, Mancozeb),
                    Rule(OtherMancozebProduct, Mancozeb, name: "Dithane M-45"),
                    Rule(ImidaclopridProduct, Imidacloprid, name: "Imidacloprid 17.8 SL"),
                ]);

            var mancozeb = profile.IngredientUsage.Single(u => u.ActiveIngredientId == Mancozeb);
            Assert.Equal(2, mancozeb.ApplicationCount);
            Assert.Equal(Today.AddDays(-5), mancozeb.LastAppliedOn);
            Assert.Equal(5, mancozeb.DaysSinceLastApplication);
            Assert.Equal("FRAC M03", mancozeb.ResistanceGroup);

            // Most-used first: that is the resistance risk an agronomist looks for.
            Assert.Equal(Mancozeb, profile.IngredientUsage[0].ActiveIngredientId);
        }

        [Fact]
        public void Is_empty_when_nothing_has_been_applied()
        {
            Assert.Empty(Compute(Today.AddDays(40)).IngredientUsage);
        }
    }

    public sealed class BlockedDates
    {
        [Fact]
        public void Lists_the_run_up_to_harvest_where_even_the_shortest_interval_no_longer_fits()
        {
            var harvest = Today.AddDays(10);

            var profile = Compute(harvest, rules: [
                Rule(MancozebProduct, Mancozeb, phi: 7),
                Rule(ImidaclopridProduct, Imidacloprid, name: "Imidacloprid 17.8 SL", phi: 21),
            ]);

            // Shortest PHI is 7, so everything after harvest-7 is blocked for every product.
            Assert.Equal(harvest.AddDays(-6), profile.PhiBlockedSprayDates[0]);
            Assert.Equal(harvest, profile.PhiBlockedSprayDates[^1]);
            Assert.Equal(7, profile.PhiBlockedSprayDates.Count);
        }

        [Fact]
        public void Never_lists_dates_in_the_past()
        {
            // Harvest is imminent, so the blocked window began before today.
            var profile = Compute(Today.AddDays(2), rules: [Rule(MancozebProduct, Mancozeb, phi: 14)]);

            Assert.Equal(Today, profile.PhiBlockedSprayDates[0]);
            Assert.All(profile.PhiBlockedSprayDates, date => Assert.True(date >= Today));
        }

        [Fact]
        public void Still_lists_the_window_when_harvest_is_far_off_so_it_can_be_planned_around()
        {
            var harvest = Today.AddDays(90);

            var profile = Compute(harvest, rules: [Rule(MancozebProduct, Mancozeb, phi: 7)]);

            // The window sits immediately before harvest wherever that falls; it is a planning
            // aid, not a "right now" warning — CanSprayToday answers that.
            Assert.Equal(harvest.AddDays(-6), profile.PhiBlockedSprayDates[0]);
            Assert.Equal(7, profile.PhiBlockedSprayDates.Count);
            Assert.True(profile.ProductWindows.Single().CanSprayToday);
        }

        [Fact]
        public void Is_empty_when_no_product_is_approved_for_the_crop()
        {
            Assert.Empty(Compute(Today.AddDays(5), rules: []).PhiBlockedSprayDates);
        }
    }

    public sealed class ReEntry
    {
        [Fact]
        public void Reports_when_the_field_becomes_safe_to_walk_into()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [Applied(MancozebProduct, Mancozeb, Today, reEntryHours: 24)]);

            // Applications carry a date, not a time, so entry is counted from the end of that day.
            Assert.Equal(new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc), profile.ReEntryClearAtUtc);
        }

        [Fact]
        public void Takes_the_latest_expiry_when_several_apply()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [
                    Applied(MancozebProduct, Mancozeb, Today, reEntryHours: 12),
                    Applied(ImidaclopridProduct, Imidacloprid, Today, reEntryHours: 48),
                ]);

            Assert.Equal(new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc), profile.ReEntryClearAtUtc);
        }

        [Fact]
        public void Is_null_once_every_re_entry_window_has_expired()
        {
            var profile = Compute(
                Today.AddDays(40),
                applications: [Applied(MancozebProduct, Mancozeb, Today.AddDays(-10), reEntryHours: 24)]);

            Assert.Null(profile.ReEntryClearAtUtc);
        }
    }
}
