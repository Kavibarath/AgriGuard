using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PlotsEndpointsTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static object NewPlot(Guid farmId, string code = "P-01", decimal area = 0.8m) => new
    {
        farmId,
        plotCode = code,
        name = "North field",
        areaHectares = area,
        latitude = 6.9497m,
        longitude = 80.7891m,
        soilType = "Loam"
    };

    [Fact]
    public async Task A_farmer_adds_a_plot_to_their_farm()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var client = await factory.SignedInAsAsync(farmer);

        var response = await client.PostAsJsonAsync("/api/plots", NewPlot(farm.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plot = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("P-01", plot.GetProperty("plotCode").GetString());
        Assert.Equal("Loam", plot.GetProperty("soilType").GetString());
        Assert.Equal("Active", plot.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, plot.GetProperty("activeCycle").ValueKind);
    }

    [Fact]
    public async Task A_plot_code_is_unique_within_a_farm_but_free_across_farms()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farmA = await factory.SeedFarmAsync(farmer, district, $"A {Guid.NewGuid():N}");
        var farmB = await factory.SeedFarmAsync(farmer, district, $"B {Guid.NewGuid():N}");
        var client = await factory.SignedInAsAsync(farmer);
        await client.PostAsJsonAsync("/api/plots", NewPlot(farmA.Id, "P-07"));

        var duplicate = await client.PostAsJsonAsync("/api/plots", NewPlot(farmA.Id, "P-07"));
        var otherFarm = await client.PostAsJsonAsync("/api/plots", NewPlot(farmB.Id, "P-07"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherFarm.StatusCode);
    }

    [Fact]
    public async Task A_plot_cannot_be_added_to_someone_elses_farm()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var owner = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(owner, district, $"Farm {Guid.NewGuid():N}");
        var client = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        var response = await client.PostAsJsonAsync("/api/plots", NewPlot(farm.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(0, "areaHectares")]
    [InlineData(-1, "areaHectares")]
    [InlineData(20000, "areaHectares")]
    public async Task An_impossible_area_is_rejected_with_a_field_error(decimal area, string field)
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var client = await factory.SignedInAsAsync(farmer);

        var response = await client.PostAsJsonAsync("/api/plots", NewPlot(farm.Id, area: area));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Coordinates_outside_the_globe_are_rejected()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var client = await factory.SignedInAsAsync(farmer);

        var response = await client.PostAsJsonAsync("/api/plots", new
        {
            farmId = farm.Id,
            plotCode = "P-99",
            areaHectares = 1m,
            latitude = 120m,
            longitude = 200m,
            soilType = "Loam"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("latitude", out _));
        Assert.True(errors.TryGetProperty("longitude", out _));
    }

    [Fact]
    public async Task The_area_is_locked_while_a_crop_is_growing()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id);
        await factory.SeedCropCycleAsync(plot.Id, await factory.CropIdAsync(), Today.AddDays(-10), await factory.CropMaturityDaysAsync());
        var client = await factory.SignedInAsAsync(farmer);

        var response = await client.PutAsJsonAsync($"/api/plots/{plot.Id}", new
        {
            plotCode = plot.PlotCode,
            name = plot.Name,
            areaHectares = 2.5m,
            latitude = plot.Latitude,
            longitude = plot.Longitude,
            soilType = "Loam",
            status = "Active"
        });

        // Dose × area is the treatment quantity (rule V4); changing area mid-cycle would
        // silently invalidate an outstanding prescription.
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("PLOT_AREA_LOCKED_DURING_CYCLE",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_plot_with_an_active_cycle_cannot_be_retired()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id);
        await factory.SeedCropCycleAsync(plot.Id, await factory.CropIdAsync(), Today.AddDays(-10), await factory.CropMaturityDaysAsync());
        var client = await factory.SignedInAsAsync(farmer);

        var response = await client.PutAsJsonAsync($"/api/plots/{plot.Id}", new
        {
            plotCode = plot.PlotCode,
            name = plot.Name,
            areaHectares = plot.AreaHectares,
            latitude = plot.Latitude,
            longitude = plot.Longitude,
            soilType = "Loam",
            status = "Retired"
        });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("PLOT_HAS_ACTIVE_CYCLE",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Listing_filters_by_farm_status_and_current_crop()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var tomatoPlot = await factory.SeedPlotAsync(farm.Id, "P-01");
        await factory.SeedPlotAsync(farm.Id, "P-02");
        var tomatoId = await factory.CropIdAsync("TOM");
        var chilliId = await factory.CropIdAsync("CHI");
        await factory.SeedCropCycleAsync(tomatoPlot.Id, tomatoId, Today.AddDays(-10), await factory.CropMaturityDaysAsync("TOM"));
        var client = await factory.SignedInAsAsync(farmer);

        var all = await client.GetFromJsonAsync<JsonElement>($"/api/plots?farmId={farm.Id}");
        Assert.Equal(2, all.Total());

        var growingTomato = await client.GetFromJsonAsync<JsonElement>($"/api/plots?farmId={farm.Id}&cropId={tomatoId}");
        var only = Assert.Single(growingTomato.Items());
        Assert.Equal("P-01", only.GetProperty("plotCode").GetString());
        Assert.Equal("Tomato", only.GetProperty("activeCycle").GetProperty("cropName").GetString());

        var growingChilli = await client.GetFromJsonAsync<JsonElement>($"/api/plots?farmId={farm.Id}&cropId={chilliId}");
        Assert.Empty(growingChilli.Items());

        var active = await client.GetFromJsonAsync<JsonElement>($"/api/plots?farmId={farm.Id}&status=Active");
        Assert.Equal(2, active.Total());
    }

    [Fact]
    public async Task A_farmer_sees_only_plots_on_their_own_farms()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var mine = await factory.CreateUserAsync(UserRole.Farmer);
        var theirs = await factory.CreateUserAsync(UserRole.Farmer);
        var myFarm = await factory.SeedFarmAsync(mine, district, $"Mine {Guid.NewGuid():N}");
        var theirFarm = await factory.SeedFarmAsync(theirs, district, $"Theirs {Guid.NewGuid():N}");
        await factory.SeedPlotAsync(myFarm.Id, "MINE-1");
        var theirPlot = await factory.SeedPlotAsync(theirFarm.Id, "THEIRS-1");
        var client = await factory.SignedInAsAsync(mine);

        var page = await client.GetFromJsonAsync<JsonElement>("/api/plots?pageSize=100");

        Assert.All(page.Items(), p => Assert.NotEqual("THEIRS-1", p.GetProperty("plotCode").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/plots/{theirPlot.Id}")).StatusCode);
    }
}
