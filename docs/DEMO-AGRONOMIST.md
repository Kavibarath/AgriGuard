# Demo case: a field agronomist's day

One story that visits **every page, tab and card** the field agronomist has on the web console.
Each step says what to do, and **(view only)** marks the cards and tabs that only show
information and have no action.

- **Account:** `agronomist.anu@agriguard.demo`, password `AgriGuard!Demo1` (Anuradhapura district).
- **Time:** about 25 minutes.
- **Records used:** the real ones in the demo database on 5 Oct 2026. Case and booking numbers may differ if more have been made since.

---

## Before you start: make one fresh proposal

The older proposals awaiting approval (AG-2026-000006, 000008, 000009 and 000011) were drafted on
28–29 Sept, so their spray dates have passed. Use those for **Reject** and **Request revision**, and
make a fresh one to **Approve**:

1. Phone, as `farmer@agriguard.demo`:
   - **Report a crop problem** on plot **A-01** (tomato);
   - tick leaf spots and choose **Spreading**;
   - **Report problem**, then **Get AI advice**.
2. Wait about a minute, until the timeline says **Treatment proposed**. Note the case number, called **AG-NEW** below.

The API, the agent service and Ollama must be running (see `MANUAL-TEST-GUIDE.md` §1).

---

## Step 1. Sign in, and the frame around every page

1. Open `http://localhost:5173`, press **Sign in**, and sign in as the agronomist.
2. Look at the frame:
   - **Sidebar:** Dashboard; **Field work** → Crop cases, Agent runs; **Farms and harvest** → Farms & plots, Harvests, Collection planner; **Insight** → Disease intelligence. There is no Inventory, Orders or Regulatory rules: those belong to other roles.
   - **Breadcrumb** (view only): where you are.
   - **Search box** ("Case, plot or farmer"): type `A-01` and press Enter. It opens **Crop cases** filtered to A-01. Clear the search afterwards.
   - **Bell** with a number: the count of **proposals awaiting your decision**. Click it to open **Agent runs**.
   - **Sign out** (bottom left): leave it for the end.

**Say:** "Each role sees only its own work. The API enforces the same rules, so typing another role's address gives *Not available for your role*."
To show this, type `/inventory` in the address bar, then go back.

---

## Step 2. Dashboard

| Card | Action |
|---|---|
| **Opening card:** a role photo with the district's name | **(view only)** |
| **Awaiting your decision** tile | Click it: opens **Agent runs** |
| **In manual review** tile | Click it: opens **Crop cases** filtered to manual review |
| **Agents working** tile | Click it: opens runs in progress (usually 0) |
| **Disease pressure** tile: the district's level and trend | **(view only)** |
| **Proposals awaiting your decision** list | Click a row: opens that run |
| **New reports** list | Click a row: opens that case |
| **Spray weather** panel: the coming days' spray suitability at the longest-waiting case's plot | **(view only)** |

**Say:** "The agronomist sees what is waiting, and the weather that decides when spraying is safe."

---

## Step 3. Agent runs: the review queue

1. **Agent runs**. Go through every entry of the **Show** filter:
   - **Awaiting your decision**: AG-NEW plus the older ones;
   - **Agents working**: usually empty;
   - **In manual review**: AG-2026-000005, 000007, 000010 and 000014;
   - **Prescribed**: AG-2026-000013;
   - **Rejected**;
   - **All cases**.
2. **Search:** type `A-02`; only the onion plot's case remains. Clear it.
3. Click a column heading to sort. The table has Case, Farmer · plot, Crop, Severity, Reported, Case status and Agent run.

---

## Step 4. One run, panel by panel, then Approve (AG-NEW)

Open **AG-NEW** from **Awaiting your decision**. Go down the page:

| Panel | Action |
|---|---|
| **The case:** farmer, plot, crop, reported, **Symptoms** | **(view only)** |
| **Photos** | Click one to enlarge, if the farmer added any. Otherwise **(view only)** |
| **Triage:** treat or hand over, and why | **(view only)** |
| **Plan and steps:** what the Coordinator planned; each agent's step; the **candidate diagnoses** with confidence and evidence | **(view only)** |
| **Proposed treatment:** product, dose, total quantity, packs, order total, spray date, **do not harvest before**, and the agent's justification | **(view only)** |
| **Safety rules:** each rule V1–V11 with Result, Detail and On failure ("Hard stop: ends the run" / "The agent can revise this") | **(view only)**: point at V5 and V9 |
| **Timeline:** every status change, tool call and verdict (times in UTC) | **(view only)** |
| **Decision panel:** "Awaiting an agronomist's decision" | **Approve and issue** → confirm |

**Result:** a prescription `RX-2026-…` is issued, the dealer's held stock is committed, an order is created, and the spray is scheduled.

**Say:** "The AI proposed; the code checked eleven rules; I decide. Approval is one transaction, and a second click or a second agronomist cannot issue it twice."

---

## Step 5. The other two decisions (on the older proposals)

1. **Request revision:** open **AG-2026-000006** → **Request revision** → write guidance, such as *"Use a spray day after this week's rain"* → **Send back to the agent**.
   - The run goes back to the agents and returns with a new proposal in about a minute. Revision is allowed at most twice.
2. **Reject:** open **AG-2026-000008** → **Reject this proposal** → give a real reason, such as *"Spray date has passed; the farmer should report again if it spreads"* → confirm.
   - The run ends, its held stock is released, and the farmer sees the reason.
3. Open **AG-2026-000010**, under **In manual review**, to show a **hand-off**:
   - the **Triage** card says the agents handed it to a person: "No approved product controls Bacterial wilt on Tomato";
   - the advice sent to the farmer is listed;
   - there is no decision panel. **(view only)**

---

## Step 6. Crop cases

1. **Crop cases**. Use every filter: **Search** (case, plot or farmer), **Status**, **Crop** and **Severity**, then clear them.
2. Open **AG-2026-000013**, which is **Prescribed**:

| Card | Action |
|---|---|
| **What the farmer saw:** checklist symptoms, note, photos | **(view only)** |
| **Where it was reported:** a map with the phone's position beside the plot's centre | **(view only)** |
| **Agent runs:** every run on this case | Click one: opens that run |
| **Case history:** reported on the phone, agents asked, and so on | **(view only)** |
| **Close case** | Press it, then **Cancel** in the confirmation. Leave it open for the demo |

3. Open **AG-2026-000014**, which is **In manual review**, for the onion plot:
   - the failed run explains why (every product was blocked that day);
   - **Close case** → confirm, if you want to show closing. *Closing cannot be undone.*
4. **Take into manual review** appears only on a **new or rejected** case. After step 5, **AG-2026-000008** is Rejected: open it and press **Take into manual review**.

**Say:** "A case can't be closed while the agents are working or a decision is waiting; the status rules stop it."

---

## Step 7. Farms & plots

1. **Farms & plots:**
   - **Search** (farm or village) is the only action here;
   - the **summary tiles** (Farms, Plots, Area under plots, Growing now) are **(view only)**;
   - the **Plots on the map** card: click a pin to open that plot. A filled dot means a crop is growing; a ring means nothing is sown.
   - **Register farm** is the farmer's or the administrator's job, so the agronomist doesn't see it.
2. Open **Dry Zone Farm**: its **Plots** table. Click **A-01**. *(Add plot belongs to the farm's owner.)*
3. **Plot A-01:**

| Card | Action |
|---|---|
| **Crop cycle:** the stage steps, expected harvest, days to harvest, **Stage history** | **(view only)** for the agronomist: **Record sowing** and **Advance to …** are the farmer's buttons |
| **Safety profile:** the tabs **Can spray today**, **Blocked today** and **All approved products** | Switch between the three tabs |
| **Keep out of the field:** the re-entry warning after a spray | **(view only)** |
| **PHI-blocked days:** the calendar of days harvest is not allowed | **(view only)**: it now includes step 4's spray |
| **Treatment history** | Filter by **Status** (All sprays, Scheduled, Applied, Cancelled) and the **From** and to dates. The prescription from step 4 shows as **Scheduled**, with "Safe to harvest from" |

**Say:** "This is the same rule engine the AI's proposals go through: what can be sprayed today, and when it is safe to harvest."

---

## Step 8. Harvests

1. **Harvests:** go through the **Show** tabs **Coming harvests**, **Past harvests** and **All forecasts**, and use the **Crop** and **District** filters.
2. On a row with no actual yield yet, press its record button: **Record the actual harvest** → **Harvested (kg)**, for example `380` → **Save**.
3. **Comparing forecasts:** **Overall bias** and **Typical miss** update. **(view only)**

**Say:** "Recording what was really harvested shows how good the forecasts are."

---

## Step 9. Collection planner: the check-in step

1. **Collection planner**. Use the **Week** arrows, and the **District** and **Centre** filters.
   - The tiles **Capacity this week**, **Booked**, **Room left** and **Full slots** are **(view only)**.
   - The **slot grid** is **(view only)**: every centre has its standard 07:00, 09:00 and 11:00 slots, opened automatically 60 days ahead.
   - **Open a slot** is the administrator's button, so the agronomist doesn't see it.
2. Go to the week of **28 Sept**. In the **Bookings** table:
   - **BK-2026-000001** (1 Oct, 400 kg onion): **Check in**. The status becomes **Checked in**. Then **Record weight** → `380` → **Complete delivery**. It now reads **Completed**, with "380 kg delivered".
   - **BK-2026-000002** (1 Oct, 5 kg): **Missed** → **Mark missed**. It becomes **Missed**.
3. Move to the week of **17 Nov**. **BK-2026-000004** and **BK-2026-000005** say **"On the day"**: nothing can be recorded before their day. **BK-2026-000003** is **Cancelled**. **(view only)**
4. Optional, on the phone as the farmer: **Harvest & collection → My bookings** shows BK-1 **Completed**, "Delivered 380 kg, weighed at the centre", and BK-2 **Missed**.

**Say:** "Booked, then checked in, then weighed. Never before the day, never backwards, and only co-op staff can record it."

---

## Step 10. Disease intelligence

1. **Disease intelligence:** use the **Window**, **Crop** and **District** filters.
2. **(view only)** for everything else:
   - the headline figures: **Pressure index**, **Confirmed cases**, **Districts reporting**, **Main threat**;
   - the **Outbreak map**;
   - **Cases per day** (confirmed against reported);
   - the district table and the pathogen shares.
3. Open **How the index is worked out** (it expands).

**Say:** "Approving a case confirms its pathogen, which feeds this signal. The Diagnosis agent reads the same signal next time."

---

## Step 11. Sign out

Press **Sign out** at the bottom of the sidebar.

---

## What the agronomist cannot do (worth saying)

| Not theirs | Whose |
|---|---|
| Register farms, add plots, record sowing, advance a crop stage | The farmer (and the administrator for farms) |
| Report a problem, or pay for an order | The farmer, on the phone |
| Stock, deliveries, orders, cash, hand-over | The agro-dealer |
| Regulatory rules, opening extra collection slots | The co-op administrator |
| Approving a prescription | **Only** the agronomist. Not the administrator, the farmer or the AI |
