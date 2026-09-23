namespace AgriGuard.Application.Auth;

/// <summary>
/// Claim types carried by the access token. Kept here (not in the API project) so services,
/// the agent-facing endpoints and the tests all agree on the exact strings.
/// </summary>
public static class AgriGuardClaims
{
    /// <summary>The user's role — one value of <see cref="Domain.Identity.UserRole"/>.</summary>
    public const string Role = "role";

    /// <summary>District the user belongs to; scopes agronomist case access. Absent for users with no district.</summary>
    public const string DistrictId = "district";

    /// <summary>
    /// Carried only by the principal the X-Agent-Key handler creates. No JWT is ever issued with it,
    /// so no user token can satisfy <see cref="AuthPolicies.AgentService"/>.
    /// </summary>
    public const string AgentService = "agent_service";
}

/// <summary>
/// Policy names used in <c>[Authorize(Policy = ...)]</c>. Strings appear in many controllers,
/// so they are declared once and referenced by constant.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Approve, reject or revise an agent run. Field Agronomist only (§1.1: sole holder of agent-runs:decide).</summary>
    public const string CanApprovePrescriptions = "CanApprovePrescriptions";

    /// <summary>Act on a farm/plot/case the caller owns. Farmers are limited to their own; agronomists and admins are not.</summary>
    public const string OwnsFarm = "OwnsFarm";

    /// <summary>Maintain stock, batches and pricing. Agro-Dealer only.</summary>
    public const string ManagesInventory = "ManagesInventory";

    /// <summary>Maintain regulatory rules, collection slots and users. Co-op Administrator only.</summary>
    public const string AdministersRules = "AdministersRules";

    /// <summary>The /internal/* surface: the Python agent service, authenticated by X-Agent-Key. Never a user.</summary>
    public const string AgentService = "AgentService";
}
