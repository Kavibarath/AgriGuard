using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Registry;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// The phone's offline queue (Component B): a report carries the phone's own reference, so a
/// retried or queued submission is stored once, and the time it was made survives the wait.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OfflineCaseReportTests(AgriGuardApiFactory factory)
{
    private static object Report(CaseFixtures.FarmSetup setup, Guid? clientReference = null, DateTime? capturedAt = null, Guid? plotId = null, Guid? cycleId = null) => new
    {
        plotId = plotId ?? setup.Plot.Id,
        cropCycleId = cycleId ?? setup.Cycle.Id,
        symptomCodes = CaseFixtures.BlightSymptoms,
        latitude = 6.95m,
        longitude = 80.79m,
        clientReference,
        capturedAt
    };

    [Fact]
    public async Task Sending_the_same_report_twice_stores_one_case_and_says_it_was_replayed()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var reference = Guid.NewGuid();

        var first = await client.PostAsJsonAsync("/api/cases", Report(setup, reference));
        var second = await client.PostAsJsonAsync("/api/cases", Report(setup, reference));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));

        var firstCase = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondCase = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstCase.GetProperty("id").GetGuid(), secondCase.GetProperty("id").GetGuid());

        var mine = await client.GetFromJsonAsync<JsonElement>($"/api/cases?plotId={setup.Plot.Id}");
        Assert.Equal(1, mine.Total());
    }

    [Fact]
    public async Task A_queued_report_and_its_retry_arriving_together_still_make_one_case()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var reference = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/cases", Report(setup, reference))));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"Got {(int)r.StatusCode}"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()));
        Assert.Single(ids.Distinct());
    }

    [Fact]
    public async Task Reports_without_a_reference_are_never_merged()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);

        await client.PostAsJsonAsync("/api/cases", Report(setup));
        await client.PostAsJsonAsync("/api/cases", Report(setup));

        var mine = await client.GetFromJsonAsync<JsonElement>($"/api/cases?plotId={setup.Plot.Id}");
        Assert.Equal(2, mine.Total());
    }

    [Fact]
    public async Task A_reference_reused_for_another_plot_is_refused_not_answered_with_the_wrong_case()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var otherPlot = await factory.SeedPlotAsync(setup.Farm.Id, plotCode: "P-02");
        var otherCycle = await factory.SeedCropCycleAsync(
            otherPlot.Id, await factory.CropIdAsync("TOM"), CaseFixtures.Today.AddDays(-30), maturityDays: 110, CropStage.Vegetative);
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var reference = Guid.NewGuid();

        (await client.PostAsJsonAsync("/api/cases", Report(setup, reference))).EnsureSuccessStatusCode();
        var reused = await client.PostAsJsonAsync("/api/cases", Report(setup, reference, plotId: otherPlot.Id, cycleId: otherCycle.Id));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        var problem = await reused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CLIENT_REFERENCE_REUSED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Another_farmer_s_reference_is_invisible()
    {
        var mine = await factory.SeedTomatoPlotAsync();
        var theirs = await factory.SeedTomatoPlotAsync();
        var reference = Guid.NewGuid();

        var first = await (await factory.SignedInAsAsync(theirs.Farmer)).PostAsJsonAsync("/api/cases", Report(theirs, reference));
        var second = await (await factory.SignedInAsAsync(mine.Farmer)).PostAsJsonAsync("/api/cases", Report(mine, reference));

        // Scoped per farmer: the same UUID from someone else is simply a new report.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task A_report_that_waited_on_the_phone_keeps_the_time_it_was_made()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var capturedAt = DateTime.UtcNow.AddHours(-3);

        var response = await client.PostAsJsonAsync("/api/cases", Report(setup, Guid.NewGuid(), capturedAt));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = created.GetProperty("capturedAt").GetDateTime();
        Assert.True(Math.Abs((stored - capturedAt).TotalSeconds) < 1, $"Stored {stored:O}, sent {capturedAt:O}");
        // The map needs both ends: where the phone was, and where the plot is registered.
        Assert.Equal(setup.Plot.Latitude, created.GetProperty("plotLatitude").GetDecimal());
    }

    [Fact]
    public async Task A_report_sent_at_once_has_no_capture_time()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);

        var response = await client.PostAsJsonAsync("/api/cases", Report(setup, Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-20)));

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, created.GetProperty("capturedAt").ValueKind);
    }

    [Theory]
    [InlineData(2)]      // two hours ahead: the phone's clock is wrong
    [InlineData(-24 * 20)] // twenty days old: too stale to diagnose from
    public async Task An_impossible_or_stale_capture_time_is_refused(int hoursFromNow)
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);

        var response = await client.PostAsJsonAsync("/api/cases", Report(setup, Guid.NewGuid(), DateTime.UtcNow.AddHours(hoursFromNow)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(problem.GetProperty("errors").EnumerateObject(), e => e.Name.Equals("capturedAt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_queue_carries_each_case_s_reported_location_for_the_map()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        await client.ReportCaseAsync(setup);

        var page = await client.GetFromJsonAsync<JsonElement>($"/api/cases?plotId={setup.Plot.Id}");
        var row = page.GetProperty("items")[0];

        Assert.Equal(6.95m, row.GetProperty("reportedLatitude").GetDecimal());
        Assert.Equal(80.79m, row.GetProperty("reportedLongitude").GetDecimal());
    }
}
