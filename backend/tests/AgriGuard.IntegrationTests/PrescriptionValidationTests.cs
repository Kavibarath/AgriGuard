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
/// The validator against the real seeded rules table, including the golden cases from §12 that
/// the viva demo walks through.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PrescriptionValidationTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(HttpClient Client, CropCycle Cycle, Plot Plot)> SetupAsync(
        string cropCode = "TOM",
        DateOnly? plannedHarvest = null,
        decimal areaHectares = 0.8m,
        decimal? creditLimit = null)
    {
        var (district, _) = await factory.TwoDistrictsAsync();
        var farmer = await factory.CreateUserAsync(UserRole.Farmer);
        if (creditLimit is not null)
            await factory.QueryAsync(async db =>
            {
                var user = await db.Users.FirstAsync(u => u.Id == farmer.Id);
                user.CreditLimit = creditLimit;
                await db.SaveChangesAsync();
                return true;
            });

        var farm = await factory.SeedFarmAsync(farmer, district, $"Farm {Guid.NewGuid():N}");
        var plot = await factory.SeedPlotAsync(farm.Id, areaHectares: areaHectares);
        var cropId = await factory.CropIdAsync(cropCode);

        var cycle = await factory.QueryAsync(async db =>
        {
            var created = new CropCycle
            {
                PlotId = plot.Id,
                CropId = cropId,
                SownDate = Today.AddDays(-60),
                ExpectedHarvestDate = Today.AddDays(50),
                PlannedHarvestDate = plannedHarvest,
                Stage = CropStage.Flowering,
                Status = CropCycleStatus.Active
            };
            db.CropCycles.Add(created);
            await db.SaveChangesAsync();
            return created;
        });

        return (await factory.SignedInAsAsync(farmer), cycle, plot);
    }

    private Task<Guid> ProductIdAsync(string name) =>
        factory.QueryAsync(db => db.Products.Where(p => p.Name == name).Select(p => p.Id).FirstAsync());

    private Task SeedApplicationAsync(Guid cycleId, Guid productId, DateOnly appliedOn) =>
        factory.QueryAsync(async db =>
        {
            db.ChemicalApplications.Add(new ChemicalApplication
            {
                CropCycleId = cycleId,
                ProductId = productId,
                ApplicationDate = appliedOn,
                DosePerHectare = 2m,
                TotalQuantity = 1.6m,
                Status = ApplicationStatus.Applied
            });
            await db.SaveChangesAsync();
            return true;
        });

    private static JsonElement RuleOf(JsonElement verdict, string code) =>
        verdict.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("code").GetString() == code);

    private static async Task<JsonElement> ValidateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/prescriptions/validate", request);
        // A rejection is a successful validation with a negative answer, never an HTTP error.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_compliant_proposal_is_approved_against_the_real_rules_table()
    {
        var (client, cycle, _) = await SetupAsync();
        var mancozeb = await ProductIdAsync("Mancozeb 80 WP");

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = mancozeb,
            dosePerHectare = 2.0m,     // seeded range on tomato is 1.5–2.5
            totalQuantity = 1.6m,      // 2.0 × 0.8 ha
            sprayDate = Today.AddDays(2)
        });

        Assert.Equal("Approved", verdict.GetProperty("outcome").GetString());
        Assert.Equal("Mancozeb 80 WP", verdict.GetProperty("productName").GetString());
        Assert.Equal(11, verdict.GetProperty("results").GetArrayLength());
        // Cost is priced in whole packs: 1.6 kg of a 1 kg pack = 2 packs at 2400.
        Assert.Equal(4800m, verdict.GetProperty("estimatedCost").GetDecimal());
    }

    [Fact]
    public async Task Golden_case_G2_harvest_in_five_days_with_a_fourteen_day_interval_is_rejected()
    {
        var (client, cycle, _) = await SetupAsync(plannedHarvest: Today.AddDays(5));
        var metalaxyl = await ProductIdAsync("Metalaxyl 25 WP");  // PHI 14 on tomato

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = metalaxyl,
            dosePerHectare = 1.0m,
            totalQuantity = 0.8m,
            sprayDate = Today.AddDays(1)
        });

        Assert.Equal("Rejected", verdict.GetProperty("outcome").GetString());
        var v5 = RuleOf(verdict, "V5");
        Assert.Equal("Failed", v5.GetProperty("status").GetString());
        Assert.Equal("Reject", v5.GetProperty("severity").GetString());
        Assert.Contains("needs 14 days", v5.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Golden_case_G5_a_withdrawn_product_is_rejected()
    {
        var (client, cycle, _) = await SetupAsync();
        var carbofuran = await ProductIdAsync("Carbofuran 3 GR");  // approval seeded inactive

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = carbofuran,
            dosePerHectare = 25m,
            totalQuantity = 20m,
            sprayDate = Today.AddDays(1)
        });

        Assert.Equal("Rejected", verdict.GetProperty("outcome").GetString());
        Assert.Contains("withdrawn", RuleOf(verdict, "V2").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Golden_case_G10_the_seasonal_limit_is_enforced_from_recorded_history()
    {
        var (client, cycle, _) = await SetupAsync();
        var azoxystrobin = await ProductIdAsync("Azoxystrobin 25 SC");  // max 2 per cycle
        await SeedApplicationAsync(cycle.Id, azoxystrobin, Today.AddDays(-40));
        await SeedApplicationAsync(cycle.Id, azoxystrobin, Today.AddDays(-20));

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = azoxystrobin,
            dosePerHectare = 0.6m,
            totalQuantity = 0.48m,
            sprayDate = Today.AddDays(1)
        });

        Assert.Equal("Rejected", verdict.GetProperty("outcome").GetString());
        Assert.Contains("used=2; max=2", RuleOf(verdict, "V6").GetProperty("evidence").GetString());
    }

    [Fact]
    public async Task A_product_not_approved_for_the_crop_is_rejected()
    {
        var (client, cycle, _) = await SetupAsync();
        var tricyclazole = await ProductIdAsync("Tricyclazole 75 WP");  // paddy only

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = tricyclazole,
            dosePerHectare = 0.4m,
            totalQuantity = 0.32m,
            sprayDate = Today.AddDays(1)
        });

        Assert.Equal("Rejected", verdict.GetProperty("outcome").GetString());
        Assert.Contains("not approved for Tomato", RuleOf(verdict, "V2").GetProperty("message").GetString());

        // The rules that needed that approval row say so rather than passing quietly.
        var notEvaluated = verdict.GetProperty("results").EnumerateArray()
            .Where(r => r.GetProperty("status").GetString() == "NotEvaluated")
            .Select(r => r.GetProperty("code").GetString());
        Assert.Contains("V5", notEvaluated);
    }

    [Fact]
    public async Task An_overdose_asks_for_a_revision_rather_than_rejecting()
    {
        var (client, cycle, _) = await SetupAsync();
        var mancozeb = await ProductIdAsync("Mancozeb 80 WP");

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = mancozeb,
            dosePerHectare = 20m,      // ten times the label maximum
            totalQuantity = 16m,
            sprayDate = Today.AddDays(2)
        });

        // Recoverable: the Action agent can propose a corrected dose.
        Assert.Equal("Revise", verdict.GetProperty("outcome").GetString());
        Assert.Equal("Failed", RuleOf(verdict, "V3").GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_quantity_computed_for_the_wrong_area_is_caught()
    {
        var (client, cycle, _) = await SetupAsync(areaHectares: 0.8m);
        var mancozeb = await ProductIdAsync("Mancozeb 80 WP");

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = mancozeb,
            dosePerHectare = 2.0m,
            totalQuantity = 16m,       // computed as if the plot were 8 ha
            sprayDate = Today.AddDays(2)
        });

        Assert.Equal("Revise", verdict.GetProperty("outcome").GetString());
        Assert.Contains("expected about 1.6", RuleOf(verdict, "V4").GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_restricted_product_is_rejected_while_no_permit_can_be_verified()
    {
        var (client, cycle, _) = await SetupAsync(cropCode: "CHI");
        var fipronil = await ProductIdAsync("Fipronil 5 SC");  // restricted on chilli

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = fipronil,
            dosePerHectare = 0.9m,
            totalQuantity = 0.72m,
            sprayDate = Today.AddDays(2)
        });

        Assert.Equal("Rejected", verdict.GetProperty("outcome").GetString());
        Assert.Contains("needs a permit", RuleOf(verdict, "V10").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Weather_and_stock_are_judged_when_supplied_and_skipped_when_not()
    {
        var (client, cycle, _) = await SetupAsync();
        var mancozeb = await ProductIdAsync("Mancozeb 80 WP");

        var withoutData = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = mancozeb,
            dosePerHectare = 2.0m,
            totalQuantity = 1.6m,
            sprayDate = Today.AddDays(2)
        });
        Assert.Equal("NotEvaluated", RuleOf(withoutData, "V8").GetProperty("status").GetString());
        Assert.Equal("NotEvaluated", RuleOf(withoutData, "V9").GetProperty("status").GetString());

        var withRain = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = mancozeb,
            dosePerHectare = 2.0m,
            totalQuantity = 1.6m,
            sprayDate = Today.AddDays(2),
            weather = new { rainProbabilityPercent = 80, windSpeedKph = 6m, temperatureC = 27m },
            stock = new { availableQuantity = 10m, earliestBatchExpiry = Today.AddMonths(8) }
        });

        Assert.Equal("Revise", withRain.GetProperty("outcome").GetString());
        Assert.Equal("Failed", RuleOf(withRain, "V8").GetProperty("status").GetString());
        Assert.Equal("Passed", RuleOf(withRain, "V9").GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_credit_limit_is_read_from_the_farmers_record()
    {
        var (client, cycle, _) = await SetupAsync(creditLimit: 1_000m);
        var mancozeb = await ProductIdAsync("Mancozeb 80 WP");

        var verdict = await ValidateAsync(client, new
        {
            cropCycleId = cycle.Id,
            productId = mancozeb,
            dosePerHectare = 2.0m,
            totalQuantity = 1.6m,
            sprayDate = Today.AddDays(2)
        });

        // 2 packs at 2400 = 4800, over the 1000 limit.
        Assert.Equal("Revise", verdict.GetProperty("outcome").GetString());
        Assert.Contains("exceeds the farmer's credit limit", RuleOf(verdict, "V11").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Editing_the_rules_table_changes_the_verdict_without_a_code_change()
    {
        // This is the viva's "modify a business rule live" task: tighten the PHI in the
        // regulatory table and the same proposal flips from approved to rejected.
        var (client, cycle, _) = await SetupAsync(plannedHarvest: Today.AddDays(10));
        var mancozeb = await ProductIdAsync("Mancozeb 80 WP");
        object proposal = new
        {
            cropCycleId = cycle.Id,
            productId = mancozeb,
            dosePerHectare = 2.0m,
            totalQuantity = 1.6m,
            sprayDate = Today.AddDays(1)
        };

        Assert.Equal("Approved", (await ValidateAsync(client, proposal)).GetProperty("outcome").GetString());

        await factory.QueryAsync(async db =>
        {
            var approval = await db.ProductCropApprovals
                .FirstAsync(a => a.ProductId == mancozeb && a.Crop.Code == "TOM");
            approval.PreHarvestIntervalDays = 21;
            await db.SaveChangesAsync();
            return true;
        });

        var after = await ValidateAsync(client, proposal);
        Assert.Equal("Rejected", after.GetProperty("outcome").GetString());
        Assert.Contains("needs 21 days", RuleOf(after, "V5").GetProperty("message").GetString());

        // Restore the seeded value so the shared database stays as other tests expect it.
        await factory.QueryAsync(async db =>
        {
            var approval = await db.ProductCropApprovals
                .FirstAsync(a => a.ProductId == mancozeb && a.Crop.Code == "TOM");
            approval.PreHarvestIntervalDays = 7;
            await db.SaveChangesAsync();
            return true;
        });
    }

    [Fact]
    public async Task An_inactive_cycle_cannot_be_prescribed_for()
    {
        var (client, cycle, _) = await SetupAsync();
        await factory.QueryAsync(async db =>
        {
            var stored = await db.CropCycles.FirstAsync(c => c.Id == cycle.Id);
            stored.Status = CropCycleStatus.Harvested;
            await db.SaveChangesAsync();
            return true;
        });

        var response = await client.PostAsJsonAsync("/api/prescriptions/validate", new
        {
            cropCycleId = cycle.Id,
            productId = await ProductIdAsync("Mancozeb 80 WP"),
            dosePerHectare = 2.0m,
            totalQuantity = 1.6m,
            sprayDate = Today.AddDays(2)
        });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [Fact]
    public async Task Validation_requires_authentication()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/prescriptions/validate", new
        {
            cropCycleId = Guid.NewGuid(),
            productId = Guid.NewGuid(),
            dosePerHectare = 1m,
            totalQuantity = 1m,
            sprayDate = Today
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
