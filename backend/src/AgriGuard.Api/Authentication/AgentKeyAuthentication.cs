using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using AgriGuard.Application.Auth;
using AgriGuard.Infrastructure.Agent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgriGuard.Api.Authentication;

public static class AgentKeyDefaults
{
    public const string Scheme = "AgentKey";
    public const string HeaderName = "X-Agent-Key";
}

/// <summary>
/// Authenticates the Python agent service on /internal/* by its shared key.
///
/// A separate scheme, not a JWT: the agent is a service, not a user, and it must not be able to
/// call the user API any more than a user may call /internal/*. The two are kept apart by
/// construction — the AgentService policy only accepts this scheme, the user policies only accept
/// JWT, and the claim this handler issues is never put in a token.
/// </summary>
internal sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<AgentServiceOptions> agentOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AgentKeyDefaults.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var expected = agentOptions.Value.ApiKey;
        if (values.Count != 1 || string.IsNullOrEmpty(expected) || !KeysMatch(values[0], expected))
        {
            // Logged for diagnosis; the caller only ever sees a bare 401.
            Logger.LogWarning("Rejected {Header} from {RemoteIp}", AgentKeyDefaults.HeaderName, Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid agent key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(AgriGuardClaims.AgentService, "true"), new Claim(ClaimTypes.Name, "agriguard-agent-service")],
            AgentKeyDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), AgentKeyDefaults.Scheme)));
    }

    /// <summary>
    /// Constant-time comparison. A plain string compare returns as soon as a character differs, and
    /// timing that across many requests leaks the key a character at a time. Hashing first gives
    /// both sides the same length, which FixedTimeEquals needs to be constant-time.
    /// </summary>
    private static bool KeysMatch(string? provided, string expected) =>
        provided is not null && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
