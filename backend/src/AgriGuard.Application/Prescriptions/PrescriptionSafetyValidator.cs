using System.Globalization;

namespace AgriGuard.Application.Prescriptions;

/// <summary>
/// The deterministic safety check every treatment proposal must clear (§9.4, rules V1–V11).
///
/// Pure code, no LLM. The limits come from the ProductCropApproval rules table and the live plot
/// state, which the caller loads into a <see cref="PrescriptionSafetyContext"/> first; nothing
/// here touches the database, so the same inputs always give the same verdict and every rule is
/// unit-tested on its own.
///
/// This is the control that makes the agent safe to use. The Validation agent does not judge
/// safety — it submits the proposal here and reports what comes back — so even a model that has
/// been talked into proposing something dangerous cannot get it past these rules.
///
/// Every rule is always reported (passed, failed or skipped with the reason), so the agronomist
/// sees all eleven, not just the first failure.
/// </summary>
public static class PrescriptionSafetyValidator
{
    public const int RuleCount = 11;

    /// <summary>V4: total quantity may differ from dose × area by this fraction (pack rounding).</summary>
    public const decimal QuantityTolerance = 0.02m;

    /// <summary>V1: a prescription is for the near future, not next season.</summary>
    public const int MaxDaysAhead = 30;

    /// <summary>V1: bounds that catch unit mix-ups (ml for L, g for kg) before any other rule runs.</summary>
    public const decimal MaxPlausibleDosePerHectare = 1_000m;
    public const decimal MaxPlausibleTotalQuantity = 100_000m;

    /// <summary>V8 thresholds within the rainfast window.</summary>
    public const decimal MaxRainProbabilityPercent = 40m;
    public const decimal MaxWindKmh = 15m;
    public const decimal MaxTemperatureC = 32m;

    private static readonly IReadOnlyDictionary<string, (string Name, RuleSeverity Severity)> Rules =
        new Dictionary<string, (string, RuleSeverity)>
        {
            ["V1"] = ("Proposal is well-formed", RuleSeverity.Reject),
            ["V2"] = ("Product is approved for this crop", RuleSeverity.Reject),
            ["V3"] = ("Dose is within the approved range", RuleSeverity.Revise),
            ["V4"] = ("Total quantity matches dose × plot area", RuleSeverity.Revise),
            ["V5"] = ("Pre-harvest interval is respected", RuleSeverity.Reject),
            ["V6"] = ("Applications per cycle stay within the limit", RuleSeverity.Reject),
            ["V7"] = ("Interval since the last application is respected", RuleSeverity.Revise),
            ["V8"] = ("Weather allows spraying", RuleSeverity.Revise),
            ["V9"] = ("Dealer stock is available", RuleSeverity.Revise),
            ["V10"] = ("Proposal belongs to the case and uses a permitted product", RuleSeverity.Reject),
            ["V11"] = ("Order is within the farmer's credit limit", RuleSeverity.Revise)
        };

    // ── V1: shape ────────────────────────────────────────────────────────────

    /// <summary>
    /// Rule V1. Runs before anything is loaded, because the other rules need well-formed ids and
    /// dates to look anything up. Returns the parsed proposal, or null with the failure.
    /// </summary>
    public static (ParsedProposal? Proposal, RuleResult Result) CheckShape(PrescriptionProposalInput input, DateOnly today)
    {
        List<string> problems = [];

        var runId = ParseId(input.RunId, "runId", problems, required: true);
        var cycleId = ParseId(input.CropCycleId, "cropCycleId", problems, required: true);
        var productId = ParseId(input.ProductId, "productId", problems, required: true);
        var dealerId = ParseId(input.DealerId, "dealerId", problems, required: false);

        CheckAmount(input.DosePerHectare, "dosePerHectare", MaxPlausibleDosePerHectare, problems);
        CheckAmount(input.TotalQuantity, "totalQuantity", MaxPlausibleTotalQuantity, problems);

        DateOnly? sprayDate = null;
        if (string.IsNullOrWhiteSpace(input.SprayDate))
            problems.Add("sprayDate is required");
        else if (!DateOnly.TryParseExact(input.SprayDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            problems.Add("sprayDate must be a date in yyyy-MM-dd form");
        else if (parsed < today)
            problems.Add($"sprayDate {Iso(parsed)} is in the past");
        else if (parsed > today.AddDays(MaxDaysAhead))
            problems.Add($"sprayDate {Iso(parsed)} is more than {MaxDaysAhead} days ahead; propose a date within the next {MaxDaysAhead} days");
        else
            sprayDate = parsed;

        if (problems.Count > 0)
            return (null, Fail("V1", $"The proposal is malformed: {string.Join("; ", problems)}.", null));

        var proposal = new ParsedProposal(
            runId!.Value, cycleId!.Value, productId!.Value,
            input.DosePerHectare!.Value, input.TotalQuantity!.Value, sprayDate!.Value, dealerId);
        return (proposal, Pass("V1", "All fields are present and well-formed.", null));
    }

    /// <summary>The verdict for a proposal that failed V1: nothing else can be checked.</summary>
    public static PrescriptionVerdict Malformed(RuleResult shapeFailure)
    {
        var results = new List<RuleResult> { shapeFailure };
        results.AddRange(Rules.Keys.Skip(1).Select(code => Skip(code, "Not checked: the proposal is malformed (V1).")));
        return Conclude(results);
    }

    // ── V1–V11: the full check ───────────────────────────────────────────────

    public static PrescriptionVerdict Validate(ParsedProposal proposal, PrescriptionSafetyContext context)
    {
        var cycle = context.Cycle;
        var product = context.Product;
        var approval = context.Approval;

        var v2 = CheckApproval(product, approval, cycle);
        // V3, V5–V8 read the approval's limits; with no valid approval there are no limits to apply.
        var limits = v2.Status == RuleStatus.Passed ? approval : null;
        const string needsApproval = "Not checked: needs an active approval for this product and crop (V2).";

        List<RuleResult> results =
        [
            Pass("V1", "All fields are present and well-formed.", null),
            v2,
            limits is null ? Skip("V3", needsApproval) : CheckDose(proposal, limits, product!),
            cycle is null ? Skip("V4", "Not checked: the crop cycle was not found (V10).") : CheckQuantity(proposal, cycle),
            limits is null ? Skip("V5", needsApproval) : CheckPreHarvestInterval(proposal, limits, cycle!),
            limits is null ? Skip("V6", needsApproval) : CheckApplicationLimit(context.History, limits, product!),
            limits is null ? Skip("V7", needsApproval) : CheckApplicationInterval(proposal, context.History, limits, product!),
            limits is null ? Skip("V8", needsApproval) : CheckWeather(context.Weather, limits),
            product is null ? Skip("V9", "Not checked: the product was not found (V2).") : CheckStock(proposal, context.Stock, product),
            CheckOwnership(proposal, cycle, context.Run, approval, product),
            product is null ? Skip("V11", "Not checked: the product was not found (V2).") : CheckCredit(proposal, product, context.Stock, context.FarmerCreditLimit)
        ];

        return Conclude(results);
    }

    // ── The rules ────────────────────────────────────────────────────────────

    private static RuleResult CheckApproval(ProductFacts? product, ApprovalFacts? approval, CycleFacts? cycle)
    {
        if (cycle is null)
            return Skip("V2", "Not checked: the crop cycle was not found, so the crop is unknown (V10).");
        if (product is null)
            return Fail("V2", "The proposed product does not exist in the catalogue.", null);
        if (!product.IsActive)
            return Fail("V2", $"{product.Name} has been withdrawn from the catalogue.", null);
        if (approval is null)
            return Fail("V2", $"{product.Name} is not approved for this crop. Choose one of the approved products.", null);
        if (!approval.IsActive)
            return Fail("V2", $"{product.Name}'s approval for this crop has been withdrawn.", "approval=inactive");

        return Pass("V2", $"{product.Name} is approved for this crop.", null);
    }

    private static RuleResult CheckDose(ParsedProposal p, ApprovalFacts limits, ProductFacts product)
    {
        var unit = PerHectare(product);
        var evidence = $"dose={Num(p.DosePerHectare)}; range={Num(limits.MinDosePerHectare)}–{Num(limits.MaxDosePerHectare)} {unit}";

        return p.DosePerHectare >= limits.MinDosePerHectare && p.DosePerHectare <= limits.MaxDosePerHectare
            ? Pass("V3", $"{Num(p.DosePerHectare)} {unit} is within the approved range.", evidence)
            : Fail("V3",
                $"{Num(p.DosePerHectare)} {unit} is outside the approved range of {Num(limits.MinDosePerHectare)}–{Num(limits.MaxDosePerHectare)} {unit} for this crop. Use a dose within that range.",
                evidence);
    }

    private static RuleResult CheckQuantity(ParsedProposal p, CycleFacts cycle)
    {
        var expected = p.DosePerHectare * cycle.AreaHectares;
        var tolerance = expected * QuantityTolerance;
        var evidence = $"expected={Num(expected)}; proposed={Num(p.TotalQuantity)}; area={Num(cycle.AreaHectares)} ha; tolerance=±{Num(QuantityTolerance * 100)}%";

        return Math.Abs(p.TotalQuantity - expected) <= tolerance
            ? Pass("V4", $"{Num(p.TotalQuantity)} matches {Num(p.DosePerHectare)} × {Num(cycle.AreaHectares)} ha.", evidence)
            : Fail("V4",
                $"Total quantity should be {Num(expected)} ({Num(p.DosePerHectare)} × {Num(cycle.AreaHectares)} ha), but the proposal says {Num(p.TotalQuantity)}.",
                evidence);
    }

    private static RuleResult CheckPreHarvestInterval(ParsedProposal p, ApprovalFacts limits, CycleFacts cycle)
    {
        var safeToHarvestFrom = p.SprayDate.AddDays(limits.PreHarvestIntervalDays);
        var latestSpray = cycle.HarvestDate.AddDays(-limits.PreHarvestIntervalDays);
        var evidence = $"spray={Iso(p.SprayDate)}; phi={limits.PreHarvestIntervalDays}d; safeFrom={Iso(safeToHarvestFrom)}; harvest={Iso(cycle.HarvestDate)}";

        return safeToHarvestFrom <= cycle.HarvestDate
            ? Pass("V5", $"The crop is safe to harvest from {Iso(safeToHarvestFrom)}, on or before the planned harvest.", evidence)
            : Fail("V5",
                $"Spraying on {Iso(p.SprayDate)} with a {limits.PreHarvestIntervalDays}-day pre-harvest interval makes the crop safe only from {Iso(safeToHarvestFrom)}, after the planned harvest on {Iso(cycle.HarvestDate)}. The latest safe spray date for this product is {Iso(latestSpray)}.",
                evidence);
    }

    private static RuleResult CheckApplicationLimit(ApplicationHistory history, ApprovalFacts limits, ProductFacts product)
    {
        var evidence = $"applications={history.CountThisCycle}; max={limits.MaxApplicationsPerCycle}";

        return history.CountThisCycle < limits.MaxApplicationsPerCycle
            ? Pass("V6", $"{product.ActiveIngredient} used {history.CountThisCycle} of {limits.MaxApplicationsPerCycle} times this cycle.", evidence)
            : Fail("V6",
                $"{product.ActiveIngredient} has already been applied {history.CountThisCycle} time(s) this cycle; the limit is {limits.MaxApplicationsPerCycle}. Choose a product with a different active ingredient.",
                evidence);
    }

    private static RuleResult CheckApplicationInterval(ParsedProposal p, ApplicationHistory history, ApprovalFacts limits, ProductFacts product)
    {
        if (history.LastApplicationDate is not { } last)
            return Pass("V7", $"No earlier use of {product.ActiveIngredient} this cycle.", null);

        var daysApart = p.SprayDate.DayNumber - last.DayNumber;
        var nextAllowed = last.AddDays(limits.MinDaysBetweenApplications);
        var evidence = $"last={Iso(last)}; spray={Iso(p.SprayDate)}; daysApart={daysApart}; min={limits.MinDaysBetweenApplications}";

        return daysApart >= limits.MinDaysBetweenApplications
            ? Pass("V7", $"{daysApart} days since {product.ActiveIngredient} was last applied.", evidence)
            : Fail("V7",
                $"{product.ActiveIngredient} was last applied on {Iso(last)}; applications must be {limits.MinDaysBetweenApplications} days apart, so the earliest spray date is {Iso(nextAllowed)}.",
                evidence);
    }

    private static RuleResult CheckWeather(SprayWeather? weather, ApprovalFacts limits)
    {
        // Documented degradation (§10): without a forecast the rule is reported as not checked
        // rather than failing every run while the weather service is down.
        if (weather is null)
            return Skip("V8", "Not checked: no weather forecast is available for the spray date.");

        List<string> problems = [];
        if (weather.MaxRainProbabilityPercent >= MaxRainProbabilityPercent)
            problems.Add($"{Num(weather.MaxRainProbabilityPercent)}% chance of rain within the {limits.RainfastHours}-hour rainfast window (limit {Num(MaxRainProbabilityPercent)}%)");
        if (weather.MaxWindKmh >= MaxWindKmh)
            problems.Add($"wind up to {Num(weather.MaxWindKmh)} km/h (limit {Num(MaxWindKmh)} km/h)");
        if (weather.MaxTemperatureC > MaxTemperatureC)
            problems.Add($"temperature up to {Num(weather.MaxTemperatureC)} °C (limit {Num(MaxTemperatureC)} °C)");

        var evidence = $"rain={Num(weather.MaxRainProbabilityPercent)}%; wind={Num(weather.MaxWindKmh)}km/h; temp={Num(weather.MaxTemperatureC)}C; rainfast={limits.RainfastHours}h";
        return problems.Count == 0
            ? Pass("V8", "The forecast allows spraying.", evidence)
            : Fail("V8", $"Poor spraying weather: {string.Join("; ", problems)}. Choose another spray date.", evidence);
    }

    private static RuleResult CheckStock(ParsedProposal p, DealerStock? stock, ProductFacts product)
    {
        var need = $"{Num(p.TotalQuantity)} {product.Unit}";

        if (stock is null)
            return Fail("V9",
                p.DealerId is null
                    ? $"No dealer in the farm's district has {product.Name} in a batch still in date on {Iso(p.SprayDate)}; {need} is needed."
                    : $"The chosen dealer has no {product.Name} in a batch still in date on {Iso(p.SprayDate)}; {need} is needed.",
                null);

        var evidence = $"dealer={stock.ShopName}; available={Num(stock.AvailableQuantity)}; needed={Num(p.TotalQuantity)}";
        return stock.AvailableQuantity >= p.TotalQuantity
            ? Pass("V9", $"{stock.ShopName} has {Num(stock.AvailableQuantity)} {product.Unit} available.", evidence)
            : Fail("V9",
                $"{stock.ShopName} has only {Num(stock.AvailableQuantity)} {product.Unit} of {product.Name} still in date on {Iso(p.SprayDate)}; {need} is needed.",
                evidence);
    }

    private static RuleResult CheckOwnership(ParsedProposal p, CycleFacts? cycle, RunFacts? run, ApprovalFacts? approval, ProductFacts? product)
    {
        List<string> problems = [];

        if (cycle is null) problems.Add("the crop cycle does not exist");
        else if (!cycle.IsActive) problems.Add("the crop cycle is no longer active");

        if (run is null) problems.Add($"run {p.RunId} does not exist");
        else if (run.IsTerminal) problems.Add("the run proposing it has already finished");

        if (cycle is not null && run is not null)
        {
            if (run.CaseCropCycleId != cycle.Id)
                problems.Add("it targets a different crop cycle from the case this run is resolving");
            if (run.CaseFarmerId != cycle.PlotOwnerId)
                problems.Add("the farmer on the case does not own this plot");
        }

        // There is no permit register, so a restricted product can never be prescribed here.
        if (approval is { IsRestricted: true })
            problems.Add($"{product?.Name ?? "the product"} is restricted and needs a permit");

        return problems.Count == 0
            ? Pass("V10", "The proposal is for the case's own crop cycle and farmer, with an unrestricted product.", null)
            : Fail("V10", $"The proposal is not permitted: {string.Join("; ", problems)}.", null);
    }

    private static RuleResult CheckCredit(ParsedProposal p, ProductFacts product, DealerStock? stock, decimal? creditLimit)
    {
        if (creditLimit is not { } limit)
            return Pass("V11", "No credit limit applies to this farmer.", null);

        var packs = (int)Math.Ceiling(p.TotalQuantity / product.PackSize);
        var packPrice = stock?.PackPrice ?? product.UnitPrice;
        var cost = packs * packPrice;
        var evidence = $"packs={packs}; packPrice={Num(packPrice)}; cost={Num(cost)}; limit={Num(limit)}";

        return cost <= limit
            ? Pass("V11", $"The order costs LKR {Money(cost)}, within the LKR {Money(limit)} limit.", evidence)
            : Fail("V11",
                $"The order would cost LKR {Money(cost)} ({packs} × {Money(packPrice)}), above the farmer's credit limit of LKR {Money(limit)}. Choose a cheaper product.",
                evidence);
    }

    // ── Verdict ──────────────────────────────────────────────────────────────

    /// <summary>Any failed Reject rule ends the run; otherwise any failed Revise rule sends it back.</summary>
    private static PrescriptionVerdict Conclude(List<RuleResult> results)
    {
        var failed = results.Where(r => r.Status == RuleStatus.Failed).ToList();
        var skipped = results.Where(r => r.Status == RuleStatus.Skipped).ToList();

        var outcome = failed.Any(r => r.Severity == RuleSeverity.Reject) ? VerdictOutcome.Rejected
            : failed.Count > 0 ? VerdictOutcome.Revise
            : VerdictOutcome.Approved;

        var summary = outcome switch
        {
            VerdictOutcome.Approved when skipped.Count == 0 => $"All {RuleCount} rules passed.",
            VerdictOutcome.Approved =>
                $"{RuleCount - skipped.Count} of {RuleCount} rules passed; not checked: {string.Join(", ", skipped.Select(r => r.Code))}.",
            _ => $"{outcome}: {string.Join(", ", failed.Select(r => r.Code))} failed."
        };

        return new PrescriptionVerdict(outcome, summary, results);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RuleResult Pass(string code, string message, string? evidence) =>
        new(code, Rules[code].Name, RuleStatus.Passed, Rules[code].Severity, message, evidence);

    private static RuleResult Fail(string code, string message, string? evidence) =>
        new(code, Rules[code].Name, RuleStatus.Failed, Rules[code].Severity, message, evidence);

    private static RuleResult Skip(string code, string message) =>
        new(code, Rules[code].Name, RuleStatus.Skipped, Rules[code].Severity, message, null);

    private static Guid? ParseId(string? value, string field, List<string> problems, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) problems.Add($"{field} is required");
            return null;
        }

        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
            return id;

        problems.Add($"{field} is not a valid id");
        return null;
    }

    private static void CheckAmount(decimal? value, string field, decimal max, List<string> problems)
    {
        if (value is null) problems.Add($"{field} is required");
        else if (value <= 0) problems.Add($"{field} must be greater than 0");
        else if (value > max) problems.Add($"{field} {Num(value.Value)} is implausibly large — check the unit");
    }

    private static string PerHectare(ProductFacts product) => product.Unit switch
    {
        "Kilogram" => "kg/ha",
        "Litre" => "L/ha",
        var other => $"{other}/ha"
    };

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Num(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
