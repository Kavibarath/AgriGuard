using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component C — the agro-input catalogue. Every signed-in role reads it (dealers stock from it,
/// agronomists read prescriptions against it); only a Co-op Administrator changes it.
/// </summary>
[ApiController]
[Route("api/products")]
[Authorize]
public sealed class ProductsController(IProductService products) : ControllerBase
{
    /// <summary>Searchable, filterable (crop with an active rule, active ingredient), sortable and paged.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ProductDto>> List([FromQuery] ProductQuery query, CancellationToken ct) =>
        products.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ProductDto> Get(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = AuthPolicies.AdministersRules)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create(ProductRequest request, CancellationToken ct)
    {
        var product = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.AdministersRules)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ProductDto> Update(Guid id, ProductRequest request, CancellationToken ct) =>
        products.UpdateAsync(id, request, ct);

    /// <summary>Refuses with 409 once the product has rules, stock or history — withdraw it instead.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.AdministersRules)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>The active-ingredient list for the product form and the ingredient filter.</summary>
    [HttpGet("/api/active-ingredients")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType<IReadOnlyList<ActiveIngredientDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ActiveIngredientDto>> ActiveIngredients(CancellationToken ct) =>
        products.ListActiveIngredientsAsync(ct);
}
