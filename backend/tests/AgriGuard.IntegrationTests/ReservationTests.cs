using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Inventory;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// Holds on stock (§5.1): hold, commit, release and expiry — each one serializable transaction with
/// the batch rows locked — and that two holds racing for the last packs never oversell.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReservationTests(AgriGuardApiFactory factory)
{
    [Fact]
    public async Task A_hold_keeps_whole_packs_off_sale_for_24_hours_without_moving_them()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);

        var response = await shop.Client.HoldAsync(shop.Batch.ProductId, 2.4m, note: "Phone order, Mr Silva");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var hold = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Held", hold.GetProperty("status").GetString());
        // 2.4 kg of a 1 kg pack is 3 packs: whole packs are what is held.
        Assert.Equal(3, hold.GetProperty("packs").GetInt32());
        Assert.Equal(3m, hold.GetProperty("totalQuantity").GetDecimal());
        Assert.Equal("Phone order, Mr Silva", hold.GetProperty("note").GetString());
        var lifetime = hold.GetProperty("expiresAt").GetDateTime() - hold.GetProperty("createdAt").GetDateTime();
        Assert.InRange(lifetime.TotalHours, 23.9, 24.1);

        // Still on the shelf, but no longer available to anyone else.
        Assert.Equal((50m, 3m), await factory.BatchLevelsAsync(shop.Batch.Id));
    }

    [Fact]
    public async Task A_hold_draws_the_batch_closest_to_expiry_first()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var old = await factory.SeedBatchAsync(shop.Dealer.Id, 2m, CaseFixtures.Today.AddDays(20));

        var hold = await (await shop.Client.HoldAsync(shop.Batch.ProductId, 3m)).Content.ReadFromJsonAsync<JsonElement>();

        var lines = hold.GetProperty("lines").EnumerateArray()
            .ToDictionary(l => l.GetProperty("batchId").GetGuid(), l => l.GetProperty("quantity").GetDecimal());
        Assert.Equal(2m, lines[old.Id]);
        Assert.Equal(1m, lines[shop.Batch.Id]);
    }

    [Fact]
    public async Task Stock_expiring_before_the_day_it_is_needed_is_not_held()
    {
        var shop = await factory.SeedShopAsync(quantity: 1m);
        await factory.SeedBatchAsync(shop.Dealer.Id, 10m, CaseFixtures.Today.AddDays(5));

        var response = await shop.Client.HoldAsync(shop.Batch.ProductId, 5m, usableOn: CaseFixtures.Today.AddDays(7));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("INSUFFICIENT_STOCK", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Holding_more_than_is_available_is_refused_and_changes_nothing()
    {
        var shop = await factory.SeedShopAsync(quantity: 5m);
        await shop.Client.HeldAsync(shop.Batch.ProductId, 4m);

        var response = await shop.Client.HoldAsync(shop.Batch.ProductId, 2m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("INSUFFICIENT_STOCK", await response.ProblemCodeAsync());
        Assert.Equal((5m, 4m), await factory.BatchLevelsAsync(shop.Batch.Id));
    }

    [Fact]
    public async Task Two_holds_racing_for_the_last_packs_never_oversell()
    {
        var shop = await factory.SeedShopAsync(quantity: 3m);

        // Each wants 2 of the 3 packs: only one can have them.
        var responses = await Task.WhenAll(
            shop.Client.HoldAsync(shop.Batch.ProductId, 2m),
            shop.Client.HoldAsync(shop.Batch.ProductId, 2m));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity],
            responses.Select(r => r.StatusCode).OrderBy(s => (int)s));
        Assert.Equal((3m, 2m), await factory.BatchLevelsAsync(shop.Batch.Id));
    }

    [Fact]
    public async Task Committing_a_hold_takes_exactly_the_held_stock_off_the_shelf()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var id = await shop.Client.HeldAsync(shop.Batch.ProductId, 3m);

        var response = await shop.Client.PostAsync($"/api/inventory/reservations/{id}/commit", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Committed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal((47m, 0m), await factory.BatchLevelsAsync(shop.Batch.Id));
    }

    [Fact]
    public async Task Releasing_a_hold_puts_the_stock_back_on_sale()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var id = await shop.Client.HeldAsync(shop.Batch.ProductId, 3m);

        var response = await shop.Client.PostAsync($"/api/inventory/reservations/{id}/release", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Released", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal((50m, 0m), await factory.BatchLevelsAsync(shop.Batch.Id));
    }

    [Fact]
    public async Task A_resolved_hold_cannot_be_committed_or_released_again()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var id = await shop.Client.HeldAsync(shop.Batch.ProductId, 3m);
        await shop.Client.PostAsync($"/api/inventory/reservations/{id}/commit", null);

        var again = await shop.Client.PostAsync($"/api/inventory/reservations/{id}/commit", null);
        var release = await shop.Client.PostAsync($"/api/inventory/reservations/{id}/release", null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, release.StatusCode);
        Assert.Equal((47m, 0m), await factory.BatchLevelsAsync(shop.Batch.Id));
    }

    [Fact]
    public async Task A_commit_and_a_release_racing_on_one_hold_resolve_it_once()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var id = await shop.Client.HeldAsync(shop.Batch.ProductId, 3m);

        var responses = await Task.WhenAll(
            shop.Client.PostAsync($"/api/inventory/reservations/{id}/commit", null),
            shop.Client.PostAsync($"/api/inventory/reservations/{id}/release", null));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).OrderBy(s => (int)s));
        var levels = await factory.BatchLevelsAsync(shop.Batch.Id);
        // Either committed (47, 0) or released (50, 0) — never both, never a hold left behind.
        Assert.Contains(levels, new[] { (47m, 0m), (50m, 0m) });
    }

    [Fact]
    public async Task An_expired_hold_cannot_be_committed_and_the_sweeper_puts_it_back_on_sale()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var id = await shop.Client.HeldAsync(shop.Batch.ProductId, 3m);
        await factory.QueryAsync(db => db.StockReservations.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        var commit = await shop.Client.PostAsync($"/api/inventory/reservations/{id}/commit", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, commit.StatusCode);
        Assert.Equal("RESERVATION_EXPIRED", await commit.ProblemCodeAsync());

        var released = await factory.Services.GetRequiredService<ReservationExpirySweeper>().SweepAsync(CancellationToken.None);

        Assert.True(released >= 1);
        Assert.Equal(ReservationStatus.Expired, await factory.QueryAsync(db => db.StockReservations.Where(r => r.Id == id).Select(r => r.Status).SingleAsync()));
        Assert.Equal((50m, 0m), await factory.BatchLevelsAsync(shop.Batch.Id));
    }

    [Fact]
    public async Task The_sweeper_leaves_live_holds_alone()
    {
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var id = await shop.Client.HeldAsync(shop.Batch.ProductId, 3m);

        await factory.Services.GetRequiredService<ReservationExpirySweeper>().SweepAsync(CancellationToken.None);

        Assert.Equal(ReservationStatus.Held, await factory.QueryAsync(db => db.StockReservations.Where(r => r.Id == id).Select(r => r.Status).SingleAsync()));
        Assert.Equal((50m, 3m), await factory.BatchLevelsAsync(shop.Batch.Id));
    }

    [Fact]
    public async Task Held_stock_is_invisible_to_the_agent_s_stock_check()
    {
        var shop = await factory.SeedShopAsync(quantity: 5m);
        await shop.Client.HeldAsync(shop.Batch.ProductId, 4m);

        var stock = await factory.AgentClient().GetFromJsonAsync<JsonElement>(
            $"/internal/tools/stock-availability?productId={shop.Batch.ProductId}&districtId={shop.Dealer.DistrictId}");

        var ours = stock.GetProperty("dealers").EnumerateArray().Single(d => d.GetProperty("dealerId").GetGuid() == shop.Dealer.Id);
        Assert.Equal(1m, ours.GetProperty("availableQuantity").GetDecimal());
    }

    [Fact]
    public async Task A_hold_made_for_an_agent_proposal_is_not_the_dealer_s_to_resolve()
    {
        var run = await factory.RunAwaitingApprovalAsync();
        var shop = await factory.SeedShopAsync(quantity: 50m);
        var id = await shop.Client.HeldAsync(shop.Batch.ProductId, 3m);
        await factory.QueryAsync(db => db.StockReservations.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AgentRunId, run.RunId)));

        var response = await shop.Client.PostAsync($"/api/inventory/reservations/{id}/release", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("HELD_FOR_APPROVAL", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_dealer_sees_and_resolves_only_their_own_holds()
    {
        var mine = await factory.SeedShopAsync();
        var theirs = await factory.SeedShopAsync();
        var id = await theirs.Client.HeldAsync(theirs.Batch.ProductId, 1m);

        var list = await mine.Client.GetFromJsonAsync<JsonElement>("/api/inventory/reservations");
        var commit = await mine.Client.PostAsync($"/api/inventory/reservations/{id}/commit", null);

        Assert.DoesNotContain(list.Items(), r => r.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.Forbidden, commit.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Farmer)]
    [InlineData(UserRole.FieldAgronomist)]
    [InlineData(UserRole.CoopAdministrator)]
    public async Task Only_a_dealer_can_hold_stock(UserRole role)
    {
        var client = await factory.SignedInAsAsync(role);
        var productId = await factory.ProductIdAsync();

        var response = await client.HoldAsync(productId, 1m);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_hold_needs_a_positive_quantity()
    {
        var shop = await factory.SeedShopAsync();

        var response = await shop.Client.HoldAsync(shop.Batch.ProductId, 0m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
