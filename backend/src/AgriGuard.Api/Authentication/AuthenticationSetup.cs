using System.IdentityModel.Tokens.Jwt;
using AgriGuard.Application.Auth;
using AgriGuard.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgriGuard.Api.Authentication;

public static class AuthenticationSetup
{
    public static IServiceCollection AddAgriGuardAuthentication(this IServiceCollection services)
    {
        // JWT is the default: every user-facing endpoint. The agent key is opt-in, only for the
        // /internal/* controllers whose policy names it.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(AgentKeyDefaults.Scheme, null);

        // Configured through the options pattern so JwtOptions can be injected properly
        // (never call BuildServiceProvider() inside AddJwtBearer — it creates a second container).
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

        return services;
    }
}

/// <summary>
/// Decides whether an incoming token is trustworthy. Issuing a token is easy; refusing a forged,
/// expired or foreign one is the actual security control. Every check below maps to an attack.
/// </summary>
internal sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions, ILoggerFactory loggerFactory)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name == JwtBearerDefaults.AuthenticationScheme)
            Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
        var jwt = jwtOptions.Value;
        var logger = loggerFactory.CreateLogger("AgriGuard.Authentication");

        // Keep claim names exactly as issued ("sub", "role", "district") instead of letting the
        // handler rewrite them into long WS-Federation URIs.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Forgery: a token signed with any other key is rejected. Without this, anyone could
            // mint themselves a FieldAgronomist token and approve prescriptions.
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.SigningKeyBytes),
            // Algorithm confusion: accept only what we sign with (blocks "alg": "none" and friends).
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            // Replay from elsewhere: a token minted by another system, or for another audience, is refused.
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            // Stolen tokens age out. The default ClockSkew is five minutes, which would let a
            // 15-minute token live for 20; issuer and validator share one clock, so no skew is needed.
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,

            // Make User.IsInRole(), [Authorize(Roles = ...)] and User.Identity.Name read the claims we issue.
            RoleClaimType = AgriGuardClaims.Role,
            NameClaimType = JwtRegisteredClaimNames.Name
        };

        options.Events = new JwtBearerEvents
        {
            // Log WHY a token was refused (expired vs bad signature vs wrong issuer) for diagnosis,
            // but never return the reason to the caller — that would be an oracle for forging tokens.
            OnAuthenticationFailed = context =>
            {
                logger.LogInformation("Rejected bearer token: {Reason}", context.Exception.GetType().Name);
                return Task.CompletedTask;
            }
        };
    }
}
