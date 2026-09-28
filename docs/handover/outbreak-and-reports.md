# Study note — outbreak intelligence and the reports (Component D, slice 3)

Viva-critical piece: Component D's third non-CRUD operation, the outbreak signal, which is also
what the Diagnosis agent reads. The reports are simpler aggregates, one or two per component.
Read this with the code open.

| File | What it holds |
|---|---|
| `backend/src/AgriGuard.Domain/Intelligence/OutbreakSignal.cs` | **Pure** calculation: weights, the 0–100 index, levels, trend, per-pathogen and per-district breakdown. |
| `backend/src/AgriGuard.Infrastructure/Intelligence/OutbreakSignalService.cs` | One query for the window's cases, then `OutbreakSignal.Assess`; the one-line summary. |
| `backend/src/AgriGuard.Infrastructure/Agent/AgentToolService.cs` | `GetOutbreakSignalAsync`: the agent tool, now a thin wrapper over the same service. |
| `backend/src/AgriGuard.Infrastructure/Cases/ApprovalService.cs` | On approval, the case's `ConfirmedPathogenId` is set: this is where the signal's data comes from. |
| `backend/src/AgriGuard.Domain/Harvest/ForecastAccuracy.cs` | **Pure** forecast-vs-actual arithmetic (variance and MAPE). |
| `backend/src/AgriGuard.Domain/Inventory/StockLevel.cs` | **Pure** low-stock rule (sellable whole packs vs a minimum). |
| `backend/src/AgriGuard.Infrastructure/Reports/*.cs` | The five report services. |
| `backend/src/AgriGuard.Api/Controllers/IntelligenceController.cs`, `ReportsController.cs` | Routes and policies. |
| `backend/src/AgriGuard.Infrastructure/Persistence/Seed/DemoHistorySeeder.cs` | Demo history: 30 cases, last season's harvests. |
| `web/src/features/intelligence`, `web/src/features/harvest` | `/intelligence`, `/harvest`, `/collection-planner`. |
| `backend/tests/AgriGuard.UnitTests/Intelligence/OutbreakSignalTests.cs` | One test per rule of the calculation. |
| `backend/tests/AgriGuard.IntegrationTests/IntelligenceAndReportsTests.cs` | Endpoints, the tool matching the API, and every report. |

## 1. The outbreak signal

`GET /api/intelligence/outbreak-signal?cropId=&districtId=&days=14`

**Where the data comes from.** A case counts as *confirmed* when an agronomist approves a
prescription for it: approving a treatment for late blight confirms the diagnosis of late blight.
`ApprovalService.IssueAsync` copies the Diagnosis agent's `primary_pathogen_code` into
`CropCase.ConfirmedPathogenId` (and into the prescription, as before). Before slice 3 nothing ever set
that column, so the old tool always answered "no outbreak".

**The calculation** (`OutbreakSignal.Assess`), for the cases reported in the last `days` days:

```
weight   = severity × recency
severity = Low 0.5 · Medium 1 · High 1.5 · Critical 2
recency  = 0.5 ^ (age in days / 7)          a week old counts half, two weeks a quarter
score    = Σ weight over confirmed cases
index    = round(100 × (1 − e^(−score / 4)))
level    = None (0) · Low (<25) · Moderate (<50) · High (<75) · Severe (≥75)
trend    = Rising  if the newer half of the window has ≥ 2 confirmed cases and ≥ 1.5× the older half
           Falling the other way round; otherwise Steady
```

Worked example (the integration test): late blight High today, High yesterday, Medium two days
ago, early blight Medium ten days ago:
1.5 + 1.5 × 0.906 + 1 × 0.820 + 1 × 0.371 = 4.05 → index 64 → **High**, and 3 of 4 in the newer
half → **Rising**.

**Why these choices** (likely viva questions):
- *Why only confirmed cases?* A farmer's report is a suspicion. Scoring suspicions would let one
  worried village raise an alarm for a whole district. Unconfirmed reports are still counted and
  drawn (orange on the chart), so a surge is visible to an agronomist.
- *Why decay by age?* Last week's cases say more about spores in the air today than those from three
  weeks ago. A half-life of 7 days is roughly one disease cycle for blight.
- *Why the exponential index instead of the raw score?* The raw score is unbounded and hard to read.
  `1 − e^(−x)` rises steeply for the first few cases and then saturates: 1 case ≈ 22, 2 ≈ 39,
  4.5 ≈ 68, 8 ≈ 86. The difference between 0 and 3 cases matters; between 30 and 33 it doesn't.
- *What is "spatio-temporal" here?* Temporal: the decay and the daily series. Spatial: the district
  breakdown, each with a map position = the mean of its reports' GPS, **rounded to 0.1° (~11 km)**
  so no single farm can be located from the aggregate.
- *Who may see it?* Every signed-in role, and it is not district-scoped: it contains counts and
  scores, never a farmer, plot or case. A neighbouring district's pressure is the useful part.

**The agent uses the same numbers.** `/internal/tools/outbreak-signal` calls
`OutbreakSignalService.ComputeAsync`, so the React page and the Diagnosis agent cannot disagree.
The integration test `The_agent_s_outbreak_tool_gives_the_same_answer_as_the_api` pins that. The
agent reads the `summary` line, e.g. *"Late blight pressure is high for tomato in Nuwara Eliya
(index 64/100, rising): 4 confirmed of 5 reported in 14 days. Also seen: Early blight (1)."*

## 2. The reports

All under `/api/reports`, read-only, each scoped exactly like the list it summarises.

| Report | Owner | Who | What |
|---|---|---|---|
| `plot-treatment-history?plotId=&from=&to=` | A | farm roles, row-scoped | Every spray on a plot, its PHI and safe-harvest date; applications per active ingredient (the V6/V7 view). |
| `case-throughput?districtId=&from=&to=` | B | farm roles, row-scoped | Reported vs prescribed, median and mean hours to prescription, cases and agent runs by status, agent success rate, daily series (default last 30 days). |
| `stock-valuation?dealerId=` | C | dealer (own shop) or administrator | Value on the shelf at each batch's **pack** price; held, expired and expiring-soon value. |
| `low-stock?dealerId=&minPacks=3` | C | dealer or administrator | Products with fewer sellable **whole packs** than the minimum; out of stock first. Expired and held stock do not count. |
| `harvest-forecast-vs-actual?districtId=&cropId=&from=&to=` | D | farm roles, row-scoped | Variance and MAPE overall, per crop and per forecaster (farmer vs agronomist). |

A new policy, `ViewsStockReports` (dealer + administrator), exists because reading stock is wider
than changing it: the administrator oversees every shop but does not run one (`ManagesInventory`
stays dealer-only).

**Forecast accuracy: two numbers, on purpose.** *Variance* (bias) = (actual − forecast) / forecast
over the whole group: +10% and −10% cancel out, so it answers "do we over- or under-estimate?".
*MAPE* = the mean of |actual − forecast| / forecast: +10% and −10% make 10%, so it answers "how far
off is a typical forecast?". A co-op planning lorries needs both.

## 3. Demo data

`DemoHistorySeeder` (runs with the other demo seeders on `dotnet ef database update`, Development only):
- four neighbouring farmers (Kandapola, Mihintale, Dambulla, Welimada) with growing crops;
- 30 cases over the last three weeks: a late-blight surge in the wet hill country (Nuwara Eliya,
  Badulla), thrips and whitefly in the dry zone; 4 still unconfirmed;
- last season's harvests with the forecast made for each, and a forecast for each growing crop.

The cases are **kept current**: each later run moves them forward by whole days so the newest is
today (farm calendar, Asia/Colombo). Otherwise a 14-day signal seeded before the viva would be
empty during it. They are marked with the reference prefix `AG-DEMO-`.

## Live modification drills (practise these)

- **Make old cases fade faster:** `OutbreakSignal.HalfLifeDays = 3.5`. `A_case_counts_half_after_a_week…`
  fails, showing the test pins the policy.
- **Make one case enough for "Moderate":** lower `IndexScale` (e.g. 2), or move the level bands in
  `LevelFor`. The theory test lists the expected index for each score.
- **Count suspicions too:** in `Assess`, score `inWindow` instead of `confirmed`. Explain why that is
  a bad idea (see "Why only confirmed cases?").
- **Change the low-stock threshold:** `StockLevel.DefaultMinPacks`, or pass `?minPacks=`.
- **Change what counts as an accurate forecast:** `ForecastAccuracy.ToleranceFraction` (the "Within
  ±10%" tile follows).
