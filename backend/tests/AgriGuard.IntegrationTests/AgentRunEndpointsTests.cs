using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

/// <summary>Component B's non-CRUD operation: starting an agent run, and failing safely when it cannot start.</summary>
[Collection(ApiCollection.Name)]
public sealed class AgentRunEndpointsTests(AgriGuardApiFactory factory)
{
    [Fact]
    public async Task Starting_a_run_dispatches_it_and_moves_the_case_to_agent_processing()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var caseId = (await client.ReportCaseAsync(setup)).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/cases/{caseId}/agent-runs", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var started = await response.Content.ReadFromJsonAsync<JsonElement>();
        var runId = started.GetProperty("runId").GetGuid();
        Assert.Equal("Planning", started.GetProperty("status").GetString());
        Assert.EndsWith($"/api/agent-runs/{runId}", response.Headers.Location!.ToString());

        // The agent receives ids and our objective — nothing the farmer typed.
        var sent = factory.Dispatcher.SentFor(runId);
        Assert.NotNull(sent);
        Assert.Equal(caseId, sent.CaseId);
        Assert.StartsWith("Resolve crop-health case AG-", sent.Objective);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        Assert.Equal("AgentProcessing", detail.GetProperty("status").GetString());
        Assert.Equal(runId, detail.GetProperty("agentRuns")[0].GetProperty("id").GetGuid());

        var events = await client.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{runId}/events");
        Assert.Equal("StatusChanged", events.Items()[0].GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task A_second_run_while_one_is_in_flight_is_refused()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var caseId = (await client.ReportCaseAsync(setup)).GetProperty("id").GetGuid();
        await client.StartRunAsync(caseId);

        var second = await client.PostAsync($"/api/cases/{caseId}/agent-runs", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task When_the_agent_service_is_unreachable_the_run_fails_safely_and_a_person_takes_over()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var caseId = (await client.ReportCaseAsync(setup)).GetProperty("id").GetGuid();
        factory.Dispatcher.Unreachable[caseId] = true;

        var response = await client.PostAsync($"/api/cases/{caseId}/agent-runs", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var started = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", started.GetProperty("status").GetString());
        Assert.Contains("could not be reached", started.GetProperty("failureReason").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        Assert.Equal("AwaitingManualReview", detail.GetProperty("status").GetString());

        // ...and from manual review a fresh run may be tried again.
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/cases/{caseId}/agent-runs", null)).StatusCode);
    }

    [Fact]
    public async Task A_run_cannot_be_started_on_a_closed_case()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var caseId = (await client.ReportCaseAsync(setup)).GetProperty("id").GetGuid();
        await client.PatchAsJsonAsync($"/api/cases/{caseId}/status", new { status = "Closed" });

        var response = await client.PostAsync($"/api/cases/{caseId}/agent-runs", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_farmer_cannot_start_or_read_a_run_on_another_farmer_s_case()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var owner = await factory.SignedInAsAsync(setup.Farmer);
        var caseId = (await owner.ReportCaseAsync(setup)).GetProperty("id").GetGuid();
        var runId = await owner.StartRunAsync(caseId);
        var intruder = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        Assert.Equal(HttpStatusCode.Forbidden, (await intruder.PostAsync($"/api/cases/{caseId}/agent-runs", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await intruder.GetAsync($"/api/agent-runs/{runId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await intruder.GetAsync($"/api/agent-runs/{runId}/events")).StatusCode);
    }
}
