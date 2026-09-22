using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class FarmsEndpointsTests(AgriGuardApiFactory factory)
{
    [Fact]
    public async Task A_farmer_creates_reads_updates_and_deletes_their_own_farm()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var client = await factory.SignedInAsAsync(farmer);

        var created = await client.PostAsJsonAsync("/api/farms",
            new { name = "Green Acres", village = "Hatton", districtId = district });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var farm = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = farm.GetProperty("id").GetGuid();
        Assert.Equal("Green Acres", farm.GetProperty("name").GetString());
        Assert.Equal(farmer.FullName, farm.GetProperty("farmerName").GetString());
        Assert.Equal(0, farm.GetProperty("plotCount").GetInt32());
        // 201 must point at the new resource.
        Assert.Equal($"/api/farms/{id}", created.Headers.Location?.AbsolutePath);

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/farms/{id}");
        Assert.Equal("Hatton", fetched.GetProperty("village").GetString());

        var updated = await client.PutAsJsonAsync($"/api/farms/{id}",
            new { name = "Green Acres Upper", village = (string?)null, districtId = district });
        updated.EnsureSuccessStatusCode();
        Assert.Equal("Green Acres Upper", (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/farms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/farms/{id}")).StatusCode);
    }

    [Fact]
    public async Task Creating_a_farm_ignores_a_farmer_id_the_caller_tries_to_supply()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var someoneElse = await factory.CreateUserAsync(UserRole.Farmer);
        var client = await factory.SignedInAsAsync(farmer);

        var response = await client.PostAsJsonAsync("/api/farms",
            new { name = "Sneaky", districtId = district, farmerId = someoneElse.Id });

        response.EnsureSuccessStatusCode();
        var farm = await response.Content.ReadFromJsonAsync<JsonElement>();
        // The owner comes from the token, never from the body.
        Assert.Equal(farmer.Id, farm.GetProperty("farmerId").GetGuid());
    }

    [Fact]
    public async Task A_farmer_sees_only_their_own_farms_in_the_list()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var mine = await factory.CreateUserAsync(UserRole.Farmer);
        var theirs = await factory.CreateUserAsync(UserRole.Farmer);
        await factory.SeedFarmAsync(mine, district, "Mine A");
        await factory.SeedFarmAsync(mine, district, "Mine B");
        await factory.SeedFarmAsync(theirs, district, "Theirs");

        var client = await factory.SignedInAsAsync(mine);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/farms?pageSize=100");

        Assert.Equal(2, page.Total());
        Assert.All(page.Items(), f => Assert.Equal(mine.Id, f.GetProperty("farmerId").GetGuid()));
    }

    [Fact]
    public async Task Another_farmers_farm_is_403_not_404()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var owner = await factory.CreateUserAsync(UserRole.Farmer);
        var intruder = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(owner, district);

        var client = await factory.SignedInAsAsync(intruder);

        // 403 distinguishes "not yours" from "does not exist" (§12 test matrix). Ids are UUIDv7,
        // so admitting existence does not help anyone enumerate.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/farms/{farm.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/farms/{farm.Id}", new { name = "Hijacked", districtId = district })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/farms/{farm.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_agronomist_reads_their_district_but_cannot_write()
    {
        var (home, other) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var inDistrict = await factory.SeedFarmAsync(farmer, home, "In district");
        var outOfDistrict = await factory.SeedFarmAsync(farmer, other, "Out of district");

        var agronomist = await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: home);
        var client = await factory.SignedInAsAsync(agronomist);

        var page = await client.GetFromJsonAsync<JsonElement>("/api/farms?pageSize=100");
        var names = page.Items().Select(f => f.GetProperty("name").GetString()).ToList();
        Assert.Contains("In district", names);
        Assert.DoesNotContain("Out of district", names);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/farms/{inDistrict.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/farms/{outOfDistrict.Id}")).StatusCode);

        // Agronomists advise; they do not edit someone's registry.
        var update = await client.PutAsJsonAsync($"/api/farms/{inDistrict.Id}", new { name = "Renamed", districtId = home });
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    public async Task A_dealer_is_refused_by_the_policy_before_any_row_is_touched()
    {
        var dealer = await factory.CreateUserAsync(UserRole.AgroDealer);
        var client = await factory.SignedInAsAsync(dealer);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/farms")).StatusCode);
    }

    [Fact]
    public async Task An_administrator_registers_a_farm_on_behalf_of_a_farmer()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var admin = await factory.CreateUserAsync(UserRole.CoopAdministrator, withDistrict: false);
        var client = await factory.SignedInAsAsync(admin);

        var response = await client.PostAsJsonAsync("/api/farms",
            new { name = "Co-op managed", districtId = district, farmerId = farmer.Id });

        response.EnsureSuccessStatusCode();
        Assert.Equal(farmer.Id, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("farmerId").GetGuid());
    }

    [Fact]
    public async Task An_administrator_must_say_which_farmer_owns_the_farm()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var admin = await factory.CreateUserAsync(UserRole.CoopAdministrator, withDistrict: false);
        var client = await factory.SignedInAsAsync(admin);

        var response = await client.PostAsJsonAsync("/api/farms", new { name = "Ownerless", districtId = district });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("farmerId", out _));
    }

    [Fact]
    public async Task Validation_rejects_an_empty_name_with_a_field_error()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var client = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        var response = await client.PostAsJsonAsync("/api/farms", new { name = "   ", districtId = district });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Give the farm a name.",
            problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task A_duplicate_farm_name_for_the_same_farmer_is_409()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var client = await factory.SignedInAsAsync(farmer);
        await client.PostAsJsonAsync("/api/farms", new { name = "Home Field", districtId = district });

        var again = await client.PostAsJsonAsync("/api/farms", new { name = "Home Field", districtId = district });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task An_unknown_district_is_404_rather_than_a_constraint_error()
    {
        var client = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        var response = await client.PostAsJsonAsync("/api/farms",
            new { name = "Nowhere", districtId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_farm_with_plots_cannot_be_deleted()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district);
        await factory.SeedPlotAsync(farm.Id);
        var client = await factory.SignedInAsAsync(farmer);

        var response = await client.DeleteAsync($"/api/farms/{farm.Id}");

        // History is never cascaded away; the FK is Restrict by design.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("1 plot", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Listing_pages_sorts_and_searches()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        foreach (var name in new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" })
            await factory.SeedFarmAsync(farmer, district, name);
        var client = await factory.SignedInAsAsync(farmer);

        var firstPage = await client.GetFromJsonAsync<JsonElement>("/api/farms?page=1&pageSize=2&sortBy=name");
        Assert.Equal(5, firstPage.Total());
        Assert.Equal(3, firstPage.GetProperty("totalPages").GetInt32());
        Assert.True(firstPage.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal(["Alpha", "Bravo"], firstPage.Items().Select(f => f.GetProperty("name").GetString()));

        var secondPage = await client.GetFromJsonAsync<JsonElement>("/api/farms?page=2&pageSize=2&sortBy=name");
        Assert.Equal(["Charlie", "Delta"], secondPage.Items().Select(f => f.GetProperty("name").GetString()));

        var descending = await client.GetFromJsonAsync<JsonElement>("/api/farms?pageSize=1&sortBy=name&desc=true");
        Assert.Equal("Echo", descending.Items()[0].GetProperty("name").GetString());

        var searched = await client.GetFromJsonAsync<JsonElement>("/api/farms?search=harl");
        Assert.Equal("Charlie", Assert.Single(searched.Items()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_unknown_sort_field_falls_back_instead_of_failing()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        await factory.SeedFarmAsync(farmer, district, "Only one");
        var client = await factory.SignedInAsAsync(farmer);

        // A stale bookmark (or someone probing for a hidden column) should still return a page.
        var response = await client.GetAsync("/api/farms?sortBy=password_hash");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>()).Total());
    }

    [Fact]
    public async Task Page_size_is_capped_so_one_request_cannot_scan_the_table()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        await factory.SeedFarmAsync(farmer, district);
        var client = await factory.SignedInAsAsync(farmer);

        var page = await client.GetFromJsonAsync<JsonElement>("/api/farms?pageSize=99999");

        Assert.Equal(100, page.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task The_farm_summary_counts_plots_and_totals_their_area()
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        var farm = await factory.SeedFarmAsync(farmer, district);
        await factory.SeedPlotAsync(farm.Id, "P-01", 0.8m);
        await factory.SeedPlotAsync(farm.Id, "P-02", 1.25m);
        var client = await factory.SignedInAsAsync(farmer);

        var dto = await client.GetFromJsonAsync<JsonElement>($"/api/farms/{farm.Id}");

        Assert.Equal(2, dto.GetProperty("plotCount").GetInt32());
        Assert.Equal(2.05m, dto.GetProperty("totalAreaHectares").GetDecimal());
    }

    [Fact]
    public async Task Anonymous_requests_are_401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/farms")).StatusCode);
    }
}
