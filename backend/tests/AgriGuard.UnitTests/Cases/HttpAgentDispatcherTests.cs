using System.Net;
using AgriGuard.Application.Agent;
using AgriGuard.Infrastructure.Agent;
using Microsoft.Extensions.Options;

namespace AgriGuard.UnitTests.Cases;

/// <summary>
/// Starting a run on the agent service, including a free cloud instance that is still waking up
/// (its host answers 502/503 until the agent is listening).
/// </summary>
public sealed class HttpAgentDispatcherTests
{
    private static readonly AgentDispatchRequest Request = new(Guid.NewGuid(), Guid.NewGuid(), "Diagnose and propose", null);

    private sealed class Stub(params HttpStatusCode[] answers) : HttpMessageHandler
    {
        private int _next;
        public int Calls => _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var status = answers[Math.Min(_next, answers.Length - 1)];
            _next++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static HttpAgentDispatcher Dispatcher(Stub stub, TimeSpan? dispatchTimeout = null) =>
        new(new HttpClient(stub) { BaseAddress = new Uri("https://agent.test/") },
            Options.Create(new AgentServiceOptions
            {
                ApiKey = "test-only-agent-key-0123456789abcdef",
                DispatchTimeout = dispatchTimeout ?? TimeSpan.FromSeconds(5),
                WakeRetryDelay = TimeSpan.FromMilliseconds(5)
            }),
            TimeProvider.System);

    [Fact]
    public async Task An_accepted_run_is_sent_once()
    {
        var stub = new Stub(HttpStatusCode.Accepted);

        await Dispatcher(stub).DispatchAsync(Request);

        Assert.Equal(1, stub.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_waking_agent_is_retried_until_it_accepts(HttpStatusCode waking)
    {
        var stub = new Stub(waking, waking, HttpStatusCode.Accepted);

        await Dispatcher(stub).DispatchAsync(Request);

        Assert.Equal(3, stub.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.GatewayTimeout)]   // the agent may have received it
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Any_other_refusal_is_not_retried(HttpStatusCode refusal)
    {
        var stub = new Stub(refusal, HttpStatusCode.Accepted);

        var error = await Assert.ThrowsAsync<AgentDispatchException>(() => Dispatcher(stub).DispatchAsync(Request));

        Assert.Equal(1, stub.Calls);
        Assert.Contains($"HTTP {(int)refusal}", error.Message);
    }

    [Fact]
    public async Task Retries_stop_within_the_dispatch_timeout()
    {
        var stub = new Stub(HttpStatusCode.BadGateway);

        var error = await Assert.ThrowsAsync<AgentDispatchException>(
            () => Dispatcher(stub, dispatchTimeout: TimeSpan.FromMilliseconds(100)).DispatchAsync(Request));

        // 5, 10, 20, 40 ms of waiting fit in 100 ms; the next 80 ms wait would not.
        Assert.InRange(stub.Calls, 2, 6);
        Assert.Contains("may still be starting up", error.Message);
    }
}
