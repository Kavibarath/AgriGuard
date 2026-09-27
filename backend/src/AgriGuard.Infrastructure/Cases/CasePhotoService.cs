using System.Security.Cryptography;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Domain.Cases;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriGuard.Infrastructure.Cases;

/// <summary>
/// Stores leaf photos on a case. The phone resizes before uploading, so photos are small enough
/// to keep in PostgreSQL (bytea, at most 2 MB) rather than in a separate file store.
///
/// Nothing about an upload is trusted by its label: the declared content type and file name are
/// ignored in favour of the file's own first bytes, so a script renamed "leaf.jpg" is refused and
/// every photo is served back with the type it really has.
/// </summary>
public sealed class CasePhotoService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ICasePhotoService
{
    public async Task<(CasePhotoDto Photo, bool Created)> AddAsync(Guid caseId, PhotoUpload upload, CancellationToken ct = default)
    {
        var status = await db.CropCases.AsNoTracking().ScopedTo(currentUser)
            .Where(c => c.Id == caseId)
            .Select(c => (CaseStatus?)c.Status)
            .FirstOrDefaultAsync(ct);
        CaseScope.EnsureVisible(status, await db.CropCases.AnyAsync(c => c.Id == caseId, ct), "Case", caseId);

        if (status == CaseStatus.Closed)
            throw new BusinessRuleException("CASE_CLOSED", "This case is closed. Report a new case to add photos.");

        var bytes = await ReadBoundedAsync(upload, ct);
        var contentType = SniffImageType(bytes)
            ?? throw new RequestValidationException("file", "That file is not a JPEG, PNG or WebP photo.");
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        // A retried upload of the same photo returns the one already stored.
        if (await FindAsync(caseId, sha256, ct) is { } existing)
            return (existing, false);

        if (await db.CaseAttachments.CountAsync(a => a.CaseId == caseId, ct) >= ICasePhotoService.MaxPhotosPerCase)
            throw new BusinessRuleException("PHOTO_LIMIT_REACHED",
                $"A case can have at most {ICasePhotoService.MaxPhotosPerCase} photos.");

        var photo = new CaseAttachment
        {
            CaseId = caseId,
            FileName = SafeFileName(upload.FileName, contentType),
            ContentType = contentType,
            SizeBytes = bytes.Length,
            Sha256 = sha256,
            Content = bytes,
            UploadedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        db.CaseAttachments.Add(photo);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same photo arrived twice at the same moment; the other request stored it.
            db.ChangeTracker.Clear();
            return (await FindAsync(caseId, sha256, ct) ?? throw new ConflictException("The photo was uploaded twice at once. Reload the case."), false);
        }

        return (new CasePhotoDto(photo.Id, photo.FileName, photo.ContentType, photo.SizeBytes, photo.UploadedAt), true);
    }

    public async Task<PhotoContent> GetAsync(Guid caseId, Guid photoId, CancellationToken ct = default)
    {
        if (!await db.CropCases.AsNoTracking().ScopedTo(currentUser).AnyAsync(c => c.Id == caseId, ct))
            CaseScope.EnsureVisible<object>(null, await db.CropCases.AnyAsync(c => c.Id == caseId, ct), "Case", caseId);

        return await db.CaseAttachments.AsNoTracking()
            .Where(a => a.Id == photoId && a.CaseId == caseId)
            .Select(a => new PhotoContent(a.Content, a.ContentType, a.FileName))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Photo", photoId);
    }

    private Task<CasePhotoDto?> FindAsync(Guid caseId, string sha256, CancellationToken ct) =>
        db.CaseAttachments.AsNoTracking()
            .Where(a => a.CaseId == caseId && a.Sha256 == sha256)
            .Select(a => new CasePhotoDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAt))
            .FirstOrDefaultAsync(ct);

    /// <summary>Reads at most one byte past the limit, so an oversized upload is refused without buffering all of it.</summary>
    private static async Task<byte[]> ReadBoundedAsync(PhotoUpload upload, CancellationToken ct)
    {
        const int max = ICasePhotoService.MaxPhotoBytes;
        if (upload.Length == 0)
            throw new RequestValidationException("file", "The photo is empty.");
        if (upload.Length > max)
            throw new RequestValidationException("file", "The photo is larger than 2 MB. Take it again at a lower resolution.");

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await upload.Content.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max)
                throw new RequestValidationException("file", "The photo is larger than 2 MB. Take it again at a lower resolution.");
        }

        if (buffer.Length == 0)
            throw new RequestValidationException("file", "The photo is empty.");
        return buffer.ToArray();
    }

    /// <summary>The image type from the file's own signature bytes, or null if it is not one we accept.</summary>
    internal static string? SniffImageType(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp", // "RIFF....WEBP"
        _ => null
    };

    /// <summary>
    /// Only the last path segment, printable characters only, bounded, and with the extension of
    /// the real type. The name is shown in the console and used for downloads, so it must not carry
    /// paths, control characters or a misleading extension.
    /// </summary>
    internal static string SafeFileName(string? fileName, string contentType)
    {
        var extension = contentType switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName ?? string.Empty).Replace('\\', '/').Split('/').Last());
        var clean = new string([.. name.Where(ch => !char.IsControl(ch) && ch is not ('"' or '<' or '>' or '|' or ':' or '*' or '?'))]).Trim();
        if (clean.Length == 0) clean = "photo";
        if (clean.Length > 100) clean = clean[..100];
        return clean + extension;
    }
}
