using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// Component D's outbreak signal (the API and the agent's tool share one calculation, fed by the
/// diagnoses agronomists confirm on approval) and the §5.1 reports of every component.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IntelligenceAndReportsTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => TestCalendar.Today;

    /// <summary>
    /// A farm growing a crop no other test uses, so the crop's outbreak signal holds only this
    /// test's cases. (Districts are reference data other tests count, so none is added.)
    /// </summary>
    private async Task<CaseFixtures.FarmSetup> CropNobodyElseGrowsAsync(string cropCode)
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer, districtId: district);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id);
        var cycle = await factory.SeedCropCycleAsync(plot.Id, await factory.CropIdAsync(cropCode), Today.AddDays(-30), maturityDays: 90, CropStage.Vegetative);
        return new CaseFixtures.FarmSetup(farmer, district, farm, plot, cycle);
    }

    /// <summary>Cases on the plot's crop, reported the given days ago; a null pathogen is an unconfirmed report.</summary>
    private Task SeedCasesAsync(CaseFixtures.FarmSetup setup, params (int DaysAgo, string? Pathogen, CaseSeverity Severity)[] cases) =>
        factory.QueryAsync(async db =>
        {
            var pathogens = await db.Pathogens.ToDictionaryAsync(p => p.Code, p => p.Id);
            var created = new List<(CropCase Case, int DaysAgo)>();
            foreach (var (daysAgo, pathogen, severity) in cases)
            {
                var c = new CropCase
                {
                    ReferenceNo = $"T-{Guid.NewGuid():N}"[..20],
                    FarmerId = setup.Farmer.Id,
                    PlotId = setup.Plot.Id,
                    CropCycleId = setup.Cycle.Id,
                    DistrictId = setup.DistrictId,
                    Status = pathogen is null ? CaseStatus.Submitted : CaseStatus.Closed,
                    Severity = severity,
                    SymptomCodes = ["leaf_brown_patches"],
                    ReportedLatitude = 6.95m,
                    ReportedLongitude = 80.79m,
                    ConfirmedPathogenId = pathogen is null ? null : pathogens[pathogen]
                };
                db.CropCases.Add(c);
                created.Add((c, daysAgo));
            }
            await db.SaveChangesAsync();

            // Noon in Sri Lanka on the day, so the local date is unambiguous.
            foreach (var (c, daysAgo) in created)
            {
                var at = Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(6, 30), DateTimeKind.Utc);
                await db.CropCases.Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, at));
            }
            return 0;
        });

    private async Task<(CaseFixtures.FarmSetup Setup, Guid CropId)> OutbreakAsync(string cropCode)
    {
        var setup = await CropNobodyElseGrowsAsync(cropCode);
        await SeedCasesAsync(setup,
            (0, "LATE_BLIGHT", CaseSeverity.High),
            (1, "LATE_BLIGHT", CaseSeverity.High),
            (2, "LATE_BLIGHT", CaseSeverity.Medium),
            (10, "EARLY_BLIGHT", CaseSeverity.Medium),
            (0, null, CaseSeverity.Critical),
            // Outside the 14-day window.
            (20, "LATE_BLIGHT", CaseSeverity.Critical));
        return (setup, setup.Cycle.CropId);
    }

    // ── Outbreak signal ──────────────────────────────────────────────────────

    [Fact]
    public async Task Confirmed_cases_become_a_district_pressure_index_with_its_pathogens_and_trend()
    {
        var (setup, cropId) = await OutbreakAsync("OKR");
        var farmer = await factory.SignedInAsAsync(setup.Farmer);

        var signal = await farmer.GetFromJsonAsync<JsonElement>(
            $"/api/intelligence/outbreak-signal?cropId={cropId}&districtId={setup.DistrictId}&days=14");

        Assert.Equal(5, signal.GetProperty("reportedCases").GetInt32());
        Assert.Equal(4, signal.GetProperty("confirmedCases").GetInt32());
        // 1.5 + 1.5·0.906 + 1·0.820 + 1·0.371 = 4.05 → index 64.
        Assert.Equal(64, signal.GetProperty("pressureIndex").GetInt32());
        Assert.Equal("High", signal.GetProperty("level").GetString());
        Assert.Equal("Rising", signal.GetProperty("trend").GetString());
        var top = signal.GetProperty("topPathogens").EnumerateArray().ToList();
        Assert.Equal(["LATE_BLIGHT", "EARLY_BLIGHT"], top.Select(p => p.GetProperty("code").GetString()));
        Assert.Equal(3, top[0].GetProperty("confirmedCases").GetInt32());
        Assert.Equal(14, signal.GetProperty("daily").GetArrayLength());
        Assert.StartsWith("Late blight pressure is high for okra in ", signal.GetProperty("summary").GetString());

        var district = signal.GetProperty("districts").EnumerateArray().Single(d => d.GetProperty("districtId").GetGuid() == setup.DistrictId);
        Assert.Equal("LATE_BLIGHT", district.GetProperty("topPathogenCode").GetString());
        // A map position, not a farm: a tenth of a degree.
        Assert.Equal(7.0m, district.GetProperty("latitude").GetDecimal());
    }

    [Fact]
    public async Task The_agent_s_outbreak_tool_gives_the_same_answer_as_the_api()
    {
        var (setup, cropId) = await OutbreakAsync("CUC");
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        var query = $"cropId={cropId}&districtId={setup.DistrictId}&days=14";

        var api = await farmer.GetFromJsonAsync<JsonElement>($"/api/intelligence/outbreak-signal?{query}");
        var tool = await factory.AgentClient().GetFromJsonAsync<JsonElement>($"/internal/tools/outbreak-signal?{query}");

        Assert.Equal(api.GetProperty("pressureIndex").GetInt32(), tool.GetProperty("pressureIndex").GetInt32());
        Assert.Equal(api.GetProperty("level").GetString(), tool.GetProperty("pressure").GetString());
        Assert.Equal(api.GetProperty("summary").GetString(), tool.GetProperty("summary").GetString());
        Assert.Equal("LATE_BLIGHT", tool.GetProperty("confirmedPathogens")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Any_signed_in_role_may_read_the_signal_but_not_an_anonymous_caller()
    {
        var (setup, _) = await OutbreakAsync("BEA");
        var dealer = await factory.SignedInAsAsync(UserRole.AgroDealer);

        Assert.Equal(HttpStatusCode.OK, (await dealer.GetAsync($"/api/intelligence/outbreak-signal?districtId={setup.DistrictId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/intelligence/outbreak-signal")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dealer.GetAsync($"/api/intelligence/outbreak-signal?districtId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Approving_a_prescription_confirms_the_diagnosis_for_the_outbreak_signal()
    {
        var run = await factory.RunAwaitingApprovalAsync();

        (await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Guid.NewGuid().ToString())).EnsureSuccessStatusCode();

        var confirmed = await factory.QueryAsync(db => db.CropCases.Where(c => c.Id == run.CaseId).Select(c => c.ConfirmedPathogen!.Code).FirstAsync());
        Assert.Equal("LATE_BLIGHT", confirmed);
    }

    // ── Reports ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Forecast_vs_actual_shows_how_far_each_forecast_was_from_the_harvest()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        async Task ForecastAsync(decimal estimate, decimal? actual)
        {
            var created = await farmer.PostAsJsonAsync("/api/harvest-forecasts", new { cropCycleId = setup.Cycle.Id, forecastHarvestDate = Today.AddDays(5), estimatedYieldKg = estimate });
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            if (actual is { } kg)
                (await farmer.PutAsJsonAsync($"/api/harvest-forecasts/{id}/actual", new { actualYieldKg = kg })).EnsureSuccessStatusCode();
        }
        await ForecastAsync(2400m, 2150m);
        await ForecastAsync(1000m, 1100m);
        await ForecastAsync(900m, null);

        var report = await farmer.GetFromJsonAsync<JsonElement>("/api/reports/harvest-forecast-vs-actual");
        var stranger = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));
        var theirs = await stranger.GetFromJsonAsync<JsonElement>("/api/reports/harvest-forecast-vs-actual");

        Assert.Equal(3, report.GetProperty("forecastCount").GetInt32());
        Assert.Equal(1, report.GetProperty("pendingActuals").GetInt32());
        var overall = report.GetProperty("overall");
        // (3250 − 3400) / 3400 = −4.4%; the misses were −10.4% and +10%, so a typical miss is 10.2%.
        Assert.Equal(-4.4m, overall.GetProperty("variancePercent").GetDecimal());
        Assert.Equal(10.2m, overall.GetProperty("meanAbsolutePercentError").GetDecimal());
        Assert.Equal("Farmer", report.GetProperty("bySource")[0].GetProperty("key").GetString());
        Assert.Equal(0, theirs.GetProperty("forecastCount").GetInt32());
    }

    [Fact]
    public async Task Plot_treatment_history_lists_each_spray_with_its_pre_harvest_interval()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var mancozeb = await factory.ProductIdAsync();
        await factory.QueryAsync(async db =>
        {
            db.ChemicalApplications.AddRange(
                new ChemicalApplication { CropCycleId = setup.Cycle.Id, ProductId = mancozeb, ApplicationDate = Today.AddDays(-10), DosePerHectare = 2m, TotalQuantity = 1.6m, Status = ApplicationStatus.Applied },
                new ChemicalApplication { CropCycleId = setup.Cycle.Id, ProductId = mancozeb, ApplicationDate = Today.AddDays(-3), DosePerHectare = 2m, TotalQuantity = 1.6m, Status = ApplicationStatus.Applied },
                new ChemicalApplication { CropCycleId = setup.Cycle.Id, ProductId = mancozeb, ApplicationDate = Today.AddDays(-1), DosePerHectare = 2m, TotalQuantity = 1.6m, Status = ApplicationStatus.Cancelled });
            return await db.SaveChangesAsync();
        });
        var farmer = await factory.SignedInAsAsync(setup.Farmer);

        var report = await farmer.GetFromJsonAsync<JsonElement>($"/api/reports/plot-treatment-history?plotId={setup.Plot.Id}");
        var stranger = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        // The cancelled spray is listed, but not counted and sets no harvest date.
        Assert.Equal(2, report.GetProperty("applications").GetInt32());
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Cancelled", rows[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("safeToHarvestFrom").ValueKind);
        // Mancozeb on tomato: 7-day pre-harvest interval.
        Assert.Equal(Today.AddDays(4), rows[1].GetProperty("safeToHarvestFrom").Deserialize<DateOnly>());
        Assert.Equal(2, report.GetProperty("byActiveIngredient")[0].GetProperty("applications").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/reports/plot-treatment-history?plotId={setup.Plot.Id}")).StatusCode);
    }

    [Fact]
    public async Task Case_throughput_counts_reports_prescriptions_and_agent_outcomes()
    {
        var run = await factory.RunAwaitingApprovalAsync();
        (await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Guid.NewGuid().ToString())).EnsureSuccessStatusCode();

        // The farmer's own view: exactly their one case.
        var report = await run.Farmer.GetFromJsonAsync<JsonElement>("/api/reports/case-throughput");

        Assert.Equal(1, report.GetProperty("reported").GetInt32());
        Assert.Equal(1, report.GetProperty("prescribed").GetInt32());
        Assert.Equal(100m, report.GetProperty("prescribedPercent").GetDecimal());
        Assert.True(report.GetProperty("medianHoursToPrescription").GetDouble() >= 0);
        Assert.Equal(100m, report.GetProperty("agentSuccessPercent").GetDecimal());
        Assert.Equal(1, report.GetProperty("casesByStatus").EnumerateArray().Single(s => s.GetProperty("status").GetString() == "Prescribed").GetProperty("count").GetInt32());
        Assert.Equal(30, report.GetProperty("daily").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await run.Farmer.GetAsync($"/api/reports/case-throughput?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task Stock_valuation_prices_each_batch_by_the_pack_for_its_own_shop_only()
    {
        // 50 kg of Mancozeb in 1 kg packs at LKR 2,400 a pack.
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var other = await factory.SeedShopAsync();
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        var farmer = await factory.SignedInAsAsync(UserRole.Farmer);

        var mine = await shop.Client.GetFromJsonAsync<JsonElement>("/api/reports/stock-valuation");
        var adminView = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/stock-valuation?dealerId={shop.Dealer.Id}");

        Assert.Equal(120_000m, mine.GetProperty("totalValue").GetDecimal());
        Assert.Equal(shop.Dealer.Id, Assert.Single(mine.GetProperty("byDealer").EnumerateArray()).GetProperty("dealerId").GetGuid());
        Assert.Equal(120_000m, adminView.GetProperty("totalValue").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await shop.Client.GetAsync($"/api/reports/stock-valuation?dealerId={other.Dealer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await farmer.GetAsync("/api/reports/stock-valuation")).StatusCode);
    }

    [Fact]
    public async Task Low_stock_flags_sold_out_and_nearly_sold_out_products()
    {
        // Two packs of Mancozeb, and a sold-out, expired and a held batch of Chlorothalonil.
        var shop = await factory.SeedShopAsync(quantity: 2m);
        await factory.SeedBatchAsync(shop.Dealer.Id, 0m, Today.AddYears(1), "Chlorothalonil 75 WP");
        await factory.SeedBatchAsync(shop.Dealer.Id, 5m, Today.AddDays(-1), "Chlorothalonil 75 WP");
        await factory.SeedBatchAsync(shop.Dealer.Id, 40m, Today.AddYears(1), "Metalaxyl 25 WP");

        var report = await shop.Client.GetFromJsonAsync<JsonElement>("/api/reports/low-stock");
        var strict = await shop.Client.GetFromJsonAsync<JsonElement>("/api/reports/low-stock?minPacks=1");

        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["Chlorothalonil 75 WP", "Mancozeb 80 WP"], rows.Select(r => r.GetProperty("productName").GetString()));
        Assert.Equal("OutOfStock", rows[0].GetProperty("state").GetString());
        Assert.Equal(5m, rows[0].GetProperty("expiredQuantity").GetDecimal());
        Assert.Equal(("Low", 2), (rows[1].GetProperty("state").GetString(), rows[1].GetProperty("sellablePacks").GetInt32()));
        Assert.Equal(1, strict.GetProperty("outOfStock").GetInt32());
        Assert.Equal(0, strict.GetProperty("low").GetInt32());
    }
}
