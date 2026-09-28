# Study note — harvest windows and collection bookings (Component D, slice 2)

Viva-critical piece: two of Component D's three non-CRUD operations. Read this with the code open.

| File | What it holds |
|---|---|
| `backend/src/AgriGuard.Domain/Harvest/HarvestWindows.cs` | **Pure** ranking of harvest days: maturity ∩ pre-harvest intervals ∩ weather. |
| `backend/src/AgriGuard.Domain/Harvest/SlotAllocation.cs` | **Pure** choice of a collection slot, and the haversine distance. |
| `backend/src/AgriGuard.Infrastructure/Harvest/HarvestWindowService.cs` | `GET /api/harvest-windows/{cropCycleId}`: gathers the facts, calls `HarvestWindows`. |
| `backend/src/AgriGuard.Infrastructure/Harvest/CollectionService.cs` | `POST /api/collection-bookings/allocate`: the serializable transaction; slots, bookings, cancel. |
| `backend/src/AgriGuard.Infrastructure/Harvest/HarvestScope.cs` | Row scoping, and `SpraysAsync`: every spray with its PHI from the rules table. |
| `mobile/lib/features/harvest/harvest_screen.dart` | The farmer's screen: ranked days, the spray-window strip, date picker, booking. |
| `backend/tests/AgriGuard.UnitTests/Harvest/HarvestWindowsTests.cs` | Ranking and allocation rules, one test each. |
| `backend/tests/AgriGuard.IntegrationTests/HarvestAndCollectionTests.cs` | Endpoints, including two farmers racing for the last room. |

## 1. Harvest windows — when should this crop be harvested?

Three facts are intersected (§5.1: "maturity ∩ PHI ∩ weather"):

1. **Maturity**: the planned harvest date, or the expected one (sowing + the crop's maturity days).
   Candidates run from 5 days before it to 14 days after.
2. **Chemical safety**: every spray on the crop that isn't cancelled, with the pre-harvest interval
   the **rules table** sets for that product on this crop. Days before the latest one clears are
   **excluded**, not just scored low: harvesting then breaks the rule V5 protects.
   `SafeFromDate` and `SafetyReason` say which spray decides.
3. **Weather**: the Open-Meteo forecast for the harvesting hours (07:00–15:00). A day is wet when
   rain is ≥ 40% likely **and** at least 0.5 mm is expected (the same "meaningful rain" idea as V8).

**Score** starts at 100:
- −15 per day before maturity;
- −4 per day after the third day past maturity;
- −40 for a wet day;
- −10 for a day beyond the forecast.

The best three are "recommended" if they score at least 65. A wet day scores at most 60, so it is
never recommended.

## 2. Allocation — which slot does a harvest go to?

`POST /api/collection-bookings/allocate { cropCycleId, quantityKg, preferredDate, centreId? }`

Checks before any transaction:
- the caller grows this crop (or is an administrator);
- the crop is active and the date is not in the past;
- **the crop is safe to harvest on the preferred date.** Otherwise the answer is 422
  `HARVEST_BEFORE_PHI`, naming the spray. Components A and D share this rule.

Then **one serializable transaction**:
1. Lock the candidate slots with `SELECT … FOR UPDATE`, in id order so racing requests queue. The
   candidates are active centres in the farm's district (or the named centre), dated from the
   preferred day to 2 days after, with room for the whole quantity.
2. `SlotAllocation.Choose`: the earliest day first (never earlier than asked), then the **nearest
   centre** (haversine distance from the plot), then the earliest slot. **A harvest is never
   split** across slots.
3. `BookedKg += quantity`, and a `CollectionBooking` numbered `BK-2026-000001` from a PostgreSQL sequence.

Nothing fits → 422 `NO_CAPACITY`, and nothing is changed.

**How overbooking is prevented** (the likely viva question):

| Layer | What it stops |
|---|---|
| `FOR UPDATE` on candidate slots | Two farmers taking the last room: the second waits, then sees it taken. |
| Serializable + retry (`SerializableTransaction`) | Any interleaving the locks miss: 40001, retry on fresh data. |
| `CHECK booked_kg <= capacity_kg` | The database refuses an overbooked slot even if the code were wrong. |

`Two_farmers_racing_for_the_last_room_never_overbook_it` fires two 60 kg bookings at a 100 kg
slot with `Task.WhenAll`, and checks for one 201, one 422, and exactly 60 kg booked.

**Cancel** puts the room back in the same kind of transaction (the slot row is locked). A booking
can be cancelled once, and not after its collection day.

**Opening slots** (`POST /api/collection-slots`, administrator only): a centre's slots on one day
may not add up to more than its `DailyCapacityKg` (422 `CENTRE_CAPACITY_EXCEEDED`).

## 3. Demo data

`DemoCollectionSeeder` creates:
- centres in Nuwara Eliya (2) and Anuradhapura (2), each with three morning slots a day for four weeks;
- **plot A-02 on the Dry Zone Farm: Big Onion, pre-harvest, maturing 4 days after seeding.**

A-02 is the harvest-and-booking walkthrough; A-01's flowering tomato is the treatment one. On 28 Sept,
A-02's best day was Thu 1 Oct (dry, a day before maturity), and a 400 kg booking went to Mihintale
Collection Point, the nearest centre (under 1 km).

## Live modification drills (practise these)

- **Allow a harvest to slip 3 days instead of 2:** `SlotAllocation.MaxDaysLater = 3`.
- **Prefer the nearest centre over the preferred day:** swap the first two `OrderBy`s in
  `SlotAllocation.Choose`. `The_preferred_day_comes_first_even_if_a_later_centre_is_nearer` fails,
  which shows the test pins the policy.
- **Punish a wet harvest day less:** change `score -= 40` in `HarvestWindows.Rank` (and the note on
  `RecommendedMinimumScore`, which relies on wet days scoring 60 at most).
- **Change a PHI in `/rules`:** the harvest window and the booking check both move, with no code change.
