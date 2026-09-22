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
/// The safety profile against the real seeded rules table — the numbers the deterministic
/// validator will later check rules V5, V6 and V7 against.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SafetyProfileEndpointTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(HttpClient Client, User Farmer, Plot Plot, Guid CropId)> SetupAsync()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id);
        return (await factory.SignedInAsAsync(farmer), farmer, plot, await factory.CropIdAsync("TOM"));
    }

    /// <summary>Records an application against the cycle, as the prescription flow will later.</summary>
    private Task SeedApplicationAsync(Guid cycleId, string productName, DateOnly appliedOn) =>
        factory.QueryAsync(async db =>
        {
            var product = await db.Products.FirstAsync(p => p.Name == productName);
            db.ChemicalApplications.Add(new ChemicalApplication
            {
                CropCycleId = cycleId,
                ProductId = product.Id,
                ApplicationDate = appliedOn,
                DosePerHectare = 2.0m,
                TotalQuantity = 1.6m,
                Status = ApplicationStatus.Applied
            });
            await db.SaveChangesAsync();
            return true;
        });

    private static JsonElement Window(JsonElement profile, string productName) =>
        profile.GetProperty("productWindows").EnumerateArray()
            .Single(w => w.GetProperty("productName").GetString() == productName);

    [Fact]
    public async Task A_plot_with_nothing_growing_reports_no_cycle_and_no_windows()
    {
        var (client, _, plot, _) = await SetupAsync();

        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/plots/{plot.Id}/safety-profile");

        Assert.Equal(plot.PlotCode, profile.GetProperty("plotCode").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("cropCycleId").ValueKind);
        Assert.Empty(profile.GetProperty("productWindows").EnumerateArray());
        Assert.Empty(profile.GetProperty("phiBlockedSprayDates").EnumerateArray());
    }

    [Fact]
    public async Task A_growing_crop_lists_every_product_approved_for_it_from_the_rules_table()
    {
        var (client, _, plot, cropId) = await SetupAsync();
        await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-30), await factory.CropMaturityDaysAsync("TOM"));

        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/plots/{plot.Id}/safety-profile");

        var windows = profile.GetProperty("productWindows").EnumerateArray().ToList();
        var activeTomatoApprovals = await factory.QueryAsync(db =>
            db.ProductCropApprovals.CountAsync(a => a.Crop.Code == "TOM" && a.IsActive));
        Assert.Equal(activeTomatoApprovals, windows.Count);

        // Withdrawn approvals (Carbofuran on tomato) are inactive and must not be offered at all.
        Assert.DoesNotContain(windows, w => w.GetProperty("productName").GetString()!.Contains("Carbofuran"));
        Assert.Equal("Tomato", profile.GetProperty("cropName").GetString());
    }

    [Fact]
    public async Task The_last_safe_spray_date_comes_from_the_products_pre_harvest_interval()
    {
        var (client, _, plot, cropId) = await SetupAsync();
        var harvest = Today.AddDays(30);
        await factory.QueryAsync(async db =>
        {
            db.CropCycles.Add(new CropCycle
            {
                PlotId = plot.Id,
                CropId = cropId,
                SownDate = Today.AddDays(-30),
                ExpectedHarvestDate = Today.AddDays(80),
                PlannedHarvestDate = harvest,
                Stage = CropStage.Flowering,
                Status = CropCycleStatus.Active
            });
            await db.SaveChangesAsync();
            return true;
        });

        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/plots/{plot.Id}/safety-profile");

        // The planned harvest date wins over the computed one, as rule V5 requires.
        Assert.Equal(harvest, profile.GetProperty("harvestDate").Deserialize<DateOnly>());
        Assert.Equal(30, profile.GetProperty("daysToHarvest").GetInt32());

        // Mancozeb on tomato has PHI 7 in the seeded rules; Imidacloprid has 21.
        Assert.Equal(harvest.AddDays(-7), Window(profile, "Mancozeb 80 WP").GetProperty("lastSafeSprayDate").Deserialize<DateOnly>());
        Assert.Equal(harvest.AddDays(-21), Window(profile, "Imidacloprid 17.8 SL").GetProperty("lastSafeSprayDate").Deserialize<DateOnly>());
    }

    [Fact]
    public async Task Applications_are_counted_and_the_allowance_reduced()
    {
        var (client, _, plot, cropId) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-40), 110);
        await SeedApplicationAsync(cycle.Id, "Mancozeb 80 WP", Today.AddDays(-30));
        await SeedApplicationAsync(cycle.Id, "Mancozeb 80 WP", Today.AddDays(-15));

        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/plots/{plot.Id}/safety-profile");

        var mancozeb = Window(profile, "Mancozeb 80 WP");
        Assert.Equal(2, mancozeb.GetProperty("applicationsUsed").GetInt32());
        Assert.Equal(2, mancozeb.GetProperty("applicationsRemaining").GetInt32()); // seeded max is 4
        Assert.Equal(Today.AddDays(-15), mancozeb.GetProperty("lastAppliedOn").Deserialize<DateOnly>());

        var usage = profile.GetProperty("ingredientUsage").EnumerateArray().Single();
        Assert.Equal("Mancozeb", usage.GetProperty("name").GetString());
        Assert.Equal(2, usage.GetProperty("applicationCount").GetInt32());
        Assert.Equal(15, usage.GetProperty("daysSinceLastApplication").GetInt32());
        Assert.Equal("FRAC M03", usage.GetProperty("resistanceGroup").GetString());
    }

    [Fact]
    public async Task A_recent_application_blocks_the_next_one_until_the_resistance_gap_passes()
    {
        var (client, _, plot, cropId) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-40), 110);
        await SeedApplicationAsync(cycle.Id, "Mancozeb 80 WP", Today.AddDays(-2));

        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/plots/{plot.Id}/safety-profile");

        var mancozeb = Window(profile, "Mancozeb 80 WP");
        Assert.False(mancozeb.GetProperty("canSprayToday").GetBoolean());
        Assert.Equal("MinimumInterval", mancozeb.GetProperty("blockedReason").GetString());
        // Seeded MinDaysBetweenApplications for Mancozeb is 7.
        Assert.Equal(Today.AddDays(5), mancozeb.GetProperty("earliestNextApplication").Deserialize<DateOnly>());
        Assert.Contains("next application is allowed from", mancozeb.GetProperty("blockedExplanation").GetString());
    }

    [Fact]
    public async Task Cancelled_applications_do_not_consume_an_allowance()
    {
        var (client, _, plot, cropId) = await SetupAsync();
        var cycle = await factory.SeedCropCycleAsync(plot.Id, cropId, Today.AddDays(-40), 110);
        await factory.QueryAsync(async db =>
        {
            var product = await db.Products.FirstAsync(p => p.Name == "Mancozeb 80 WP");
            db.ChemicalApplications.Add(new ChemicalApplication
            {
                CropCycleId = cycle.Id,
                ProductId = product.Id,
                ApplicationDate = Today.AddDays(-1),
                DosePerHectare = 2.0m,
                TotalQuantity = 1.6m,
                Status = ApplicationStatus.Cancelled
            });
            await db.SaveChangesAsync();
            return true;
        });

        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/plots/{plot.Id}/safety-profile");

        // It never touched the crop, so it neither counts against the limit nor starts a gap.
        var mancozeb = Window(profile, "Mancozeb 80 WP");
        Assert.Equal(0, mancozeb.GetProperty("applicationsUsed").GetInt32());
        Assert.True(mancozeb.GetProperty("canSprayToday").GetBoolean());
        Assert.Empty(profile.GetProperty("applications").EnumerateArray());
    }

    [Fact]
    public async Task Close_to_harvest_the_long_interval_products_are_blocked_first()
    {
        var (client, _, plot, cropId) = await SetupAsync();
        await factory.QueryAsync(async db =>
        {
            db.CropCycles.Add(new CropCycle
            {
                PlotId = plot.Id,
                CropId = cropId,
                SownDate = Today.AddDays(-100),
                ExpectedHarvestDate = Today.AddDays(10),
                PlannedHarvestDate = Today.AddDays(10),
                Stage = CropStage.PreHarvest,
                Status = CropCycleStatus.Active
            });
            await db.SaveChangesAsync();
            return true;
        });

        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/plots/{plot.Id}/safety-profile");

        // Imidacloprid needs 21 days and harvest is 10 away; Mancozeb needs 7 and still fits.
        Assert.False(Window(profile, "Imidacloprid 17.8 SL").GetProperty("canSprayToday").GetBoolean());
        Assert.Equal("PreHarvestInterval", Window(profile, "Imidacloprid 17.8 SL").GetProperty("blockedReason").GetString());
        Assert.Contains("Too close to harvest", Window(profile, "Imidacloprid 17.8 SL").GetProperty("blockedExplanation").GetString());
        Assert.True(Window(profile, "Mancozeb 80 WP").GetProperty("canSprayToday").GetBoolean());

        // No date is blocked outright, because Bt kurstaki is approved on tomato with a
        // zero-day interval: a biological can be sprayed up to picking. "Blocked" means no
        // approved product at all may be used that day, which is stricter than "most are".
        Assert.Empty(profile.GetProperty("phiBlockedSprayDates").EnumerateArray());

        var bt = Window(profile, "Bt kurstaki WP");
        Assert.True(bt.GetProperty("canSprayToday").GetBoolean());
        Assert.Equal(0, bt.GetProperty("preHarvestIntervalDays").GetInt32());
    }

    [Fact]
    public async Task Restricted_products_are_flagged_rather_than_hidden()
    {
        var (client, _, plot, _) = await SetupAsync();
        var chilli = await factory.CropIdAsync("CHI");
        await factory.SeedCropCycleAsync(plot.Id, chilli, Today.AddDays(-30), await factory.CropMaturityDaysAsync("CHI"));

        var profile = await client.GetFromJsonAsync<JsonElement>($"/api/plots/{plot.Id}/safety-profile");

        // Fipronil is permit-only (rule V10): the profile reports the fact; the validator decides.
        Assert.True(Window(profile, "Fipronil 5 SC").GetProperty("isRestricted").GetBoolean());
    }

    [Fact]
    public async Task An_agronomist_can_read_the_profile_for_their_district()
    {
        var (home, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, home, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id);
        await factory.SeedCropCycleAsync(plot.Id, await factory.CropIdAsync(), Today.AddDays(-20), 110);
        var client = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: home));

        var response = await client.GetAsync($"/api/plots/{plot.Id}/safety-profile");

        // Advising on treatment is exactly what an agronomist needs this for.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_farmers_plot_is_refused()
    {
        var (_, _, plot, _) = await SetupAsync();
        var intruder = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        Assert.Equal(HttpStatusCode.Forbidden, (await intruder.GetAsync($"/api/plots/{plot.Id}/safety-profile")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_plot_is_404_and_anonymous_is_401()
    {
        var (client, _, _, _) = await SetupAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/plots/{Guid.NewGuid()}/safety-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync($"/api/plots/{Guid.NewGuid()}/safety-profile")).StatusCode);
    }
}
