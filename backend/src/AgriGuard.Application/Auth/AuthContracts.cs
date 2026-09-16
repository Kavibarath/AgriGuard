using AgriGuard.Domain.Identity;

namespace AgriGuard.Application.Auth;

/// <summary>A signed access token and the instant it stops being valid.</summary>
public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// What a successful login or refresh hands back. The refresh token is the raw value —
/// only its SHA-256 hash is stored, so this is the only moment it exists server-side.
/// </summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    Guid UserId,
    string Email,
    string FullName,
    UserRole Role,
    Guid? DistrictId);

public interface IPasswordHasher
{
    /// <summary>Hashes a password, salt and parameters included in the returned string.</summary>
    string Hash(string password);

    /// <summary>Verifies a password against a stored hash. Never throws on malformed input.</summary>
    bool Verify(string password, string passwordHash);
}

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
}

public interface IAuthService
{
    /// <summary>Throws <see cref="Common.Exceptions.AuthenticationFailedException"/> on bad credentials or a disabled account.</summary>
    Task<AuthTokens> LoginAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Exchanges a refresh token for a new pair, rotating the refresh token.</summary>
    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Revokes a single refresh token. Silent if it is unknown or already revoked.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
}
