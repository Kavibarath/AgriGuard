# Study note — the Coordinator's triage (Component B's agent)

Viva-critical piece: the plan says the Coordinator "decides the terminal outcome". Before this, it
only wrote a plan. Now it also decides, after the diagnosis, whether a product can treat the case
at all, or whether a person must look first. Read this with the code open.

| File | What it holds |
|---|---|
| `agent/app/graph.py` | `triage` node, `route_after_triage`, `escalated`; `primary_confidence`. |
| `agent/app/contracts.py` | `Triage` (the model's output), `TriageDecision` (what the run does), `is_safe_advice`. |
| `agent/app/prompts.py` | `TRIAGE_SYSTEM`. |
| `backend/src/AgriGuard.Infrastructure/Agent/AgentToolService.cs` | `GetPathogenProfileAsync`: the Coordinator's second tool. |
| `backend/src/AgriGuard.Infrastructure/Agent/AgentCallbackService.cs` | Outcome `ManualReview` → run `Escalated`; stores `FarmerAdvice`. |
| Migration `AgentRunFarmerAdvice` | The `agent_runs.farmer_advice` column. |
| `web/src/features/agent-runs/TriageCard.tsx` | The console's Triage card. |
| `mobile/lib/features/cases/case_widgets.dart` `AdviceCard` | "What you can do now" on the phone. |
| `agent/tests/test_coordinator_triage.py` | Every rule of the triage, one test each. |

## The graph now

```
START → Coordinator (plan) → Diagnosis → Coordinator (triage) ─TREAT─→ Action → Validation …
                                                   └─AGRONOMIST─→ run ends "Escalated", case to manual review
```

The step numbers are Coordinator 1, Diagnosis 2, triage 3, Action 4, Validation 5. The backend
shows the run as `Triaging` while step 3 runs.

## How the decision is made

1. **Facts from a tool.** `get_pathogen_profile` returns the pathogen's type and how many active,
   unrestricted products approved for this crop target it. It uses the same filter as the Action
   agent's `search_approved_products`, so "no product" means Action could not have found one either.
2. **Hard stops, in code** (they win, whatever the model says):
   - the diagnosis is not in the catalogue;
   - no approved product controls it on this crop (bacterial wilt, leaf curl virus);
   - the Diagnosis agent's confidence in its own primary pick is below
     `min_diagnosis_confidence` (0.5, configurable as `AGENT_MIN_DIAGNOSIS_CONFIDENCE`).
3. **The model's judgement** (`Triage`): with no hard stop, it chooses TREAT or AGRONOMIST, and
   it may escalate, for example when the farmer's note describes the whole field collapsing. In
   one line: **the model may be more cautious than the rules, never less.**
4. **The advice.** The model writes 2–4 non-chemical tips. `is_safe_advice` drops any tip that
   mentions a pesticide (fungicide, insecticide, …, "spray") or an amount with a unit ("2 g",
   "30 ml"). The number dropped is recorded. The only chemical advice a farmer ever gets is an
   approved, validated prescription.

The `TriageDecided` timeline event records the route, the reason, `decided_by` (rules or
coordinator), what the model itself said (`model_route`), whether a rule overrode it
(`overridden`), and `advice_dropped`.

If the model is unreachable, the hard stops still decide. With no hard stop the run goes on, and
fails safely at the Action step, which needs the model too.

## Why "Escalated" is its own status

A deliberate hand-off is the workflow doing its job, not a failure. So:

- **Console:** it shows an amber "Handed to an agronomist" alert, not a red "Run failed".
- **Case report:** the run counts as finished, but not as a prescription.
- **Phone:** it says "The AI thinks an agronomist should look at this first", with the reason and
  the tips.

The case goes to `AwaitingManualReview`, like after a failure, so a new run can be started later.

Live, 29 Sept: a case with `plant_wilting` and `stem_vascular_browning` on tomato was escalated in
45 s. The rule ("No approved product controls Bacterial wilt on Tomato") decided, the model agreed,
and the four tips were practical and chemical-free. A late-blight case passed triage and reached
PendingApproval in 37 s.

## Questions to expect

- *"Isn't the Coordinator just a planner?"* It now owns a decision with its own tool, its own
  typed output and its own route in the graph. That decision has real consequences: nothing is
  drafted, no stock is held, the farmer gets advice.
- *"Why not let the model decide alone?"* The things that make treatment impossible are facts
  (no approved product, a weak diagnosis). Facts are checked in code. The model adds judgement
  where facts run out.
- *"Could an injected note make it prescribe for bacterial wilt?"* No. The hard stop comes before
  the model, and with no approved product the Action agent has nothing to choose anyway.

## Practice changes

- Raise the confidence threshold to 0.7 (`min_diagnosis_confidence`) and watch
  `test_an_uncertain_diagnosis_goes_to_an_agronomist` still pass while more cases escalate.
- Add a hard stop: severity `Critical` always goes to an agronomist. The severity is in
  `case_detail`, so put the check next to the others in `triage`.
- Tighten `is_safe_advice` to also drop tips naming a product from the catalogue.
