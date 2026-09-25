using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AgriGuard.Application.Agent;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// Starts a run on the Python agent service: POST /runs, which answers 202 as soon as it has
/// queued the work. No retry: the run is not idempotent on the agent's side, and a second POST
/// after a slow first one could start the same run twice. A failed dispatch is recorded as a
/// failed run instead, and the case goes to manual review.
/// </summary>
public sealed class HttpAgentDispatcher(HttpClient http, IOptions<AgentServiceOptions> options) : IAgentDispatcher
{
    public const string AgentKeyHeader = "X-Agent-Key";

    public async Task DispatchAsync(AgentDispatchRequest request, CancellationToken ct = default)
    {
        // Field names are agent/app/contracts.py RunRequest, which forbids unknown fields.
        var body = new RunRequestBody(request.RunId.ToString(), request.CaseId.ToString(), request.Objective, request.ReviewerNote);

        using var message = new HttpRequestMessage(HttpMethod.Post, "runs") { Content = JsonContent.Create(body) };
        message.Headers.Add(AgentKeyHeader, options.Value.ApiKey);
        message.Headers.Add("X-Correlation-Id", $"agent-{request.RunId}");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AgentDispatchException($"The agent service could not be reached ({ex.Message}).", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AgentDispatchException($"The agent service did not answer within {options.Value.DispatchTimeout.TotalSeconds:0} seconds.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AgentDispatchException($"The agent service refused the run with HTTP {(int)response.StatusCode}.");
        }
    }

    private sealed record RunRequestBody(
        [property: JsonPropertyName("run_id")] string RunId,
        [property: JsonPropertyName("case_id")] string CaseId,
        [property: JsonPropertyName("objective")] string Objective,
        [property: JsonPropertyName("reviewer_note")] string? ReviewerNote);
}
