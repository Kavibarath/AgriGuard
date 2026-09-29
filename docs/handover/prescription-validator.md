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

## How the agent reaches it

The agent calls `POST /internal/tools/validate-prescription` with its `X-Agent-Key`. That route
goes through `AgentPrescriptionGate` (`backend/src/AgriGuard.Infrastructure/Agent/`), which judges
nothing itself. It:

1. refuses a proposal for a crop cycle other than the one on the run's own case (422
   `PROPOSAL_OUTSIDE_CASE`), which is the part of V10 only the agent path can know;
2. passes a mangled product id or date through as empty, so this validator's V1 reports it
   instead of the request failing with a 400;
3. looks up the best dealer's in-date, unreserved stock and passes it in, so **V9 is always
   evaluated** on the agent path. No stock is a V9 failure, not "not evaluated";
4. trims the verdict to the fields the agent's `Verdict` model accepts (it forbids extras).

The same gate runs a second time when the agent reports a proposal as ready for approval
(`AgentCallbackService.AcceptProposalAsync`). If this validator does not return `Approved`, the run
fails instead of reaching an agronomist, so a compromised agent cannot claim a pass.

## The Validation agent's own reasoning (Student A's agent)

The Validation agent (`agent/app/graph.py`, node `validation` and `review_verdict`) does two things.

1. **It submits the proposal** to this validator through `validate_prescription`. The verdict it
   gets back decides the route (pass → wait for a human, revise → back to the Action agent,
   reject → end). That route never comes from the model.
2. **It reasons about the verdict.** It fetches the rules-table row with its second tool,
   `get_rule_limits` (`GET /internal/tools/rule-limits`), and asks the model for a `SafetyReview`
   (`agent/app/contracts.py`). The review holds the decision restated, one fix per failed rule
   with a suggested value, and a plain explanation with the real numbers. Example from a live run:
   *"0.6 L/ha is within the 0.4 to 0.8 L/ha limit, and spraying on 2026-10-05 leaves the 3-day
   pre-harvest interval well before the 2026-11-17 harvest."*

**The guardrail** (`SafetyReview.problem_with`). The review is discarded, and the discard recorded
on the timeline as `SafetyReviewed` with the reason, when:
- its decision differs from the verdict (e.g. it says PASS on a Revise);
- it cites a rule that did not fail;
- it gives no fix on a Revise, or gives fixes on a pass.

So a model steered by an injected note cannot reword a failure into a pass. If the model is down,
the review is skipped and the run carries on, because the verdict does not depend on it.

**Where the fixes go.** On a revision the Action agent is told the rule engine's own messages
first (always), then the Validation agent's suggestions (`revision_guidance` in `contracts.py`). The
review can add advice; it can never remove or replace the validator's words. The next proposal
is validated again from scratch anyway.

**Where you see it.** The console's "Safety rules" card shows "Validation agent's reading" under
the eleven rules. The run's timeline shows each `SafetyReviewed` event, accepted or set aside.

**Viva questions to expect.**
- *"Is your Validation agent just an HTTP call?"* No. It has its own tools (the validator and the
  rules table), its own typed output (`SafetyReview`), and its own reasoning (turning failures
  into concrete fixes). But deliberately it has no authority: the decision is deterministic.
- *"Why let the model near safety at all?"* It explains and advises; it does not decide. The
  explanation helps the agronomist approve faster, and the fixes help the Action agent converge in
  fewer revisions. The consistency check means a wrong or manipulated review is thrown away.
- Tests: `agent/tests/test_validation_agent.py` (a review that says PASS on a Revise is discarded
  and the run still follows the verdict).

**Practice change.** Make the check stricter: also discard a review whose `suggested_value` for V3
lies outside the rule's dose range (read it from the `get_rule_limits` answer).

## What is not done yet

- On the user route (`/api/prescriptions/validate`), V9 is still only evaluated when the caller
  supplies stock figures.
