using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// The catalogue and the regulatory rules table: who may read and change them, and — the viva's
/// "modify a business rule" task — that a limit edited through the API changes the validator's
/// verdict on the very next check, with no code change.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CatalogueAndRulesTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => CaseFixtures.Today;

    private async Task<Guid> IngredientIdAsync(string name = "Mancozeb") =>
        await factory.QueryAsync(db => db.ActiveIngredients.Where(a => a.Name == name).Select(a => a.Id).FirstAsync());

    /// <summary>A brand-new product, so each test owns its rules and never disturbs the seeded ones.</summary>
    private async Task<JsonElement> CreateProductAsync(HttpClient admin, bool isActive = true)
    {
        var response = await admin.PostAsJsonAsync("/api/products", new
        {
            name = $"Test Fungicide {Guid.NewGuid():N}"[..30],
            manufacturer = "Test Chem",
            activeIngredientId = await IngredientIdAsync(),
            formulation = "WP",
            unit = "Kilogram",
            packSize = 1m,
            unitPrice = 1000m,
            isActive
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Limits(decimal min = 1.5m, decimal max = 2.5m, int phi = 7) => new
    {
        minDosePerHectare = min,
        maxDosePerHectare = max,
        preHarvestIntervalDays = phi,
        reEntryIntervalHours = 24,
        maxApplicationsPerCycle = 4,
        minDaysBetweenApplications = 7,
        rainfastHours = 4,
        isRestricted = false,
        isActive = true
    };

    private static object RuleFor(Guid productId, Guid cropId, decimal min = 1.5m, decimal max = 2.5m) => new
    {
        productId,
        cropId,
        minDosePerHectare = min,
        maxDosePerHectare = max,
        preHarvestIntervalDays = 7,
        reEntryIntervalHours = 24,
        maxApplicationsPerCycle = 4,
        minDaysBetweenApplications = 7,
        rainfastHours = 4,
        isRestricted = false
    };

    [Fact]
    public async Task Editing_a_limit_in_the_rules_table_changes_the_next_verdict_with_no_code_change()
    {
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        var product = (await CreateProductAsync(admin)).GetProperty("id").GetGuid();
        var created = await admin.PostAsJsonAsync("/api/product-crop-approvals", RuleFor(product, await factory.CropIdAsync("TOM")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ruleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var setup = await factory.SeedTomatoPlotAsync();
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        object proposal = new
        {
            cropCycleId = setup.Cycle.Id,
            productId = product,
            dosePerHectare = 2.0m,
            totalQuantity = 1.6m,
            sprayDate = Today.AddDays(1)
        };

        var before = await (await farmer.PostAsJsonAsync("/api/prescriptions/validate", proposal)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", before.GetProperty("outcome").GetString());

        // The administrator lowers the maximum dose to 1.8 kg/ha in /rules.
        var edit = await admin.PutAsJsonAsync($"/api/product-crop-approvals/{ruleId}", Limits(max: 1.8m));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var after = await (await farmer.PostAsJsonAsync("/api/prescriptions/validate", proposal)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Revise", after.GetProperty("outcome").GetString());
        var v3 = after.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("code").GetString() == "V3");
        Assert.Equal("Failed", v3.GetProperty("status").GetString());
        Assert.Contains("1.8", v3.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Deactivating_a_rule_withdraws_the_approval_on_the_next_check()
    {
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        var product = (await CreateProductAsync(admin)).GetProperty("id").GetGuid();
        var created = await admin.PostAsJsonAsync("/api/product-crop-approvals", RuleFor(product, await factory.CropIdAsync("TOM")));
        var ruleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var setup = await factory.SeedTomatoPlotAsync();
        var farmer = await factory.SignedInAsAsync(setup.Farmer);

        var limits = new
        {
            minDosePerHectare = 1.5m,
            maxDosePerHectare = 2.5m,
            preHarvestIntervalDays = 7,
            reEntryIntervalHours = 24,
            maxApplicationsPerCycle = 4,
            minDaysBetweenApplications = 7,
            rainfastHours = 4,
            isRestricted = false,
            isActive = false
        };
        await admin.PutAsJsonAsync($"/api/product-crop-approvals/{ruleId}", limits);

        var verdict = await (await farmer.PostAsJsonAsync("/api/prescriptions/validate", new
        {
            cropCycleId = setup.Cycle.Id,
            productId = product,
            dosePerHectare = 2.0m,
            totalQuantity = 1.6m,
            sprayDate = Today.AddDays(1)
        })).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Rejected", verdict.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task A_product_crop_pair_has_exactly_one_rule()
    {
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        var product = (await CreateProductAsync(admin)).GetProperty("id").GetGuid();
        var tomato = await factory.CropIdAsync("TOM");
        await admin.PostAsJsonAsync("/api/product-crop-approvals", RuleFor(product, tomato));

        var second = await admin.PostAsJsonAsync("/api/product-crop-approvals", RuleFor(product, tomato));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task A_nonsensical_limit_is_refused_before_it_reaches_the_table()
    {
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        var product = (await CreateProductAsync(admin)).GetProperty("id").GetGuid();

        var response = await admin.PostAsJsonAsync("/api/product-crop-approvals", RuleFor(product, await factory.CropIdAsync("TOM"), min: 3m, max: 2m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("maxDosePerHectare", out _));
    }

    [Fact]
    public async Task The_rules_table_lists_with_filters_and_names()
    {
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        var mancozeb = await factory.ProductIdAsync();

        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/product-crop-approvals?productId={mancozeb}&cropId={await factory.CropIdAsync("TOM")}");

        var rule = Assert.Single(page.Items());
        Assert.Equal("Mancozeb 80 WP", rule.GetProperty("productName").GetString());
        Assert.Equal("Tomato", rule.GetProperty("cropName").GetString());
        Assert.Equal(7, rule.GetProperty("preHarvestIntervalDays").GetInt32());
    }

    [Theory]
    [InlineData(UserRole.Farmer)]
    [InlineData(UserRole.FieldAgronomist)]
    [InlineData(UserRole.AgroDealer)]
    public async Task Only_an_administrator_can_see_or_change_the_rules(UserRole role)
    {
        var client = await factory.SignedInAsAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/product-crop-approvals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/product-crop-approvals", RuleFor(Guid.NewGuid(), Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Products_are_searchable_and_filterable_by_crop_and_ingredient()
    {
        var farmer = await factory.SignedInAsAsync(UserRole.Farmer);
        var tomato = await factory.CropIdAsync("TOM");

        var bySearch = await farmer.GetFromJsonAsync<JsonElement>("/api/products?search=mancozeb");
        var byCrop = await farmer.GetFromJsonAsync<JsonElement>($"/api/products?cropId={tomato}&pageSize=100");
        var byIngredient = await farmer.GetFromJsonAsync<JsonElement>($"/api/products?activeIngredientId={await IngredientIdAsync()}");

        Assert.Contains(bySearch.Items(), p => p.GetProperty("name").GetString() == "Mancozeb 80 WP");
        Assert.All(bySearch.Items(), p => Assert.Contains("ancozeb", p.GetProperty("name").GetString() + p.GetProperty("activeIngredientName").GetString()));
        Assert.Contains(byCrop.Items(), p => p.GetProperty("name").GetString() == "Mancozeb 80 WP");
        Assert.All(byCrop.Items(), p => Assert.True(p.GetProperty("approvedCropCount").GetInt32() >= 1));
        Assert.All(byIngredient.Items(), p => Assert.Equal("Mancozeb", p.GetProperty("activeIngredientName").GetString()));
    }

    [Fact]
    public async Task Products_sort_by_price_on_the_server()
    {
        var farmer = await factory.SignedInAsAsync(UserRole.Farmer);

        var page = await farmer.GetFromJsonAsync<JsonElement>("/api/products?sortBy=unitPrice&desc=true&pageSize=10");

        var prices = page.Items().Select(p => p.GetProperty("unitPrice").GetDecimal()).ToList();
        Assert.Equal(prices.OrderByDescending(p => p), prices);
    }

    [Fact]
    public async Task Withdrawn_products_are_hidden_unless_asked_for()
    {
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        var withdrawn = await CreateProductAsync(admin, isActive: false);
        var name = withdrawn.GetProperty("name").GetString();

        var hidden = await admin.GetFromJsonAsync<JsonElement>($"/api/products?search={name}");
        var shown = await admin.GetFromJsonAsync<JsonElement>($"/api/products?search={name}&includeInactive=true");

        Assert.Empty(hidden.Items());
        Assert.Single(shown.Items());
    }

    [Fact]
    public async Task Only_an_administrator_changes_the_catalogue()
    {
        var dealer = await factory.SignedInAsAsync(UserRole.AgroDealer);

        var response = await dealer.PostAsJsonAsync("/api/products", new
        {
            name = "Sneaky 50 WP",
            activeIngredientId = await IngredientIdAsync(),
            formulation = "WP",
            unit = "Kilogram",
            packSize = 1m,
            unitPrice = 1m
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_product_with_history_cannot_be_deleted_only_withdrawn()
    {
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        var mancozeb = await factory.ProductIdAsync();
        var unused = (await CreateProductAsync(admin)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/products/{mancozeb}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{unused}")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_product_names_are_refused()
    {
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);

        var response = await admin.PostAsJsonAsync("/api/products", new
        {
            name = "Mancozeb 80 WP",
            activeIngredientId = await IngredientIdAsync(),
            formulation = "WP",
            unit = "Kilogram",
            packSize = 1m,
            unitPrice = 1m
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
