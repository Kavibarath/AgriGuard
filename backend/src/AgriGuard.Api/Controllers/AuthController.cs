using System.ComponentModel.DataAnnotations;
using AgriGuard.Api.Infrastructure;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriGuard.Api.Controllers;

// Validation attributes sit on the constructor parameters: MVC rejects them on record properties.
public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(256)] string Password);

public sealed record RefreshRequest([Required, MaxLength(512)] string RefreshToken);

/// <summary>What the clients store. Mirrors <see cref="AuthTokens"/> minus anything internal.</summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserSummary User)
{
    public static AuthResponse From(AuthTokens t) => new(
        t.AccessToken,
        t.AccessTokenExpiresAtUtc,
        t.RefreshToken,
        t.RefreshTokenExpiresAtUtc,
        new UserSummary(t.UserId, t.Email, t.FullName, t.Role, t.DistrictId));
}

public sealed record UserSummary(Guid Id, string Email, string FullName, UserRole Role, Guid? DistrictId);

[ApiController]
[Route("api/auth")]
// Credential endpoints are the obvious brute-force target, so they get their own tighter bucket (§5).
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(IAuthService authService, ICurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Exchanges email and password for an access token and a refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        AuthResponse.From(await authService.LoginAsync(request.Email, request.Password, ct));

    /// <summary>Exchanges a refresh token for a new pair; the presented token is revoked.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct) =>
        AuthResponse.From(await authService.RefreshAsync(request.RefreshToken, ct));

    /// <summary>Revokes one refresh token (this device). Idempotent.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Echoes the claims in the caller's token — used by React and Flutter to restore a session.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<UserSummary> Me() =>
        currentUser.UserId is { } id && currentUser.Role is { } role
            ? new UserSummary(id, currentUser.Email ?? string.Empty, User.Identity?.Name ?? string.Empty, role, currentUser.DistrictId)
            : Unauthorized();
}
