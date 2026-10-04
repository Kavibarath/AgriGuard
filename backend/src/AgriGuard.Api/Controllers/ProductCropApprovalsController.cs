using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component C — the regulatory rules table (ProductCropApproval). Co-op Administrator only.
/// The deterministic validator reads these rows on every check, so a change here alters the next
/// verdict with no code change: the /rules editor is the viva's "modify a business rule" screen.
/// </summary>
[ApiController]
[Route("api/product-crop-approvals")]
[Authorize(Policy = AuthPolicies.AdministersRules)]
public sealed class ProductCropApprovalsController(IProductCropApprovalService rules) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductCropApprovalDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ProductCropApprovalDto>> List([FromQuery] ApprovalQuery query, CancellationToken ct) =>
        rules.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductCropApprovalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ProductCropApprovalDto> Get(Guid id, CancellationToken ct) => rules.GetAsync(id, ct);

    /// <summary>Approves a product for a crop within these limits. 409 if the pair already has a rule.</summary>
    [HttpPost]
    [ProducesResponseType<ProductCropApprovalDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductCropApprovalDto>> Create(CreateApprovalRequest request, CancellationToken ct)
    {
        var rule = await rules.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = rule.Id }, rule);
    }

    /// <summary>Changes the limits. Takes effect on the next validation.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProductCropApprovalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ProductCropApprovalDto> Update(Guid id, ApprovalLimitsRequest request, CancellationToken ct) =>
        rules.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await rules.DeleteAsync(id, ct);
        return NoContent();
    }
}
