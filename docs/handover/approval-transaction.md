# Study note — the approval decision and transaction

Viva-critical piece. Read this with the code open.

| File | What it holds |
|---|---|
| `backend/src/AgriGuard.Infrastructure/Cases/ApprovalService.cs` | The decision: approve (the transaction), reject, revise; idempotency and race handling. |
| `backend/src/AgriGuard.Domain/Inventory/StockAllocation.cs` | Pure FEFO allocation and whole-pack rounding. No database. |
| `backend/src/AgriGuard.Api/Controllers/AgentRunsController.cs` | `POST /api/agent-runs/{id}/decision`: the policy and the `Idempotency-Key` header. |
| `backend/tests/AgriGuard.IntegrationTests/ApprovalDecisionTests.cs` | One test per promise below, including two approvals racing. |
| `backend/tests/AgriGuard.UnitTests/Inventory/StockAllocationTests.cs` | FEFO order, spill-over, all-or-nothing, pack rounding. |

## The endpoint

`POST /api/agent-runs/{id}/decision` with `{ "decision": "Approve" | "Reject" | "Revise", "reason": "…" }` and an `Idempotency-Key` header (a new UUID per decision).

- **Who:** Field Agronomist only (policy `CanApprovePrescriptions`), and only for runs in their own district (row scope). A farmer, an administrator or an agronomist from another district gets 403.
- **Reason:** required for Reject and Revise, at least 10 characters. A Revise reason is at most 300 characters, because it is passed to the agent as `reviewer_note`.

## Approve = one serializable transaction

All seven steps commit together, or none do:

1. **Re-validate** the stored proposal through the same gate and validator the agent used (#21, V1–V11). Time has passed: stock may be gone, another spray may be scheduled, the spray date may now be in the past. If the proposal fails, the answer is **422 `PROPOSAL_NO_LONGER_VALID`**. A proposal from 23 Sept approved on 25 Sept fails V1 exactly like this.
2. **Lock the batch rows** with `SELECT … FOR UPDATE` (raw SQL, because LINQ has no locking clause), then draw **first-expiry-first-out**: the oldest stock is sold while it is still in date. The farmer buys **whole packs** (0.48 L in 0.25 L packs is 2 packs = 0.5 L), so whole packs are what leave the shelf.
3. **A `StockReservation`, created already Committed**, records exactly which batches were drawn. Component C's reservation flow will create it earlier, as Held; the audit trail is the same shape.
4. **The `Prescription`** is issued, with `EarliestSafeHarvestDate = SprayDate + PHI` from the rules table. Its instructions are written from the rules table, **never from the model's text**, because this is what the farmer acts on.
5. **The `InputOrder`** is confirmed with the dealer: packs × pack price.
6. **A `ChemicalApplication`** is scheduled on the crop's record, so the *next* proposal's V6/V7 count this spray.
7. The case becomes **Prescribed**, the run **Completed**, and an `ApprovalDecision` plus timeline events are appended.

**Reject** ends the run (case → Rejected; nothing is committed). **Revise** sends the *same run* back to the agent with the reason as `reviewer_note` (case → AgentProcessing), at most twice. After that, reject and prescribe manually.

## How "exactly once" is guaranteed (the likely viva question)

Three layers, cheapest first:

| Situation | What stops a double commit |
|---|---|
| The phone retries after a dropped connection (same key) | The key is looked up first and **the stored result is returned** (`Idempotent-Replayed: true`). Nothing runs twice. The key is also re-checked *inside* the transaction, for two same-key requests arriving together. |
| Someone decides a run that is already decided (new key) | The status check refuses it with 409. |
| Two agronomists click Approve at the same instant | **Serializable isolation + the run's `xmin` row version + `FOR UPDATE` on the batches.** One transaction commits; the other gets a serialization failure or a stale row version, retries, now sees the run Completed, and answers 409. The unique index "one prescription per run" is the last backstop. |

`Two_approvals_racing_each_other_commit_exactly_once` fires both requests with `Task.WhenAll` and checks for one 200, one 409, one prescription, and one stock draw.

## Why it is built this way

- **"Why can't the agent approve?"** It has no tool that reaches this code. `issue_prescription` and `reserve_stock` do not exist in the agent. Only a signed-in Field Agronomist can call this endpoint.
- **"Why re-validate if the agent already validated?"** The world changed in between. The proposal from 23 Sept shows it: valid when proposed, refused at approval.
- **"Why serializable and not the default (read committed)?"** Under read committed, two approvals could both read "PendingApproval" and "50 kg in stock" and both commit. Serializable, plus the row locks, makes the second one fail instead.
- **"What if it crashes halfway?"** Nothing halfway is ever visible: the transaction rolls back and the run stays PendingApproval. `If_the_stock_is_gone_by_approval_time_nothing_is_committed` checks that no decision, prescription or stock change survives a failure.
- **"Why FEFO?"** Selling the oldest stock first means dealers don't throw away expired chemicals. It is pure and deterministic, so it is unit-tested alone.

## Live modification drills (practise these)

- **Allow three human revisions:** change `ApprovalLimits.MaxHumanRevisions` from 2 to 3.
- **Draw from the freshest batch first instead:** change `OrderBy(b => b.ExpiryDate)` to `OrderByDescending` in `StockAllocation.PlanFefo`. `The_batch_closest_to_expiry_is_drawn_first` fails, which shows the test pins the policy.
- **Add the agronomist's name to the instructions:** extend the `Instructions` string in `IssueAsync`.
- **Make Reject need 20 characters:** change `MinimumLength(10)` in `DecideRequestValidator`.
