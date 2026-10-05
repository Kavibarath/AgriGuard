using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// Component D's harvest side: ranked harvest days (maturity ∩ pre-harvest intervals ∩ weather),
/// harvest forecasts, and capacity-constrained collection bookings that can never overbook.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HarvestAndCollectionTests(AgriGuardApiFactory factory)
{
    private static DateOnly Today => TestCalendar.Today;

    /// <summary>A tomato cycle maturing in <paramref name="daysToMaturity"/> days, and its signed-in farmer.</summary>
    private async Task<(CaseFixtures.FarmSetup Setup, HttpClient Farmer)> CropAsync(int daysToMaturity = 3)
    {
        var setup = await factory.SeedTomatoPlotAsync();
        await factory.QueryAsync(db => db.CropCycles.Where(c => c.Id == setup.Cycle.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpectedHarvestDate, Today.AddDays(daysToMaturity))));
        return (setup, await factory.SignedInAsAsync(setup.Farmer));
    }

    private Task SprayAsync(Guid cycleId, DateOnly on, string product = "Mancozeb 80 WP") =>
        factory.QueryAsync(async db =>
        {
            db.ChemicalApplications.Add(new ChemicalApplication
            {
                CropCycleId = cycleId,
                ProductId = await db.Products.Where(p => p.Name == product).Select(p => p.Id).FirstAsync(),
                ApplicationDate = on,
                DosePerHectare = 2m,
                TotalQuantity = 1.6m,
                Status = ApplicationStatus.Applied
            });
            return await db.SaveChangesAsync();
        });

    /// <summary>A centre near the demo plot with one slot per given day, on dates no other test uses.</summary>
    private Task<CollectionCentre> CentreAsync(Guid districtId, IEnumerable<(DateOnly Day, decimal Capacity)> slots, decimal lat = 6.95m, decimal lon = 80.79m) =>
        factory.QueryAsync(async db =>
        {
            var centre = new CollectionCentre
            {
                Name = $"Centre {Guid.NewGuid():N}"[..20],
                DistrictId = districtId,
                Latitude = lat,
                Longitude = lon,
                DailyCapacityKg = 10_000m
            };
            db.CollectionCentres.Add(centre);
            foreach (var (day, capacity) in slots)
                db.CollectionSlots.Add(new CollectionSlot
                {
                    Centre = centre,
                    SlotDate = day,
                    SlotIndex = 1,
                    StartTime = new TimeOnly(7, 0),
                    EndTime = new TimeOnly(9, 0),
                    CapacityKg = capacity
                });
            await db.SaveChangesAsync();
            return centre;
        });

    /// <summary>Dates far enough out, and random enough, that no other test's slots share them.</summary>
    private static DateOnly FreshDay() => Today.AddDays(Random.Shared.Next(400, 4000));

    private static Task<HttpResponseMessage> AllocateAsync(HttpClient client, Guid cycleId, decimal kg, DateOnly preferred, Guid? centreId = null) =>
        client.PostAsJsonAsync("/api/collection-bookings/allocate", new { cropCycleId = cycleId, quantityKg = kg, preferredDate = preferred, centreId });

    private Task<decimal> BookedKgAsync(Guid centreId, DateOnly day) =>
        factory.QueryAsync(db => db.CollectionSlots.Where(s => s.CentreId == centreId && s.SlotDate == day).SumAsync(s => s.BookedKg));

    // ── Harvest windows ──────────────────────────────────────────────────────

    [Fact]
    public async Task Harvest_days_are_ranked_around_maturity_and_weather()
    {
        var (setup, farmer) = await CropAsync(daysToMaturity: 3);

        await factory.ChangeWeatherAsync(w => w.Set(Today.AddDays(3), rain: 90), async () =>
        {
            var window = await farmer.GetFromJsonAsync<JsonElement>($"/api/harvest-windows/{setup.Cycle.Id}");

            Assert.Equal(Today.AddDays(3), window.GetProperty("maturityDate").Deserialize<DateOnly>());
            var days = window.GetProperty("days").EnumerateArray().ToList();
            // Maturity itself is forecast wet, so the dry day after it ranks first.
            Assert.Equal(Today.AddDays(4), days[0].GetProperty("date").Deserialize<DateOnly>());
            Assert.True(days[0].GetProperty("recommended").GetBoolean());
            var wet = days.Single(d => d.GetProperty("date").Deserialize<DateOnly>() == Today.AddDays(3));
            Assert.Equal(60, wet.GetProperty("score").GetInt32());
            // However close to maturity, a wet day is never recommended.
            Assert.False(wet.GetProperty("recommended").GetBoolean());
            Assert.StartsWith("Best:", window.GetProperty("summary").GetString());
        });
    }

    [Fact]
    public async Task No_harvest_day_is_offered_before_the_pre_harvest_interval_clears()
    {
        var (setup, farmer) = await CropAsync(daysToMaturity: 3);
        // Mancozeb on tomato: 7-day interval. Sprayed today → safe from Today + 7.
        await SprayAsync(setup.Cycle.Id, Today);

        var window = await farmer.GetFromJsonAsync<JsonElement>($"/api/harvest-windows/{setup.Cycle.Id}");

        Assert.Equal(Today.AddDays(7), window.GetProperty("safeFromDate").Deserialize<DateOnly>());
        Assert.Contains("Mancozeb 80 WP", window.GetProperty("safetyReason").GetString());
        Assert.All(window.GetProperty("days").EnumerateArray(),
            d => Assert.True(d.GetProperty("date").Deserialize<DateOnly>() >= Today.AddDays(7)));
    }

    [Fact]
    public async Task Another_farmer_cannot_see_my_harvest_window()
    {
        var (setup, _) = await CropAsync();
        var stranger = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/harvest-windows/{setup.Cycle.Id}")).StatusCode);
    }

    // ── Harvest forecasts ────────────────────────────────────────────────────

    [Fact]
    public async Task A_farmer_records_a_forecast_and_later_the_actual_yield()
    {
        var (setup, farmer) = await CropAsync();

        var created = await farmer.PostAsJsonAsync("/api/harvest-forecasts",
            new { cropCycleId = setup.Cycle.Id, forecastHarvestDate = Today.AddDays(3), estimatedYieldKg = 2400m, notes = "Good fruit set." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var forecast = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Farmer", forecast.GetProperty("source").GetString());

        var actual = await farmer.PutAsJsonAsync($"/api/harvest-forecasts/{forecast.GetProperty("id").GetGuid()}/actual", new { actualYieldKg = 2150m });
        var list = await farmer.GetFromJsonAsync<JsonElement>($"/api/harvest-forecasts?cropCycleId={setup.Cycle.Id}");

        Assert.Equal(HttpStatusCode.OK, actual.StatusCode);
        Assert.Equal(2150m, Assert.Single(list.Items()).GetProperty("actualYieldKg").GetDecimal());
    }

    [Fact]
    public async Task An_agronomist_s_forecast_is_marked_as_theirs()
    {
        var (setup, _) = await CropAsync();
        var agronomist = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: setup.DistrictId));

        var created = await agronomist.PostAsJsonAsync("/api/harvest-forecasts",
            new { cropCycleId = setup.Cycle.Id, forecastHarvestDate = Today.AddDays(5), estimatedYieldKg = 2000m });

        Assert.Equal("Agronomist", (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("source").GetString());
    }

    // ── Collection bookings ─────────────────────────────────────────────────

    [Fact]
    public async Task A_harvest_is_booked_into_the_nearest_centre_on_the_preferred_day()
    {
        var (setup, farmer) = await CropAsync();
        var day = FreshDay();
        var far = await CentreAsync(setup.DistrictId, [(day, 1000m)], lat: 7.40m);
        var near = await CentreAsync(setup.DistrictId, [(day, 1000m)], lat: 6.951m);

        var response = await AllocateAsync(farmer, setup.Cycle.Id, 400m, day);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Matches(@"^BK-\d{4}-\d{6}$", booking.GetProperty("bookingNo").GetString());
        Assert.Equal(near.Name, booking.GetProperty("centreName").GetString());
        Assert.Equal(day, booking.GetProperty("slotDate").Deserialize<DateOnly>());
        Assert.Equal(400m, await BookedKgAsync(near.Id, day));
        Assert.Equal(0m, await BookedKgAsync(far.Id, day));
    }

    [Fact]
    public async Task A_full_preferred_day_moves_to_the_next_day_with_room()
    {
        var (setup, farmer) = await CropAsync();
        var day = FreshDay();
        var centre = await CentreAsync(setup.DistrictId, [(day, 100m), (day.AddDays(1), 1000m)]);

        var booking = await (await AllocateAsync(farmer, setup.Cycle.Id, 400m, day, centre.Id)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(day.AddDays(1), booking.GetProperty("slotDate").Deserialize<DateOnly>());
    }

    [Fact]
    public async Task A_harvest_that_fits_no_single_slot_is_refused_not_split()
    {
        var (setup, farmer) = await CropAsync();
        var day = FreshDay();
        var centre = await CentreAsync(setup.DistrictId, [(day, 300m), (day.AddDays(1), 300m)]);

        var response = await AllocateAsync(farmer, setup.Cycle.Id, 500m, day, centre.Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("NO_CAPACITY", await response.ProblemCodeAsync());
        Assert.Equal(0m, await BookedKgAsync(centre.Id, day));
    }

    [Fact]
    public async Task Collection_before_the_crop_is_safe_to_harvest_is_refused()
    {
        var (setup, farmer) = await CropAsync();
        await SprayAsync(setup.Cycle.Id, Today);
        var centre = await CentreAsync(setup.DistrictId, [(Today.AddDays(3), 1000m)]);

        var response = await AllocateAsync(farmer, setup.Cycle.Id, 100m, Today.AddDays(3), centre.Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("HARVEST_BEFORE_PHI", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Two_farmers_racing_for_the_last_room_never_overbook_it()
    {
        var first = await CropAsync();
        var second = await CropAsync();
        var day = FreshDay();
        // One slot, room for one of the two harvests only.
        var centre = await CentreAsync(first.Setup.DistrictId, [(day, 100m)]);

        var responses = await Task.WhenAll(
            AllocateAsync(first.Farmer, first.Setup.Cycle.Id, 60m, day, centre.Id),
            AllocateAsync(second.Farmer, second.Setup.Cycle.Id, 60m, day, centre.Id));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity],
            responses.Select(r => r.StatusCode).OrderBy(s => (int)s));
        Assert.Equal(60m, await BookedKgAsync(centre.Id, day));
    }

    [Fact]
    public async Task Cancelling_gives_the_room_back_once()
    {
        var (setup, farmer) = await CropAsync();
        var day = FreshDay();
        var centre = await CentreAsync(setup.DistrictId, [(day, 1000m)]);
        var id = (await (await AllocateAsync(farmer, setup.Cycle.Id, 400m, day, centre.Id)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var cancel = await farmer.PostAsync($"/api/collection-bookings/{id}/cancel", null);
        var again = await farmer.PostAsync($"/api/collection-bookings/{id}/cancel", null);

        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(0m, await BookedKgAsync(centre.Id, day));
    }

    [Fact]
    public async Task Only_the_grower_books_their_harvest()
    {
        var (setup, _) = await CropAsync();
        var day = FreshDay();
        var centre = await CentreAsync(setup.DistrictId, [(day, 1000m)]);
        var stranger = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));
        var agronomist = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: setup.DistrictId));

        Assert.Equal(HttpStatusCode.Forbidden, (await AllocateAsync(stranger, setup.Cycle.Id, 100m, day, centre.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AllocateAsync(agronomist, setup.Cycle.Id, 100m, day, centre.Id)).StatusCode);
    }

    [Fact]
    public async Task A_farmer_sees_their_own_bookings()
    {
        var (setup, farmer) = await CropAsync();
        var day = FreshDay();
        var centre = await CentreAsync(setup.DistrictId, [(day, 1000m)]);
        await AllocateAsync(farmer, setup.Cycle.Id, 250m, day, centre.Id);

        var page = await farmer.GetFromJsonAsync<JsonElement>("/api/collection-bookings");

        var booking = Assert.Single(page.Items());
        Assert.Equal(250m, booking.GetProperty("quantityKg").GetDecimal());
        Assert.True(booking.GetProperty("distanceKm").GetDouble() < 5);
    }

    // ── At the centre: check-in, weight, missed ─────────────────────────────

    /// <summary>A booking on <paramref name="day"/> at a fresh centre in the farm's district, with co-op staff signed in.</summary>
    private async Task<(Guid BookingId, HttpClient Staff, HttpClient Farmer, CaseFixtures.FarmSetup Setup)> BookingOnAsync(DateOnly day)
    {
        var (setup, farmer) = await CropAsync();
        var centre = await CentreAsync(setup.DistrictId, [(day, 1000m)]);
        var response = await AllocateAsync(farmer, setup.Cycle.Id, 400m, day, centre.Id);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var staff = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: setup.DistrictId));
        return (id, staff, farmer, setup);
    }

    private static Task<HttpResponseMessage> RecordAsync(HttpClient client, Guid bookingId, string status, decimal? kg = null) =>
        client.PostAsJsonAsync($"/api/collection-bookings/{bookingId}/record", new { status, actualQuantityKg = kg });

    [Fact]
    public async Task On_the_day_staff_check_the_farmer_in_then_record_the_weight_delivered()
    {
        var (id, staff, farmer, _) = await BookingOnAsync(Today);

        var checkIn = await RecordAsync(staff, id, "CheckedIn");
        var repeat = await RecordAsync(staff, id, "CheckedIn");
        var weighed = await RecordAsync(staff, id, "Completed", 387.5m);

        Assert.Equal("CheckedIn", (await checkIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var done = await weighed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal(387.5m, done.GetProperty("actualQuantityKg").GetDecimal());

        // The farmer sees it on their own list.
        var mine = (await farmer.GetFromJsonAsync<JsonElement>("/api/collection-bookings")).Items().Single(b => b.GetProperty("id").GetGuid() == id);
        Assert.Equal("Completed", mine.GetProperty("status").GetString());
        Assert.Equal(387.5m, mine.GetProperty("actualQuantityKg").GetDecimal());
    }

    [Fact]
    public async Task Nothing_is_recorded_before_the_day_and_steps_cannot_be_skipped()
    {
        var (future, staff, _, _) = await BookingOnAsync(FreshDay());
        var (today, _, _, _) = await BookingOnAsync(Today);

        var early = await RecordAsync(staff, future, "CheckedIn");
        var skip = await RecordAsync(staff, today, "Completed", 400m);

        Assert.Equal("ILLEGAL_BOOKING_TRANSITION", await early.ProblemCodeAsync());
        Assert.Equal("ILLEGAL_BOOKING_TRANSITION", await skip.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_booking_whose_day_passed_can_be_marked_missed_and_then_nothing_else()
    {
        var (id, staff, _, _) = await BookingOnAsync(Today);
        // The collection day was yesterday.
        await factory.QueryAsync(db => db.CollectionSlots.Where(s => s.Bookings.Any(b => b.Id == id))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.SlotDate, Today.AddDays(-1))));

        var missed = await RecordAsync(staff, id, "NoShow");
        var lateCheckIn = await RecordAsync(staff, id, "CheckedIn");

        Assert.Equal("NoShow", (await missed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal("ILLEGAL_BOOKING_TRANSITION", await lateCheckIn.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_weight_is_required_to_complete_and_refused_on_any_other_step()
    {
        var (id, staff, _, _) = await BookingOnAsync(Today);
        await RecordAsync(staff, id, "CheckedIn");

        var noWeight = await RecordAsync(staff, id, "Completed");
        var weightOnCheckIn = await RecordAsync(staff, id, "NoShow", 10m);
        var cancelledStatus = await RecordAsync(staff, id, "Cancelled");

        Assert.Equal(HttpStatusCode.BadRequest, noWeight.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weightOnCheckIn.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cancelledStatus.StatusCode);
    }

    [Fact]
    public async Task Only_co_op_staff_record_and_an_agronomist_only_in_their_district()
    {
        var (id, _, farmer, setup) = await BookingOnAsync(Today);
        var (_, otherDistrict) = await factory.TwoDistrictsAsync();
        var outsider = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist,
            districtId: otherDistrict == setup.DistrictId ? (await factory.TwoDistrictsAsync()).First : otherDistrict));
        var dealer = await factory.SignedInAsAsync(UserRole.AgroDealer);
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);

        Assert.Equal(HttpStatusCode.Forbidden, (await RecordAsync(farmer, id, "CheckedIn")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RecordAsync(dealer, id, "CheckedIn")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RecordAsync(outsider, id, "CheckedIn")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RecordAsync(admin, id, "CheckedIn")).StatusCode);
    }

    [Fact]
    public async Task A_cancelled_booking_cannot_be_checked_in()
    {
        var (id, staff, farmer, _) = await BookingOnAsync(Today);
        (await farmer.PostAsync($"/api/collection-bookings/{id}/cancel", null)).EnsureSuccessStatusCode();

        var response = await RecordAsync(staff, id, "CheckedIn");

        Assert.Equal("ILLEGAL_BOOKING_TRANSITION", await response.ProblemCodeAsync());
    }

    // ── Slots ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_administrator_opens_slots_within_the_centre_s_daily_capacity()
    {
        var (setup, farmer) = await CropAsync();
        var day = FreshDay();
        var centre = await CentreAsync(setup.DistrictId, [(day, 9_500m)]);
        var admin = await factory.SignedInAsAsync(UserRole.CoopAdministrator);
        object Slot(int index, decimal kg) => new { centreId = centre.Id, slotDate = day, slotIndex = index, startTime = "09:00", endTime = "11:00", capacityKg = kg };

        var fits = await admin.PostAsJsonAsync("/api/collection-slots", Slot(2, 500m));
        var tooMuch = await admin.PostAsJsonAsync("/api/collection-slots", Slot(3, 1m));
        var byFarmer = await farmer.PostAsJsonAsync("/api/collection-slots", Slot(4, 1m));

        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
        Assert.Equal("CENTRE_CAPACITY_EXCEEDED", await tooMuch.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byFarmer.StatusCode);
    }
}
