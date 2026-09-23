using AgriGuard.Application.Prescriptions;

namespace AgriGuard.UnitTests.Prescriptions;

/// <summary>
/// One passing baseline, then each rule broken on its own. Every test starts from a proposal that
/// passes all eleven rules and changes exactly one fact, so a failure points at exactly one rule.
/// </summary>
public sealed class PrescriptionSafetyValidatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);
    private static readonly Guid RunId = Guid.CreateVersion7();
    private static readonly Guid CycleId = Guid.CreateVersion7();
    private static readonly Guid ProductId = Guid.CreateVersion7();
    private static readonly Guid FarmerId = Guid.CreateVersion7();

    // 0.8 ha of tomato, harvest in 40 days; Mancozeb 1.5–2.5 kg/ha, 7-day PHI, 4 uses, 7 days apart.
    private static readonly ParsedProposal Proposal = new(RunId, CycleId, ProductId, 2.0m, 1.6m, Today.AddDays(1), null);

    private static readonly PrescriptionSafetyContext Context = new(
        Today,
        new CycleFacts(CycleId, IsActive: true, CropId: Guid.CreateVersion7(), AreaHectares: 0.8m, HarvestDate: Today.AddDays(40), PlotOwnerId: FarmerId),
        new ProductFacts(ProductId, "Mancozeb 80 WP", IsActive: true, "Mancozeb", "Kilogram", PackSize: 1.0m, UnitPrice: 2400m),
        new ApprovalFacts(IsActive: true, 1.5m, 2.5m, PreHarvestIntervalDays: 7, MaxApplicationsPerCycle: 4,
            MinDaysBetweenApplications: 7, RainfastHours: 4, IsRestricted: false),
        ApplicationHistory.None,
        new RunFacts(RunId, CycleId, FarmerId, IsTerminal: false),
        new DealerStock(Guid.CreateVersion7(), "Kandy Agro Supplies", AvailableQuantity: 40m, PackPrice: 2400m),
        new SprayWeather(MaxRainProbabilityPercent: 10m, MaxWindKmh: 5m, MaxTemperatureC: 25m),
        FarmerCreditLimit: 50_000m);

    private static RuleResult Rule(PrescriptionVerdict verdict, string code) => verdict.Results.Single(r => r.Code == code);

    private static void AssertOnlyFailure(PrescriptionVerdict verdict, string code, VerdictOutcome outcome)
    {
        Assert.Equal(outcome, verdict.Outcome);
        Assert.Equal([code], verdict.Results.Where(r => r.Status == RuleStatus.Failed).Select(r => r.Code));
    }

    // ── Baseline ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_compliant_proposal_passes_all_eleven_rules()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context);

        Assert.Equal(VerdictOutcome.Approved, verdict.Outcome);
        Assert.Equal(11, verdict.Results.Count);
        Assert.All(verdict.Results, r => Assert.Equal(RuleStatus.Passed, r.Status));
        Assert.Equal("All 11 rules passed.", verdict.Summary);
        Assert.Equal(Enumerable.Range(1, 11).Select(i => $"V{i}"), verdict.Results.Select(r => r.Code));
    }

    // ── V1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void V1_a_well_formed_proposal_parses()
    {
        var input = new PrescriptionProposalInput(RunId.ToString(), CycleId.ToString(), ProductId.ToString(), 2.0m, 1.6m, "2026-09-25", null);

        var (proposal, result) = PrescriptionSafetyValidator.CheckShape(input, Today);

        Assert.Equal(RuleStatus.Passed, result.Status);
        Assert.Equal(Proposal, proposal);
    }

    [Theory]
    [InlineData(null, "2.0", "2026-09-25", "runId is required")]
    [InlineData("not-a-guid", "2.0", "2026-09-25", "runId is not a valid id")]
    [InlineData("RUN", "-1", "2026-09-25", "dosePerHectare must be greater than 0")]
    [InlineData("RUN", "2000", "2026-09-25", "implausibly large")]
    [InlineData("RUN", "2.0", "25/09/2026", "yyyy-MM-dd")]
    [InlineData("RUN", "2.0", "2026-09-20", "in the past")]
    [InlineData("RUN", "2.0", "2026-12-25", "more than 30 days ahead")]
    public void V1_a_malformed_proposal_is_rejected_and_nothing_else_is_checked(string? runId, string dose, string sprayDate, string expected)
    {
        var input = new PrescriptionProposalInput(
            runId == "RUN" ? RunId.ToString() : runId, CycleId.ToString(), ProductId.ToString(),
            decimal.Parse(dose, System.Globalization.CultureInfo.InvariantCulture), 1.6m, sprayDate, null);

        var (proposal, result) = PrescriptionSafetyValidator.CheckShape(input, Today);
        var verdict = PrescriptionSafetyValidator.Malformed(result);

        Assert.Null(proposal);
        Assert.Contains(expected, result.Message);
        Assert.Equal(VerdictOutcome.Rejected, verdict.Outcome);
        Assert.All(verdict.Results.Skip(1), r => Assert.Equal(RuleStatus.Skipped, r.Status));
    }

    // ── V2 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void V2_a_product_not_approved_for_the_crop_is_rejected()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Approval = null });

        Assert.Equal(VerdictOutcome.Rejected, verdict.Outcome);
        Assert.Equal(RuleStatus.Failed, Rule(verdict, "V2").Status);
        // No approval means no limits: the dose and interval rules cannot be applied.
        Assert.Equal(RuleStatus.Skipped, Rule(verdict, "V3").Status);
        Assert.Equal(RuleStatus.Skipped, Rule(verdict, "V5").Status);
    }

    [Fact]
    public void V2_a_withdrawn_approval_is_rejected()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Approval = Context.Approval! with { IsActive = false } });

        Assert.Equal(RuleStatus.Failed, Rule(verdict, "V2").Status);
        Assert.Contains("withdrawn", Rule(verdict, "V2").Message);
    }

    // ── V3 ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.5)]
    public void V3_the_bounds_of_the_dose_range_are_allowed(decimal dose)
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal with { DosePerHectare = dose, TotalQuantity = dose * 0.8m }, Context);

        Assert.Equal(RuleStatus.Passed, Rule(verdict, "V3").Status);
    }

    [Fact]
    public void V3_a_dose_above_the_range_is_sent_back_for_revision()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal with { DosePerHectare = 3.0m, TotalQuantity = 2.4m }, Context);

        AssertOnlyFailure(verdict, "V3", VerdictOutcome.Revise);
        Assert.Contains("1.5–2.5 kg/ha", Rule(verdict, "V3").Message);
    }

    // ── V4 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void V4_a_total_within_two_percent_is_accepted()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal with { TotalQuantity = 1.63m }, Context);

        Assert.Equal(RuleStatus.Passed, Rule(verdict, "V4").Status);
    }

    [Fact]
    public void V4_a_total_that_does_not_match_dose_times_area_is_revised_with_the_right_figure()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal with { TotalQuantity = 2.0m }, Context);

        AssertOnlyFailure(verdict, "V4", VerdictOutcome.Revise);
        Assert.Contains("should be 1.6", Rule(verdict, "V4").Message);
    }

    // ── V5 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void V5_spraying_exactly_one_interval_before_harvest_is_allowed()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal,
            Context with { Cycle = Context.Cycle! with { HarvestDate = Proposal.SprayDate.AddDays(7) } });

        Assert.Equal(RuleStatus.Passed, Rule(verdict, "V5").Status);
    }

    [Fact]
    public void V5_spraying_inside_the_pre_harvest_interval_is_rejected()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal,
            Context with { Cycle = Context.Cycle! with { HarvestDate = Proposal.SprayDate.AddDays(6) } });

        AssertOnlyFailure(verdict, "V5", VerdictOutcome.Rejected);
        Assert.Contains("latest safe spray date", Rule(verdict, "V5").Message);
    }

    // ── V6 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void V6_the_last_allowed_application_passes()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { History = new ApplicationHistory(3, Today.AddDays(-30)) });

        Assert.Equal(RuleStatus.Passed, Rule(verdict, "V6").Status);
    }

    [Fact]
    public void V6_exceeding_the_applications_per_cycle_is_rejected()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { History = new ApplicationHistory(4, Today.AddDays(-30)) });

        AssertOnlyFailure(verdict, "V6", VerdictOutcome.Rejected);
    }

    // ── V7 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void V7_an_application_exactly_the_minimum_interval_later_passes()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { History = new ApplicationHistory(1, Proposal.SprayDate.AddDays(-7)) });

        Assert.Equal(RuleStatus.Passed, Rule(verdict, "V7").Status);
    }

    [Fact]
    public void V7_spraying_the_same_ingredient_too_soon_is_revised_with_the_earliest_date()
    {
        var last = Proposal.SprayDate.AddDays(-3);
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { History = new ApplicationHistory(1, last) });

        AssertOnlyFailure(verdict, "V7", VerdictOutcome.Revise);
        Assert.Contains(last.AddDays(7).ToString("yyyy-MM-dd"), Rule(verdict, "V7").Message);
    }

    // ── V8 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void V8_rain_within_the_rainfast_window_is_revised()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Weather = new SprayWeather(70m, 5m, 25m) });

        AssertOnlyFailure(verdict, "V8", VerdictOutcome.Revise);
    }

    [Fact]
    public void V8_without_a_forecast_is_reported_as_not_checked_and_does_not_block()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Weather = null });

        Assert.Equal(VerdictOutcome.Approved, verdict.Outcome);
        Assert.Equal(RuleStatus.Skipped, Rule(verdict, "V8").Status);
        Assert.Equal("10 of 11 rules passed; not checked: V8.", verdict.Summary);
    }

    // ── V9 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void V9_too_little_stock_is_revised()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Stock = Context.Stock! with { AvailableQuantity = 1.0m } });

        AssertOnlyFailure(verdict, "V9", VerdictOutcome.Revise);
        Assert.Contains("only 1", Rule(verdict, "V9").Message);
    }

    [Fact]
    public void V9_no_in_date_stock_anywhere_is_revised()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Stock = null });

        AssertOnlyFailure(verdict, "V9", VerdictOutcome.Revise);
        Assert.StartsWith("No dealer in the farm's district", Rule(verdict, "V9").Message);
    }

    // ── V10 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void V10_a_proposal_for_another_case_s_crop_cycle_is_rejected()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Run = Context.Run! with { CaseCropCycleId = Guid.CreateVersion7() } });

        AssertOnlyFailure(verdict, "V10", VerdictOutcome.Rejected);
    }

    [Fact]
    public void V10_a_restricted_product_is_rejected()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Approval = Context.Approval! with { IsRestricted = true } });

        AssertOnlyFailure(verdict, "V10", VerdictOutcome.Rejected);
        Assert.Contains("restricted", Rule(verdict, "V10").Message);
    }

    [Fact]
    public void V10_a_proposal_from_a_finished_run_is_rejected()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Run = Context.Run! with { IsTerminal = true } });

        AssertOnlyFailure(verdict, "V10", VerdictOutcome.Rejected);
    }

    [Fact]
    public void V10_an_unknown_crop_cycle_is_rejected_and_the_rules_needing_it_are_skipped()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { Cycle = null, Approval = null });

        Assert.Equal(VerdictOutcome.Rejected, verdict.Outcome);
        Assert.Equal(RuleStatus.Failed, Rule(verdict, "V10").Status);
        Assert.Equal(RuleStatus.Skipped, Rule(verdict, "V2").Status);
        Assert.Equal(RuleStatus.Skipped, Rule(verdict, "V4").Status);
    }

    // ── V11 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void V11_an_order_over_the_credit_limit_is_revised()
    {
        // 2 packs × 2 400 = 4 800 LKR against a 4 000 limit.
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { FarmerCreditLimit = 4_000m });

        AssertOnlyFailure(verdict, "V11", VerdictOutcome.Revise);
        Assert.Contains("4,800", Rule(verdict, "V11").Message);
    }

    [Fact]
    public void V11_a_farmer_without_a_credit_limit_passes()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal, Context with { FarmerCreditLimit = null });

        Assert.Equal(RuleStatus.Passed, Rule(verdict, "V11").Status);
    }

    // ── Outcome ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_reject_level_failure_outranks_revise_level_failures()
    {
        var verdict = PrescriptionSafetyValidator.Validate(Proposal with { DosePerHectare = 3.0m },
            Context with { Cycle = Context.Cycle! with { HarvestDate = Proposal.SprayDate.AddDays(2) } });

        Assert.Equal(VerdictOutcome.Rejected, verdict.Outcome);
        Assert.Equal("Rejected: V3, V4, V5 failed.", verdict.Summary);
    }
}
