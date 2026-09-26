using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Domain.Registry;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// The human gate (§9.5): what an approval commits, that it commits it once, that it commits all of
/// it or nothing, and who may decide at all.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApprovalDecisionTests(AgriGuardApiFactory factory)
{
    private static string Key() => Guid.NewGuid().ToString();

    [Fact]
    public async Task Approving_issues_the_prescription_confirms_the_order_and_draws_the_stock()
    {
        var run = await factory.RunAwaitingApprovalAsync(stock: 50m);

        var response = await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Key());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", result.GetProperty("runStatus").GetString());
        Assert.Equal("Prescribed", result.GetProperty("caseStatus").GetString());

        var rx = result.GetProperty("prescription");
        var spray = CaseFixtures.Today.AddDays(1);
        Assert.Matches(@"^RX-\d{4}-\d{6}$", rx.GetProperty("prescriptionNo").GetString());
        Assert.Matches(@"^ORD-\d{4}-\d{6}$", rx.GetProperty("orderNo").GetString());
        Assert.Equal(spray, rx.GetProperty("sprayDate").Deserialize<DateOnly>());
        // Mancozeb on tomato: 7-day pre-harvest interval.
        Assert.Equal(spray.AddDays(7), rx.GetProperty("earliestSafeHarvestDate").Deserialize<DateOnly>());
        // 1.6 kg in 1 kg packs is 2 packs, at 2 400 LKR each.
        Assert.Equal(2, rx.GetProperty("packs").GetInt32());
        Assert.Equal(4800m, rx.GetProperty("orderTotal").GetDecimal());
        Assert.Contains("Do not harvest before", rx.GetProperty("instructions").GetString());

        // What left the shelf is whole packs, recorded as a committed reservation.
        Assert.Equal(48m, await factory.OnHandAsync(run.Dealer.Id));
        var (reservationStatus, applications, pathogen) = await factory.QueryAsync(async db => (
            await db.StockReservations.Where(r => r.AgentRunId == run.RunId).Select(r => r.Status).SingleAsync(),
            await db.ChemicalApplications.Where(a => a.CropCycleId == run.Setup.Cycle.Id).Select(a => new { a.Status, a.ApplicationDate }).ToListAsync(),
            await db.Prescriptions.Where(p => p.AgentRunId == run.RunId).Select(p => p.DiagnosedPathogen!.Code).SingleAsync()));
        Assert.Equal(ReservationStatus.Committed, reservationStatus);
        // Scheduled on the crop's record, so the next proposal's V6/V7 count this spray.
        var application = Assert.Single(applications);
        Assert.Equal(ApplicationStatus.Scheduled, application.Status);
        Assert.Equal(spray, application.ApplicationDate);
        Assert.Equal("LATE_BLIGHT", pathogen);

        // The farmer sees the outcome on their case.
        var detail = await run.Farmer.GetFromJsonAsync<JsonElement>($"/api/cases/{run.CaseId}");
        Assert.Equal("Prescribed", detail.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_run_shows_the_product_by_name_and_after_approval_the_issued_prescription()
    {
        var run = await factory.RunAwaitingApprovalAsync();

        var before = await run.Agronomist.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{run.RunId}");
        await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Key());
        var after = await run.Agronomist.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{run.RunId}");

        Assert.Equal("Mancozeb 80 WP", before.GetProperty("proposedProductName").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("prescription").ValueKind);
        Assert.Matches(@"^RX-\d{4}-\d{6}$", after.GetProperty("prescription").GetProperty("prescriptionNo").GetString());
    }

    [Fact]
    public async Task Replaying_the_same_key_returns_the_original_result_and_draws_nothing_more()
    {
        var run = await factory.RunAwaitingApprovalAsync();
        var key = Key();

        var first = await (await run.Agronomist.DecideAsync(run.RunId, "Approve", key: key)).Content.ReadFromJsonAsync<JsonElement>();
        var replay = await run.Agronomist.DecideAsync(run.RunId, "Approve", key: key);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        var second = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(second.GetProperty("replayed").GetBoolean());
        Assert.Equal(first.GetProperty("decisionId").GetGuid(), second.GetProperty("decisionId").GetGuid());
        Assert.Equal(
            first.GetProperty("prescription").GetProperty("prescriptionNo").GetString(),
            second.GetProperty("prescription").GetProperty("prescriptionNo").GetString());
        Assert.Equal(48m, await factory.OnHandAsync(run.Dealer.Id));
    }

    [Fact]
    public async Task A_second_decision_on_a_decided_run_is_refused()
    {
        var run = await factory.RunAwaitingApprovalAsync();
        await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Key());

        var again = await run.Agronomist.DecideAsync(run.RunId, "Reject", "Changed my mind about this one.", Key());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Two_approvals_racing_each_other_commit_exactly_once()
    {
        var run = await factory.RunAwaitingApprovalAsync();
        var second = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: run.Setup.DistrictId));

        var responses = await Task.WhenAll(
            run.Agronomist.DecideAsync(run.RunId, "Approve", key: Key()),
            second.DecideAsync(run.RunId, "Approve", key: Key()));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).OrderBy(s => (int)s));
        Assert.Equal(1, await factory.QueryAsync(db => db.Prescriptions.CountAsync(p => p.AgentRunId == run.RunId)));
        Assert.Equal(48m, await factory.OnHandAsync(run.Dealer.Id));
    }

    [Fact]
    public async Task Stock_is_drawn_from_the_batch_closest_to_expiry_first()
    {
        var run = await factory.RunAwaitingApprovalAsync(stock: 50m);
        var old = await factory.SeedBatchAsync(run.Dealer.Id, 1.5m, CaseFixtures.Today.AddDays(20));

        await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Key());

        // 2 kg needed: all 1.5 kg of the old batch, then 0.5 kg of the fresh one.
        var lines = await factory.QueryAsync(db => db.StockReservationLines
            .Where(l => l.Reservation.AgentRunId == run.RunId)
            .Select(l => new { l.BatchId, l.Quantity })
            .ToListAsync());
        Assert.Equal(1.5m, lines.Single(l => l.BatchId == old.Id).Quantity);
        Assert.Equal(0.5m, lines.Single(l => l.BatchId != old.Id).Quantity);
        Assert.Equal(49.5m, await factory.OnHandAsync(run.Dealer.Id));
    }

    [Fact]
    public async Task If_the_stock_is_gone_by_approval_time_nothing_is_committed()
    {
        var run = await factory.RunAwaitingApprovalAsync(stock: 50m);
        // Sold over the counter after the agent proposed.
        await factory.QueryAsync(db => db.InventoryBatches.Where(b => b.DealerId == run.Dealer.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, 1m)));

        var response = await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Key());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("PROPOSAL_NO_LONGER_VALID", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var state = await factory.QueryAsync(async db => new
        {
            Run = await db.AgentRuns.Where(r => r.Id == run.RunId).Select(r => r.Status.ToString()).SingleAsync(),
            Prescriptions = await db.Prescriptions.CountAsync(p => p.AgentRunId == run.RunId),
            Decisions = await db.ApprovalDecisions.CountAsync(d => d.AgentRunId == run.RunId)
        });
        Assert.Equal("PendingApproval", state.Run);
        Assert.Equal(0, state.Prescriptions);
        Assert.Equal(0, state.Decisions);
        Assert.Equal(1m, await factory.OnHandAsync(run.Dealer.Id));
    }

    [Fact]
    public async Task Rejecting_ends_the_run_and_commits_nothing()
    {
        var run = await factory.RunAwaitingApprovalAsync(stock: 50m);

        var response = await run.Agronomist.DecideAsync(run.RunId, "Reject", "Symptoms look like bacterial wilt, not blight.", Key());

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rejected", result.GetProperty("runStatus").GetString());
        Assert.Equal("Rejected", result.GetProperty("caseStatus").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("prescription").ValueKind);
        Assert.Equal(50m, await factory.OnHandAsync(run.Dealer.Id));

        var detail = await run.Farmer.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{run.RunId}");
        Assert.Contains("bacterial wilt", detail.GetProperty("failureReason").GetString());
    }

    [Fact]
    public async Task Requesting_a_revision_sends_the_run_back_to_the_agent_with_the_reason()
    {
        var run = await factory.RunAwaitingApprovalAsync();

        var response = await run.Agronomist.DecideAsync(run.RunId, "Revise", "Use a product with a shorter pre-harvest interval.", Key());

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RevisionRequested", result.GetProperty("runStatus").GetString());
        Assert.Equal("AgentProcessing", result.GetProperty("caseStatus").GetString());
        Assert.Equal("Use a product with a shorter pre-harvest interval.", factory.Dispatcher.SentFor(run.RunId)!.ReviewerNote);
    }

    [Theory]
    [InlineData("Reject", null)]
    [InlineData("Revise", "fix it")]
    public async Task Rejecting_or_revising_needs_a_real_reason(string decision, string? reason)
    {
        var run = await factory.RunAwaitingApprovalAsync();

        var response = await run.Agronomist.DecideAsync(run.RunId, decision, reason, Key());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_decision_without_an_idempotency_key_is_refused()
    {
        var run = await factory.RunAwaitingApprovalAsync();

        var response = await run.Agronomist.DecideAsync(run.RunId, "Approve", key: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_key_reused_for_another_run_is_refused()
    {
        var first = await factory.RunAwaitingApprovalAsync();
        var key = Key();
        await first.Agronomist.DecideAsync(first.RunId, "Approve", key: key);
        var second = await factory.RunAwaitingApprovalAsync();

        var response = await first.Agronomist.DecideAsync(second.RunId, "Approve", key: key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Only_an_agronomist_from_the_case_s_district_may_decide()
    {
        var run = await factory.RunAwaitingApprovalAsync();
        var (first, second) = await factory.TwoDistrictsAsync();
        var elsewhere = run.Setup.DistrictId == first ? second : first;
        var outsider = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: elsewhere));
        var admin = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.CoopAdministrator));

        Assert.Equal(HttpStatusCode.Forbidden, (await run.Farmer.DecideAsync(run.RunId, "Approve", key: Key())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DecideAsync(run.RunId, "Approve", key: Key())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.DecideAsync(run.RunId, "Approve", key: Key())).StatusCode);
    }
}
