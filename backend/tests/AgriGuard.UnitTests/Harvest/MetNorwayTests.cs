using System.Net;
using AgriGuard.Application.Common;
using AgriGuard.Infrastructure.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriGuard.UnitTests.Harvest;

public sealed class MetNorwayClientTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    // Trimmed from a real Locationforecast 2.0 "complete" response for Kandapola (6.99, 80.82):
    // hourly steps first, then 6-hour steps, then a last step with no period at all.
    public const string Recorded = """
        {"type":"Feature","properties":{"meta":{"units":{"wind_speed":"m/s","precipitation_amount":"mm"}},
         "timeseries":[
          {"time":"2026-10-06T00:00:00Z","data":{"instant":{"details":{"air_temperature":13.44,"wind_speed":1.5,"relative_humidity":99.2}},
            "next_1_hours":{"details":{"precipitation_amount":0.0}},"next_6_hours":{"details":{"precipitation_amount":2.0}}}},
          {"time":"2026-10-06T01:00:00Z","data":{"instant":{"details":{"air_temperature":14.0,"wind_speed":2.0,"relative_humidity":97.0}},
            "next_1_hours":{"details":{"precipitation_amount":1.6}}}},
          {"time":"2026-10-06T02:00:00Z","data":{"instant":{"details":{"air_temperature":15.0,"wind_speed":3.0,"relative_humidity":90.0}},
            "next_6_hours":{"details":{"precipitation_amount":3.0}}}},
          {"time":"2026-10-06T08:00:00Z","data":{"instant":{"details":{"air_temperature":19.0,"wind_speed":4.0,"relative_humidity":70.0}},
            "next_6_hours":{"details":{"precipitation_amount":0.0}}}},
          {"time":"2026-10-06T12:00:00Z","data":{"instant":{"details":{"air_temperature":17.0,"wind_speed":2.5,"relative_humidity":80.0}},
            "next_6_hours":{"details":{"precipitation_amount":0.05}}}},
          {"time":"2026-10-06T18:00:00Z","data":{"instant":{"details":{"air_temperature":14.0,"wind_speed":1.0,"relative_humidity":95.0}}}}
         ]}}
        """;

    [Fact]
    public void Hours_are_read_in_local_time_with_wind_in_kph()
    {
        var hours = MetNorwayClient.Parse(Recorded, Colombo);

        // 00:00 UTC is 05:30 in Colombo.
        Assert.Equal(new DateTime(2026, 10, 6, 5, 30, 0), hours[0].LocalTime);
        Assert.Equal(DateTimeKind.Unspecified, hours[0].LocalTime.Kind);
        Assert.Equal(5.4m, hours[0].WindSpeedKph);
        Assert.Equal(13.4m, hours[0].TemperatureC);
        Assert.Equal(99, hours[0].RelativeHumidityPercent);
    }

    [Fact]
    public void The_hourly_amount_wins_over_the_six_hour_one()
    {
        var hours = MetNorwayClient.Parse(Recorded, Colombo);

        // The first step has both; its own hour was dry even though the next six were not.
        Assert.Equal(0m, hours[0].PrecipitationMm);
        Assert.Equal(0, hours[0].RainProbabilityPercent);
    }

    [Fact]
    public void Without_a_probability_forecast_rain_counts_as_certain()
    {
        var hours = MetNorwayClient.Parse(Recorded, Colombo);

        Assert.Equal(1.6m, hours[1].PrecipitationMm);
        Assert.Equal(100, hours[1].RainProbabilityPercent);
    }

    [Fact]
    public void A_six_hour_step_is_spread_evenly_over_its_hours()
    {
        var hours = MetNorwayClient.Parse(Recorded, Colombo);

        var block = hours.Where(h => h.LocalTime >= new DateTime(2026, 10, 6, 7, 30, 0)
                                     && h.LocalTime < new DateTime(2026, 10, 6, 13, 30, 0)).ToList();
        Assert.Equal(6, block.Count);
        Assert.All(block, h => Assert.Equal(0.5m, h.PrecipitationMm));
        Assert.All(block, h => Assert.Equal(100, h.RainProbabilityPercent));
    }

    [Fact]
    public void A_step_stops_at_the_next_one_and_drizzle_below_a_tenth_of_a_millimetre_is_dry()
    {
        var hours = MetNorwayClient.Parse(Recorded, Colombo);

        // 08:00 UTC has a 6-hour period but the next step is at 12:00, so only 4 hours come from it.
        Assert.Equal(4, hours.Count(h => h.TemperatureC == 19.0m));
        // 12:00 UTC: 0.05 mm over six hours is under the 0.1 mm MET reports as rain.
        Assert.All(hours.Where(h => h.TemperatureC == 17.0m), h => Assert.Equal(0, h.RainProbabilityPercent));
    }

    [Fact]
    public void A_step_without_any_rain_figure_is_skipped_never_read_as_dry()
    {
        var hours = MetNorwayClient.Parse(Recorded, Colombo);

        Assert.Equal(1 + 1 + 6 + 4 + 6, hours.Count);
        Assert.DoesNotContain(hours, h => h.LocalTime == new DateTime(2026, 10, 6, 23, 30, 0) && h.TemperatureC == 14.0m);
        Assert.Equal(hours.Count, hours.Select(h => h.LocalTime).Distinct().Count());
    }

    [Fact]
    public void A_response_without_a_timeseries_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => MetNorwayClient.Parse("""{"type":"Feature","properties":{}}""", Colombo));
}

public sealed class FallbackWeatherProviderTests
{
    private const string OpenMeteoRecorded = """
        {"hourly":{"time":["2026-10-06T06:00"],"precipitation_probability":[10],"precipitation":[0.0],
                   "wind_speed_10m":[4.0],"temperature_2m":[16.0],"relative_humidity_2m":[90]}}
        """;

    private static readonly FarmCalendar Calendar =
        new(TimeProvider.System, TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo"));

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (FallbackWeatherProvider Provider, Stub OpenMeteo, Stub Met) Build(
        HttpStatusCode openMeteoStatus, bool fallbackEnabled = true, HttpStatusCode metStatus = HttpStatusCode.OK)
    {
        var openMeteo = new Stub(openMeteoStatus, openMeteoStatus == HttpStatusCode.OK ? OpenMeteoRecorded : "Too many requests");
        var met = new Stub(metStatus, MetNorwayClientTests.Recorded);
        var provider = new FallbackWeatherProvider(
            new OpenMeteoClient(new HttpClient(openMeteo) { BaseAddress = new Uri("https://open-meteo.test/") },
                Calendar, Options.Create(new OpenMeteoOptions())),
            new MetNorwayClient(new HttpClient(met) { BaseAddress = new Uri("https://met.test/") }, Calendar),
            Options.Create(new MetNorwayOptions { Enabled = fallbackEnabled }),
            NullLogger<FallbackWeatherProvider>.Instance);
        return (provider, openMeteo, met);
    }

    [Fact]
    public async Task Open_meteo_answers_first_and_met_norway_is_not_called()
    {
        var (provider, _, met) = Build(HttpStatusCode.OK);

        var hours = await provider.FetchAsync(7.0m, 80.8m);

        Assert.Equal(10, Assert.Single(hours).RainProbabilityPercent);
        Assert.Empty(met.Requests);
    }

    [Fact]
    public async Task A_refusal_from_open_meteo_falls_back_to_met_norway()
    {
        var (provider, openMeteo, met) = Build(HttpStatusCode.TooManyRequests);

        var hours = await provider.FetchAsync(7.0m, 80.8m);

        Assert.Single(openMeteo.Requests);
        var request = Assert.Single(met.Requests);
        Assert.Equal("/weatherapi/locationforecast/2.0/complete", request.AbsolutePath);
        Assert.Equal("?lat=7.0&lon=80.8", request.Query);
        Assert.NotEmpty(hours);
    }

    [Fact]
    public async Task When_the_fallback_is_switched_off_the_refusal_stands()
    {
        var (provider, _, met) = Build(HttpStatusCode.TooManyRequests, fallbackEnabled: false);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAsync(7.0m, 80.8m));
        Assert.Empty(met.Requests);
    }

    [Fact]
    public async Task When_both_fail_the_caller_hears_about_it()
    {
        var (provider, _, _) = Build(HttpStatusCode.TooManyRequests, metStatus: HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAsync(7.0m, 80.8m));
    }
}
