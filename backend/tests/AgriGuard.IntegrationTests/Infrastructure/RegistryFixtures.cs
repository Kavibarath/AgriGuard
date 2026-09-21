using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Reference;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>Seeding and signed-in-client helpers for the registry tests.</summary>
public static class RegistryFixtures
{
    /// <summary>A client whose every request carries this user's access token.</summary>
    public static async Task<HttpClient> SignedInAsAsync(this AgriGuardApiFactory factory, User user)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = AgriGuardApiFactory.TestPassword });
        response.EnsureSuccessStatusCode();

        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Two districts, so district-scoped access can actually be told apart.</summary>
    public static Task<(Guid First, Guid Second)> TwoDistrictsAsync(this AgriGuardApiFactory factory) =>
        factory.QueryAsync(async db =>
        {
            var ids = await db.Districts.OrderBy(d => d.Code).Select(d => d.Id).Take(2).ToListAsync();
            return (ids[0], ids[1]);
        });

    public static Task<Guid> CropIdAsync(this AgriGuardApiFactory factory, string code = "TOM") =>
        factory.QueryAsync(db => db.Crops.Where(c => c.Code == code).Select(c => c.Id).FirstAsync());

    public static Task<int> CropMaturityDaysAsync(this AgriGuardApiFactory factory, string code = "TOM") =>
        factory.QueryAsync(db => db.Crops.Where(c => c.Code == code).Select(c => c.MaturityDays).FirstAsync());

    /// <summary>Creates a farm directly in the database, bypassing the API's own rules.</summary>
    public static Task<Farm> SeedFarmAsync(
        this AgriGuardApiFactory factory,
        User owner,
        Guid districtId,
        string name = "Test Farm") =>
        factory.QueryAsync(async db =>
        {
            var farm = new Farm { FarmerId = owner.Id, DistrictId = districtId, Name = name, Village = "Testville" };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
            return farm;
        });

    public static Task<Plot> SeedPlotAsync(
        this AgriGuardApiFactory factory,
        Guid farmId,
        string plotCode = "P-01",
        decimal areaHectares = 0.8m) =>
        factory.QueryAsync(async db =>
        {
            var plot = new Plot
            {
                FarmId = farmId,
                PlotCode = plotCode,
                Name = "North field",
                AreaHectares = areaHectares,
                Latitude = 6.9497m,
                Longitude = 80.7891m,
                SoilType = SoilType.Loam
            };
            db.Plots.Add(plot);
            await db.SaveChangesAsync();
            return plot;
        });

    public static Task<CropCycle> SeedCropCycleAsync(
        this AgriGuardApiFactory factory,
        Guid plotId,
        Guid cropId,
        DateOnly sownDate,
        int maturityDays,
        CropStage stage = CropStage.Sown) =>
        factory.QueryAsync(async db =>
        {
            var cycle = new CropCycle
            {
                PlotId = plotId,
                CropId = cropId,
                SownDate = sownDate,
                Stage = stage,
                Status = CropCycleStatus.Active,
                ExpectedHarvestDate = sownDate.AddDays(maturityDays)
            };
            db.CropCycles.Add(cycle);
            await db.SaveChangesAsync();
            return cycle;
        });

    /// <summary>Reads a PagedResult's items without needing the generic type.</summary>
    public static JsonElement[] Items(this JsonElement paged) => [.. paged.GetProperty("items").EnumerateArray()];

    public static int Total(this JsonElement paged) => paged.GetProperty("totalCount").GetInt32();
}
