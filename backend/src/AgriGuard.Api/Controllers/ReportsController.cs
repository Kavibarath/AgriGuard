using AgriGuard.Application.Auth;
using AgriGuard.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// The §5.1 reports, one or two per component. Read-only aggregates; each is scoped exactly like
/// the rows it summarises, so a report never reveals what the matching list endpoint would hide.
/// </summary>
[ApiController]
[Route("api/reports")]
public sealed class ReportsController(
    IRegistryReportService registryReports,
    ICaseReportService caseReports,
    IStockReportService stockReports,
    IHarvestReportService harvestReports) : ControllerBase
{
    /// <summary>Component A: every spray on a plot, with its pre-harvest interval and active-ingredient use.</summary>
    [HttpGet("plot-treatment-history")]
    [Authorize(Policy = AuthPolicies.OwnsFarm)]
    [ProducesResponseType<PlotTreatmentHistoryReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<PlotTreatmentHistoryReport> PlotTreatmentHistory(
        [FromQuery, BindRequired] Guid plotId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct) =>
        registryReports.PlotTreatmentHistoryAsync(new PlotTreatmentQuery { PlotId = plotId, From = from, To = to }, ct);

    /// <summary>Component B: cases reported and prescribed, time to prescription, and agent-run outcomes (default: last 30 days).</summary>
    [HttpGet("case-throughput")]
    [Authorize(Policy = AuthPolicies.OwnsFarm)]
    [ProducesResponseType<CaseThroughputReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<CaseThroughputReport> CaseThroughput([FromQuery] CaseThroughputQuery query, CancellationToken ct) =>
        caseReports.CaseThroughputAsync(query, ct);

    /// <summary>Component C: what the stock on the shelf is worth, and how much of it is held or expired.</summary>
    [HttpGet("stock-valuation")]
    [Authorize(Policy = AuthPolicies.ViewsStockReports)]
    [ProducesResponseType<StockValuationReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<StockValuationReport> StockValuation([FromQuery] StockReportQuery query, CancellationToken ct) =>
        stockReports.StockValuationAsync(query, ct);

    /// <summary>Component C: products a shop has run out of, or has fewer than <c>minPacks</c> sellable packs of (default 3).</summary>
    [HttpGet("low-stock")]
    [Authorize(Policy = AuthPolicies.ViewsStockReports)]
    [ProducesResponseType<LowStockReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<LowStockReport> LowStock([FromQuery] LowStockQuery query, CancellationToken ct) =>
        stockReports.LowStockAsync(query, ct);

    /// <summary>Component D: how close harvest forecasts came to the actual yield, overall, per crop and per forecaster.</summary>
    [HttpGet("harvest-forecast-vs-actual")]
    [Authorize(Policy = AuthPolicies.OwnsFarm)]
    [ProducesResponseType<HarvestForecastVsActualReport>(StatusCodes.Status200OK)]
    public Task<HarvestForecastVsActualReport> HarvestForecastVsActual([FromQuery] HarvestReportQuery query, CancellationToken ct) =>
        harvestReports.ForecastVsActualAsync(query, ct);
}
