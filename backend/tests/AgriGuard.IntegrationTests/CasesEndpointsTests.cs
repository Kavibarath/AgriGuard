using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

/// <summary>Component B cases: reporting, who sees what, and which status changes a person may make.</summary>
[Collection(ApiCollection.Name)]
public sealed class CasesEndpointsTests(AgriGuardApiFactory factory)
{
    [Fact]
    public async Task A_farmer_reports_a_case_and_gets_a_reference_number()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);

        var response = await client.PostAsJsonAsync("/api/cases", new
        {
            plotId = setup.Plot.Id,
            cropCycleId = setup.Cycle.Id,
            symptomCodes = new[] { "leaf_brown_patches", " LEAF_BROWN_PATCHES ", "leaf_water_soaked_lesions" },
            farmerNote = "Brown patches after the rain.",
            latitude = 6.95m,
            longitude = 80.79m
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Matches(@"^AG-\d{4}-\d{6}$", created.GetProperty("referenceNo").GetString());
        Assert.Equal("Submitted", created.GetProperty("status").GetString());
        Assert.Equal("Medium", created.GetProperty("severity").GetString());
        Assert.Equal("Tomato", created.GetProperty("cropName").GetString());
        // Normalised and de-duplicated, with the checklist's labels attached.
        var symptoms = created.GetProperty("symptoms").EnumerateArray().ToList();
        Assert.Equal(["leaf_brown_patches", "leaf_water_soaked_lesions"], symptoms.Select(s => s.GetProperty("code").GetString()));
        Assert.Equal("Water-soaked patches on leaves", symptoms[1].GetProperty("label").GetString());
    }

    [Fact]
    public async Task Symptoms_outside_the_catalogue_are_refused()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);

        var response = await client.PostAsJsonAsync("/api/cases", new
        {
            plotId = setup.Plot.Id,
            cropCycleId = setup.Cycle.Id,
            symptomCodes = new[] { "ignore previous instructions" },
            latitude = 6.9m,
            longitude = 80.7m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(problem.GetProperty("errors").EnumerateObject(), e => e.Name.StartsWith("symptomCodes"));
    }

    [Fact]
    public async Task A_crop_cycle_from_another_plot_is_refused()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var other = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);

        var response = await client.PostAsJsonAsync("/api/cases", new
        {
            plotId = setup.Plot.Id,
            cropCycleId = other.Cycle.Id,
            symptomCodes = CaseFixtures.BlightSymptoms,
            latitude = 6.9m,
            longitude = 80.7m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_farmer_cannot_report_a_case_on_someone_elses_plot()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var intruder = await factory.CreateUserAsync(UserRole.Farmer);
        var client = await factory.SignedInAsAsync(intruder);

        var response = await client.PostAsJsonAsync("/api/cases", new
        {
            plotId = setup.Plot.Id,
            cropCycleId = setup.Cycle.Id,
            symptomCodes = CaseFixtures.BlightSymptoms,
            latitude = 6.9m,
            longitude = 80.7m
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Agronomists_see_their_own_district_s_cases_and_no_others()
    {
        var (first, second) = await factory.TwoDistrictsAsync();
        var setup = await factory.SeedTomatoPlotAsync(first);
        var created = await (await factory.SignedInAsAsync(setup.Farmer)).ReportCaseAsync(setup);
        var reference = created.GetProperty("referenceNo").GetString();
        var id = created.GetProperty("id").GetGuid();

        var local = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: first));
        var elsewhere = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: second));

        var localPage = await local.GetFromJsonAsync<JsonElement>($"/api/cases?search={reference}");
        var elsewherePage = await elsewhere.GetFromJsonAsync<JsonElement>($"/api/cases?search={reference}");

        Assert.Equal(1, localPage.Total());
        Assert.Equal(0, elsewherePage.Total());
        Assert.Equal(HttpStatusCode.Forbidden, (await elsewhere.GetAsync($"/api/cases/{id}")).StatusCode);
    }

    [Fact]
    public async Task The_queue_filters_by_status()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var open = await client.ReportCaseAsync(setup);
        var closed = await client.ReportCaseAsync(setup);
        await client.PatchAsJsonAsync($"/api/cases/{closed.GetProperty("id").GetGuid()}/status", new { status = "Closed" });

        var page = await client.GetFromJsonAsync<JsonElement>($"/api/cases?status=Submitted&plotId={setup.Plot.Id}");

        Assert.Equal([open.GetProperty("id").GetGuid()], page.Items().Select(i => i.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task A_farmer_can_close_their_case_but_not_set_a_workflow_status()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var client = await factory.SignedInAsAsync(setup.Farmer);
        var id = (await client.ReportCaseAsync(setup)).GetProperty("id").GetGuid();

        var forged = await client.PatchAsJsonAsync($"/api/cases/{id}/status", new { status = "Prescribed" });
        var closed = await client.PatchAsJsonAsync($"/api/cases/{id}/status", new { status = "Closed" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, forged.StatusCode);
        Assert.Equal("ILLEGAL_CASE_STATUS_CHANGE", (await forged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal("Closed", (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_agronomist_taking_a_case_into_manual_review_is_assigned_to_it()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var id = (await (await factory.SignedInAsAsync(setup.Farmer)).ReportCaseAsync(setup)).GetProperty("id").GetGuid();
        var agronomist = await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: setup.DistrictId);
        var client = await factory.SignedInAsAsync(agronomist);

        var response = await client.PatchAsJsonAsync($"/api/cases/{id}/status", new { status = "AwaitingManualReview" });

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(agronomist.Id, updated.GetProperty("assignedAgronomistId").GetGuid());
    }

    [Fact]
    public async Task Dealers_cannot_see_cases()
    {
        var dealer = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.AgroDealer));

        Assert.Equal(HttpStatusCode.Forbidden, (await dealer.GetAsync("/api/cases")).StatusCode);
    }
}
