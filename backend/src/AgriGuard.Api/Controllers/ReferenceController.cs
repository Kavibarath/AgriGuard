using AgriGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Api.Controllers;

public sealed record DistrictDto(Guid Id, string Code, string Name, string Province);

public sealed record CropDto(Guid Id, string Code, string Name, string? ScientificName, int MaturityDays);

/// <summary>
/// Look-up lists that populate dropdowns in both clients (districts on the farm form, crops on
/// the sowing form). Small, shared and rarely changing, so they are returned whole rather than
/// paginated, and cached by the browser for an hour — a new district is an annual event.
///
/// Any signed-in user may read them: knowing that "Nuwara Eliya" exists is not sensitive, and
/// every role needs at least one of these lists.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public sealed class ReferenceController(AgriGuardDbContext db) : ControllerBase
{
    [HttpGet("districts")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType<IReadOnlyList<DistrictDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<DistrictDto>> Districts(CancellationToken ct) =>
        await db.Districts.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DistrictDto(d.Id, d.Code, d.Name, d.Province))
            .ToListAsync(ct);

    [HttpGet("crops")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType<IReadOnlyList<CropDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CropDto>> Crops(CancellationToken ct) =>
        await db.Crops.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CropDto(c.Id, c.Code, c.Name, c.ScientificName, c.MaturityDays))
            .ToListAsync(ct);
}
