# Study note — Open-Meteo, rule V8 and the spray window (Component D, slice 1)

Viva-critical piece: this is the third-party integration (§10), and it drives a business rule.
Read this with the code open.

| File | What it holds |
|---|---|
| `backend/src/AgriGuard.Infrastructure/Weather/OpenMeteoClient.cs` | The HTTP client: builds the request, reads the response. Sends rounded coordinates only. |
| `backend/src/AgriGuard.Infrastructure/DependencyInjection.cs` | The resilience pipeline on that client: 2 retries with jitter, circuit breaker, 5 s timeout. |
| `backend/src/AgriGuard.Infrastructure/Weather/WeatherService.cs` | The 3-hour `WeatherSnapshot` cache per ~11 km cell, and the degradation when Open-Meteo is down. |
| `backend/src/AgriGuard.Domain/Harvest/SprayWeather.cs` | **Pure**: an hourly forecast → "does this day suit spraying?". Used by V8, the screen and the agent. |
| `backend/src/AgriGuard.Domain/Validation/PrescriptionSafetyValidator.cs` | Rule V8 itself, and its thresholds (`MaxRainProbabilityPercent`, `MinWashOffRainMm`, …). |
| `backend/src/AgriGuard.Infrastructure/Agent/AgentPrescriptionGate.cs` | Feeds V8 the forecast for the proposal's spray date (`LoadWeatherAsync`). |
| `backend/src/AgriGuard.Infrastructure/Weather/SprayWindowService.cs` | `GET /api/weather/spray-window` and the agent's `get_weather_forecast` tool. |
| `agent/app/graph.py` | Diagnosis reads recent rain and humidity; Action may only propose a day the forecast allows. |
| `backend/tests/AgriGuard.IntegrationTests/WeatherTests.cs`, `…UnitTests/Harvest/SprayWeatherTests.cs` | One test per behaviour below. Tests never call the real Open-Meteo (`FakeWeatherProvider`). |

## How a forecast becomes a verdict

1. **Fetch.** `OpenMeteoClient` asks Open-Meteo for hourly rain probability, rainfall, wind,
   temperature and humidity, 16 days ahead and 2 days back, in Asia/Colombo time. Only the plot's
   coordinates **rounded to 0.1°** are sent — no farmer, plot or case identifiers (§11).
2. **Cache.** `WeatherService` keeps one forecast per rounded cell for 3 hours, so neighbouring
   plots share a fetch. The cache uses its **own database scope**: it is called from inside the
   approval's serializable transaction, and a cache write there could abort the approval.
3. **Judge a day.** `SprayWeather.Assess` assumes spraying in the **06:00–10:00** window. Wind and
   heat are judged over that window. Rain is judged from 06:00 until the product is **rainfast**
   (10:00 + the product's `RainfastHours` from the rules table), because rain before then washes it off.
4. **Rule V8** fails (severity **Revise**) when any of these hold:
   - rain probability ≥ 40% **and** at least 0.5 mm expected before rainfast (meaningful rain);
   - wind ≥ 15 km/h (drift);
   - temperature > 32 °C (evaporation).

   The same function judges the screen, the agent's tool and V8, so they can never disagree.

## Why "meaningful" rain (the likely viva question)

In the late-September wet season, Nuwara Eliya showed a 50% chance of 0.2 mm on some mornings. By
probability alone that blocks spraying, but 0.2 mm cannot wash a product off. So V8 now receives
the expected rainfall too (`WeatherAssessment.ExpectedRainMm`) and only refuses rain that is both
likely and at least 0.5 mm. If a caller sends no rainfall figure, the probability alone decides,
as before. With this rule Nuwara Eliya still had only 1 sprayable morning in 14. That's a real
forecast, and the system correctly refuses to spray into rain.

## What happens when Open-Meteo is down

Retries (2, jittered) → circuit breaker (opens after 5 straight failures, for a minute) → 5 s per
attempt. If there is still no forecast, `WeatherService` returns null and every caller **degrades**:
V8 is "not evaluated" with the reason, the spray-window screen says the forecast is unavailable,
and the agent proposes the earliest safe date. Nothing fails because the weather service is down.
Tested by `When_Open_Meteo_is_down_V8_is_not_evaluated_and_nothing_fails`.

## The agent's use of weather

- **Diagnosis** calls `get_weather_forecast` and reads the last 48 h of rain and humidity. Wet,
  humid weather favours fungal disease, which supports late blight. If the tool fails, the
  diagnosis goes ahead without it.
- **Action** gets the days that suit spraying and must pick one of them. If the forecast says no
  day suits, the run ends at once with a clear reason, instead of three rounds of revisions.
  The tool allow-lists stay disjoint: only Diagnosis calls the weather tool, and Action reads the
  result from the graph state.

## Demo data

In the wet season the Nuwara Eliya demo farm is a good **"V8 refuses a rainy spray"** demo. For the
happy path, `DemoDryZoneSeeder` adds the demo farmer's **Dry Zone Farm, plot A-01** (Anuradhapura),
with an Anuradhapura dealer (`dealer.anu@agriguard.demo`) and agronomist
(`agronomist.anu@agriguard.demo`, same demo password). On 28 Sept, A-01 had 7 sprayable mornings in
14. Its limiting factor is morning wind, not rain.

## Live modification drills (practise these)

- **Allow a little more rain:** change `MaxRainProbabilityPercent` from 40 to 50 in the validator.
  The spray window, the agent's allowed days and V8 all change together.
- **Spray in the evening instead:** change `ApplicationStart`/`ApplicationEnd` in `SprayWeather`.
- **Cache for 1 hour:** set `OpenMeteo:CacheFor` to `01:00:00` in `appsettings.json`; no code change.
- **A product that needs 8 dry hours:** raise its `RainfastHours` in `/rules`. Its rain window
  grows, and V8 judges the next proposal on it.
