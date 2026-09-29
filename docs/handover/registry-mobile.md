# Study note — farms, plots and crop cycles on the phone (Component A's Flutter screens)

Component A's part of the Flutter app (§8 "Farm/plot onboarding and crop-cycle tracker"). It calls
the same registry API as the React `/farms` pages; nothing here decides anything the API does not
check again.

| File | What it holds |
|---|---|
| `mobile/lib/features/registry/registry_models.dart` | Farm, Plot, CropCycle, SafetyProfile, and the `CropStage` / `SoilType` enums with their wire values. |
| `mobile/lib/features/registry/registry_repository.dart` | `RegistryRepository` (interface + Dio implementation) and the Riverpod providers. |
| `mobile/lib/features/registry/farms_screen.dart` | `/farms` (list, "Add farm" sheet) and `/farms/:farmId` (its plots). |
| `mobile/lib/features/registry/new_plot_screen.dart` | `/farms/:farmId/plots/new`: the plot form, with the location from the phone's GPS. |
| `mobile/lib/features/registry/plot_screen.dart` | `/plots/:plotId`: the crop-cycle tracker, "Start a crop", and spray safety. |
| `mobile/test/registry_screens_test.dart` | 12 widget tests against a mocked repository. |

## What each screen does

- **Farms.** The API scopes the list: a farmer sees their own farms, an agronomist their district's
  ("Farms in my district", read-only). "Add farm" starts with the farmer's own district.
- **Add a plot.** The GPS fix is requested as the screen opens (the same `LocationService` as the
  case report). Its coordinates fill the latitude and longitude fields and can be edited. Without a
  fix the farmer types them. The client checks the same bounds as the API validator and the
  database CHECK constraint (area > 0 and ≤ 10,000 ha; valid latitude and longitude), and a 400's
  field errors are shown on the field they are about. The location matters because the weather
  (rule V8), the harvest windows and the nearest collection centre are all worked out from it.
- **The tracker.** It shows all six stages with this crop's place among them, the sowing and
  harvest dates, and the history of stage changes. "Mark as <next stage>" offers only
  `allowedNextStages` from the API (one step on, from `CropStageRules`). The API still refuses an
  illegal move with 422 `ILLEGAL_STAGE_TRANSITION`, and the phone shows that message.
- **Start a crop.** Offered when nothing is growing: the crop and the sowing day. The expected
  harvest is worked out on the server from the crop's maturity days.
- **Spray safety.** This is the plot's safety profile, the same calculation behind the agent's
  `get_plot_safety_profile` tool. It lists each approved product as "can be sprayed today"
  (with the last safe day before harvest and the sprays used) or as blocked, with the reason in
  words (pre-harvest interval, season limit, gap between sprays). It also warns when people must
  stay out of the field after a spray (re-entry interval), and lists this season's sprays.

## A bug these screens exposed (fixed)

`CropStageRules.ReviseExpectedHarvestDate` re-estimates the harvest when a stage is reached, by
scaling the whole season by how early or late the stage arrived. A crop registered today and
marked Vegetative the same day gave a scale of zero, so the "expected harvest" became tomorrow.
That is wrong in both directions. Every spray's pre-harvest check (V5) and the harvest windows are
measured from that date, and an immature crop looked ready for a collection booking.

The fix: `MinimumScale = 0.75`. However early a stage is reached, the season is not assumed to be
shorter than three quarters of the crop's maturity period. The existing caps (never before the
stage itself, never beyond twice the maturity period) are unchanged. There is a new unit test,
`A_stage_recorded_on_the_day_of_sowing_does_not_put_the_harvest_tomorrow`. This is a good viva
example of a boundary case that only showed up when a second client made the action easy.

## Questions to expect

- *"Why is the farmer told which products are blocked, if the validator checks anyway?"* So they
  do not buy something they cannot use. The validator is the enforcement, and this is the same rule
  shown in advance.
- *"What stops a farmer skipping a stage?"* The button offers only the next stage, and the API's
  transition matrix refuses anything else. The client is convenience; the server is the control.
- *"Why can't an agronomist edit here?"* The API's `CanWriteFarm` allows only the owner or an
  administrator. The phone hides the buttons to match, and the API would refuse anyway.

## Practice changes

- Show the plot's coordinates with a "Open in Maps" link (`geo:` URI).
- Let the farmer set a planned harvest date when starting a crop (`plannedHarvestDate` is
  already accepted by `POST /api/crop-cycles`).
