using AgriGuard.Api.Authentication;
using AgriGuard.Application.Auth;
using AgriGuard.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace AgriGuard.Api.Authorization;

/// <summary>
/// The four role policies from §1.1 of the plan. They are enforced here, on the server, for every
/// client: the React app hiding a button is convenience, this is the control — Flutter and curl
/// never see the React guard at all.
///
/// Policies answer "may this ROLE call this endpoint". Which ROWS a caller may touch (a farmer's
/// own plots, an agronomist's district) is a per-resource check in the component services, where
/// the resource is actually loaded.
/// </summary>
public static class AuthorizationSetup
{
    public static IServiceCollection AddAgriGuardAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Unauthenticated requests are rejected unless an endpoint opts out with [AllowAnonymous],
            // so forgetting [Authorize] on a new controller fails closed.
            options.FallbackPolicy = options.DefaultPolicy;

            // The single most important rule in the system: only a human agronomist can turn the
            // agent's proposal into a prescription. Not the farmer, not the AI, not the administrator.
            options.AddPolicy(AuthPolicies.CanApprovePrescriptions, RoleIn(UserRole.FieldAgronomist));

            options.AddPolicy(AuthPolicies.ManagesInventory, RoleIn(UserRole.AgroDealer));

            options.AddPolicy(AuthPolicies.AdministersRules, RoleIn(UserRole.CoopAdministrator));

            // Everyone with a legitimate interest in farm data; the dealer is not one of them.
            options.AddPolicy(AuthPolicies.OwnsFarm,
                RoleIn(UserRole.Farmer, UserRole.FieldAgronomist, UserRole.CoopAdministrator));

            // The /internal/* surface. Evaluated against the agent-key scheme only, so a user's JWT
            // is not even looked at here — and the claim it requires is never issued in a JWT.
            options.AddPolicy(AuthPolicies.AgentService, policy => policy
                .AddAuthenticationSchemes(AgentKeyDefaults.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AgriGuardClaims.AgentService, "true"));
        });

        return services;
    }

    /// <summary>
    /// Authenticated AND holding one of the roles. RequireAuthenticatedUser is explicit so an
    /// anonymous caller gets 401 ("who are you?") rather than 403 ("known, but no").
    /// </summary>
    private static Action<AuthorizationPolicyBuilder> RoleIn(params UserRole[] roles) => policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(AgriGuardClaims.Role, roles.Select(r => r.ToString()));
}
