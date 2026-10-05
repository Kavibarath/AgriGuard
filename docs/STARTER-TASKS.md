# Starter tasks: a step-by-step guide

Each student adds one to three new unit tests on a **boundary** in their own component. A boundary
is a case where `<` against `<=` decides the answer. None of these cases is tested yet.

The guide shows you where the rule is, which helpers your test file already has, and which
existing test to model yours on. It does **not** tell you the expected answer: reading the rule
and deciding is the task, and it is what the viva will ask you to do. Write the test yourself.

Each student edits a **different test file**, so your pull requests cannot conflict.

| Student | Test file | Branch name |
|---|---|---|
| A | `backend/tests/AgriGuard.UnitTests/Validation/PrescriptionSafetyValidatorTests.cs` | `test/a-validator-boundaries` |
| B | `backend/tests/AgriGuard.UnitTests/Cases/CaseStatusRulesTests.cs` | `test/b-case-status-boundaries` |
| C | `backend/tests/AgriGuard.UnitTests/Inventory/StockAllocationTests.cs` | `test/c-fefo-boundaries` |
| D | `backend/tests/AgriGuard.UnitTests/Harvest/HarvestWindowsTests.cs` | `test/d-slot-boundaries` |

---

## Step 0: Kavibarath gives each student access (once)

Signed in to GitHub as **Kavibarath** (the repository owner):

1. Open `https://github.com/Kavibarath/AgriGuard` and go to **Settings**, then **Collaborators**, then **Add people**.
2. Add each student's GitHub username, with the **Write** role.
3. Each student accepts the invitation from their email, or at `https://github.com/notifications`.

Check that the email in each student's GitHub account (**Settings**, then **Emails**) is the one
they will use in Step 2. Otherwise GitHub cannot link their commits to their profile.

## Step 1: install the tools (each student, once)

1. **Git:** from `https://git-scm.com/downloads`. Check it in a new terminal with `git --version`.
2. **The .NET 10 SDK:** from `https://dotnet.microsoft.com/download/dotnet/10.0`. The repo asks for 10.0.302 or a later 10.0 SDK (`global.json`). Check with `dotnet --version`.
3. An editor: **VS Code** with the C# Dev Kit extension, or **Rider**, or **Visual Studio 2022+**.

Open a **new** terminal after installing, so it can find the new commands.

## Step 2: get the code and set your identity (each student, once)

```bash
git clone https://github.com/Kavibarath/AgriGuard.git
```

```bash
cd AgriGuard
```

Set your own name and the email of **your** GitHub account, for this repository:

```bash
git config user.name "Your Full Name"
```

```bash
git config user.email "you@example.com"
```

Check it. This is what your commit will be signed with:

```bash
git config user.name && git config user.email
```

## Step 3: make your branch, and check that everything passes first

Use your branch name from the table above. Start it from `main`, which holds the current code.
If you cloned earlier, run `git fetch origin` first.

```bash
git switch -c test/a-validator-boundaries origin/main
```

Build and run all the unit tests. They need no database or Docker. The first run downloads
packages and takes a few minutes.

```bash
dotnet test backend/tests/AgriGuard.UnitTests
```

You should see `Passed!` with no failures. If not, see Troubleshooting at the end before changing anything.

## Step 4: your task

Do only your own section below. Read the rule, decide the answer, then write the test.

How tests in this project are written:
- `[Fact]` marks one test. `[Theory]` with `[InlineData(...)]` runs the same test for several inputs.
- **The name says what is true:** `Rejects_a_spray_date_in_the_past`, not `Test1`. Words are joined by underscores.
- **Arrange, act, assert:** build the inputs, call the rule, check the result. Look at the tests already in your file and write yours the same way.
- **Put your test inside the right class**, after the last test in it, before its closing `}`.
- **Only add tests.** Do not change anything under `backend/src`. If your test fails, either your expectation or your reading of the rule is wrong. Re-read the rule and decide which.

---

### A: the validator's rain boundary (V8)

**1. Read the rule.** Open `backend/src/AgriGuard.Domain/Validation/PrescriptionSafetyValidator.cs`.
- Line 25: `MaxRainProbabilityPercent`. Line 30: `MinWashOffRainMm`, and the comment above it.
- Line 240 decides whether the expected rain is "meaningful". Note its operator.
- Line 241 refuses the spray only when the chance of rain is high **and** the rain is meaningful. Note this operator too.
- Lines 236–238 explain why both conditions are needed.

**2. Know your helpers.** These are at the top of the test file:
- `Validate(proposal, context)` runs the validator. Leave either one out to get a proposal or context that passes everything.
- `Context(weather: ...)` builds the context with only the weather changed.
- `new WeatherAssessment(rainPercent, windKph, temperatureC, ExpectedRainMm: mm)` is a forecast, for example `new WeatherAssessment(5, 8m, 28m)`. The `m` makes a C# decimal. Keep wind and heat calm (8 km/h, 28 °C), so only the rain decides.
- `Rule(verdict, "V8")` picks the V8 result. Check `.Status` (`RuleStatus.Passed` or `RuleStatus.Failed`) and `.Message`. `verdict.Outcome` is the overall result.

**3. Model your tests on:** `Asks_for_a_revision_when_likely_rain_is_enough_to_wash_it_off` and
`Does_not_refuse_a_likely_drizzle_too_light_to_wash_the_spray_off`, in class `V8_Weather`.

**4. Write, inside `V8_Weather`:**
- a test where the chance of rain is **exactly** `MaxRainProbabilityPercent` and the expected rain is **exactly** `MinWashOffRainMm`: does V8 pass or fail?
- a test with the same chance of rain, but expected rain **just under** `MinWashOffRainMm` (for example 0.4 mm): pass or fail?

Use the constants (`PrescriptionSafetyValidator.MaxRainProbabilityPercent`, `PrescriptionSafetyValidator.MinWashOffRainMm`) rather
than typing 40 and 0.5, so the tests stay right if a limit changes. Name each test after what you found.

**5. Run only your tests:**

```bash
dotnet test backend/tests/AgriGuard.UnitTests --filter "FullyQualifiedName~PrescriptionSafetyValidatorTests"
```

**Be ready to explain at the viva:**
- why a likely drizzle does not stop a spray, but likely heavier rain does;
- what the rainfast window is, and why rain inside it matters;
- why V8 asks the agent to revise rather than rejecting outright;
- why this rule is checked in C# and not by the AI agent.

---

### B: hand-made status changes that must be refused

**1. Read the rule.** Open `backend/src/AgriGuard.Domain/Cases/CaseStatusRules.cs`.
- `ExplainManualChange(from, to, role)` returns **`null` when the change is allowed**. Otherwise it returns a message explaining why not.
- Line 28: moving a case to the status it is already in.
- Lines 42–49: who may send a case to manual review, and from which statuses.

**2. Know your helpers.** There are none to learn: call `CaseStatusRules.ExplainManualChange(...)`
directly with values such as `CaseStatus.Closed`, `CaseStatus.Prescribed` and `UserRole.FieldAgronomist`.
- `Assert.Null(x)` checks that a change is allowed.
- `Assert.Contains("some words", x)` checks that the refusal message says something specific.

**3. Model your tests on:** `A_closed_case_cannot_be_reopened` and `A_farmer_cannot_send_a_case_to_manual_review`.

**4. Write:**
- a test for closing a case that is **already `Closed`**: what comes back?
- a test for a **field agronomist** sending a **`Prescribed`** case to `AwaitingManualReview`: allowed or refused, and what does the message say?

Check the message by a few telling words, not the whole sentence, so a small wording change does not break your test.

**5. Run only your tests:**

```bash
dotnet test backend/tests/AgriGuard.UnitTests --filter "FullyQualifiedName~CaseStatusRulesTests"
```

**Be ready to explain at the viva:**
- why a prescribed case can be closed but not sent to manual review;
- why people may only set two statuses by hand;
- why these rules live in the Domain project with no database.

---

### C: FEFO with exactly enough stock

**1. Read the rule.** Open `backend/src/AgriGuard.Domain/Inventory/StockAllocation.cs`.
- `PlanFefo(batches, quantity)` plans which batches to draw an order from, earliest expiry first.
- Line 35: what happens to a quantity of zero or less.
- Line 43: when it gives up and returns `null`. Note the operator, and what it means when the batches hold **exactly** the quantity.
- Lines 46–56: how the plan is built.

**2. Know your helpers.** These are at the top of the test file:
- `Batch("B-OLD", daysToExpiry: 20, available: 5m)` is a batch; the arguments are its number, days to expiry and stock.
- The plan is a list of `new BatchDraw(batch.BatchId, quantity)`. `Assert.Equal([...], plan)` compares the whole plan, in order.
- If you find that a call throws an exception, `Assert.Throws<ExceptionType>(() => StockAllocation.PlanFefo(...))` checks that.

**3. Model your tests on:** `An_order_larger_than_the_oldest_batch_spills_into_the_next` and `Not_enough_stock_in_total_draws_nothing_at_all`.

**4. Write:**
- a test with two batches, of different expiry, whose stock adds up to **exactly** the order: is a plan returned, what does it draw from each, and in what order?
- a test for an order of **0**: what happens?

**5. Run only your tests:**

```bash
dotnet test backend/tests/AgriGuard.UnitTests --filter "FullyQualifiedName~StockAllocationTests"
```

**Be ready to explain at the viva:**
- why FEFO draws nothing at all, rather than a part, when stock is short;
- what FEFO protects against;
- where the approval transaction uses this plan (`backend/src/AgriGuard.Infrastructure/Inventory/StockLedger.cs`).

---

### D: collection slots at their limits

**1. Read the rule.** Open `backend/src/AgriGuard.Domain/Harvest/SlotAllocation.cs`.
- `Choose(slots, quantityKg, preferredDate, plotLat, plotLon)` picks one slot, or returns `null`.
- Line 36: which slots qualify. Note the operator on `RemainingKg`, and the date range up to `MaxDaysLater` (line 29).
- Lines 37–39: the order of preference. The last tie-breaker is `SlotIndex`.

**2. Know your helpers.** These are in class `SlotAllocationTests`, near the end of the test file:
- `Day` is the preferred day. `PlotLat` and `PlotLon` are where the plot is.
- `Slot(date, remaining, lat: ..., lon: ..., index: ..., centre: ...)` is a slot. To put two slots at the **same centre**, create one `Guid.NewGuid()` and pass it as `centre:` to both, with the same `lat` and `lon`.
- `Assert.Equal(expectedSlot, SlotAllocation.Choose(...))` checks which slot was chosen. `Assert.Null(...)` checks that none was.

**3. Model your tests on:** `On_the_same_day_the_nearest_centre_wins` and `A_full_preferred_day_moves_to_the_next_day_with_room_but_never_earlier_or_too_late`.

**4. Write, inside `SlotAllocationTests`:**
- a test with a slot that has **exactly** as much room as the harvest: is it chosen?
- a test with the only slot **exactly `MaxDaysLater` days** after the preferred day: is it allowed? Day +3 is already tested.
- a test with two slots on the same day at the **same centre**, with different `index`: which one wins?

**5. Run only your tests:**

```bash
dotnet test backend/tests/AgriGuard.UnitTests --filter "FullyQualifiedName~SlotAllocationTests"
```

**Be ready to explain at the viva:**
- why a harvest is never split across slots;
- why the preferred day beats a nearer centre;
- how a booking is refused if the harvest would fall inside the pre-harvest interval (`HARVEST_BEFORE_PHI`).

---

## Step 5: run everything once more

Your new tests must pass, and no other test may break:

```bash
dotnet test backend/tests/AgriGuard.UnitTests
```

## Step 6: commit, in your own words

Check that you changed only your test file:

```bash
git status
```

Add your file. Use your own path from the table at the top:

```bash
git add backend/tests/AgriGuard.UnitTests/Validation/PrescriptionSafetyValidatorTests.cs
```

Commit with a one-line message that says what your tests prove, in your own words, for example
"Pin the V8 wind and heat limits and the V9 expiry-day boundary":

```bash
git commit -m "Your one-line summary of what the tests prove"
```

## Step 7: push, and open a pull request

```bash
git push -u origin test/a-validator-boundaries
```

1. GitHub prints a link after the push; open it, or go to the repo and press **Compare & pull request**.
2. Leave **base** as `main` and set **compare** to your branch. The pull request should show only your one file.
3. Give it a title. In the description, say which boundaries you tested and what you found, and why.
4. Press **Create pull request**. The CI checks run on it; wait for them to go green.

## Step 8: Kavibarath reviews and merges

- Read the tests, and ask the student to explain one of them, as practice for the viva.
- Merge with **Create a merge commit**. The student's own commit then stays in `main`'s history under their name.
- After the merge, each student updates their copy before starting anything new:

```bash
git switch main
```

```bash
git pull
```

---

## Troubleshooting

| What you see | What to do |
|---|---|
| `dotnet` or `git` is not recognised | Open a new terminal after installing. On Windows, sign out and in again if that does not help. |
| `A compatible .NET SDK was not found` (mentions `global.json`) | Install the latest .NET **10.0** SDK; 10.0.302 or newer. |
| `Permission denied` or `403` on push | Accept the collaborator invitation (Step 0). Make sure you are signed in to Git as yourself; Git Credential Manager asks on the first push. |
| `remote rejected … protected branch` | You pushed to `main`. Push your own branch instead (Step 7). |
| Your new test fails | Re-read the rule's line. Either your expected answer or your reading is wrong. Do not change the code under `backend/src`. |
| Some *other* test fails | Run `git status` and `git diff` to make sure you changed only your test file. Then ask Kavibarath. |
| The commit shows the wrong name on GitHub | The email in Step 2 must match one in your GitHub account's email settings. Fix it with `git config user.email`, then `git commit --amend --reset-author --no-edit` before pushing. |
