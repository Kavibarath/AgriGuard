using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ReferenceEndpointsTests(AgriGuardApiFactory factory)
{
    [Fact]
    public async Task Districts_are_listed_alphabetically_for_any_signed_in_role()
    {
        var client = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.AgroDealer));

        var districts = await client.GetFromJsonAsync<JsonElement>("/api/districts");

        var names = districts.EnumerateArray().Select(d => d.GetProperty("name").GetString()!).ToList();
        Assert.Equal(4, names.Count);
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        Assert.Contains("Nuwara Eliya", names);
    }

    [Fact]
    public async Task Crops_carry_the_maturity_days_the_sowing_form_needs()
    {
        var client = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        var crops = await client.GetFromJsonAsync<JsonElement>("/api/crops");

        var tomato = crops.EnumerateArray().First(c => c.GetProperty("code").GetString() == "TOM");
        Assert.Equal("Tomato", tomato.GetProperty("name").GetString());
        Assert.Equal(110, tomato.GetProperty("maturityDays").GetInt32());
    }

    [Fact]
    public async Task The_symptom_checklist_is_the_catalogue_the_api_validates_against()
    {
        var client = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        var symptoms = await client.GetFromJsonAsync<JsonElement>("/api/symptoms");

        var codes = symptoms.EnumerateArray().Select(s => s.GetProperty("code").GetString()!).ToList();
        Assert.Equal(Domain.Reference.SymptomCatalogue.All.Keys.Order(), codes.Order());
        var blight = symptoms.EnumerateArray().Single(s => s.GetProperty("code").GetString() == "leaf_water_soaked_lesions");
        Assert.Equal("Water-soaked patches on leaves", blight.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Look_ups_still_require_a_token()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/districts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/crops")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/symptoms")).StatusCode);
    }
}
