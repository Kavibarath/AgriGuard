using System.Collections.Concurrent;
using AgriGuard.Application.Agent;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>Stands in for the Python service: records what would have been sent, or fails on request.</summary>
public sealed class FakeAgentDispatcher : IAgentDispatcher
{
    private readonly ConcurrentDictionary<Guid, AgentDispatchRequest> _sent = new();

    /// <summary>Case ids whose next dispatch should fail as if the agent service were down.</summary>
    public ConcurrentDictionary<Guid, bool> Unreachable { get; } = new();

    public AgentDispatchRequest? SentFor(Guid runId) => _sent.GetValueOrDefault(runId);

    public Task DispatchAsync(AgentDispatchRequest request, CancellationToken ct = default)
    {
        if (Unreachable.TryRemove(request.CaseId, out _))
            throw new AgentDispatchException("The agent service could not be reached (simulated).");

        _sent[request.RunId] = request;
        return Task.CompletedTask;
    }
}
