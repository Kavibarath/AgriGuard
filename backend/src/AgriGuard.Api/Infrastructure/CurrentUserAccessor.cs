using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Domain.Identity;

namespace AgriGuard.Api.Infrastructure;

/// <summary>
/// Reads the caller's identity from the validated JWT. Claims are only trusted because the
/// bearer handler has already checked the signature, issuer, audience and expiry.
/// </summary>
public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Find(JwtRegisteredClaimNames.Sub, ClaimTypes.NameIdentifier), out var id) ? id : null;

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Find(AgriGuardClaims.Role, ClaimTypes.Role), out var role) ? role : null;

    public Guid? DistrictId =>
        Guid.TryParse(Find(AgriGuardClaims.DistrictId), out var id) ? id : null;

    public string? Email => Find(JwtRegisteredClaimNames.Email, ClaimTypes.Email);

    /// <summary>
    /// Falls back to the mapped ClaimTypes.* URI so the accessor keeps working whether or not
    /// MapInboundClaims is turned off in the bearer setup.
    /// </summary>
    private string? Find(params string[] claimTypes) =>
        claimTypes.Select(type => Principal?.FindFirst(type)?.Value).FirstOrDefault(value => value is not null);
}
