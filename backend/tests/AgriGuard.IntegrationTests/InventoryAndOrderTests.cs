using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// A dealer's shelf (batches, expiry warnings, recounts) and their orders (the fulfilment workflow
/// after an approved prescription), each limited to the dealer's own shop.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InventoryAndOrderTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => CaseFixtures.Today;

    // ── Batches ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_dealer_sees_their_own_batches_soonest_expiry_first_with_warnings()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        await factory.SeedBatchAsync(shop.Dealer.Id, 3m, Today.AddDays(10));
        await factory.SeedBatchAsync(shop.Dealer.Id, 2m, Today);
        var other = await factory.SeedShopAsync();

        var page = await shop.Client.GetFromJsonAsync<JsonElement>("/api/inventory");

        var batches = page.Items();
        Assert.Equal(3, batches.Length);
        Assert.All(batches, b => Assert.Equal(shop.Dealer.Id, b.GetProperty("dealerId").GetGuid()));
        Assert.Equal(["Expired", "ExpiringSoon", "InDate"], batches.Select(b => b.GetProperty("expiryState").GetString()));
        Assert.Equal(10, batches[1].GetProperty("daysToExpiry").GetInt32());
        Assert.DoesNotContain(batches, b => b.GetProperty("id").GetGuid() == other.Batch.Id);
    }

    [Fact]
    public async Task The_expiring_before_filter_finds_what_needs_selling_first()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var soon = await factory.SeedBatchAsync(shop.Dealer.Id, 3m, Today.AddDays(10));

        var page = await shop.Client.GetFromJsonAsync<JsonElement>($"/api/inventory?expiringBefore={Today.AddDays(30):yyyy-MM-dd}");

        Assert.Equal(soon.Id, Assert.Single(page.Items()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Naming_another_dealer_s_shop_is_refused()
    {
        var shop = await factory.SeedShopAsync();
        var other = await factory.SeedShopAsync();

        var response = await shop.Client.GetAsync($"/api/inventory?dealerId={other.Dealer.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Receiving_a_delivery_adds_a_batch_and_a_repeat_is_refused()
    {
        var shop = await factory.SeedShopAsync();
        var delivery = new { productId = shop.Batch.ProductId, batchNo = $"NEW-{Guid.NewGuid():N}"[..12], expiryDate = Today.AddMonths(6), quantityOnHand = 12m, unitPrice = 2350m };

        var first = await shop.Client.PostAsJsonAsync("/api/inventory/batches", delivery);
        var again = await shop.Client.PostAsJsonAsync("/api/inventory/batches", delivery);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var batch = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(12m, batch.GetProperty("quantityAvailable").GetDecimal());
        Assert.Equal("InDate", batch.GetProperty("expiryState").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task An_already_expired_delivery_is_not_put_on_the_shelf()
    {
        var shop = await factory.SeedShopAsync();

        var response = await shop.Client.PostAsJsonAsync("/api/inventory/batches", new
        {
            productId = shop.Batch.ProductId,
            batchNo = "OLD-1",
            expiryDate = Today.AddDays(-1),
            quantityOnHand = 5m,
            unitPrice = 100m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_recount_cannot_make_held_stock_disappear()
    {
        var shop = await factory.SeedShopAsync(quantity: 10m);
        await shop.Client.HeldAsync(shop.Batch.ProductId, 4m);

        var tooLow = await shop.Client.PutAsJsonAsync($"/api/inventory/batches/{shop.Batch.Id}", new { expiryDate = shop.Batch.ExpiryDate, quantityOnHand = 3m, unitPrice = 2400m });
        var fine = await shop.Client.PutAsJsonAsync($"/api/inventory/batches/{shop.Batch.Id}", new { expiryDate = shop.Batch.ExpiryDate, quantityOnHand = 6m, unitPrice = 2500m });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLow.StatusCode);
        Assert.Equal("BELOW_RESERVED", await tooLow.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
        var batch = await fine.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2m, batch.GetProperty("quantityAvailable").GetDecimal());
        Assert.Equal(2500m, batch.GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public async Task A_dealer_cannot_change_another_shop_s_batch()
    {
        var shop = await factory.SeedShopAsync();
        var other = await factory.SeedShopAsync();

        var response = await shop.Client.PutAsJsonAsync($"/api/inventory/batches/{other.Batch.Id}", new { expiryDate = Today.AddYears(1), quantityOnHand = 0m, unitPrice = 1m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Farmer)]
    [InlineData(UserRole.FieldAgronomist)]
    [InlineData(UserRole.CoopAdministrator)]
    public async Task Inventory_and_orders_are_for_dealers_only(UserRole role)
    {
        var client = await factory.SignedInAsAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task A_dealer_without_a_shop_sees_an_empty_shelf_and_cannot_stock_it()
    {
        var dealer = await factory.SignedInAsAsync(UserRole.AgroDealer);

        var page = await dealer.GetFromJsonAsync<JsonElement>("/api/inventory");
        var create = await dealer.PostAsJsonAsync("/api/inventory/batches", new
        {
            productId = await factory.ProductIdAsync(),
            batchNo = "X-1",
            expiryDate = Today.AddYears(1),
            quantityOnHand = 1m,
            unitPrice = 1m
        });

        Assert.Empty(page.Items());
        Assert.Equal("NO_SHOP", await create.ProblemCodeAsync());
    }

    // ── Orders ───────────────────────────────────────────────────────────────

    /// <summary>An approved run: its order sits Confirmed at the run's dealer, who is signed in.</summary>
    private async Task<(HttpClient Dealer, Guid OrderId)> ApprovedOrderAsync()
    {
        var (dealer, _, orderId) = await ApprovedOrderWithFarmerAsync();
        return (dealer, orderId);
    }

    /// <summary>The same, with the farmer who owns the order signed in too.</summary>
    private async Task<(HttpClient Dealer, HttpClient Farmer, Guid OrderId)> ApprovedOrderWithFarmerAsync()
    {
        var run = await factory.RunAwaitingApprovalAsync();
        var decision = await run.Agronomist.DecideAsync(run.RunId, "Approve", key: Guid.NewGuid().ToString());
        decision.EnsureSuccessStatusCode();

        var owner = await factory.QueryAsync(db => db.Users.SingleAsync(u => u.Id == run.Dealer.UserId));
        var orderId = await factory.QueryAsync(db => db.InputOrders.Where(o => o.Prescription!.AgentRunId == run.RunId).Select(o => o.Id).SingleAsync());
        return (await factory.SignedInAsAsync(owner), run.Farmer, orderId);
    }

    private Task<string> PickupCodeAsync(Guid orderId) =>
        factory.QueryAsync(db => db.InputOrders.Where(o => o.Id == orderId).Select(o => o.PickupCode!).SingleAsync());

    private static Task<HttpResponseMessage> FulfilAsync(HttpClient dealer, Guid orderId, string status, string? pickupCode = null) =>
        dealer.PostAsJsonAsync($"/api/orders/{orderId}/fulfil", new { status, pickupCode });

    /// <summary>The farmer pays in cash at the counter, which the hand-over needs (PaymentTests covers paying).</summary>
    private static async Task PayCashAsync(HttpClient dealer, Guid orderId) =>
        (await dealer.PostAsync($"/api/orders/{orderId}/payments/cash", null)).EnsureSuccessStatusCode();

    [Fact]
    public async Task An_approved_prescription_s_order_reaches_the_dealer_confirmed()
    {
        var (dealer, orderId) = await ApprovedOrderAsync();

        var page = await dealer.GetFromJsonAsync<JsonElement>("/api/orders?status=Confirmed");

        var order = Assert.Single(page.Items());
        Assert.Equal(orderId, order.GetProperty("id").GetGuid());
        Assert.Equal("Packed", order.GetProperty("nextStatus").GetString());
        Assert.Matches(@"^RX-\d{4}-\d{6}$", order.GetProperty("prescriptionNo").GetString());
        var line = Assert.Single(order.GetProperty("lines").EnumerateArray());
        Assert.Equal("Mancozeb 80 WP", line.GetProperty("productName").GetString());
        Assert.Equal(2, line.GetProperty("packs").GetInt32());
    }

    [Fact]
    public async Task Fulfilment_goes_confirmed_packed_collected_and_stamps_each_step()
    {
        var (dealer, orderId) = await ApprovedOrderAsync();

        var packed = await FulfilAsync(dealer, orderId, "Packed");
        await PayCashAsync(dealer, orderId);
        var collected = await FulfilAsync(dealer, orderId, "Collected", await PickupCodeAsync(orderId));

        Assert.Equal(HttpStatusCode.OK, packed.StatusCode);
        var order = await collected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Collected", order.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("nextStatus").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, order.GetProperty("packedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, order.GetProperty("collectedAt").ValueKind);
    }

    [Fact]
    public async Task Only_the_farmer_s_pickup_code_hands_the_order_over()
    {
        var (dealer, _, orderId) = await ApprovedOrderWithFarmerAsync();
        await FulfilAsync(dealer, orderId, "Packed");
        await PayCashAsync(dealer, orderId);
        var code = await PickupCodeAsync(orderId);
        var wrong = code == "000000" ? "111111" : "000000";

        var none = await FulfilAsync(dealer, orderId, "Collected");
        var guessed = await FulfilAsync(dealer, orderId, "Collected", wrong);
        var right = await FulfilAsync(dealer, orderId, "Collected", $"{code[..3]} {code[3..]}");

        Assert.Equal("PICKUP_CODE_REQUIRED", await none.ProblemCodeAsync());
        Assert.Equal("WRONG_PICKUP_CODE", await guessed.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal("Collected", (await right.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_farmer_sees_their_own_order_where_to_collect_it_and_its_code()
    {
        var (dealer, farmer, orderId) = await ApprovedOrderWithFarmerAsync();
        var stranger = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        var mine = await farmer.GetFromJsonAsync<JsonElement>("/api/orders/mine");
        var theirs = await stranger.GetFromJsonAsync<JsonElement>("/api/orders/mine");
        var asDealer = await dealer.GetAsync("/api/orders/mine");
        var dealerView = await dealer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");

        var order = Assert.Single(mine.Items());
        Assert.Equal(orderId, order.GetProperty("id").GetGuid());
        Assert.Equal("Confirmed", order.GetProperty("status").GetString());
        Assert.Equal(await PickupCodeAsync(orderId), order.GetProperty("pickupCode").GetString());
        Assert.StartsWith("Shop ", order.GetProperty("shopName").GetString());
        Assert.Equal(2, order.GetProperty("lines")[0].GetProperty("packs").GetInt32());
        Assert.Empty(theirs.Items());
        Assert.Equal(HttpStatusCode.Forbidden, asDealer.StatusCode);
        // The dealer never sees the code: they have to ask the farmer for it.
        Assert.False(dealerView.TryGetProperty("pickupCode", out _));
    }

    [Fact]
    public async Task Once_collected_the_code_is_no_longer_shown()
    {
        var (dealer, farmer, orderId) = await ApprovedOrderWithFarmerAsync();
        await FulfilAsync(dealer, orderId, "Packed");
        await PayCashAsync(dealer, orderId);
        await FulfilAsync(dealer, orderId, "Collected", await PickupCodeAsync(orderId));

        var order = Assert.Single((await farmer.GetFromJsonAsync<JsonElement>("/api/orders/mine")).Items());

        Assert.Equal("Collected", order.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("pickupCode").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, order.GetProperty("collectedAt").ValueKind);
    }

    [Fact]
    public async Task A_repeated_click_is_harmless_and_a_skipped_step_is_refused()
    {
        var (dealer, orderId) = await ApprovedOrderAsync();

        var skip = await FulfilAsync(dealer, orderId, "Collected");
        await FulfilAsync(dealer, orderId, "Packed");
        var repeat = await FulfilAsync(dealer, orderId, "Packed");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, skip.StatusCode);
        Assert.Equal("ILLEGAL_ORDER_TRANSITION", await skip.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal("Packed", (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Fulfilment_only_moves_forward_to_packed_or_collected()
    {
        var (dealer, orderId) = await ApprovedOrderAsync();

        var response = await FulfilAsync(dealer, orderId, "Cancelled");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_dealer_cannot_see_or_fulfil_another_shop_s_order()
    {
        var (_, orderId) = await ApprovedOrderAsync();
        var other = await factory.SeedShopAsync();

        var list = await other.Client.GetFromJsonAsync<JsonElement>("/api/orders");
        var fulfil = await FulfilAsync(other.Client, orderId, "Packed");

        Assert.DoesNotContain(list.Items(), o => o.GetProperty("id").GetGuid() == orderId);
        Assert.Equal(HttpStatusCode.Forbidden, fulfil.StatusCode);
    }
}
