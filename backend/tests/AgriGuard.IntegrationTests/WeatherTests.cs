using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// Open-Meteo (§10): the spray-window endpoint, the agent's weather tool, rule V8 judged on real
/// forecasts, the 3-hour cache, and the degradation when the provider is down.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WeatherTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => TestCalendar.Today;

    private static JsonElement DayOf(JsonElement window, DateOnly date) =>
        window.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("date").Deserialize<DateOnly>() == date);

    private async Task<JsonElement> ValidateAsync(Guid runId, Guid cycleId, DateOnly sprayDate)
    {
        var response = await factory.AgentClient().PostAsJsonAsync("/internal/tools/validate-prescription", new
        {
            runId,
            cropCycleId = cycleId,
            productId = await factory.ProductIdAsync(),
            dosePerHectare = 2.0m,
            totalQuantity = 1.6m,
            sprayDate = sprayDate.ToString("yyyy-MM-dd"),
            dealerId = (Guid?)null
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement RuleOf(JsonElement verdict, string code) =>
        verdict.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("code").GetString() == code);

    [Fact]
    public async Task A_farmer_sees_which_coming_days_suit_spraying_on_their_plot()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var farmer = await factory.SignedInAsAsync(setup.Farmer);

        await factory.ChangeWeatherAsync(w => w.Set(Today.AddDays(2), rain: 80), async () =>
        {
            var window = await farmer.GetFromJsonAsync<JsonElement>($"/api/weather/spray-window?plotId={setup.Plot.Id}&days=7");

            Assert.True(window.GetProperty("forecastAvailable").GetBoolean());
            Assert.Equal(7, window.GetProperty("days").GetArrayLength());
            Assert.True(DayOf(window, Today.AddDays(1)).GetProperty("suitable").GetBoolean());

            var rainy = DayOf(window, Today.AddDays(2));
            Assert.False(rainy.GetProperty("suitable").GetBoolean());
            Assert.Equal(80, rainy.GetProperty("rainProbabilityPercent").GetInt32());
            Assert.Contains("mm of rain", rainy.GetProperty("problems")[0].GetString());
            Assert.Contains("Not suitable", window.GetProperty("summary").GetString());
            // The thresholds shown are V8's own.
            Assert.Equal(40, window.GetProperty("thresholds").GetProperty("maxRainProbabilityPercent").GetInt32());
        });
    }

    [Fact]
    public async Task Naming_a_product_judges_rain_over_its_own_rainfast_time()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        var mancozeb = await factory.ProductIdAsync();
        var rainfast = await factory.QueryAsync(db => Task.FromResult(db.ProductCropApprovals
            .Where(a => a.ProductId == mancozeb && a.Crop.Code == "TOM").Select(a => a.RainfastHours).Single()));

        var window = await farmer.GetFromJsonAsync<JsonElement>($"/api/weather/spray-window?plotId={setup.Plot.Id}&productId={mancozeb}");

        Assert.Equal(rainfast, window.GetProperty("rainfastHours").GetInt32());
        Assert.Equal("Mancozeb 80 WP", window.GetProperty("productName").GetString());
    }

    [Fact]
    public async Task Rule_V8_now_judges_the_real_forecast_for_the_spray_date()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        await factory.SeedDealerStockAsync(setup.DistrictId);
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        var runId = await farmer.StartRunAsync((await farmer.ReportCaseAsync(setup)).GetProperty("id").GetGuid());

        await factory.ChangeWeatherAsync(w => w.Set(Today.AddDays(2), rain: 75, wind: 22m), async () =>
        {
            var dry = await ValidateAsync(runId, setup.Cycle.Id, Today.AddDays(1));
            var wet = await ValidateAsync(runId, setup.Cycle.Id, Today.AddDays(2));

            Assert.Equal("Passed", RuleOf(dry, "V8").GetProperty("status").GetString());
            Assert.Equal("Approved", dry.GetProperty("outcome").GetString());

            // A bad forecast is fixable by picking another day, so it asks for a revision.
            Assert.Equal("Failed", RuleOf(wet, "V8").GetProperty("status").GetString());
            Assert.Equal("Revise", wet.GetProperty("outcome").GetString());
            Assert.Matches(@"75% chance of [\d.]+ mm of rain", RuleOf(wet, "V8").GetProperty("message").GetString());
        });
    }

    [Fact]
    public async Task When_Open_Meteo_is_down_V8_is_not_evaluated_and_nothing_fails()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        await factory.SeedDealerStockAsync(setup.DistrictId);
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        var runId = await farmer.StartRunAsync((await farmer.ReportCaseAsync(setup)).GetProperty("id").GetGuid());

        await factory.ChangeWeatherAsync(w => w.Unavailable = true, async () =>
        {
            var verdict = await ValidateAsync(runId, setup.Cycle.Id, Today.AddDays(1));
            var window = await farmer.GetFromJsonAsync<JsonElement>($"/api/weather/spray-window?plotId={setup.Plot.Id}");

            Assert.Equal("NotEvaluated", RuleOf(verdict, "V8").GetProperty("status").GetString());
            Assert.Equal("Approved", verdict.GetProperty("outcome").GetString());
            Assert.False(window.GetProperty("forecastAvailable").GetBoolean());
            Assert.Empty(window.GetProperty("days").EnumerateArray());
            Assert.Contains("unavailable", window.GetProperty("summary").GetString());
        });
    }

    [Fact]
    public async Task A_spray_date_beyond_the_forecast_horizon_is_not_guessed()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        var runId = await farmer.StartRunAsync((await farmer.ReportCaseAsync(setup)).GetProperty("id").GetGuid());

        var verdict = await ValidateAsync(runId, setup.Cycle.Id, Today.AddDays(30));

        Assert.Equal("NotEvaluated", RuleOf(verdict, "V8").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Neighbouring_plots_share_one_cached_forecast()
    {
        var first = await factory.SeedTomatoPlotAsync();
        var second = await factory.SeedTomatoPlotAsync();

        await factory.ChangeWeatherAsync(_ => { }, async () =>
        {
            var before = factory.Weather.Calls;
            await (await factory.SignedInAsAsync(first.Farmer)).GetFromJsonAsync<JsonElement>($"/api/weather/spray-window?plotId={first.Plot.Id}");
            await (await factory.SignedInAsAsync(second.Farmer)).GetFromJsonAsync<JsonElement>($"/api/weather/spray-window?plotId={second.Plot.Id}");

            // Same ~11 km cell: one call to the provider, the second answer came from the cache.
            Assert.Equal(before + 1, factory.Weather.Calls);
        });
    }

    [Fact]
    public async Task The_agent_s_weather_tool_includes_recent_rain_and_humidity()
    {
        var setup = await factory.SeedTomatoPlotAsync();

        var tool = await factory.AgentClient().GetFromJsonAsync<JsonElement>($"/internal/tools/weather-forecast?plotId={setup.Plot.Id}&days=5");

        Assert.Equal(5, tool.GetProperty("days").GetArrayLength());
        Assert.Equal(80, tool.GetProperty("recentHumidityPercent").GetInt32());
        Assert.StartsWith("Suitable to spray on", tool.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Another_farmer_s_plot_and_non_farm_roles_are_refused()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var stranger = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));
        var dealer = await factory.SignedInAsAsync(UserRole.AgroDealer);

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/weather/spray-window?plotId={setup.Plot.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dealer.GetAsync($"/api/weather/spray-window?plotId={setup.Plot.Id}")).StatusCode);
    }
}
