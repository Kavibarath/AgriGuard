using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AgriGuard.Application.Agent;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// Starts a run on the Python agent service: POST /runs, which answers 202 as soon as it has
/// queued the work. A failed dispatch is recorded as a failed run, and the case goes to manual review.
///
/// Retries are deliberately narrow. Only 502 and 503 are retried: on a cloud host those come from
/// the host's proxy while the agent instance is still starting (a free instance waking from
/// sleep), so the request never reached the agent and sending it again cannot start the run
/// twice. Anything else (a 504, a timeout, a 4xx, a 500 from the agent itself) may mean the agent
/// did receive it, so it is not retried. The agent also ignores a run id it has already accepted,
/// which makes even an unlucky repeat harmless. All attempts together stay within DispatchTimeout.
/// </summary>
public sealed class HttpAgentDispatcher(HttpClient http, IOptions<AgentServiceOptions> options, TimeProvider time) : IAgentDispatcher
{
    public const string AgentKeyHeader = "X-Agent-Key";

    public async Task DispatchAsync(AgentDispatchRequest request, CancellationToken ct = default)
    {
        var settings = options.Value;
        var started = time.GetTimestamp();
        var delay = settings.WakeRetryDelay;

        for (var attempt = 1; ; attempt++)
        {
            var status = await SendOnceAsync(request, settings, ct);
            if (status is null)
                return;

            var waking = status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable;
            var timeLeft = settings.DispatchTimeout - time.GetElapsedTime(started);
            if (!waking || delay >= timeLeft)
            {
                var detail = waking ? $" after {attempt} attempts; it may still be starting up, so try again in a minute" : "";
                throw new AgentDispatchException($"The agent service refused the run with HTTP {(int)status}{detail}.");
            }

            await Task.Delay(delay, time, ct);
            delay *= 2;
        }
    }

    /// <summary>One POST. Returns null when the agent accepted the run, or the refusing status.</summary>
    private async Task<HttpStatusCode?> SendOnceAsync(AgentDispatchRequest request, AgentServiceOptions settings, CancellationToken ct)
    {
        // Field names are agent/app/contracts.py RunRequest, which forbids unknown fields.
        var body = new RunRequestBody(request.RunId.ToString(), request.CaseId.ToString(), request.Objective, request.ReviewerNote);

        // A request message can be sent only once, so each attempt builds its own.
        using var message = new HttpRequestMessage(HttpMethod.Post, "runs") { Content = JsonContent.Create(body) };
        message.Headers.Add(AgentKeyHeader, settings.ApiKey);
        message.Headers.Add("X-Correlation-Id", $"agent-{request.RunId}");

        try
        {
            using var response = await http.SendAsync(message, ct);
            return response.IsSuccessStatusCode ? null : response.StatusCode;
        }
        catch (HttpRequestException ex)
        {
            throw new AgentDispatchException($"The agent service could not be reached ({ex.Message}).", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AgentDispatchException($"The agent service did not answer within {settings.DispatchTimeout.TotalSeconds:0} seconds.", ex);
        }
    }

    private sealed record RunRequestBody(
        [property: JsonPropertyName("run_id")] string RunId,
        [property: JsonPropertyName("case_id")] string CaseId,
        [property: JsonPropertyName("objective")] string Objective,
        [property: JsonPropertyName("reviewer_note")] string? ReviewerNote);
}
