namespace AgriGuard.Domain.Validation;

/// <summary>
/// The deterministic safety gate for every prescription the agent proposes (§9.4, rules V1–V11).
///
/// WHY THIS EXISTS: an LLM cannot be trusted to enforce chemical safety, and asking it to check
/// its own proposal is not a control. Every proposal — however confident, however well argued —
/// passes through this plain C# code, which reads the ProductCropApproval rules table and the
/// plot's real application history. Nothing here is a heuristic, a prompt or a model call; the
/// same inputs always give the same verdict, and that verdict is reproducible in a unit test.
///
/// It is also the defence against prompt injection: a farmer's note that talks the model into
/// proposing 10× the maximum dose still fails V3, and a compromised model cannot spray anything,
/// because issuing a prescription happens in ASP.NET Core after a human approves (§9.5).
///
/// Severity decides what happens next: Reject is terminal, Revise lets the Action agent try again.
/// The split follows harm — a dose outside the label range can be corrected, spraying inside the
/// pre-harvest interval means residue on food that someone eats.
/// </summary>
public static class PrescriptionSafetyValidator
{
    /// <summary>Tolerance on the quantity arithmetic (V4): packs come in fixed sizes and get rounded.</summary>
    private const decimal QuantityTolerance = 0.02m;

    private const int MaxRainProbabilityPercent = 40;
    private const decimal MaxWindSpeedKph = 15m;
    private const decimal MaxTemperatureC = 32m;

    public static ValidationVerdict Validate(
        PrescriptionProposal proposal,
        PrescriptionValidationContext context,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);

        List<RuleResult> results =
        [
            SchemaAndRanges(proposal, today),
            ApprovalExists(proposal, context),
        ];

        // V3–V7 all read the approval row. Without one there is nothing to measure against, so
        // they are reported as not evaluated rather than passing by default.
        var approval = context.Approval is { IsActive: true } ? context.Approval : null;

        results.Add(DoseWithinLabel(proposal, approval));
        results.Add(QuantityMatchesArea(proposal, context, approval));
        results.Add(PreHarvestInterval(proposal, context, approval));
        results.Add(ApplicationsPerCycle(context, approval));
        results.Add(ResistanceInterval(proposal, context, approval));
        results.Add(SprayWindow(context, approval));
        results.Add(StockAvailable(proposal, context));
        results.Add(Authorisation(context, approval));
        results.Add(WithinCreditLimit(context));

        return new ValidationVerdict(Outcome(results), results);
    }

    private static ValidationOutcome Outcome(IReadOnlyList<RuleResult> results)
    {
        if (results.Any(r => r.IsFailure && r.Severity == RuleSeverity.Reject)) return ValidationOutcome.Rejected;
        if (results.Any(r => r.IsFailure)) return ValidationOutcome.Revise;
        return ValidationOutcome.Approved;
    }

    // ── V1 ───────────────────────────────────────────────────────────────────
    private static RuleResult SchemaAndRanges(PrescriptionProposal proposal, DateOnly today)
    {
        List<string> problems = [];

        if (proposal.ProductId == Guid.Empty) problems.Add("no product chosen");
        if (proposal.DosePerHectare <= 0) problems.Add($"dose must be greater than 0 (got {proposal.DosePerHectare})");
        if (proposal.TotalQuantity <= 0) problems.Add($"quantity must be greater than 0 (got {proposal.TotalQuantity})");
        if (proposal.SprayDate == default) problems.Add("no spray date");
        // A spray date in the past cannot be planned, only recorded — and recording is a
        // different operation that does not go through the agent.
        else if (proposal.SprayDate < today) problems.Add($"spray date {proposal.SprayDate:yyyy-MM-dd} is in the past");

        return problems.Count == 0
            ? Pass("V1", "Proposal is well formed", RuleSeverity.Reject, "All required fields present and in range.")
            : Fail("V1", "Proposal is well formed", RuleSeverity.Reject,
                $"The proposal is not usable: {string.Join(", ", problems)}.");
    }

    // ── V2 ───────────────────────────────────────────────────────────────────
    private static RuleResult ApprovalExists(PrescriptionProposal proposal, PrescriptionValidationContext context)
    {
        const string name = "Product is approved for this crop";

        if (context.Approval is null)
            return Fail("V2", name, RuleSeverity.Reject,
                $"This product is not approved for {context.CropName}.",
                $"No active ProductCropApproval for product {proposal.ProductId} on crop {context.CropId}.");

        if (!context.Approval.IsActive)
            return Fail("V2", name, RuleSeverity.Reject,
                $"{context.Approval.ProductName} has been withdrawn for {context.CropName} and may no longer be used.",
                "ProductCropApproval.IsActive = false.");

        return Pass("V2", name, RuleSeverity.Reject,
            $"{context.Approval.ProductName} is approved for {context.CropName}.");
    }

    // ── V3 ───────────────────────────────────────────────────────────────────
    private static RuleResult DoseWithinLabel(PrescriptionProposal proposal, ProductApprovalSnapshot? approval)
    {
        const string name = "Dose is within the label range";
        if (approval is null) return NotChecked("V3", name, RuleSeverity.Revise);

        var withinRange = proposal.DosePerHectare >= approval.MinDosePerHectare
                       && proposal.DosePerHectare <= approval.MaxDosePerHectare;

        return withinRange
            ? Pass("V3", name, RuleSeverity.Revise,
                $"{proposal.DosePerHectare}/ha is within the approved {approval.MinDosePerHectare}–{approval.MaxDosePerHectare}/ha.")
            : Fail("V3", name, RuleSeverity.Revise,
                $"{proposal.DosePerHectare}/ha is outside the approved range for {approval.ProductName} " +
                $"({approval.MinDosePerHectare}–{approval.MaxDosePerHectare}/ha).",
                $"dose={proposal.DosePerHectare}; min={approval.MinDosePerHectare}; max={approval.MaxDosePerHectare}");
    }

    // ── V4 ───────────────────────────────────────────────────────────────────
    private static RuleResult QuantityMatchesArea(
        PrescriptionProposal proposal,
        PrescriptionValidationContext context,
        ProductApprovalSnapshot? approval)
    {
        const string name = "Quantity matches dose × area";
        if (approval is null) return NotChecked("V4", name, RuleSeverity.Revise);

        var expected = proposal.DosePerHectare * context.AreaHectares;
        if (expected <= 0) return NotChecked("V4", name, RuleSeverity.Revise);

        var drift = Math.Abs(proposal.TotalQuantity - expected) / expected;

        // Catches the mistake that actually matters: ordering (and spraying) for the wrong area.
        return drift <= QuantityTolerance
            ? Pass("V4", name, RuleSeverity.Revise,
                $"{proposal.TotalQuantity} for {context.AreaHectares} ha at {proposal.DosePerHectare}/ha.")
            : Fail("V4", name, RuleSeverity.Revise,
                $"Quantity {proposal.TotalQuantity} does not match {proposal.DosePerHectare}/ha over " +
                $"{context.AreaHectares} ha (expected about {Math.Round(expected, 3)}).",
                $"expected={Math.Round(expected, 4)}; proposed={proposal.TotalQuantity}; drift={drift:P1}");
    }

    // ── V5 ───────────────────────────────────────────────────────────────────
    private static RuleResult PreHarvestInterval(
        PrescriptionProposal proposal,
        PrescriptionValidationContext context,
        ProductApprovalSnapshot? approval)
    {
        const string name = "Pre-harvest interval is respected";
        if (approval is null) return NotChecked("V5", name, RuleSeverity.Reject);

        var earliestHarvest = proposal.SprayDate.AddDays(approval.PreHarvestIntervalDays);
        var daysSpare = context.HarvestDate.DayNumber - earliestHarvest.DayNumber;

        // The one rule that puts residue on food someone eats. Terminal, never negotiable.
        return daysSpare >= 0
            ? Pass("V5", name, RuleSeverity.Reject,
                $"Spraying on {proposal.SprayDate:yyyy-MM-dd} clears the {approval.PreHarvestIntervalDays}-day interval " +
                $"with {daysSpare} day(s) to spare before harvest on {context.HarvestDate:yyyy-MM-dd}.")
            : Fail("V5", name, RuleSeverity.Reject,
                $"{approval.ProductName} needs {approval.PreHarvestIntervalDays} days between spraying and harvest. " +
                $"Spraying on {proposal.SprayDate:yyyy-MM-dd} would allow harvest from {earliestHarvest:yyyy-MM-dd}, " +
                $"but harvest is planned for {context.HarvestDate:yyyy-MM-dd}.",
                $"sprayDate={proposal.SprayDate:yyyy-MM-dd}; phi={approval.PreHarvestIntervalDays}; " +
                $"harvest={context.HarvestDate:yyyy-MM-dd}; shortfall={-daysSpare} day(s)");
    }

    // ── V6 ───────────────────────────────────────────────────────────────────
    private static RuleResult ApplicationsPerCycle(PrescriptionValidationContext context, ProductApprovalSnapshot? approval)
    {
        const string name = "Seasonal application limit is not exceeded";
        if (approval is null) return NotChecked("V6", name, RuleSeverity.Reject);

        // Counted per active ingredient, not per product label: two brands of the same chemical
        // must not quietly double the allowance the regulator set.
        var used = context.Applications.Count(a => a.ActiveIngredientId == approval.ActiveIngredientId);

        return used < approval.MaxApplicationsPerCycle
            ? Pass("V6", name, RuleSeverity.Reject,
                $"{approval.ActiveIngredientName} used {used} of {approval.MaxApplicationsPerCycle} allowed this season.")
            : Fail("V6", name, RuleSeverity.Reject,
                $"{approval.ActiveIngredientName} has already been applied {used} time(s) this season, " +
                $"the maximum allowed for {context.CropName}.",
                $"ingredient={approval.ActiveIngredientId}; used={used}; max={approval.MaxApplicationsPerCycle}");
    }

    // ── V7 ───────────────────────────────────────────────────────────────────
    private static RuleResult ResistanceInterval(
        PrescriptionProposal proposal,
        PrescriptionValidationContext context,
        ProductApprovalSnapshot? approval)
    {
        const string name = "Resistance-management interval has passed";
        if (approval is null) return NotChecked("V7", name, RuleSeverity.Revise);

        var lastSameIngredient = context.Applications
            .Where(a => a.ActiveIngredientId == approval.ActiveIngredientId)
            .Select(a => a.AppliedOn)
            .DefaultIfEmpty()
            .Max();

        if (lastSameIngredient == default)
            return Pass("V7", name, RuleSeverity.Revise,
                $"{approval.ActiveIngredientName} has not been used on this cycle yet.");

        var gap = proposal.SprayDate.DayNumber - lastSameIngredient.DayNumber;
        var earliest = lastSameIngredient.AddDays(approval.MinDaysBetweenApplications);

        return gap >= approval.MinDaysBetweenApplications
            ? Pass("V7", name, RuleSeverity.Revise,
                $"{gap} days since the last {approval.ActiveIngredientName} application " +
                $"(minimum {approval.MinDaysBetweenApplications}).")
            : Fail("V7", name, RuleSeverity.Revise,
                $"{approval.ActiveIngredientName} was applied on {lastSameIngredient:yyyy-MM-dd}. " +
                $"Repeating it after only {gap} day(s) breeds resistance — the next application is due " +
                $"from {earliest:yyyy-MM-dd}.",
                $"lastApplied={lastSameIngredient:yyyy-MM-dd}; gap={gap}; required={approval.MinDaysBetweenApplications}");
    }

    // ── V8 ───────────────────────────────────────────────────────────────────
    private static RuleResult SprayWindow(PrescriptionValidationContext context, ProductApprovalSnapshot? approval)
    {
        const string name = "Weather suits spraying";
        if (approval is null || context.Weather is null)
            return NotChecked("V8", name, RuleSeverity.Revise, "No forecast was available for the spray date.");

        var weather = context.Weather;
        List<string> problems = [];

        if (weather.RainProbabilityPercent >= MaxRainProbabilityPercent)
            problems.Add($"{weather.RainProbabilityPercent}% chance of rain within the {approval.RainfastHours}-hour rainfast window");
        if (weather.WindSpeedKph >= MaxWindSpeedKph)
            problems.Add($"wind {weather.WindSpeedKph} km/h would cause spray drift");
        if (weather.TemperatureC > MaxTemperatureC)
            problems.Add($"{weather.TemperatureC} °C is too hot — the spray evaporates before it works");

        return problems.Count == 0
            ? Pass("V8", name, RuleSeverity.Revise,
                $"Forecast is suitable: {weather.RainProbabilityPercent}% rain, {weather.WindSpeedKph} km/h wind, {weather.TemperatureC} °C.")
            : Fail("V8", name, RuleSeverity.Revise,
                $"The forecast does not suit spraying: {string.Join("; ", problems)}.",
                $"rain={weather.RainProbabilityPercent}%; wind={weather.WindSpeedKph}kph; temp={weather.TemperatureC}C; " +
                $"rainfastHours={approval.RainfastHours}");
    }

    // ── V9 ───────────────────────────────────────────────────────────────────
    private static RuleResult StockAvailable(PrescriptionProposal proposal, PrescriptionValidationContext context)
    {
        const string name = "Dealer stock covers the order";
        if (context.Stock is null)
            return NotChecked("V9", name, RuleSeverity.Revise, "Stock figures were not available.");

        var stock = context.Stock;

        if (stock.AvailableQuantity < proposal.TotalQuantity)
            return Fail("V9", name, RuleSeverity.Revise,
                $"Only {stock.AvailableQuantity} available, but {proposal.TotalQuantity} is needed.",
                $"available={stock.AvailableQuantity}; required={proposal.TotalQuantity}");

        // Stock that expires before the spray date is not usable stock.
        if (stock.EarliestBatchExpiry is { } expiry && expiry <= proposal.SprayDate)
            return Fail("V9", name, RuleSeverity.Revise,
                $"The available batch expires on {expiry:yyyy-MM-dd}, before the planned spray date of {proposal.SprayDate:yyyy-MM-dd}.",
                $"batchExpiry={expiry:yyyy-MM-dd}; sprayDate={proposal.SprayDate:yyyy-MM-dd}");

        return Pass("V9", name, RuleSeverity.Revise,
            $"{stock.AvailableQuantity} available for an order of {proposal.TotalQuantity}.");
    }

    // ── V10 ──────────────────────────────────────────────────────────────────
    private static RuleResult Authorisation(PrescriptionValidationContext context, ProductApprovalSnapshot? approval)
    {
        const string name = "Caller may treat this plot with this product";

        // The agent proposes for a specific farmer's plot; a run that drifted onto someone
        // else's land is a bug or an attack, and either way it stops here.
        if (context.PlotOwnerId != context.RequestedForUserId)
            return Fail("V10", name, RuleSeverity.Reject,
                "This prescription is for a plot the farmer does not own.",
                $"plotOwner={context.PlotOwnerId}; requestedFor={context.RequestedForUserId}");

        if (approval is { IsRestricted: true } && !context.HasRestrictedUsePermit)
            return Fail("V10", name, RuleSeverity.Reject,
                $"{approval.ProductName} is restricted and needs a permit, which this farm does not hold.",
                "approval.IsRestricted = true; permit = none");

        return Pass("V10", name, RuleSeverity.Reject, "The farmer owns the plot and may use this product.");
    }

    // ── V11 ──────────────────────────────────────────────────────────────────
    private static RuleResult WithinCreditLimit(PrescriptionValidationContext context)
    {
        const string name = "Order is within the farmer's credit limit";

        if (context.CreditLimit is null)
            return Pass("V11", name, RuleSeverity.Revise, "No credit limit applies to this farmer.");

        if (context.EstimatedCost is null)
            return NotChecked("V11", name, RuleSeverity.Revise, "The order cost could not be priced.");

        return context.EstimatedCost <= context.CreditLimit
            ? Pass("V11", name, RuleSeverity.Revise,
                $"Estimated cost {context.EstimatedCost:N2} is within the {context.CreditLimit:N2} limit.")
            : Fail("V11", name, RuleSeverity.Revise,
                $"Estimated cost {context.EstimatedCost:N2} exceeds the farmer's credit limit of {context.CreditLimit:N2}.",
                $"cost={context.EstimatedCost}; limit={context.CreditLimit}");
    }

    private static RuleResult Pass(string code, string name, RuleSeverity severity, string message) =>
        new(code, name, RuleStatus.Passed, severity, message);

    private static RuleResult Fail(string code, string name, RuleSeverity severity, string message, string? evidence = null) =>
        new(code, name, RuleStatus.Failed, severity, message, evidence);

    private static RuleResult NotChecked(string code, string name, RuleSeverity severity, string? because = null) =>
        new(code, name, RuleStatus.NotEvaluated, severity,
            because ?? "Could not be checked because the product is not approved for this crop.");
}
