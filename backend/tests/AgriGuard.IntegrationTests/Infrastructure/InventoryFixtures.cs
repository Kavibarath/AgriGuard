using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>Seeding and client helpers for the catalogue, inventory, reservation and order tests.</summary>
public static class InventoryFixtures
{
    /// <summary>A signed-in dealer and their shop, holding one batch of the product.</summary>
    public sealed record Shop(User Owner, Dealer Dealer, HttpClient Client, InventoryBatch Batch);

    public static async Task<Shop> SeedShopAsync(this AgriGuardApiFactory factory, decimal quantity = 50m, string productName = "Mancozeb 80 WP")
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var dealer = await factory.SeedDealerStockAsync(district, productName, quantity);
        var owner = await factory.QueryAsync(db => Task.FromResult(db.Users.Single(u => u.Id == dealer.UserId)));
        var batch = await factory.QueryAsync(db => Task.FromResult(db.InventoryBatches.Single(b => b.DealerId == dealer.Id)));
        return new Shop(owner, dealer, await factory.SignedInAsAsync(owner), batch);
    }

    public static async Task<HttpClient> SignedInAsAsync(this AgriGuardApiFactory factory, UserRole role) =>
        await factory.SignedInAsAsync(await factory.CreateUserAsync(role));

    /// <summary>POST /api/inventory/reservations and the response.</summary>
    public static Task<HttpResponseMessage> HoldAsync(this HttpClient dealer, Guid productId, decimal quantity, DateOnly? usableOn = null, string? note = null) =>
        dealer.PostAsJsonAsync("/api/inventory/reservations", new { productId, quantity, usableOn, note });

    /// <summary>A hold that must succeed; returns its id.</summary>
    public static async Task<Guid> HeldAsync(this HttpClient dealer, Guid productId, decimal quantity)
    {
        var response = await dealer.HoldAsync(productId, quantity);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static Task<(decimal OnHand, decimal Reserved)> BatchLevelsAsync(this AgriGuardApiFactory factory, Guid batchId) =>
        factory.QueryAsync(db => Task.FromResult(db.InventoryBatches
            .Where(b => b.Id == batchId)
            .Select(b => new ValueTuple<decimal, decimal>(b.QuantityOnHand, b.QuantityReserved))
            .Single()));

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;
}
