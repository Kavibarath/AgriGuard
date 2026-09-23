using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Domain.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>Seeding and client helpers for the case and agent-run tests.</summary>
public static class CaseFixtures
{
    public static readonly string[] BlightSymptoms = ["leaf_brown_patches", "leaf_water_soaked_lesions"];

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>A farmer with 0.8 ha of flowering tomato, harvest in 50 days — the §11 demo plot.</summary>
    public sealed record FarmSetup(User Farmer, Guid DistrictId, Farm Farm, Plot Plot, CropCycle Cycle);

    public static async Task<FarmSetup> SeedTomatoPlotAsync(this AgriGuardApiFactory factory, Guid? districtId = null)
    {
        var district = districtId ?? (await factory.TwoDistrictsAsync()).First;
        var farmer = await factory.CreateUserAsync(UserRole.Farmer, districtId: district);
        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id, areaHectares: 0.8m);
        var cycle = await factory.SeedCropCycleAsync(
            plot.Id, await factory.CropIdAsync("TOM"), Today.AddDays(-60), maturityDays: 110, CropStage.Flowering);
        return new FarmSetup(farmer, district, farm, plot, cycle);
    }

    /// <summary>Reports a case through the API, as the farmer would.</summary>
    public static async Task<JsonElement> ReportCaseAsync(this HttpClient farmerClient, FarmSetup setup, string? note = "Brown patches after the rain.")
    {
        var response = await farmerClient.PostAsJsonAsync("/api/cases", new
        {
            plotId = setup.Plot.Id,
            cropCycleId = setup.Cycle.Id,
            symptomCodes = BlightSymptoms,
            farmerNote = note,
            latitude = 6.95m,
            longitude = 80.79m
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Starts a run through the API and returns its id.</summary>
    public static async Task<Guid> StartRunAsync(this HttpClient client, Guid caseId)
    {
        var response = await client.PostAsync($"/api/cases/{caseId}/agent-runs", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid();
    }

    /// <summary>A client that authenticates as the agent service.</summary>
    public static HttpClient AgentClient(this AgriGuardApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Key", AgriGuardApiFactory.AgentKey);
        return client;
    }

    public static Task<Guid> ProductIdAsync(this AgriGuardApiFactory factory, string name = "Mancozeb 80 WP") =>
        factory.QueryAsync(db => db.Products.Where(p => p.Name == name).Select(p => p.Id).FirstAsync());

    /// <summary>A dealer in the district holding in-date stock of the product.</summary>
    public static async Task<Dealer> SeedDealerStockAsync(this AgriGuardApiFactory factory, Guid districtId, string productName = "Mancozeb 80 WP", decimal quantity = 50m)
    {
        var owner = await factory.CreateUserAsync(UserRole.AgroDealer, districtId: districtId);
        var productId = await factory.ProductIdAsync(productName);

        return await factory.QueryAsync(async db =>
        {
            var dealer = new Dealer { UserId = owner.Id, ShopName = $"Shop {Guid.NewGuid():N}"[..20], DistrictId = districtId, Latitude = 6.97m, Longitude = 80.78m };
            db.Dealers.Add(dealer);
            db.InventoryBatches.Add(new InventoryBatch
            {
                Dealer = dealer,
                ProductId = productId,
                BatchNo = $"B-{Guid.NewGuid():N}"[..12],
                ExpiryDate = Today.AddYears(1),
                QuantityOnHand = quantity,
                UnitPrice = 2400m
            });
            await db.SaveChangesAsync();
            return dealer;
        });
    }
}
