using AgriGuard.Application.Agent;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Prescriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// The agent's tools (§9.3). Routes and parameter names are the contract in agent/app/tools.py
/// (TOOL_ROUTES) and agent/app/graph.py. Which agent may call which tool is enforced on the agent's
/// side by its allow-lists; this side guarantees that no tool can change anything.
/// </summary>
[ApiController]
[Route("internal/tools")]
[Authorize(Policy = AuthPolicies.AgentService)]
public sealed class InternalToolsController(IAgentToolService tools) : ControllerBase
{
    /// <summary>get_case_detail — the case, its crop and plot, and candidate pathogens.</summary>
    [HttpGet("case-detail")]
    public Task<CaseDetailTool> CaseDetail([FromQuery, BindRequired] Guid caseId, CancellationToken ct) =>
        tools.GetCaseDetailAsync(caseId, ct);

    /// <summary>get_crop_history — chemical applications recorded on this crop cycle.</summary>
    [HttpGet("crop-history")]
    public Task<CropHistoryTool> CropHistory([FromQuery, BindRequired] Guid cropCycleId, CancellationToken ct) =>
        tools.GetCropHistoryAsync(cropCycleId, ct);

    /// <summary>get_regional_outbreak_signal — confirmed disease pressure for the crop in the district.</summary>
    [HttpGet("outbreak-signal")]
    public Task<OutbreakSignalTool> OutbreakSignal(
        [FromQuery, BindRequired] Guid cropId,
        [FromQuery, BindRequired] Guid districtId,
        [FromQuery] int days = 14,
        CancellationToken ct = default) =>
        tools.GetOutbreakSignalAsync(cropId, districtId, days, ct);

    /// <summary>search_approved_products — products approved for the crop and labelled for the pathogen.</summary>
    [HttpGet("approved-products")]
    public Task<ApprovedProductsTool> ApprovedProducts(
        [FromQuery, BindRequired] Guid cropId,
        [FromQuery, BindRequired] string pathogenCode,
        CancellationToken ct) =>
        tools.SearchApprovedProductsAsync(cropId, pathogenCode, ct);

    /// <summary>get_plot_safety_profile — per product: last PHI-safe spray date and use so far this cycle.</summary>
    [HttpGet("plot-safety-profile")]
    public Task<PlotSafetyProfileTool> PlotSafetyProfile([FromQuery, BindRequired] Guid plotId, CancellationToken ct) =>
        tools.GetPlotSafetyProfileAsync(plotId, ct);

    /// <summary>check_stock_availability — unreserved, in-date stock by dealer.</summary>
    [HttpGet("stock-availability")]
    public Task<StockAvailabilityTool> StockAvailability(
        [FromQuery, BindRequired] Guid productId,
        [FromQuery] Guid? districtId,
        [FromQuery] DateOnly? usableOn,
        CancellationToken ct) =>
        tools.CheckStockAvailabilityAsync(productId, districtId, usableOn, ct);

    /// <summary>get_product_pricing — pack size and price; with a quantity, the packs and cost.</summary>
    [HttpGet("product-pricing")]
    public Task<ProductPricingTool> ProductPricing(
        [FromQuery, BindRequired] Guid productId,
        [FromQuery] decimal? quantity,
        CancellationToken ct) =>
        tools.GetProductPricingAsync(productId, quantity, ct);

    /// <summary>
    /// validate_prescription — the deterministic rules V1–V11. Always 200 with a verdict, even for a
    /// malformed proposal (that is rule V1 failing), so the agent gets something it can act on.
    /// </summary>
    [HttpPost("validate-prescription")]
    public Task<PrescriptionVerdict> ValidatePrescription(PrescriptionProposalInput proposal, CancellationToken ct) =>
        tools.ValidatePrescriptionAsync(proposal, ct);
}
