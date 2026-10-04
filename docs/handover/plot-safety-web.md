# Study note: the plot page on the web (Component A's React safety profile and treatment history)

`/plots/:plotId` (`web/src/features/registry/PlotDetailPage.tsx`) shows one plot in three parts:

1. **Crop cycle.** This reuses `CropCyclePanel` from the farm page, so sowing and stage advances
   behave the same in both places.
2. **Spray safety** (`SafetyProfilePanel.tsx`) reads `GET /api/plots/{id}/safety-profile`.
3. **Treatment history** (`TreatmentHistory.tsx`) reads `GET /api/reports/plot-treatment-history`.

You reach the page from the farm page: every plot code is now a link.

## The one idea to defend

The panel **calculates nothing**. `SafetyProfileCalculator` (C#, pure, unit-tested) decides:

- whether each product can be sprayed today;
- why not, as a `SprayBlock`;
- the PHI-blocked days;
- the re-entry time.

The validator's rules V5, V6 and V7 are checked against the same numbers. The page only groups and
words them. So the console, the phone (slice 3) and the Validation agent cannot disagree about
whether a spray is legal. The page does two small things of its own:

- `toDayRanges` (`safety.ts`) folds the list of blocked dates into runs of days. "17–19 Oct"
  reads better than a hundred chips.
- `isReEntryActive` compares the re-entry time with the browser's clock again, because a page
  left open can outlive the time the server sent.

Each block names its rule ("Rule V5." / V6 / V7), so the farmer sees the same wording as the
validator's report.

## Filters live in the URL

- `?products=sprayable|blocked` filters the product table.
- `?from=&to=` is sent to the report API, because it narrows the SQL.
- `?status=Applied|Scheduled|Cancelled` narrows the rows on the page. The report is small and
  never paged, so a second request would buy nothing.

A shared link opens with the same view. An inverted date range is caught on the page, and the
query is disabled (`useTreatmentHistory(query, enabled)`), so the API's 400 is never earned.

## Who sees what

The route sits under the `OwnsFarm` policy. The API scopes rows as it does for the farm page:

- the owning farmer and the district's agronomist can view;
- anyone else gets a 403, which shows as "Could not load this".

Only the owner or an administrator gets the crop-cycle buttons (`canEdit`, which needs the farm's
`farmerId`, hence the extra `useFarm`).

## Numbers worth knowing

- **"Safe to harvest from"** on a history row is `applicationDate + PHI`, taken from the rules
  table as it is today. It is null for a cancelled spray, and null when the product's approval was
  removed (shown as "No rule on file").
- **Cancelled sprays** are listed but not counted in "Sprays by active ingredient". They never
  touched the crop. This is the same reason the safety profile ignores them.

## Questions to expect

- *"Why fetch two endpoints, not one?"* They answer different questions. The profile is the
  current cycle only, and "now". The history covers every cycle on the plot, in any date range.
  The profile feeds the agent too, so it must stay small.
- *"How do you know the page and the validator agree?"* There is one calculator, and both read
  it. The MSW tests pin the wording. The live check on P-07 showed V7 (Azoxystrobin applied
  27 Sept, next from 7 Oct) and V5 (Imidacloprid, 21-day PHI).

## Practice changes

- Add a "Restricted only" option to the product filter (`isRestricted` is already in the DTO).
- Show `ingredientUsage.daysSinceLastApplication` next to each product in the safety table.
