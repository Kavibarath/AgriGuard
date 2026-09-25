using AgriGuard.Domain.Validation;

namespace AgriGuard.UnitTests.Validation;

/// <summary>
/// The safety gate every agent proposal passes through (§9.4). These tests are the specification
/// of rules V1–V11: if a rule changes, a test here changes with it and says why.
/// </summary>
public sealed class PrescriptionSafetyValidatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 22);
    private static readonly Guid Farmer = Guid.Parse("00000000-0000-0000-0000-00000000f001");
    private static readonly Guid Plot = Guid.Parse("00000000-0000-0000-0000-00000000p001".Replace('p', 'd'));
    private static readonly Guid MancozebProduct = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Mancozeb = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtherMancozebBrand = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private static ProductApprovalSnapshot Approval(
        decimal minDose = 1.5m,
        decimal maxDose = 2.5m,
        int phi = 7,
        int maxApplications = 4,
        int minInterval = 7,
        int rainfastHours = 4,
        bool restricted = false,
        bool active = true) =>
        new(MancozebProduct, "Mancozeb 80 WP", Mancozeb, "Mancozeb",
            minDose, maxDose, phi, maxApplications, minInterval, rainfastHours, restricted, active);

    private static PrescriptionValidationContext Context(
        ProductApprovalSnapshot? approval = null,
        // Separate flag, because `approval: null` cannot mean both "use the default" and
        // "there is no approval row" — the distinction is exactly what V2 is about.
        bool withoutApproval = false,
        IReadOnlyList<AppliedTreatmentFact>? applications = null,
        decimal areaHectares = 0.8m,
        DateOnly? harvestDate = null,
        Guid? plotOwner = null,
        bool permit = false,
        decimal? creditLimit = null,
        decimal? estimatedCost = null,
        WeatherAssessment? weather = null,
        StockAssessment? stock = null) =>
        new(Plot, plotOwner ?? Farmer, areaHectares, Guid.NewGuid(), "Tomato",
            harvestDate ?? Today.AddDays(30), Farmer,
            withoutApproval ? null : approval ?? Approval(), applications ?? [],
            permit, creditLimit, estimatedCost, weather, stock);

    /// <summary>A proposal that passes every rule, so each test can break exactly one thing.</summary>
    private static PrescriptionProposal Proposal(
        decimal dose = 2.0m,
        decimal? total = null,
        DateOnly? sprayDate = null) =>
        new(MancozebProduct, dose, total ?? dose * 0.8m, sprayDate ?? Today.AddDays(1));

    private static RuleResult Rule(ValidationVerdict verdict, string code) =>
        verdict.Results.Single(r => r.Code == code);

    private static ValidationVerdict Validate(
        PrescriptionProposal? proposal = null,
        PrescriptionValidationContext? context = null) =>
        PrescriptionSafetyValidator.Validate(proposal ?? Proposal(), context ?? Context(), Today);

    [Fact]
    public void A_sound_proposal_is_approved_with_every_rule_reported()
    {
        var verdict = Validate(
            context: Context(
                weather: new WeatherAssessment(10, 6m, 27m),
                stock: new StockAssessment(5m, Today.AddMonths(6)),
                creditLimit: 50_000m,
                estimatedCost: 4_800m));

        Assert.Equal(ValidationOutcome.Approved, verdict.Outcome);
        Assert.Empty(verdict.Failures);
        // All eleven rules are always reported, so the console can show the full checklist.
        Assert.Equal(11, verdict.Results.Count);
        Assert.Equal(["V1", "V2", "V3", "V4", "V5", "V6", "V7", "V8", "V9", "V10", "V11"],
            verdict.Results.Select(r => r.Code));
    }

    public sealed class V1_Schema
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Rejects_a_non_positive_dose(decimal dose)
        {
            var verdict = Validate(Proposal(dose: dose, total: 1m));

            Assert.Equal(ValidationOutcome.Rejected, verdict.Outcome);
            Assert.Equal(RuleStatus.Failed, Rule(verdict, "V1").Status);
        }

        [Fact]
        public void Rejects_a_spray_date_in_the_past()
        {
            // Planning a spray for yesterday is a malformed proposal, not a late one.
            var verdict = Validate(Proposal(sprayDate: Today.AddDays(-1)));

            Assert.Equal(RuleStatus.Failed, Rule(verdict, "V1").Status);
            Assert.Contains("in the past", Rule(verdict, "V1").Message);
        }

        [Fact]
        public void Accepts_a_spray_date_of_today()
        {
            Assert.Equal(RuleStatus.Passed, Rule(Validate(Proposal(sprayDate: Today)), "V1").Status);
        }
    }

    public sealed class V2_Approval
    {
        [Fact]
        public void Rejects_a_product_with_no_approval_for_the_crop()
        {
            var verdict = Validate(context: Context(withoutApproval: true));

            Assert.Equal(ValidationOutcome.Rejected, verdict.Outcome);
            Assert.Contains("not approved for Tomato", Rule(verdict, "V2").Message);
        }

        [Fact]
        public void Rejects_a_withdrawn_approval()
        {
            // Golden case G5: Carbofuran-style legacy products stay in the catalogue but are
            // inactive, and must never be prescribed again.
            var verdict = Validate(context: Context(approval: Approval(active: false)));

            Assert.Equal(ValidationOutcome.Rejected, verdict.Outcome);
            Assert.Contains("withdrawn", Rule(verdict, "V2").Message);
        }

        [Fact]
        public void Rules_that_need_the_approval_are_reported_as_not_evaluated()
        {
            // Stock supplied so the only unevaluated rules are the ones the missing approval caused.
            var verdict = Validate(context: Context(withoutApproval: true, stock: new StockAssessment(50m, Today.AddMonths(6))));

            // Honest reporting: they were not checked, which is different from passing.
            Assert.Equal(["V3", "V4", "V5", "V6", "V7", "V8"], verdict.NotEvaluated.Select(r => r.Code));
            Assert.DoesNotContain(verdict.NotEvaluated, r => r.Status == RuleStatus.Passed);
        }
    }

    public sealed class V3_Dose
    {
        [Theory]
        [InlineData(1.5)]  // exactly the minimum
        [InlineData(2.0)]
        [InlineData(2.5)]  // exactly the maximum
        public void Accepts_a_dose_inside_the_label_range(decimal dose)
        {
            Assert.Equal(RuleStatus.Passed, Rule(Validate(Proposal(dose: dose)), "V3").Status);
        }

        [Theory]
        [InlineData(1.4)]
        [InlineData(25.0)]
        public void Asks_for_a_revision_outside_it(decimal dose)
        {
            var verdict = Validate(Proposal(dose: dose));

            // Recoverable: the agent can propose a corrected dose.
            Assert.Equal(ValidationOutcome.Revise, verdict.Outcome);
            Assert.Equal(RuleSeverity.Revise, Rule(verdict, "V3").Severity);
        }

        [Fact]
        public void A_ten_times_overdose_is_caught_however_convincingly_it_was_argued()
        {
            // The prompt-injection case: a farmer note that talks the model into a huge dose
            // still meets plain arithmetic here.
            var verdict = Validate(Proposal(dose: 20m, total: 16m));

            Assert.Equal(RuleStatus.Failed, Rule(verdict, "V3").Status);
        }
    }

    public sealed class V4_Quantity
    {
        [Fact]
        public void Accepts_quantity_equal_to_dose_times_area()
        {
            var verdict = Validate(Proposal(dose: 2m, total: 1.6m), Context(areaHectares: 0.8m));

            Assert.Equal(RuleStatus.Passed, Rule(verdict, "V4").Status);
        }

        [Fact]
        public void Tolerates_two_percent_of_pack_rounding()
        {
            var verdict = Validate(Proposal(dose: 2m, total: 1.63m), Context(areaHectares: 0.8m));

            Assert.Equal(RuleStatus.Passed, Rule(verdict, "V4").Status);
        }

        [Fact]
        public void Catches_a_quantity_computed_for_the_wrong_area()
        {
            // 8 ha instead of 0.8 — the decimal-point slip that would order ten times the chemical.
            var verdict = Validate(Proposal(dose: 2m, total: 16m), Context(areaHectares: 0.8m));

            Assert.Equal(ValidationOutcome.Revise, verdict.Outcome);
            Assert.Contains("expected about 1.6", Rule(verdict, "V4").Message);
        }
    }

    public sealed class V5_PreHarvestInterval
    {
        [Fact]
        public void Passes_when_the_interval_fits_before_harvest()
        {
            var verdict = Validate(
                Proposal(sprayDate: Today.AddDays(1)),
                Context(approval: Approval(phi: 7), harvestDate: Today.AddDays(30)));

            Assert.Equal(RuleStatus.Passed, Rule(verdict, "V5").Status);
        }

        [Fact]
        public void Passes_when_the_spray_lands_exactly_on_the_boundary()
        {
            var verdict = Validate(
                Proposal(sprayDate: Today.AddDays(3)),
                Context(approval: Approval(phi: 7), harvestDate: Today.AddDays(10)));

            Assert.Equal(RuleStatus.Passed, Rule(verdict, "V5").Status);
        }

        [Fact]
        public void Rejects_outright_when_residue_would_still_be_on_the_crop_at_harvest()
        {
            // Golden case G2: harvest in 5 days, product needs 14. Terminal, not revisable —
            // this is the rule that keeps residue off food.
            var verdict = Validate(
                Proposal(sprayDate: Today),
                Context(approval: Approval(phi: 14), harvestDate: Today.AddDays(5)));

            Assert.Equal(ValidationOutcome.Rejected, verdict.Outcome);
            Assert.Equal(RuleSeverity.Reject, Rule(verdict, "V5").Severity);
            Assert.Contains("shortfall=9", Rule(verdict, "V5").Evidence);
        }
    }

    public sealed class V6_SeasonalLimit
    {
        [Fact]
        public void Rejects_once_the_allowance_is_spent()
        {
            // Golden case G10.
            var applications = Enumerable.Range(1, 4)
                .Select(i => new AppliedTreatmentFact(MancozebProduct, Mancozeb, Today.AddDays(-10 * i)))
                .ToList();

            var verdict = Validate(context: Context(approval: Approval(maxApplications: 4), applications: applications));

            Assert.Equal(ValidationOutcome.Rejected, verdict.Outcome);
            Assert.Contains("used=4; max=4", Rule(verdict, "V6").Evidence);
        }

        [Fact]
        public void Counts_the_active_ingredient_not_the_brand()
        {
            // Two labels, one chemical: switching brands must not reset the regulator's limit.
            var applications = new List<AppliedTreatmentFact>
            {
                new(MancozebProduct, Mancozeb, Today.AddDays(-40)),
                new(OtherMancozebBrand, Mancozeb, Today.AddDays(-25)),
            };

            var verdict = Validate(context: Context(approval: Approval(maxApplications: 2), applications: applications));

            Assert.Equal(RuleStatus.Failed, Rule(verdict, "V6").Status);
        }

        [Fact]
        public void Ignores_applications_of_a_different_ingredient()
        {
            var otherIngredient = Guid.NewGuid();
            var applications = new List<AppliedTreatmentFact>
            {
                new(Guid.NewGuid(), otherIngredient, Today.AddDays(-5)),
            };

            var verdict = Validate(context: Context(approval: Approval(maxApplications: 1), applications: applications));

            Assert.Equal(RuleStatus.Passed, Rule(verdict, "V6").Status);
        }
    }

    public sealed class V7_ResistanceInterval
    {
        [Fact]
        public void Passes_when_the_ingredient_is_new_to_this_cycle()
        {
            Assert.Equal(RuleStatus.Passed, Rule(Validate(), "V7").Status);
        }

        [Fact]
        public void Asks_for_a_revision_when_repeated_too_soon()
        {
            var verdict = Validate(
                Proposal(sprayDate: Today.AddDays(1)),
                Context(approval: Approval(minInterval: 7),
                        applications: [new AppliedTreatmentFact(MancozebProduct, Mancozeb, Today.AddDays(-2))]));

            Assert.Equal(ValidationOutcome.Revise, verdict.Outcome);
            Assert.Contains("breeds resistance", Rule(verdict, "V7").Message);
        }

        [Fact]
        public void Measures_the_gap_from_the_spray_date_not_from_today()
        {
            // Proposing a spray far enough in the future satisfies the interval.
            var verdict = Validate(
                Proposal(sprayDate: Today.AddDays(6)),
                Context(approval: Approval(minInterval: 7),
                        applications: [new AppliedTreatmentFact(MancozebProduct, Mancozeb, Today.AddDays(-2))]));

            Assert.Equal(RuleStatus.Passed, Rule(verdict, "V7").Status);
        }
    }

    public sealed class V8_Weather
    {
        [Fact]
        public void Is_not_evaluated_without_a_forecast()
        {
            var result = Rule(Validate(), "V8");

            Assert.Equal(RuleStatus.NotEvaluated, result.Status);
            Assert.Contains("No forecast", result.Message);
        }

        [Fact]
        public void Asks_for_a_revision_when_rain_would_wash_the_spray_off()
        {
            // Golden case G6.
            var verdict = Validate(context: Context(weather: new WeatherAssessment(70, 5m, 26m)));

            Assert.Equal(ValidationOutcome.Revise, verdict.Outcome);
            Assert.Contains("chance of rain", Rule(verdict, "V8").Message);
        }

        [Theory]
        [InlineData(10, 20, 26, "drift")]
        [InlineData(10, 5, 35, "too hot")]
        public void Also_refuses_drift_and_heat(int rain, decimal wind, decimal temp, string expected)
        {
            var verdict = Validate(context: Context(weather: new WeatherAssessment(rain, wind, temp)));

            Assert.Contains(expected, Rule(verdict, "V8").Message);
        }

        [Fact]
        public void Accepts_a_calm_dry_day()
        {
            var verdict = Validate(context: Context(weather: new WeatherAssessment(5, 8m, 28m)));

            Assert.Equal(RuleStatus.Passed, Rule(verdict, "V8").Status);
        }
    }

    public sealed class V9_Stock
    {
        [Fact]
        public void Is_not_evaluated_without_stock_figures()
        {
            Assert.Equal(RuleStatus.NotEvaluated, Rule(Validate(), "V9").Status);
        }

        [Fact]
        public void Asks_for_a_revision_when_there_is_not_enough()
        {
            var verdict = Validate(Proposal(dose: 2m, total: 1.6m), Context(stock: new StockAssessment(1m, Today.AddMonths(6))));

            Assert.Equal(ValidationOutcome.Revise, verdict.Outcome);
            Assert.Contains("Only 1 available", Rule(verdict, "V9").Message);
        }

        [Fact]
        public void Refuses_stock_that_expires_before_the_spray_date()
        {
            var verdict = Validate(
                Proposal(sprayDate: Today.AddDays(10)),
                Context(stock: new StockAssessment(50m, Today.AddDays(5))));

            Assert.Equal(RuleStatus.Failed, Rule(verdict, "V9").Status);
            Assert.Contains("expires", Rule(verdict, "V9").Message);
        }
    }

    public sealed class V10_Authorisation
    {
        [Fact]
        public void Rejects_a_prescription_for_someone_elses_plot()
        {
            var verdict = Validate(context: Context(plotOwner: Guid.NewGuid()));

            Assert.Equal(ValidationOutcome.Rejected, verdict.Outcome);
            Assert.Contains("does not own", Rule(verdict, "V10").Message);
        }

        [Fact]
        public void Rejects_a_restricted_product_without_a_permit()
        {
            var verdict = Validate(context: Context(approval: Approval(restricted: true), permit: false));

            Assert.Equal(ValidationOutcome.Rejected, verdict.Outcome);
            Assert.Contains("needs a permit", Rule(verdict, "V10").Message);
        }

        [Fact]
        public void Allows_a_restricted_product_when_the_permit_is_held()
        {
            var verdict = Validate(context: Context(approval: Approval(restricted: true), permit: true));

            Assert.Equal(RuleStatus.Passed, Rule(verdict, "V10").Status);
        }
    }

    public sealed class V11_Credit
    {
        [Fact]
        public void Passes_when_no_limit_applies()
        {
            Assert.Equal(RuleStatus.Passed, Rule(Validate(), "V11").Status);
        }

        [Fact]
        public void Asks_for_a_revision_when_the_order_exceeds_the_limit()
        {
            var verdict = Validate(context: Context(creditLimit: 5_000m, estimatedCost: 12_000m));

            Assert.Equal(ValidationOutcome.Revise, verdict.Outcome);
            Assert.Equal(RuleSeverity.Revise, Rule(verdict, "V11").Severity);
        }

        [Fact]
        public void Is_not_evaluated_when_the_order_could_not_be_priced()
        {
            Assert.Equal(RuleStatus.NotEvaluated, Rule(Validate(context: Context(creditLimit: 5_000m)), "V11").Status);
        }
    }

    public sealed class Outcomes
    {
        [Fact]
        public void A_reject_outranks_any_number_of_revise_failures()
        {
            // Dose wrong (Revise) and pre-harvest interval breached (Reject) together.
            var verdict = Validate(
                Proposal(dose: 20m, total: 16m, sprayDate: Today),
                Context(approval: Approval(phi: 14), harvestDate: Today.AddDays(2)));

            Assert.Equal(ValidationOutcome.Rejected, verdict.Outcome);
            Assert.Contains("V5", verdict.Summary);
        }

        [Fact]
        public void The_summary_names_the_rules_that_failed()
        {
            var verdict = Validate(Proposal(dose: 20m, total: 16m));

            Assert.Equal(ValidationOutcome.Revise, verdict.Outcome);
            Assert.Contains("V3", verdict.Summary);
        }

        [Fact]
        public void Every_failure_carries_evidence_that_can_be_audited()
        {
            var verdict = Validate(
                Proposal(sprayDate: Today),
                Context(approval: Approval(phi: 14), harvestDate: Today.AddDays(5)));

            Assert.All(verdict.Failures.Where(f => f.Code != "V1"), failure => Assert.False(string.IsNullOrWhiteSpace(failure.Evidence)));
        }

        [Fact]
        public void The_same_inputs_always_give_the_same_verdict()
        {
            // Determinism is the property that makes this a control rather than an opinion.
            var proposal = Proposal(dose: 20m, total: 16m);
            var context = Context(weather: new WeatherAssessment(70, 20m, 35m));

            var first = PrescriptionSafetyValidator.Validate(proposal, context, Today);
            var second = PrescriptionSafetyValidator.Validate(proposal, context, Today);

            Assert.Equal(first.Outcome, second.Outcome);
            Assert.Equal(
                first.Results.Select(r => (r.Code, r.Status, r.Message)),
                second.Results.Select(r => (r.Code, r.Status, r.Message)));
        }
    }
}
