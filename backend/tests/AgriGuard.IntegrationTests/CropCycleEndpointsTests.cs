using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// Component A's non-CRUD operation end to end: the legal-transition matrix, the revised
/// harvest date, the audit trail, and who is allowed to move a cycle along.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CropCycleEndpointsTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(HttpClient Client, User Farmer, Plot Plot, Guid CropId, int MaturityDays)> SetupAsync()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id);
        return (await factory.SignedInAsAsync(farmer), farmer, plot, await factory.CropIdAsync(), await factory.CropMaturityDaysAsync());
    }

    [Fact]
    public async Task Sowing_a_plot_computes_the_expected_harvest_date_from_crop_maturity()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var sownDate = Today.AddDays(-10);

        var response = await client.PostAsJsonAsync("/api/crop-cycles",
            new { plotId = plot.Id, cropId, sownDate, plannedHarvestDate = (DateOnly?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cycle = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sown", cycle.GetProperty("stage").GetString());
        Assert.Equal(sownDate.AddDays(maturityDays), cycle.GetProperty("expectedHarvestDate").Deserialize<DateOnly>());
        // Nothing planned yet, so PHI is checked against the expected date.
        Assert.Equal(
            cycle.GetProperty("expectedHarvestDate").Deserialize<DateOnly>(),
            cycle.GetProperty("effectiveHarvestDate").Deserialize<DateOnly>());
        Assert.Equal(["Vegetative"], cycle.GetProperty("allowedNextStages").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public async Task A_planned_harvest_date_overrides_the_computed_one()
    {
        var (client, _, plot, cropId, _) = await SetupAsync();
        var planned = Today.AddDays(40);

        var response = await client.PostAsJsonAsync("/api/crop-cycles",
            new { plotId = plot.Id, cropId, sownDate = Today.AddDays(-5), plannedHarvestDate = planned });

        var cycle = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(planned, cycle.GetProperty("effectiveHarvestDate").Deserialize<DateOnly>());
        Assert.Equal(40, cycle.GetProperty("daysToHarvest").GetInt32());
    }

    [Fact]
    public async Task A_plot_cannot_have_two_active_cycles()
    {
        var (client, _, plot, cropId, _) = await SetupAsync();
        await client.PostAsJsonAsync("/api/crop-cycles", new { plotId = plot.Id, cropId, sownDate = Today.AddDays(-5) });

        var second = await client.PostAsJsonAsync("/api/crop-cycles", new { plotId = plot.Id, cropId, sownDate = Today });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Sowing_in_the_future_is_rejected()
    {
        var (client, _, plot, cropId, _) = await SetupAsync();

        var response = await client.PostAsJsonAsync("/api/crop-cycles",
            new { plotId = plot.Id, cropId, sownDate = Today.AddDays(3) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("sownDate", out _));
    }

    [Fact]
    public async Task Advancing_one_stage_records_the_move_and_revises_the_harvest_date()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var sownDate = Today.AddDays(-40);
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, sownDate, maturityDays);

        var response = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "Vegetative", reachedOn = Today.AddDays(-10), note = "First true leaves" });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Vegetative", updated.GetProperty("stage").GetString());

        var transition = Assert.Single(updated.GetProperty("transitions").EnumerateArray());
        Assert.Equal("Sown", transition.GetProperty("fromStage").GetString());
        Assert.Equal("Vegetative", transition.GetProperty("toStage").GetString());
        Assert.Equal("First true leaves", transition.GetProperty("note").GetString());

        // Vegetative is 25% of the way, so on a 110-day crop it is due around day 27.
        // This one arrived on day 30 — slightly late — so the whole cycle stretches and the
        // estimate moves later than the plain sown + maturity baseline.
        var baseline = sownDate.AddDays(maturityDays);
        var revised = updated.GetProperty("expectedHarvestDate").Deserialize<DateOnly>();
        Assert.True(revised > baseline, $"expected {revised} to be later than the baseline {baseline}");
        Assert.InRange(revised.DayNumber - baseline.DayNumber, 1, 20);
    }

    [Fact]
    public async Task Skipping_a_stage_is_422_with_a_machine_readable_code()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-20), maturityDays);

        var response = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "PreHarvest" });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ILLEGAL_STAGE_TRANSITION", problem.GetProperty("code").GetString());
        Assert.Contains("Vegetative", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Going_backwards_is_refused()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-30), maturityDays, CropStage.Flowering);

        var response = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "Vegetative" });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("cannot go back", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reaching_Harvested_closes_the_cycle_and_frees_the_plot()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-100), maturityDays, CropStage.PreHarvest);

        var response = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "Harvested", reachedOn = Today });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Harvested", updated.GetProperty("status").GetString());
        Assert.Equal(Today, updated.GetProperty("actualHarvestDate").Deserialize<DateOnly>());
        Assert.Empty(updated.GetProperty("allowedNextStages").EnumerateArray());

        // The plot is free again: the filtered unique index only covers Active cycles.
        var sowAgain = await client.PostAsJsonAsync("/api/crop-cycles",
            new { plotId = plot.Id, cropId, sownDate = Today });
        Assert.Equal(HttpStatusCode.Created, sowAgain.StatusCode);
    }

    [Fact]
    public async Task A_harvested_cycle_cannot_move_again()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-120), maturityDays, CropStage.PreHarvest);
        await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage", new { toStage = "Harvested" });

        var again = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage", new { toStage = "Harvested" });

        Assert.Equal((HttpStatusCode)422, again.StatusCode);
        Assert.Equal("CYCLE_NOT_ACTIVE", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_stage_cannot_be_reached_in_the_future_or_before_sowing()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var sownDate = Today.AddDays(-20);
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, sownDate, maturityDays);

        var future = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "Vegetative", reachedOn = Today.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);

        var beforeSowing = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "Vegetative", reachedOn = sownDate.AddDays(-1) });
        Assert.Equal(HttpStatusCode.BadRequest, beforeSowing.StatusCode);
    }

    [Fact]
    public async Task A_later_stage_cannot_be_dated_before_the_previous_one()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-40), maturityDays);
        await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "Vegetative", reachedOn = Today.AddDays(-10) });

        var response = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "Flowering", reachedOn = Today.AddDays(-20) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_whole_sequence_leaves_a_complete_audit_trail()
    {
        var (client, _, plot, cropId, maturityDays) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-100), maturityDays);

        foreach (var (stage, daysAgo) in new[] { ("Vegetative", 80), ("Flowering", 55), ("FruitSet", 35), ("PreHarvest", 10), ("Harvested", 0) })
        {
            var step = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
                new { toStage = stage, reachedOn = Today.AddDays(-daysAgo) });
            Assert.True(step.IsSuccessStatusCode, $"advancing to {stage} failed with {step.StatusCode}");
        }

        var final = await client.GetFromJsonAsync<JsonElement>($"/api/crop-cycles/{cycle.Id}");
        var transitions = final.GetProperty("transitions").EnumerateArray().ToList();
        Assert.Equal(5, transitions.Count);
        Assert.Equal(["Sown", "Vegetative", "Flowering", "FruitSet", "PreHarvest"],
            transitions.Select(t => t.GetProperty("fromStage").GetString()));

        // The audit row records who moved it, which the DTO deliberately does not expose.
        var recordedBy = await factory.QueryAsync(db => db.CropStageTransitions
            .Where(t => t.CropCycleId == cycle.Id)
            .Select(t => t.TransitionedByUserId)
            .Distinct()
            .ToListAsync());
        Assert.Single(recordedBy);
        Assert.NotEqual(Guid.Empty, recordedBy[0]);
    }

    [Fact]
    public async Task Another_farmers_cycle_cannot_be_advanced()
    {
        var (_, _, plot, cropId, maturityDays) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-20), maturityDays);
        var intruderClient = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        var response = await intruderClient.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage",
            new { toStage = "Vegetative" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_agronomist_can_read_a_cycle_but_not_advance_it()
    {
        var (home, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, home, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id);
        var cycle = await factory.SeedCropCycleAsync(plot.Id, await factory.CropIdAsync(), Today.AddDays(-20), await factory.CropMaturityDaysAsync());

        var agronomist = await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: home);
        var client = await factory.SignedInAsAsync(agronomist);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/crop-cycles/{cycle.Id}")).StatusCode);
        var advance = await client.PostAsJsonAsync($"/api/crop-cycles/{cycle.Id}/advance-stage", new { toStage = "Vegetative" });
        Assert.Equal(HttpStatusCode.Forbidden, advance.StatusCode);
    }

    [Fact]
    public async Task An_unknown_cycle_is_404()
    {
        var (client, _, _, _, _) = await SetupAsync();

        var response = await client.PostAsJsonAsync($"/api/crop-cycles/{Guid.NewGuid()}/advance-stage",
            new { toStage = "Vegetative" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
