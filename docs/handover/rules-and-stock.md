# Study note — the rules table, stock holds and orders (Component C)

Viva-critical piece. Read this with the code open. The "modify a business rule" task is almost
certainly done on this screen, so practise the drills at the end.

| File | What it holds |
|---|---|
| `backend/src/AgriGuard.Domain/Inventory/Catalogue.cs` | `ProductCropApproval`: the rules table, one row per product-crop pair. |
| `backend/src/AgriGuard.Infrastructure/Inventory/ProductCropApprovalService.cs` | Rule CRUD, with an audit log line of every limit before and after a change. |
| `backend/src/AgriGuard.Infrastructure/Validation/PrescriptionValidationService.cs` | Reads the rule row fresh on every check and hands it to `PrescriptionSafetyValidator` (V1–V11). |
| `backend/src/AgriGuard.Infrastructure/Inventory/StockLedger.cs` | **The one place stock moves**: lock, FEFO pick, draw, hold, commit, release. Used by the approval transaction and by reservations. |
| `backend/src/AgriGuard.Infrastructure/Inventory/ReservationService.cs` | `POST /api/inventory/reservations`, `/commit`, `/release`. |
| `backend/src/AgriGuard.Infrastructure/Inventory/ProposalStockHolds.cs` | The hold under an agent proposal awaiting approval: placed, found and released. |
| `backend/src/AgriGuard.Infrastructure/Inventory/ReservationExpirySweeper.cs` | Background service: releases holds older than 24 h. |
| `backend/src/AgriGuard.Domain/Inventory/OrderStatusRules.cs` | The fulfilment workflow Confirmed → Packed → Collected. Pure, unit-tested. |
| `web/src/features/rules/` | The `/rules` editor. |
| `web/src/features/inventory/` | `/inventory` (batches, expiry warnings, holds) and `/orders`. |
| `backend/tests/AgriGuard.IntegrationTests/CatalogueAndRulesTests.cs` | Includes `Editing_a_limit_in_the_rules_table_changes_the_next_verdict_with_no_code_change`. |
| `backend/tests/AgriGuard.IntegrationTests/ReservationTests.cs` | Hold/commit/release/expiry, FEFO, and two holds racing for the last packs. |

## 1. The rules table drives the validator

Nothing in C# holds a dose limit, a PHI or a seasonal cap. Each lives in a `ProductCropApproval`
row: `MinDosePerHectare`/`MaxDosePerHectare` (V3), `PreHarvestIntervalDays` (V5),
`MaxApplicationsPerCycle` (V6), `MinDaysBetweenApplications` (V7), `RainfastHours` (V8),
`IsRestricted` (V10) and `IsActive` (V2).

`PrescriptionValidationService.ValidateAsync` loads the row for exactly this product on this crop,
**with no cache**, every time it is called. So a change saved in `/rules` is what the next check
sees. That check might be the agent's `validate_prescription` tool, the backend's re-check when
the agent reports, or the approval transaction's re-check. No restart and no code change.

**How the edit travels:** `/rules` → Edit → `PUT /api/product-crop-approvals/{id}` (Co-op
Administrator only, policy `AdministersRules`) → `ProductCropApprovalService.UpdateAsync` → the
row is updated and the change is logged as "before → after" with the admin's id. The next
`ValidateAsync` reads the new values.

**Three layers of checks on a rule:** the React form (zod), the API (`ApprovalLimitsValidator`,
FluentValidation), and the database (`CHECK min_dose_per_hectare <= max_dose_per_hectare` and
others in `InventoryConfigurations.cs`). A bug in the first two still cannot store a nonsensical
rule. The unique index `(product_id, crop_id)` means the validator never has to choose between two
rows.

**Withdraw rather than delete:** unticking *Approved* sets `IsActive = false`, so V2 rejects the
product on that crop while the limits are kept for later. Delete exists (CRUD), but the editor
offers withdrawal.

## 2. Stock holds (reservations)

`POST /api/inventory/reservations { productId, quantity, usableOn?, note? }` runs as **one
serializable transaction**:

1. `StockLedger.PickAsync` locks the dealer's in-date batch rows with `SELECT … FOR UPDATE`, in id
   order so two transactions queue instead of deadlocking;
2. `StockAllocation.PlanFefo` (the same pure function the approval uses) plans the draw,
   **first-expiry-first-out**, in whole packs;
3. `QuantityReserved` goes up on each batch drawn, and a `StockReservation` (Held, `ExpiresAt =
   now + 24 h`) records one line per batch.

`/commit` takes exactly the held quantities off the shelf (`QuantityOnHand −`, `QuantityReserved
−`). `/release` puts them back on sale (`QuantityReserved −`). Both lock the reservation row
first, so a commit, a release and the sweeper can never act on one hold at once.
`ReservationExpirySweeper` runs every minute and releases holds past `ExpiresAt` with status
`Expired`, using the same `ReleaseAsync`.

### A proposal's hold (the assessed workflow, §11 step 5)

The same hold is placed automatically for every agent proposal that reaches PendingApproval
(`ProposalStockHolds`, called from `AgentCallbackService` inside the serializable transaction that
records the result). Its `AgentRunId` ties it to the run, and the console timeline shows *Stock
held* with the batches. From there:

| What happens | The hold |
|---|---|
| Agronomist approves | Committed by the approval transaction: those batches, those packs. |
| Agronomist rejects or requests a revision | Released (back on sale); a revised proposal is held again. |
| Nobody decides within 24 h | The sweeper expires it; an approval afterwards draws the stock afresh. |
| The dealer tries to commit or release it | Refused (422 `HELD_FOR_APPROVAL`): it is the agronomist's decision. |

**"Why hold at PendingApproval rather than draw at approval?"** Between the agent's proposal and
the agronomist's click, possibly hours later, the dealer keeps selling. Without a hold, the last
packs could be sold over the counter, and an approved prescription would have nothing to collect.
The hold keeps them off sale, and the 24-hour limit stops an undecided run from locking stock up
for good. The approval re-check counts the run's *own* hold as available (`AgentPrescriptionGate`),
so V9 does not fail against it.

**Why held stock is invisible to the agent:** every stock query — V9 in the gate, the agent's
`check_stock_availability` tool, the approval's lock — counts `QuantityOnHand − QuantityReserved`.
`Held_stock_is_invisible_to_the_agent_s_stock_check` proves it.

## 3. How overselling is prevented (the likely viva question)

| Layer | What it stops |
|---|---|
| `FOR UPDATE` on the batch rows | Two holds for the last packs: the second waits for the first, then sees what is left. |
| Serializable isolation + retry (`SerializableTransaction`) | Any interleaving the locks miss: the loser gets 40001, retries on fresh data, and is refused if there is no stock left. |
| `xmin` row version on batches and reservations | A recount saved over a hold made meanwhile: 409 and "reload". |
| `CHECK quantity_reserved <= quantity_on_hand` and `quantity_on_hand >= 0` | The database refuses the row even if all of the above had a bug. |

`Two_holds_racing_for_the_last_packs_never_oversell` fires two holds of 2 packs at a batch of 3,
and checks for one 201, one 422, and exactly 2 held.

## 4. Orders

The approval transaction creates the order **Confirmed**, with its stock already drawn. The
dealer's `/orders` screen moves it to **Packed**, then **Collected** (`POST
/api/orders/{id}/fulfil { status }`). The request names the *target* status, so a double click
asking for Packed twice is harmless and cannot skip to Collected. `OrderStatusRules` explains every
refusal (422 `ILLEGAL_ORDER_TRANSITION`).

## 5. Who may do what

| Endpoint | Policy | Row scope |
|---|---|---|
| `GET /api/products` | any signed-in user | — |
| product writes, `/api/product-crop-approvals/*` | `AdministersRules` (Co-op Administrator) | — |
| `/api/inventory/*`, `/api/orders/*` | `ManagesInventory` (Agro-Dealer) | own shop only (`DealerScope`); another shop's row is 403 |

## Live modification drills (practise these)

- **The rule task itself:** in `/rules`, set Mancozeb 80 WP on Tomato's *Maximum dose* to 1.8 and
  save. Validate (or run the agent on) a 2.0 kg/ha proposal: V3 now fails with "…is outside the
  approved range for Mancozeb 80 WP". Put it back to 2.5 afterwards.
- **Tighten the PHI:** set the pre-harvest interval to 21 days. A spray within 21 days of the
  planned harvest now fails V5 and is **rejected** (not just revised).
- **Make holds last 48 hours:** change `ReservationLimits.HoldFor` in `InventoryContracts.cs`.
- **Warn 60 days ahead instead of 30:** change `BatchExpiry.WarningDays` and `WARNING_DAYS` in
  `InventoryPage.tsx`. `BatchExpiryTests` shows which cases move.
- **Add a "Ready" step between Packed and Collected:** add the enum value, one entry in
  `OrderStatusRules.NextStep`, a timestamp column (migration), and a label in `types.ts`.
- **Sell the freshest stock first:** flip `OrderBy(b => b.ExpiryDate)` in
  `StockAllocation.PlanFefo`. Holds *and* approvals both change, because both call the same
  function, and `A_hold_draws_the_batch_closest_to_expiry_first` fails.
