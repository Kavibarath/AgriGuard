namespace AgriGuard.Domain.Registry;

/// <summary>One recorded application on the cycle, reduced to what the safety maths needs.</summary>
public sealed record AppliedTreatment(
    Guid ProductId,
    Guid ActiveIngredientId,
    DateOnly AppliedOn,
    int ReEntryIntervalHours);

/// <summary>One row of the regulatory rules table, for a product approved on this crop.</summary>
public sealed record ProductRule(
    Guid ProductId,
    string ProductName,
    Guid ActiveIngredientId,
    string ActiveIngredientName,
    string? ResistanceGroup,
    int PreHarvestIntervalDays,
    int ReEntryIntervalHours,
    int MaxApplicationsPerCycle,
    int MinDaysBetweenApplications,
    bool IsRestricted);

/// <summary>Why a product cannot be sprayed today. Null <see cref="ProductWindow.BlockedReason"/> means it can.</summary>
public enum SprayBlock
{
    None,

    /// <summary>Spraying today would leave less than the pre-harvest interval before harvest (V5).</summary>
    PreHarvestInterval,

    /// <summary>The per-cycle application limit is already used up (V6).</summary>
    MaxApplicationsReached,

    /// <summary>The minimum gap since the last application of this active ingredient has not passed (V7).</summary>
    MinimumInterval
}

/// <summary>What the farmer (and the validator) may do with one product, right now.</summary>
public sealed record ProductWindow(
    Guid ProductId,
    string ProductName,
    Guid ActiveIngredientId,
    string ActiveIngredientName,
    string? ResistanceGroup,
    bool IsRestricted,
    int PreHarvestIntervalDays,
    int ApplicationsUsed,
    int MaxApplicationsPerCycle,
    int ApplicationsRemaining,
    DateOnly? LastAppliedOn,
    // Latest date a spray still leaves the full pre-harvest interval before harvest.
    DateOnly LastSafeSprayDate,
    // First date the resistance-management gap allows another application.
    DateOnly? EarliestNextApplication,
    SprayBlock BlockedReason)
{
    public bool CanSprayToday => BlockedReason == SprayBlock.None;
}

/// <summary>How often one active ingredient has been used on this cycle (resistance management).</summary>
public sealed record IngredientUsage(
    Guid ActiveIngredientId,
    string Name,
    string? ResistanceGroup,
    int ApplicationCount,
    DateOnly? LastAppliedOn,
    int? DaysSinceLastApplication);

public sealed record SafetyProfile(
    DateOnly HarvestDate,
    int DaysToHarvest,
    IReadOnlyList<IngredientUsage> IngredientUsage,
    IReadOnlyList<ProductWindow> ProductWindows,
    // Dates from today to harvest on which no approved product may legally be sprayed.
    IReadOnlyList<DateOnly> PhiBlockedSprayDates,
    // When the field is safe to walk into again, or null if no recent application blocks entry.
    DateTime? ReEntryClearAtUtc);

/// <summary>
/// Computes a plot's chemical-safety position: what has been applied to the growing crop, and
/// what may still be applied before harvest.
///
/// This is Component A's second non-CRUD operation (§5.1) and the factual basis for the
/// deterministic validator's rules V5 (pre-harvest interval), V6 (applications per cycle) and
/// V7 (resistance interval). It reads the ProductCropApproval rules table rather than hard-coding
/// limits — editing a rule in the admin screen changes these answers.
///
/// Pure functions over plain records: no EF, no clock, no HTTP, so every rule below is unit-tested.
/// </summary>
public static class SafetyProfileCalculator
{
    public static SafetyProfile Compute(
        DateOnly today,
        DateTime nowUtc,
        DateOnly harvestDate,
        IReadOnlyList<AppliedTreatment> applications,
        IReadOnlyList<ProductRule> rules)
    {
        var byIngredient = applications
            .GroupBy(a => a.ActiveIngredientId)
            .ToDictionary(group => group.Key, group => group.OrderBy(a => a.AppliedOn).ToList());

        var byProduct = applications
            .GroupBy(a => a.ProductId)
            .ToDictionary(group => group.Key, group => group.OrderBy(a => a.AppliedOn).ToList());

        var ingredientNames = rules
            .GroupBy(r => r.ActiveIngredientId)
            .ToDictionary(group => group.Key, group => group.First());

        var usage = byIngredient
            .Select(entry =>
            {
                var last = entry.Value[^1].AppliedOn;
                ingredientNames.TryGetValue(entry.Key, out var rule);
                return new IngredientUsage(
                    entry.Key,
                    rule?.ActiveIngredientName ?? "Unknown",
                    rule?.ResistanceGroup,
                    entry.Value.Count,
                    last,
                    today.DayNumber - last.DayNumber);
            })
            .OrderByDescending(u => u.ApplicationCount)
            .ThenBy(u => u.Name)
            .ToList();

        var windows = rules
            .Select(rule => BuildWindow(today, harvestDate, rule, byProduct, byIngredient))
            .OrderBy(w => w.ProductName)
            .ToList();

        return new SafetyProfile(
            harvestDate,
            harvestDate.DayNumber - today.DayNumber,
            usage,
            windows,
            BlockedDates(today, harvestDate, rules),
            ReEntryClearAt(nowUtc, applications));
    }

    private static ProductWindow BuildWindow(
        DateOnly today,
        DateOnly harvestDate,
        ProductRule rule,
        Dictionary<Guid, List<AppliedTreatment>> byProduct,
        Dictionary<Guid, List<AppliedTreatment>> byIngredient)
    {
        var productApplications = byProduct.GetValueOrDefault(rule.ProductId) ?? [];
        var used = productApplications.Count;
        var lastAppliedOn = productApplications.Count > 0 ? productApplications[^1].AppliedOn : (DateOnly?)null;

        // Spraying on date D requires D + PHI <= harvest, so the last safe date is harvest - PHI.
        var lastSafeSprayDate = harvestDate.AddDays(-rule.PreHarvestIntervalDays);

        // The gap is measured per active ingredient, not per product: two products sharing an
        // ingredient are one chemical as far as resistance is concerned (rule V7).
        var ingredientApplications = byIngredient.GetValueOrDefault(rule.ActiveIngredientId) ?? [];
        var earliestNext = ingredientApplications.Count > 0
            ? ingredientApplications[^1].AppliedOn.AddDays(rule.MinDaysBetweenApplications)
            : (DateOnly?)null;

        // Order matters: report the limit that would stop the farmer first. An exhausted
        // allowance is a harder "no" than a gap that will pass on its own.
        var blocked = used >= rule.MaxApplicationsPerCycle ? SprayBlock.MaxApplicationsReached
            : today > lastSafeSprayDate ? SprayBlock.PreHarvestInterval
            : earliestNext is { } next && today < next ? SprayBlock.MinimumInterval
            : SprayBlock.None;

        return new ProductWindow(
            rule.ProductId,
            rule.ProductName,
            rule.ActiveIngredientId,
            rule.ActiveIngredientName,
            rule.ResistanceGroup,
            rule.IsRestricted,
            rule.PreHarvestIntervalDays,
            used,
            rule.MaxApplicationsPerCycle,
            Math.Max(rule.MaxApplicationsPerCycle - used, 0),
            lastAppliedOn,
            lastSafeSprayDate,
            earliestNext,
            blocked);
    }

    /// <summary>
    /// The run-up to harvest where even the shortest pre-harvest interval no longer fits:
    /// every date after (harvest − smallest PHI). Past dates are not listed — a farmer can only
    /// act from today onwards.
    /// </summary>
    private static List<DateOnly> BlockedDates(DateOnly today, DateOnly harvestDate, IReadOnlyList<ProductRule> rules)
    {
        if (rules.Count == 0) return [];

        var shortestPhi = rules.Min(r => r.PreHarvestIntervalDays);
        var firstBlocked = harvestDate.AddDays(-shortestPhi + 1);
        var from = firstBlocked < today ? today : firstBlocked;

        List<DateOnly> blocked = [];
        for (var date = from; date <= harvestDate; date = date.AddDays(1))
            blocked.Add(date);

        return blocked;
    }

    /// <summary>
    /// Latest re-entry expiry across recent applications: until then the field is unsafe to walk
    /// into. Applications are dated, not timed, so entry is counted from the end of that day —
    /// the cautious reading.
    /// </summary>
    private static DateTime? ReEntryClearAt(DateTime nowUtc, IReadOnlyList<AppliedTreatment> applications)
    {
        DateTime? latest = null;

        foreach (var application in applications)
        {
            var clearAt = application.AppliedOn
                .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                .AddDays(1)
                .AddHours(application.ReEntryIntervalHours);

            if (clearAt > nowUtc && (latest is null || clearAt > latest)) latest = clearAt;
        }

        return latest;
    }
}
