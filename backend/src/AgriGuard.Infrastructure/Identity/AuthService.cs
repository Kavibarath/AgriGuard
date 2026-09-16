using System.Security.Cryptography;
using System.Text;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Domain.Identity;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Identity;

/// <summary>
/// Login, refresh-token rotation and logout.
///
/// Two token lifetimes, on purpose: the access token is short and self-contained (no database
/// hit per request), the refresh token is long-lived but stored server-side, so revoking it
/// actually logs the device out.
/// </summary>
public sealed class AuthService(
    AgriGuardDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<JwtOptions> options,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<AuthTokens> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var normalised = NormaliseEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalised, ct);

        // Verify against a dummy hash when the user does not exist so that "unknown email" and
        // "wrong password" take the same time — otherwise response timing enumerates accounts.
        var passwordOk = user is null
            ? passwordHasher.Verify(password, DummyHash) && false
            : passwordHasher.Verify(password, user.PasswordHash);

        if (user is null || !passwordOk)
        {
            logger.LogInformation("Failed login for {Email}", normalised);
            throw new AuthenticationFailedException();
        }

        if (!user.IsActive)
        {
            logger.LogInformation("Login attempt on disabled account {UserId}", user.Id);
            throw new AuthenticationFailedException("This account is disabled.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        user.LastLoginAt = now;

        var tokens = await IssueAsync(user, now, ct);
        logger.LogInformation("User {UserId} ({Role}) logged in", user.Id, user.Role);
        return tokens;
    }

    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
            throw new AuthenticationFailedException("Invalid refresh token.");

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (stored.RevokedAt is not null)
        {
            // A token that was already rotated is being replayed: either the client is buggy or
            // the token was stolen. Assume the worst and log every session of that user out.
            logger.LogWarning("Replayed refresh token for user {UserId}; revoking all sessions", stored.UserId);
            await RevokeAllAsync(stored.UserId, now, ct);
            throw new AuthenticationFailedException("Invalid refresh token.");
        }

        if (!stored.IsActive(now))
            throw new AuthenticationFailedException("Refresh token has expired.");

        if (!stored.User.IsActive)
            throw new AuthenticationFailedException("This account is disabled.");

        // Rotation: the presented token dies as the new one is born, in one SaveChanges.
        stored.RevokedAt = now;
        return await IssueAsync(stored.User, now, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        // Unknown or already-revoked tokens are not an error: logout must be idempotent.
        if (stored?.RevokedAt is null && stored is not null)
        {
            stored.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<AuthTokens> IssueAsync(User user, DateTime now, CancellationToken ct)
    {
        var accessToken = tokenService.CreateAccessToken(user);

        // 256 bits of CSPRNG output: the refresh token is a bearer secret, never a guessable id.
        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var refreshExpiresAt = now.Add(_options.RefreshTokenLifetime);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawRefreshToken),
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt
        });

        await db.SaveChangesAsync(ct);

        return new AuthTokens(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            rawRefreshToken,
            refreshExpiresAt,
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            user.DistrictId);
    }

    private async Task RevokeAllAsync(Guid userId, DateTime now, CancellationToken ct) =>
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    internal static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// SHA-256 with no salt, deliberately: refresh tokens are already 256 bits of randomness,
    /// so there is nothing to brute-force, and an unsalted hash is what makes lookup by hash possible.
    /// </summary>
    internal static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));

    /// <summary>A real-shaped hash of a random password, used only to burn the same CPU time on unknown emails.</summary>
    private static readonly string DummyHash =
        new Pbkdf2PasswordHasher().Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));
}
