# Study guide: the deterministic prescription validator

Viva-critical piece 2 of 4. If an examiner probes one thing in this project, it will be this:
*how do you stop an LLM prescribing something dangerous?* The answer is this file.

## The one-sentence version

Every prescription the agent proposes — however confident, however well argued — passes through
plain C# that reads the `ProductCropApproval` regulatory table and the plot's real application
history. No model call, no heuristic, no prompt. Same inputs, same verdict, every time.

## Where it lives

| File | What it is |
|---|---|
| [PrescriptionSafetyValidator.cs](../../backend/src/AgriGuard.Domain/Validation/PrescriptionSafetyValidator.cs) | The eleven rules. Pure functions, no EF, no clock, no HTTP |
| [PrescriptionValidationContracts.cs](../../backend/src/AgriGuard.Domain/Validation/PrescriptionValidationContracts.cs) | Proposal, context snapshot, verdict |
| [PrescriptionValidationService.cs](../../backend/src/AgriGuard.Infrastructure/Validation/PrescriptionValidationService.cs) | Gathers the facts from the database, prices the order |
| [PrescriptionValidationController.cs](../../backend/src/AgriGuard.Api/Controllers/PrescriptionValidationController.cs) | `POST /api/prescriptions/validate` |

## The eleven rules

| ID | Checks | Severity | Why that severity |
|---|---|---|---|
| V1 | Required fields, positive numbers, spray date not in the past | Reject | A malformed proposal cannot be repaired by arguing |
| V2 | `(product, crop)` has an **active** approval row | Reject | Withdrawn chemicals never come back |
| V3 | Dose within the label's min–max | Revise | The agent can propose a corrected dose |
| V4 | Quantity ≈ dose × plot area (±2% for pack rounding) | Revise | Usually a decimal-point slip; recoverable |
| V5 | Spray date + pre-harvest interval ≤ harvest date | **Reject** | Residue on food someone eats. Never negotiable |
| V6 | Applications of this **active ingredient** this cycle < max | Reject | The regulator's seasonal ceiling |
| V7 | Gap since last use of the same ingredient ≥ minimum | Revise | Resistance management; shifting the date fixes it |
| V8 | Rain < 40% in the rainfast window, wind < 15 km/h, temp ≤ 32 °C | Revise | Wasted spray, not unsafe food |
| V9 | Dealer stock covers it, from a batch not expiring first | Revise | Another dealer or date may work |
| V10 | Farmer owns the plot; restricted product needs a permit | Reject | Authorisation, not agronomy |
| V11 | Estimated cost ≤ the farmer's credit limit | Revise | Commercial, not safety |

**Reject is terminal. Revise sends it back to the Action agent, at most twice** (§9.4).

## Decisions to be ready to defend

**V6 and V7 count the active ingredient, not the product label.** Two brands of mancozeb are one
chemical. Counting by product would let a farmer double the regulator's seasonal limit by
switching brands, which is precisely the loophole resistance limits exist to close.

**`NotEvaluated` is a third state, not a pass.** V8 without a forecast and V9 without stock
figures report that they could not be checked. A validator that silently treats "I couldn't
check" as "fine" is worse than one that admits the gap, because nobody reviewing the verdict
would know.

**Rules that depend on a missing approval cascade to `NotEvaluated`.** If V2 fails there is no
dose range or interval to measure against, so V3–V8 say so instead of inventing an answer.

**The context is a snapshot, not a `DbContext`.** The validator physically cannot make an extra
query or read ambient state, so its verdict is a pure function of inputs you can write down in a
test — which is what makes it a control rather than an opinion.

**Failures carry `Evidence`.** `sprayDate=2026-09-22; phi=14; harvest=2026-09-27; shortfall=9`
sits beside the human-readable message, so a verdict can be audited months later without
re-running anything.

**Restricted products currently always fail V10**, because permits are not modelled yet. Failing
closed on a permission we cannot verify is the right default.

## Why this beats "ask the LLM to check"

1. **Determinism** — a test can assert the verdict. You cannot unit-test a model's opinion.
2. **Auditability** — every rule reports pass/fail/not-checked with its numbers.
3. **Injection resistance** — a farmer note that talks the model into a 10× dose still meets
   arithmetic at V3. The test `A_ten_times_overdose_is_caught_however_convincingly_it_was_argued`
   is exactly that scenario.
4. **Defence in depth** — even a fully compromised model cannot act: issuing a prescription and
   committing stock happen in ASP.NET Core *after* a human approves, outside the agent's reach.

## The rules table is data, not code

Every limit is a row in `ProductCropApproval`. Change a pre-harvest interval in the admin screen
and this endpoint's answers change with no deployment. The test
`Editing_the_rules_table_changes_the_verdict_without_a_code_change` tightens tomato mancozeb from
7 days to 21 and watches the same proposal flip from Approved to Rejected — **that is the viva's
"modify a business rule live" task, already automated.**

## Questions to expect

1. The model proposes a dose ten times the maximum and gives a persuasive agronomic justification.
   What happens, and at which line?
2. Why is V5 a Reject when V3 is only a Revise? Both are "wrong numbers".
3. A farmer sprays Mancozeb 80 WP twice, then proposes Dithane M-45 (also mancozeb). Which rule
   catches it, and why would counting by product be wrong?
4. V8 and V9 are currently `NotEvaluated`. Is that a bug? What would make them evaluate?
5. Someone edits the `ProductCropApproval` row for tomato + mancozeb to PHI 30. What changes, and
   what redeployment is needed?
6. How would you test that the validator is deterministic? *(There is such a test — find it.)*

## Practice changes (do these cold, then `git checkout .`)

- Add rule V12: refuse spraying within 48 hours of a previous application of **any** product.
  Which severity, and why?
- Make V4's tolerance configurable per product instead of a fixed 2%.
- Change V1 so a spray date more than 30 days out is rejected. Which test breaks first?
- Turn V11 into a Reject and explain to an examiner why that would be the wrong call.

```bash
dotnet test backend/tests/AgriGuard.UnitTests --filter "FullyQualifiedName~PrescriptionSafetyValidator"
```

## What is not done yet

- `POST /internal/tools/validate-prescription` — the agent-facing route with its `X-Agent-Key`.
  The logic is identical; only the authentication differs. It arrives with the agent tool surface.
- V8 needs the Open-Meteo client; V9 needs Component C's inventory. Both already have their input
  shapes defined, so wiring them is passing a populated record instead of `null`.
