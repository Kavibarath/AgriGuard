using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AgriGuard.Application.Auth;
using AgriGuard.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgriGuard.Infrastructure.Identity;

/// <summary>
/// Builds the signed access token every client request carries.
///
/// The token is signed, not encrypted: anyone holding it can read the payload (paste it into
/// jwt.io). So it carries only what authorization needs — who, which role, which district —
/// and nothing secret. It is trusted solely because the signature is checked on the way back in
/// (see ConfigureJwtBearerOptions in the API project).
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    // Handler and credentials are thread-safe and the key never changes at runtime, so build them once.
    private static readonly JwtSecurityTokenHandler Handler = new();
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(options.Value.SigningKeyBytes),
        // HMAC-SHA256: the same secret signs and verifies. Correct here because this one API does
        // both; RS256 would only be needed if a third party had to verify without being able to mint.
        SecurityAlgorithms.HmacSha256);

    public AccessToken CreateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        // TimeProvider rather than DateTime.UtcNow, so tests can control "now".
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(_options.AccessTokenLifetime);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            // Unique per token: lets a single token be identified in logs or a future deny-list.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(AgriGuardClaims.Role, user.Role.ToString())
        ];

        // Only when present: an empty district claim would read as "district = empty string"
        // rather than "no district", and district-scoped queries must not match on that.
        if (user.DistrictId is { } districtId)
            claims.Add(new Claim(AgriGuardClaims.DistrictId, districtId.ToString()));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: _credentials);

        return new AccessToken(Handler.WriteToken(token), expiresAt);
    }
}
