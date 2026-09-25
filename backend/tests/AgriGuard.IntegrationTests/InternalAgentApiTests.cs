using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// The /internal/* surface the Python agent uses: the key boundary, the tool contract the agent
/// reads (agent/app/graph.py), and the callbacks that record a run — including the backend's own
/// re-check of anything the agent says is ready for approval.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InternalAgentApiTests(AgriGuardApiFactory factory)
{
    private static string Tomorrow => CaseFixtures.Today.AddDays(1).ToString("yyyy-MM-dd");

    private async Task<(CaseFixtures.FarmSetup Setup, HttpClient Farmer, Guid CaseId, Guid RunId)> RunInFlightAsync()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        var caseId = (await farmer.ReportCaseAsync(setup)).GetProperty("id").GetGuid();
        return (setup, farmer, caseId, await farmer.StartRunAsync(caseId));
    }

    // ── The key boundary ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key-wrong-key-wrong-key-wrong")]
    public async Task Internal_endpoints_refuse_a_missing_or_wrong_key(string? key)
    {
        var client = factory.CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Add("X-Agent-Key", key);

        var response = await client.GetAsync($"/internal/tools/case-detail?caseId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_user_token_does_not_open_the_internal_surface()
    {
        var admin = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.CoopAdministrator));

        var response = await admin.PostAsJsonAsync($"/internal/agent-runs/{Guid.NewGuid()}/events", new { eventType = "RunCompleted" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_agent_key_does_not_open_the_user_api()
    {
        var response = await factory.AgentClient().GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Tools ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Case_detail_carries_every_field_the_agent_reads()
    {
        var (setup, _, caseId, _) = await RunInFlightAsync();

        var detail = await factory.AgentClient().GetFromJsonAsync<JsonElement>($"/internal/tools/case-detail?caseId={caseId}");

        Assert.Equal(setup.Cycle.Id, detail.GetProperty("cropCycleId").GetGuid());
        Assert.Equal(setup.Plot.Id, detail.GetProperty("plotId").GetGuid());
        Assert.Equal(setup.DistrictId, detail.GetProperty("districtId").GetGuid());
        Assert.Equal(await factory.CropIdAsync("TOM"), detail.GetProperty("cropId").GetGuid());
        Assert.Equal("Tomato", detail.GetProperty("cropName").GetString());
        Assert.Equal("Flowering", detail.GetProperty("stage").GetString());
        Assert.Equal(0.8m, detail.GetProperty("areaHectares").GetDecimal());
        Assert.Equal("Brown patches after the rain.", detail.GetProperty("farmerNote").GetString());
        Assert.Equal(CaseFixtures.BlightSymptoms, detail.GetProperty("symptomCodes").EnumerateArray().Select(s => s.GetString()));
        // Both symptoms point at late blight, so it ranks first.
        var top = detail.GetProperty("candidatePathogens")[0];
        Assert.Equal("LATE_BLIGHT", top.GetProperty("code").GetString());
        Assert.Equal("Late blight", top.GetProperty("commonName").GetString());
        Assert.True(detail.TryGetProperty("sownDate", out _));
    }

    [Fact]
    public async Task Approved_products_are_those_labelled_for_the_pathogen_and_approved_for_the_crop()
    {
        var cropId = await factory.CropIdAsync("TOM");

        var result = await factory.AgentClient().GetFromJsonAsync<JsonElement>(
            $"/internal/tools/approved-products?cropId={cropId}&pathogenCode=late_blight");

        var products = result.GetProperty("products").EnumerateArray().ToList();
        var mancozeb = products.Single(p => p.GetProperty("productName").GetString() == "Mancozeb 80 WP");
        Assert.Equal(1.5m, mancozeb.GetProperty("minDosePerHectare").GetDecimal());
        Assert.Equal(2.5m, mancozeb.GetProperty("maxDosePerHectare").GetDecimal());
        Assert.DoesNotContain(products, p => p.GetProperty("productName").GetString() == "Imidacloprid 17.8 SL");
    }

    [Fact]
    public async Task An_unknown_pathogen_gives_no_products_rather_than_an_error()
    {
        var cropId = await factory.CropIdAsync("TOM");

        var result = await factory.AgentClient().GetFromJsonAsync<JsonElement>(
            $"/internal/tools/approved-products?cropId={cropId}&pathogenCode=MADE_UP");

        Assert.Empty(result.GetProperty("products").EnumerateArray());
    }

    [Fact]
    public async Task The_plot_safety_profile_gives_each_product_its_last_safe_spray_date()
    {
        var setup = await factory.SeedTomatoPlotAsync();

        var profile = await factory.AgentClient().GetFromJsonAsync<JsonElement>($"/internal/tools/plot-safety-profile?plotId={setup.Plot.Id}");

        var harvest = profile.GetProperty("harvestDate").Deserialize<DateOnly>();
        Assert.Equal(setup.Cycle.ExpectedHarvestDate, harvest);
        var imidacloprid = profile.GetProperty("productWindows").EnumerateArray()
            .Single(w => w.GetProperty("productName").GetString() == "Imidacloprid 17.8 SL");
        Assert.Equal(harvest.AddDays(-21), imidacloprid.GetProperty("lastSafeSprayDate").Deserialize<DateOnly>());
        Assert.True(imidacloprid.GetProperty("canSprayToday").GetBoolean());
    }

    [Fact]
    public async Task Stock_and_pricing_answer_for_a_product()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        await factory.SeedDealerStockAsync(setup.DistrictId, quantity: 12m);
        var productId = await factory.ProductIdAsync();
        var agent = factory.AgentClient();

        var stock = await agent.GetFromJsonAsync<JsonElement>($"/internal/tools/stock-availability?productId={productId}&districtId={setup.DistrictId}");
        var pricing = await agent.GetFromJsonAsync<JsonElement>($"/internal/tools/product-pricing?productId={productId}&quantity=1.6");

        Assert.True(stock.GetProperty("availableQuantity").GetDecimal() >= 12m);
        Assert.Equal(2400m, pricing.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(2, pricing.GetProperty("packsNeeded").GetInt32());
        Assert.Equal(4800m, pricing.GetProperty("estimatedCost").GetDecimal());
    }

    [Fact]
    public async Task Validate_prescription_approves_a_compliant_proposal()
    {
        var (setup, _, _, runId) = await RunInFlightAsync();
        await factory.SeedDealerStockAsync(setup.DistrictId);

        var verdict = await Validate(runId, setup.Cycle.Id, dose: 2.0m, total: 1.6m, Tomorrow);

        Assert.Equal("Approved", verdict.GetProperty("outcome").GetString());
        Assert.Equal(11, verdict.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Validate_prescription_rejects_a_spray_inside_the_pre_harvest_interval()
    {
        var (setup, _, _, runId) = await RunInFlightAsync();
        await factory.SeedDealerStockAsync(setup.DistrictId);
        // Harvest in 5 days; Mancozeb's interval on tomato is 7.
        await factory.QueryAsync(async db =>
        {
            var cycle = await db.CropCycles.FindAsync(setup.Cycle.Id);
            cycle!.PlannedHarvestDate = CaseFixtures.Today.AddDays(5);
            return await db.SaveChangesAsync();
        });

        var verdict = await Validate(runId, setup.Cycle.Id, dose: 2.0m, total: 1.6m, Tomorrow);

        Assert.Equal("Rejected", verdict.GetProperty("outcome").GetString());
        var v5 = verdict.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("code").GetString() == "V5");
        Assert.Equal("Failed", v5.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_proposal_the_model_got_wrong_gets_a_verdict_not_an_error()
    {
        var (setup, _, _, runId) = await RunInFlightAsync();

        // A product name where the id should be, and a date the model did not format.
        var response = await factory.AgentClient().PostAsJsonAsync("/internal/tools/validate-prescription",
            new { runId, cropCycleId = setup.Cycle.Id, productId = "Mancozeb 80 WP", dosePerHectare = 2.0, totalQuantity = 1.6, sprayDate = "tomorrow" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var verdict = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rejected", verdict.GetProperty("outcome").GetString());
        Assert.Equal("V1", verdict.GetProperty("results")[0].GetProperty("code").GetString());
        Assert.Equal("Failed", verdict.GetProperty("results")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_verdict_carries_only_the_fields_the_agent_accepts()
    {
        var (setup, _, _, runId) = await RunInFlightAsync();
        await factory.SeedDealerStockAsync(setup.DistrictId);

        var verdict = await Validate(runId, setup.Cycle.Id, dose: 2.0m, total: 1.6m, Tomorrow);

        // agent/app/contracts.py Verdict forbids unknown fields, so extra keys would fail every run.
        Assert.Equal(["outcome", "summary", "results"], verdict.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["code", "name", "status", "severity", "message", "evidence"],
            verdict.GetProperty("results")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task A_proposal_for_a_crop_outside_the_run_s_case_is_refused()
    {
        var (_, _, _, runId) = await RunInFlightAsync();
        var otherFarm = await factory.SeedTomatoPlotAsync();

        var response = await factory.AgentClient().PostAsJsonAsync("/internal/tools/validate-prescription", new
        {
            runId,
            cropCycleId = otherFarm.Cycle.Id,
            productId = await factory.ProductIdAsync(),
            dosePerHectare = 2.0,
            totalQuantity = 1.6,
            sprayDate = Tomorrow
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("PROPOSAL_OUTSIDE_CASE", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_no_stock_anywhere_the_stock_rule_fails_rather_than_going_unchecked()
    {
        // No test ever stocks Metalaxyl (approved on tomato at 0.8–1.2/ha).
        var (setup, _, _, runId) = await RunInFlightAsync();

        var verdict = await Validate(runId, setup.Cycle.Id, dose: 1.0m, total: 0.8m, Tomorrow, product: "Metalaxyl 25 WP");

        var v9 = verdict.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("code").GetString() == "V9");
        Assert.Equal("Failed", v9.GetProperty("status").GetString());
        Assert.Equal("Revise", verdict.GetProperty("outcome").GetString());
    }

    // ── Callbacks ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Progress_events_build_the_plan_steps_and_timeline()
    {
        var (_, farmer, _, runId) = await RunInFlightAsync();
        var agent = factory.AgentClient();
        agent.DefaultRequestHeaders.Add("X-Correlation-Id", $"agent-{runId}");

        await Post(agent, runId, new { eventType = "StepStarted", agentRole = "Coordinator", sequenceNo = 1, goal = "Plan the run" });
        await Post(agent, runId, new { eventType = "ToolCalled", agentRole = "Coordinator", toolName = "get_case_detail", durationMs = 42, payload = new { attempt = 1 } });
        await Post(agent, runId, new { eventType = "PlanCreated", agentRole = "Coordinator", payload = new { steps = new[] { new { seq = 1, agent = "Diagnosis" } } } });
        await Post(agent, runId, new { eventType = "StepCompleted", agentRole = "Coordinator", sequenceNo = 1 });
        await Post(agent, runId, new { eventType = "StepStarted", agentRole = "Diagnosis", sequenceNo = 2, goal = "Identify the most likely pest or disease" });
        await Post(agent, runId, new { eventType = "ToolFailed", agentRole = "Diagnosis", toolName = "get_crop_history", durationMs = 20000, payload = new { attempt = 1, error = "timeout", prompt = "secret prompt" } });

        var run = await farmer.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{runId}");
        Assert.Equal("Diagnosing", run.GetProperty("status").GetString());
        Assert.Equal("Diagnosis", run.GetProperty("plan").GetProperty("steps")[0].GetProperty("agent").GetString());
        var steps = run.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["Succeeded", "Running"], steps.Select(s => s.GetProperty("status").GetString()));
        Assert.Equal(1, steps[1].GetProperty("retryCount").GetInt32());

        var events = (await farmer.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{runId}/events?pageSize=50")).Items();
        var toolCall = events.Single(e => e.GetProperty("eventType").GetString() == "ToolCalled");
        Assert.Equal("get_case_detail", toolCall.GetProperty("toolName").GetString());
        Assert.Equal($"agent-{runId}", toolCall.GetProperty("correlationId").GetString());
        // Prompts are never stored, even if the agent sends one.
        var failure = events.Single(e => e.GetProperty("eventType").GetString() == "ToolFailed");
        Assert.Equal("[redacted]", failure.GetProperty("payload").GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task A_proposal_that_passes_the_backend_check_goes_to_pending_approval()
    {
        var (setup, farmer, caseId, runId) = await RunInFlightAsync();
        await factory.SeedDealerStockAsync(setup.DistrictId);
        var productId = await factory.ProductIdAsync();

        var response = await PostResult(runId, "PendingApproval", productId, dose: 2.0m, total: 1.6m);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var run = await farmer.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{runId}");
        Assert.Equal("PendingApproval", run.GetProperty("status").GetString());
        Assert.Equal("Approved", run.GetProperty("verdict").GetProperty("outcome").GetString());
        Assert.Equal("PendingApproval", (await farmer.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}")).GetProperty("status").GetString());

        // A result is recorded once.
        Assert.Equal(HttpStatusCode.Conflict, (await PostResult(runId, "PendingApproval", productId, 2.0m, 1.6m)).StatusCode);
    }

    [Fact]
    public async Task A_proposal_the_agent_calls_valid_but_the_rules_refuse_fails_the_run()
    {
        var (setup, farmer, caseId, runId) = await RunInFlightAsync();
        await factory.SeedDealerStockAsync(setup.DistrictId);

        // 9 kg/ha is far above Mancozeb's 2.5 kg/ha limit: a compromised agent claiming otherwise.
        await PostResult(runId, "PendingApproval", await factory.ProductIdAsync(), dose: 9.0m, total: 7.2m);

        var run = await farmer.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{runId}");
        Assert.Equal("Failed", run.GetProperty("status").GetString());
        Assert.Contains("backend's check", run.GetProperty("failureReason").GetString());
        Assert.Equal("Revise", run.GetProperty("verdict").GetProperty("outcome").GetString());
        Assert.Equal("AwaitingManualReview", (await farmer.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_failed_run_is_recorded_with_its_reason_and_the_case_goes_to_review()
    {
        var (_, farmer, caseId, runId) = await RunInFlightAsync();

        await factory.AgentClient().PostAsJsonAsync($"/internal/agent-runs/{runId}/result",
            new { run_id = runId, outcome = "Failed", failure_reason = "The language model is unreachable", revisions = 0 });

        var run = await farmer.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{runId}");
        Assert.Equal("Failed", run.GetProperty("status").GetString());
        Assert.Equal("The language model is unreachable", run.GetProperty("failureReason").GetString());
        Assert.Equal("AwaitingManualReview", (await farmer.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_result_for_another_run_is_refused()
    {
        var (_, _, _, runId) = await RunInFlightAsync();

        var response = await factory.AgentClient().PostAsJsonAsync($"/internal/agent-runs/{runId}/result",
            new { run_id = Guid.NewGuid(), outcome = "Failed", revisions = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task Post(HttpClient agent, Guid runId, object body) =>
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/internal/agent-runs/{runId}/events", body)).StatusCode);

    private async Task<JsonElement> Validate(Guid runId, Guid cycleId, decimal dose, decimal total, string sprayDate, string product = "Mancozeb 80 WP")
    {
        var response = await factory.AgentClient().PostAsJsonAsync("/internal/tools/validate-prescription", new
        {
            runId,
            cropCycleId = cycleId,
            productId = await factory.ProductIdAsync(product),
            dosePerHectare = dose,
            totalQuantity = total,
            sprayDate,
            dealerId = (Guid?)null
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The body agent/app/runner.py posts: RunResult dumped in snake_case.</summary>
    private Task<HttpResponseMessage> PostResult(Guid runId, string outcome, Guid productId, decimal dose, decimal total) =>
        factory.AgentClient().PostAsJsonAsync($"/internal/agent-runs/{runId}/result", new
        {
            run_id = runId,
            outcome,
            proposal = new
            {
                product_id = productId,
                dose_per_hectare = dose,
                total_quantity = total,
                spray_date = Tomorrow,
                dealer_id = (Guid?)null,
                justification = "Protectant fungicide."
            },
            verdict = new { outcome = "Approved", summary = "All rules passed.", results = Array.Empty<object>() },
            revisions = 0
        });
}
