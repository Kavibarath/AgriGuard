# Study note — `PrescriptionSafetyValidator` (rules V1–V11)

Viva-critical piece. Read this with the code open.

| File | What it holds |
|---|---|
| `backend/src/AgriGuard.Application/Prescriptions/PrescriptionSafetyValidator.cs` | The eleven rules. Pure: no database, no clock, no HTTP. |
| `backend/src/AgriGuard.Application/Prescriptions/PrescriptionSafetyModels.cs` | Inputs (`PrescriptionProposalInput`, `PrescriptionSafetyContext`) and the verdict. |
| `backend/src/AgriGuard.Infrastructure/Prescriptions/PrescriptionSafetyChecker.cs` | Loads the context from PostgreSQL, then calls the validator. All I/O lives here. |
| `backend/tests/AgriGuard.UnitTests/Prescriptions/PrescriptionSafetyValidatorTests.cs` | One baseline that passes all 11, then each rule broken on its own. |

## How a proposal is checked

1. **Shape (V1)**: `CheckShape` parses the raw strings. A bad id or date becomes a *recorded V1 failure*, not an HTTP 400. That way the agent receives a verdict it can act on, and the agronomist can see what went wrong.
2. **Load**: `PrescriptionSafetyChecker` reads the crop cycle, the product, the `ProductCropApproval` row, this cycle's applications of the same **active ingredient**, the run and its case, dealer stock, and the farmer's credit limit.
3. **Validate**: every rule is reported: passed, failed, or skipped *with the reason*. The console shows all eleven.
4. **Outcome**: any failed **Reject** rule gives `Rejected`, and the run ends. Otherwise any failed **Revise** rule gives `Revise`, and the proposal goes back to the Action agent, at most twice. Otherwise the outcome is `Approved`.

| Rule | Checks | Severity | Data source |
|---|---|---|---|
| V1 | Fields present, parseable, plausible; spray date between today and today + 30 | Reject | the proposal |
| V2 | Product exists and is active; approval row exists and is active | Reject | `products`, `product_crop_approvals` |
| V3 | min ≤ dose/ha ≤ max | Revise | approval row |
| V4 | total = dose × plot area, ±2 % | Revise | `plots.area_hectares` |
| V5 | spray date + PHI ≤ harvest date (planned, else expected) | Reject | approval row + crop cycle |
| V6 | uses of this active ingredient this cycle < max | Reject | `chemical_applications` |
| V7 | days since the last use of the same active ingredient ≥ minimum | Revise | `chemical_applications` |
| V8 | rain < 40 %, wind < 15 km/h, temp ≤ 32 °C in the rainfast window | Revise | **no forecast yet → Skipped** |
| V9 | a dealer has enough unreserved stock, in date on the spray date | Revise | `inventory_batches` |
| V10 | cycle active; run exists, is live, and belongs to this cycle's case; case farmer owns the plot; product not restricted | Reject | runs, cases, farms |
| V11 | packs × pack price ≤ farmer credit limit | Revise | `users.credit_limit` |

## Why it is built this way (likely viva questions)

- **"Why not let the LLM check safety?"** A model can be talked out of anything, including by the farmer's note (prompt injection). The Validation agent has exactly one tool, this validator, and only reports what comes back.
- **"What if the agent itself is compromised and just *says* the proposal passed?"** The backend re-runs this validator when the agent posts its result (`AgentCallbackService.AcceptProposalAsync`). If the backend's verdict isn't `Approved`, the run is marked **Failed** instead of going to an agronomist. The integration test `A_proposal_the_agent_calls_valid_but_the_rules_refuse_fails_the_run` proves this.
- **"Why pure?"** Same inputs, same verdict, so each rule is unit-tested without a database. That's 24+ focused tests that run in about a second.
- **"Why count the active ingredient, not the product?"** Two brands of mancozeb are the same chemical to the pathogen, and resistance builds against the chemical.
- **"Why is V8 skipped?"** There is no weather client yet (Component D's Open-Meteo integration). The plan (§10) chooses documented degradation over failing every run. The rule logic and thresholds exist and are tested. The loader just passes `Weather: null` today.
- **"Why does a malformed proposal return 200?"** A 400 makes the agent's tool client retry blindly three times. A verdict with V1 failed tells it what is wrong.

## Live modification drills (practise these)

- **Change a business rule without touching code:** edit a `product_crop_approvals` row (e.g. Mancozeb on tomato, `max_dose_per_hectare` 2.5 → 2.0), then re-run validate-prescription with dose 2.2. V3 now fails.
- **Tighten V4's tolerance:** change `QuantityTolerance` from `0.02m` to `0.01m`. The unit test `V4_a_total_within_two_percent_is_accepted` now fails, which shows the test pins the rule.
- **Add a rule:** add a `["V12"]` entry to `Rules`, write a `CheckX` method, add it to the list in `Validate`, and bump `RuleCount`.
- **Make V7 a Reject:** change its severity in the `Rules` table. `Conclude` derives the outcome from severities, so nothing else needs to change.
