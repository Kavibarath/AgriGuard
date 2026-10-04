using AgriGuard.Api.Infrastructure;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// Component B — crop-health cases. The policy admits farmers, agronomists and administrators;
/// which cases each sees, and who may change what, is decided per row in the service.
/// </summary>
[ApiController]
[Route("api/cases")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class CasesController(ICaseService cases, ICasePhotoService photos) : ControllerBase
{
    /// <summary>The case queue: filter by status, crop, district, severity or plot; search by reference, plot or farmer.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CaseSummaryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CaseSummaryDto>> List([FromQuery] CaseQuery query, CancellationToken ct) =>
        cases.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CaseDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<CaseDetailDto> Get(Guid id, CancellationToken ct) => cases.GetAsync(id, ct);

    /// <summary>
    /// Reports a problem on a plot's current crop. Symptom codes come from the closed catalogue.
    /// With a <c>clientReference</c>, sending the same report again (a retry, or the phone's offline
    /// queue) returns the case already made: 200 with <c>Idempotent-Replayed: true</c>, not a 201.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Cases)]
    [ProducesResponseType<CaseDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<CaseDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CaseDetailDto>> Create(CreateCaseRequest request, CancellationToken ct)
    {
        var (created, isNew) = await cases.CreateAsync(request, ct);
        if (isNew)
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);

        Response.Headers["Idempotent-Replayed"] = "true";
        return Ok(created);
    }

    /// <summary>
    /// Closes a case, or sends it to manual review. Every other status is set by the agent workflow;
    /// asking for one here gives 422 with code ILLEGAL_CASE_STATUS_CHANGE.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<CaseDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<CaseDetailDto> UpdateStatus(Guid id, UpdateCaseStatusRequest request, CancellationToken ct) =>
        cases.UpdateStatusAsync(id, request, ct);

    /// <summary>
    /// Adds a leaf photo (multipart field "file"): JPEG, PNG or WebP, at most 2 MB, up to three per
    /// case. The type is read from the file's own bytes. Uploading the same photo again returns the
    /// stored one (200) instead of a copy (201).
    /// </summary>
    [HttpPost("{id:guid}/photos")]
    [EnableRateLimiting(RateLimitPolicies.Cases)]
    [Consumes("multipart/form-data")]
    // A little above 2 MB for the multipart framing; the service enforces the exact limit.
    [RequestSizeLimit(PhotoRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = PhotoRequestLimit)]
    [ProducesResponseType<CasePhotoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<CasePhotoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CasePhotoDto>> AddPhoto(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var (photo, created) = await photos.AddAsync(id, new PhotoUpload(file.FileName, file.Length, content), ct);
        return created
            ? CreatedAtAction(nameof(GetPhoto), new { id, photoId = photo.Id }, photo)
            : Ok(photo);
    }

    /// <summary>The photo itself, to anyone who can see the case.</summary>
    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(Guid id, Guid photoId, CancellationToken ct)
    {
        var photo = await photos.GetAsync(id, photoId, ct);
        // Private: a farm photo must never sit in a shared proxy cache. Photos never change, so
        // the browser may keep its own copy.
        Response.Headers.CacheControl = "private, max-age=86400";
        // The type was sniffed on upload; stop the browser second-guessing it.
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(photo.Bytes, photo.ContentType);
    }

    private const long PhotoRequestLimit = 3 * 1024 * 1024;
}
