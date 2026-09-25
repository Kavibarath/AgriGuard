using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// Bound from the "AgentService" section. The key is a shared secret in both directions: the API
/// sends it when dispatching a run, and the agent sends it on every /internal/* call. It comes from
/// user-secrets locally and AgentService__ApiKey in the cloud, never from a committed file.
/// </summary>
public sealed class AgentServiceOptions
{
    public const string SectionName = "AgentService";

    /// <summary>Minimum key length. The key is the only thing standing between the network and /internal/*.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>Where the FastAPI service listens (agent/app/main.py).</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:8000");

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>How long to wait for the agent to accept a run. It answers 202 immediately, so this is short.</summary>
    public TimeSpan DispatchTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A run that has not reported a result by now is marked TimedOut. The agent enforces its own
    /// 5-minute limit; this is the backstop for when the agent process itself dies mid-run.
    /// </summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(6);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class AgentServiceOptionsValidator : IValidateOptions<AgentServiceOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentServiceOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("AgentService:ApiKey is not configured. Run: dotnet user-secrets set \"AgentService:ApiKey\" \"<32+ random characters>\" --project backend/src/AgriGuard.Api, and give the agent the same value as AGENT_API_KEY.");
        else if (options.ApiKey.Length < AgentServiceOptions.MinimumKeyLength)
            failures.Add($"AgentService:ApiKey must be at least {AgentServiceOptions.MinimumKeyLength} characters.");

        if (!options.BaseUrl.IsAbsoluteUri)
            failures.Add("AgentService:BaseUrl must be an absolute URL.");

        if (options.DispatchTimeout <= TimeSpan.Zero)
            failures.Add("AgentService:DispatchTimeout must be positive.");

        if (options.RunTimeout < TimeSpan.FromMinutes(1))
            failures.Add("AgentService:RunTimeout must be at least one minute.");

        if (options.SweepInterval <= TimeSpan.Zero)
            failures.Add("AgentService:SweepInterval must be positive.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
