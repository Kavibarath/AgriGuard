using System.Globalization;
using System.Text.Json;
using AgriGuard.Application.Agent;
using AgriGuard.Application.Common;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Validation;
using AgriGuard.Application.Weather;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// How an agent proposal reaches Component A's validator (<see cref="IPrescriptionValidationService"/>,
/// rules V1–V11). This class judges nothing itself. It adds the two things only the agent path
/// knows about:
///
/// 1. **The run.** A proposal must target the crop cycle of the case its run is resolving. A run
///    that drifted onto another farmer's crop is a bug or an attack and is refused outright.
/// 2. **Real stock.** The validator checks V9 only when it is given stock figures, so the best
///    dealer's in-date, unreserved stock is looked up here. "No stock" then fails V9 rather than
///    going unchecked.
/// 3. **The forecast.** V8 needs the weather at the plot on the spray date, judged over the
///    product's rainfast time. It comes from Open-Meteo through the cached WeatherService; if the
///    forecast is unavailable or the date is beyond its horizon, V8 is "not evaluated" (§10's
///    deliberate degradation) rather than the whole check failing.
///
/// Used twice per run: by the validate_prescription tool, and again by the backend when the agent
/// reports a proposal as ready for approval — the backend never takes the agent's word for it.
/// </summary>
public sealed class AgentPrescriptionGate(
    AgriGuardDbContext db,
    IPrescriptionValidationService validation,
    IWeatherService weather,
    FarmCalendar calendar)
{
    public async Task<AgentVerdictTool> CheckAsync(AgentProposalInput input, CancellationToken ct = default)
    {
        // These two come from the backend itself (the dispatch and the case-detail tool), so a
        // malformed one is a contract bug, not something for the model to revise.
        var runId = RequireId(input.RunId, "runId");
        var cropCycleId = RequireId(input.CropCycleId, "cropCycleId");
        var districtId = await EnsureRunOwnsCycleAsync(runId, cropCycleId, ct);

        // The model's own choices go through as-is, even when unusable: an empty id or a missing
        // date is exactly what the validator's V1 reports.
        var productId = Guid.TryParse(input.ProductId, out var product) ? product : Guid.Empty;
        var sprayDate = DateOnly.TryParseExact(input.SprayDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : default;
        // A dealer id the model invented matches no dealer, so it finds no stock and fails V9.
        Guid? dealerId = string.IsNullOrWhiteSpace(input.DealerId) ? null
            : Guid.TryParse(input.DealerId, out var dealer) ? dealer : Guid.Empty;

        var verdict = await validation.ValidateAsync(new ValidatePrescriptionRequest(
            cropCycleId,
            productId,
            input.DosePerHectare ?? 0,
            input.TotalQuantity ?? 0,
            sprayDate,
            dealerId,
            Weather: await LoadWeatherAsync(cropCycleId, productId, sprayDate, ct),
            Stock: await LoadStockAsync(runId, productId, dealerId, districtId, sprayDate, ct)), ct);

        return new AgentVerdictTool(verdict.Outcome, verdict.Summary, verdict.Results);
    }

    /// <summary>
    /// A stored proposal (agent/app/contracts.py PrescriptionProposal, snake_case) as gate input,
    /// for the run's own crop cycle. The crop cycle comes from the case, never from the proposal.
    /// </summary>
    public static AgentProposalInput FromStoredProposal(JsonElement proposal, Guid runId, Guid cropCycleId) => new(
        runId.ToString(),
        cropCycleId.ToString(),
        ReadString(proposal, "product_id"),
        ReadDecimal(proposal, "dose_per_hectare"),
        ReadDecimal(proposal, "total_quantity"),
        ReadString(proposal, "spray_date"),
        ReadString(proposal, "dealer_id"));

    private static string? ReadString(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ReadDecimal(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;

    /// <summary>
    /// The forecast at the plot for the spray date, judged exactly as the spray-window screen judges
    /// it (<see cref="SprayWeather"/>): wind and heat over the morning application window, rain until
    /// the product is rainfast. Null — V8 not evaluated — when there is no rule to read the rainfast
    /// time from (V2 fails anyway), no usable date (V1 fails), or no forecast.
    /// </summary>
    private async Task<WeatherInput?> LoadWeatherAsync(Guid cropCycleId, Guid productId, DateOnly sprayDate, CancellationToken ct)
    {
        if (sprayDate == default)
            return null;

        var facts = await db.CropCycles.AsNoTracking()
            .Where(c => c.Id == cropCycleId)
            .Select(c => new
            {
                c.Plot.Latitude,
                c.Plot.Longitude,
                Rainfast = db.ProductCropApprovals
                    .Where(a => a.ProductId == productId && a.CropId == c.CropId)
                    .Select(a => (int?)a.RainfastHours)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (facts?.Rainfast is not { } rainfastHours)
            return null;

        if (await weather.ForecastAsync(facts.Latitude, facts.Longitude, ct) is not { } forecast
            || SprayWeather.Assess(forecast.Hours, sprayDate, rainfastHours) is not { } day)
            return null;

        return new WeatherInput(day.RainProbabilityPercent, day.WindSpeedKph, day.TemperatureC, day.PrecipitationMm);
    }

    /// <summary>Returns the farm's district, which the stock search is limited to.</summary>
    private async Task<Guid> EnsureRunOwnsCycleAsync(Guid runId, Guid cropCycleId, CancellationToken ct)
    {
        var run = await db.AgentRuns.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => new { r.Status, r.Case.CropCycleId, r.Case.DistrictId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Agent run", runId);

        // Awaiting approval still counts: the approval transaction re-checks the proposal then.
        if (CaseStatusRules.TerminalRunStatuses.Contains(run.Status))
            throw new BusinessRuleException("RUN_NOT_ACTIVE", $"Run {runId} is {run.Status} and can no longer propose.");

        if (run.CropCycleId != cropCycleId)
            throw new BusinessRuleException("PROPOSAL_OUTSIDE_CASE",
                "The proposal targets a crop cycle other than the one on the case this run is resolving.");

        return run.DistrictId;
    }

    /// <summary>
    /// The named dealer if there is one, otherwise the best-stocked dealer in the farm's district.
    /// Only batches still in date on the spray date count, and only what is not already reserved —
    /// except by this run itself: stock held for this very proposal is available to it, so the
    /// approval's re-check does not fail against the proposal's own hold.
    /// </summary>
    private async Task<StockInput> LoadStockAsync(Guid runId, Guid productId, Guid? dealerId, Guid districtId, DateOnly sprayDate, CancellationToken ct)
    {
        var inDateOn = sprayDate == default ? calendar.Today : sprayDate;

        var batches = db.InventoryBatches.AsNoTracking()
            .Where(b => b.ProductId == productId && b.ExpiryDate > inDateOn);
        batches = dealerId is { } id
            ? batches.Where(b => b.DealerId == id)
            : batches.Where(b => b.Dealer.DistrictId == districtId);

        var ownHold = db.StockReservationLines
            .Where(l => l.Reservation.AgentRunId == runId && l.Reservation.Status == ReservationStatus.Held);

        var rows = await batches
            .Select(b => new
            {
                b.DealerId,
                Available = b.QuantityOnHand - b.QuantityReserved
                            + (ownHold.Where(l => l.BatchId == b.Id).Sum(l => (decimal?)l.Quantity) ?? 0m),
                b.ExpiryDate
            })
            .Where(b => b.Available > 0)
            .ToListAsync(ct);

        var best = rows
            .GroupBy(r => r.DealerId)
            .Select(g => new StockInput(g.Sum(r => r.Available), g.Min(r => r.ExpiryDate)))
            .OrderByDescending(s => s.AvailableQuantity)
            .FirstOrDefault();

        // Nothing in stock is a fact the validator should judge (V9 fails), not a missing input.
        return best ?? new StockInput(0, null);
    }

    private static Guid RequireId(string? value, string field) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new RequestValidationException(field, $"{field} must be a valid id.");
}
