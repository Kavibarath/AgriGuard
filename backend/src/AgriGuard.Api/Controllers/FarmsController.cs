using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component A — farm registry. The policy admits farmers, agronomists and administrators;
/// which rows each of them sees, and who may write, is decided per row in the service.
/// </summary>
[ApiController]
[Route("api/farms")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class FarmsController(IFarmService farms) : ControllerBase
{
    /// <summary>Paginated, searchable, sortable list scoped to the caller.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<FarmDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<FarmDto>> List([FromQuery] FarmQuery query, CancellationToken ct) =>
        farms.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<FarmDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<FarmDto> Get(Guid id, CancellationToken ct) => farms.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<FarmDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FarmDto>> Create(CreateFarmRequest request, CancellationToken ct)
    {
        var farm = await farms.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = farm.Id }, farm);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<FarmDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<FarmDto> Update(Guid id, UpdateFarmRequest request, CancellationToken ct) =>
        farms.UpdateAsync(id, request, ct);

    /// <summary>Refuses with 409 while the farm still has plots — history is never cascaded away.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await farms.DeleteAsync(id, ct);
        return NoContent();
    }
}
