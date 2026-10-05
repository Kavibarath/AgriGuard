# AgriGuard

**Crop advice, checked for safety.** A crop-health advisory and agro-input system for smallholder
farms in Sri Lanka.

A farmer reports a sick crop from their phone. Four AI agents work out what it probably is and
propose a treatment. Plain C# code checks that proposal against eleven safety rules, and a field
agronomist, a person, approves it before anything is prescribed. The prescription then becomes a
dealer order, paid by card or in cash and collected with a pickup code. When the crop is ready, the
farmer books a safe harvest day and a collection slot.

> *The AI proposes, the rules check, a person decides.*

SLIIT SE3090, Assignment 1, 2026. An academic project: all names, places and figures in the
demo data are sample data.

---

## Contents

- [Live system](#live-system)
- [What it does](#what-it-does)
- [How it works](#how-it-works)
- [The eleven safety rules](#the-eleven-safety-rules)
- [The four components](#the-four-components)
- [Technology](#technology)
- [Repository layout](#repository-layout)
- [Running it locally](#running-it-locally)
- [Demo accounts](#demo-accounts)
- [Card payments (optional)](#card-payments-optional)
- [Tests and CI](#tests-and-ci)
- [Documentation](#documentation)

---

## Live system

| | URL |
|---|---|
| Web console (React) | `https://<web>.vercel.app` *(to be filled in)* |
| API health | `https://<api>.onrender.com/health` *(to be filled in)* |
| API reference (Swagger) | `https://<api>.onrender.com/swagger` *(to be filled in)* |
| Agent service health | `https://<agent>.onrender.com/health` *(to be filled in)* |
| Android APK | GitHub **Releases** → `app-release.apk` *(to be filled in)* |

The services run on free plans: PostgreSQL on **Neon**, the API and the agent service on
**Render** (Docker, Singapore), the web console on **Vercel**, and the language model on **Groq**.
A free Render service sleeps after 15 minutes without requests, so **the first request after a
quiet spell takes about a minute**. Open the API health URL first and wait for it to answer. The
demo accounts below work on the live system too.

How it was deployed, every environment variable by name, and how to wake it before a demo:
[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

### The model in the cloud

The agent service's model provider is chosen by configuration:

| Setting | Local (default) | Deployed |
|---|---|---|
| `AGENT_LLM_PROVIDER` | `ollama` | `openai-compatible` |
| `AGENT_LLM_MODEL` | `qwen2.5:7b` | `llama-3.3-70b-versatile` |
| `AGENT_OLLAMA_BASE_URL` | `http://localhost:11434` | not used |
| `AGENT_LLM_BASE_URL` | not used | `https://api.groq.com/openai/v1` |
| `AGENT_LLM_API_KEY` | not used | the Groq key (a Render secret) |

Both providers ask for JSON-only output (Ollama's `format: json`, the OpenAI API's JSON mode), and
every reply is still validated against its Pydantic schema and repaired at most twice. The agents,
prompts and safety rules are identical in both places. On a hosted rate limit (HTTP 429), the
agent waits as the provider asks, a bounded number of times. Gemini's OpenAI-compatible endpoint
can replace Groq by changing the three `AGENT_LLM_*` values.

### Deployed environment variables (names only)

- **API:** `ConnectionStrings__Default`, `Jwt__SigningKey`, `AgentService__ApiKey`,
  `AgentService__BaseUrl`, `AgentService__DispatchTimeout`, `Cors__AllowedOrigins__0`,
  `Payments__PublicBaseUrl`, `Payments__Stripe__SecretKey`, `Payments__Stripe__WebhookSecret`,
  `ForwardedHeaders__Enabled`, `Swagger__Enabled`, `Calendar__TimeZone`, `Seed__DemoUsers`
- **Agent service:** `AGENT_API_KEY`, `AGENT_API_BASE_URL`, `AGENT_LLM_PROVIDER`,
  `AGENT_LLM_BASE_URL`, `AGENT_LLM_API_KEY`, `AGENT_LLM_MODEL`
- **Web (build time, Vercel):** `VITE_API_BASE_URL`
- **Phone (build time, GitHub Actions variable):** `API_BASE_URL`

---

## What it does

| Role | Uses | Can |
|---|---|---|
| **Farmer** | Phone app (Flutter) | Report a crop problem with symptoms, photos and GPS, even offline; follow it to a prescription; see the do-not-harvest date; pay for and collect orders with a pickup code; manage farms, plots and crop stages; book harvest collection |
| **Field agronomist** | Web console (React) | Review each AI proposal with its evidence and safety report, then approve, request a revision or reject; manage the district's cases; check in collections at the centre |
| **Agro-dealer** | Web console | Keep stock by batch (oldest expiry first); see stock held for proposals; pack orders; take cash; hand over on the farmer's pickup code |
| **Co-op administrator** | Web console | Keep the rules table of approved products and limits; oversee every district; open collection slots; watch disease outbreaks |

Only a **field agronomist** can approve a prescription: not the administrator, not the farmer, not the AI.

---

## How it works

```
 Farmer (phone)              API (ASP.NET Core + PostgreSQL)                      Web console
 ──────────────              ───────────────────────────────                      ───────────
 Report a problem ─► case ─► "Get AI advice" starts an agent run
                                   │
                                   ▼
                     Agent service (Python, LangGraph, local Ollama model)
                     Coordinator → Diagnosis → triage → Action → Validation
                     every fact through allow-listed /internal/tools calls; no database access
                                   │
                                   ▼
                     C# validator: rules V1–V11 ──► dealer stock held
                                   │
                                   ▼
                             PendingApproval ───────────────────────────────► Agronomist decides
                                                                              Approve / Revise / Reject
                     one serializable transaction ◄─────────────────────────────┘
                     prescription RX-…, order ORD-…, stock committed, spray scheduled
 Prescription and do-not-harvest date ◄┘
 Pay (card or cash), pickup code ◄──────────────────────────────────────────► Dealer packs and hands over
 Safe harvest days, book a slot ────────────────────────────────────────────► Collection planner, check-in
                     Outbreak signal and reports ───────────────────────────► Disease intelligence
```

**What keeps it safe:**
- **The model cannot touch the database.** The agent service has no connection string. It reads facts only through allow-listed tool endpoints, and each agent has its own allow-list: the Diagnosis agent physically cannot check stock.
- **The rules are code, not prompts.** Every proposal passes the deterministic C# validator, which reads the rules table and the plot's real spray history. The same inputs always give the same verdict.
- **A person decides.** Nothing is prescribed until a field agronomist approves. Approval is idempotent and concurrency-safe: a double click or two agronomists at once can never issue twice.
- **No privileged agent tools.** Reserving stock and issuing prescriptions are not agent tools at all; the API does them after approval.
- **Untrusted text stays data.** A farmer's note is bounded, fenced and flagged before it reaches the model, and any proposal it might influence must still pass the rules.
- **A safe way out.** When no treatment is possible right now (no approved product, everything blocked today, no spray day in the forecast, or no stock in the district), the run hands the case to an agronomist with the reason, rather than guessing.

---

## The eleven safety rules

| Rule | Checks | If it fails |
|---|---|---|
| V1 | The proposal is well formed: product, dose, quantity, spray date not in the past | Reject |
| V2 | The product is approved for this crop (and not withdrawn) | Reject |
| V3 | The dose is within the label range | Revise |
| V4 | The quantity matches dose × area (2% tolerance for pack sizes) | Revise |
| V5 | **The pre-harvest interval is respected:** no residue on food at harvest | Reject |
| V6 | The season's application limit is not exceeded (counted per active ingredient) | Reject |
| V7 | The resistance-management interval has passed | Revise |
| V8 | The weather suits spraying: not ≥ 40% chance of ≥ 0.5 mm rain, not wind ≥ 15 km/h, not above 32 °C | Revise |
| V9 | A dealer in the district has the stock, in date on the spray day | Revise |
| V10 | The farmer may treat this plot with this product (owner; permit for restricted products) | Reject |
| V11 | The order is within the farmer's credit limit | Revise |

*Reject* ends the run; *Revise* sends it back to the Action agent (at most twice).
Source: `backend/src/AgriGuard.Domain/Validation/PrescriptionSafetyValidator.cs`.

---

## The four components

| | Component | Non-CRUD operations | Agent |
|---|---|---|---|
| **A** | Farm, Plot & Crop-Cycle Registry | Guarded crop-stage transitions; the plot safety profile; the prescription validator (V1–V11) | Validation & Safety |
| **B** | Crop Health Cases & Agentic Workflow | Starting an agent run; the approve / revise / reject decision (idempotent, concurrency-guarded); offline report queue | Coordinator / Planner |
| **C** | Agro-Input Catalogue, Dealer Inventory & Orders | Stock held on proposal, committed on approval, released on rejection (FEFO); order fulfilment; card and cash payments | Action / Prescription & Procurement |
| **D** | Harvest Windows, Collection & Regional Intelligence | Safe harvest days (maturity ∩ PHI ∩ weather); capacity-constrained slot booking and check-in; the regional outbreak signal | Diagnosis / Field Intelligence |

Each component has a paginated, searchable, filterable list API, a status workflow and a report
endpoint. Which files belong to which component: [docs/COMPONENT-OWNERSHIP.md](docs/COMPONENT-OWNERSHIP.md).

---

## Technology

| Part | Stack |
|---|---|
| API | .NET 10, ASP.NET Core, EF Core 10, PostgreSQL 17, FluentValidation, Serilog, JWT with role policies, rate limiting, Polly resilience |
| Agents | Python 3.12, LangGraph, FastAPI, Pydantic (strict schemas), Ollama with `qwen2.5:7b` (local, CPU) |
| Web console | React 19, TypeScript 6, Vite 8, Tailwind CSS 4, TanStack Query, React Router, React Hook Form + Zod |
| Phone app | Flutter 3.47 (Dart 3.13), Riverpod, go_router, Dio |
| Outside services | Open-Meteo (weather), OpenStreetMap (map tiles), Stripe test mode (card payments, optional) |
| Tests | xUnit + Testcontainers (real PostgreSQL), pytest, Vitest + Testing Library + MSW, flutter_test |

---

## Repository layout

```
backend/
  src/AgriGuard.Domain/          pure rules: validator, stage rules, FEFO, harvest windows, slot allocation, payments
  src/AgriGuard.Application/     contracts and interfaces
  src/AgriGuard.Infrastructure/  EF Core, services, migrations, seeders, Stripe and Open-Meteo clients
  src/AgriGuard.Api/             controllers, auth policies, validation, error handling
  tests/                         unit tests and integration tests (Testcontainers)
  Dockerfile                     the API's container image (Render)
agent/                           the four LangGraph agents (FastAPI service), their tests and Dockerfile
web/                             the React web console (vercel.json: SPA rewrite for Vercel)
mobile/                          the Flutter phone app
docs/                            plan, design notes, study notes (handover/), guides
docker-compose.yml               local PostgreSQL
render.yaml                      Render Blueprint for the API and the agent service
```

---

## Running it locally

### Prerequisites

- **.NET SDK 10.0.302** or a later 10.0 feature band (`global.json`)
- **Docker Desktop**, for PostgreSQL
- **Node.js 24**
- **Python 3.12**
- **Ollama**, with the model pulled: `ollama pull qwen2.5:7b`
- **Flutter 3.47** and an Android emulator or phone, for the phone app

### 1. Database

```bash
cp .env.example .env
```

Edit `.env` and set `POSTGRES_PASSWORD`, then:

```bash
docker compose up -d
```

PostgreSQL listens on **localhost:5433**, so it doesn't clash with a native install on 5432.

### 2. API secrets (once, with your own values)

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5433;Database=agriguard;Username=agriguard;Password=<POSTGRES_PASSWORD>" --project backend/src/AgriGuard.Api
```

```bash
dotnet user-secrets set "Jwt:SigningKey" "<32+ random bytes, base64>" --project backend/src/AgriGuard.Api
```

```bash
dotnet user-secrets set "AgentService:ApiKey" "<32+ random characters>" --project backend/src/AgriGuard.Api
```

### 3. Create the database and the demo data

```bash
dotnet tool restore
```

```bash
dotnet tool run dotnet-ef database update --project backend/src/AgriGuard.Infrastructure --startup-project backend/src/AgriGuard.Api
```

In Development this also seeds the demo accounts, farms, stock, collection centres and 30 cases of
disease history.

### 4. Run the API (port 5000)

```bash
dotnet run --project backend/src/AgriGuard.Api --launch-profile http
```

Check it at `http://localhost:5000/health`; the API reference is at `http://localhost:5000/swagger`.

### 5. Run the agent service (port 8000)

```bash
cd agent
python -m venv .venv
.venv\Scripts\activate          # Windows; source .venv/bin/activate elsewhere
pip install -e ".[dev]"
```

Create `agent/.env` with the **same key** as `AgentService:ApiKey`:

```
AGENT_API_KEY=<the same value>
AGENT_API_BASE_URL=http://localhost:5000
AGENT_OLLAMA_BASE_URL=http://localhost:11434
AGENT_LLM_MODEL=qwen2.5:7b
```

`AGENT_LLM_PROVIDER` defaults to `ollama`, so nothing else is needed locally. To try the hosted
model instead, see "The model in the cloud" above.

```bash
uvicorn app.main:app --port 8000
```

Ollama must be running (`ollama serve`). On machines where GPU inference crashes, run it on the
CPU: set `OLLAMA_NUM_GPU=0` and `CUDA_VISIBLE_DEVICES=-1` before `ollama serve`. A full agent run
takes about a minute on a CPU.

### 6. Run the web console (port 5173)

```bash
cd web
npm ci
npm run dev
```

Open `http://localhost:5173`. The API URL can be changed in `web/.env.local` (`VITE_API_BASE_URL`).

### 7. Run the phone app

Start an Android emulator, then:

```bash
cd mobile
flutter pub get
flutter run
```

The app reaches the API at `http://10.0.2.2:5000`, the emulator's name for your computer. For a
real phone on the same network: `flutter run --dart-define=API_BASE_URL=http://<your-PC-IP>:5000`.

---

## Demo accounts

All demo accounts share the password **`AgriGuard!Demo1`**, locally and on the live system. They
are sample accounts on sample data, seeded for the demo and marking; card payments are test mode only.

| Account | Role | Notes |
|---|---|---|
| `farmer@agriguard.demo` | Farmer | Owns plot **A-01** (tomato) and **A-02** (onion) in Anuradhapura: the main demo |
| `agronomist.anu@agriguard.demo` | Field agronomist | Anuradhapura: reviews A-01's proposals |
| `dealer.anu@agriguard.demo` | Agro-dealer | Anuradhapura: holds the stock for A-01 |
| `admin@agriguard.demo` | Co-op administrator | Every district; the rules table |
| `agronomist@agriguard.demo`, `dealer@agriguard.demo` | Agronomist, dealer | Nuwara Eliya |
| `farmer.kandapola@agriguard.demo` and three more farmers | Farmer | Neighbours, for changes you don't want on the main demo plots |

The dry-zone plot A-01 is used for the main story because its weather usually allows a spray (V8).

---

## Card payments (optional)

Orders can be paid in cash at the counter without any setup. To also pay **by card** in the phone
app (Visa or Mastercard, through Stripe's hosted checkout in **test mode**):

1. Create a free Stripe account, stay in the **sandbox** (test mode), and copy the secret key (`sk_test_…`) from **Developers → API keys**.
2. Set it as a user-secret, then restart the API:

```bash
dotnet user-secrets set "Payments:Stripe:SecretKey" "sk_test_..." --project backend/src/AgriGuard.Api
```

3. Pay with the test card `4242 4242 4242 4242`, any future date, any CVC.

AgriGuard never sees a card number. An order counts as paid only when the API reads the payment
back from Stripe with its own key, and the amount matches. Live keys are refused at startup. With
no key, card payments are simply switched off. Details: [docs/handover/payments.md](docs/handover/payments.md).

---

## Tests and CI

| Part | Command | Notes |
|---|---|---|
| Backend unit | `dotnet test backend/tests/AgriGuard.UnitTests` | Pure domain rules, no database |
| Backend integration | `dotnet test backend/tests/AgriGuard.IntegrationTests` | Needs Docker: starts a throwaway PostgreSQL |
| Agents | `cd agent` then `pytest -q`, `ruff check .`, `ruff format --check .`, `mypy app` | No model or database needed |
| Web | `cd web` then `npm test`, `npm run typecheck`, `npm run lint`, `npm run build` | |
| Phone | `cd mobile` then `flutter test`, `flutter analyze` | |

If the full web suite times out on a busy machine, run it with fewer workers:
`npx vitest run --maxWorkers=2`.

GitHub Actions runs a separate workflow for the backend, the agents, the web console and the phone
app on every pull request to `main` and every push to it, plus a secret scan (gitleaks). The phone
workflow also builds the release APK as a downloadable artifact (`agriguard-mobile-apk`), pointed
at the live API through the repository variable `API_BASE_URL`. Two manual workflows support the
deployment: `warm-up` wakes the sleeping free services before a demo, and `db-migrate` applies the
migrations to the deployed database when a local network blocks PostgreSQL.

---

## Documentation

| Document | What it is |
|---|---|
| [docs/PROJECT-PLAN.md](docs/PROJECT-PLAN.md) | The full project plan and its decisions |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | The live deployment step by step (Neon, Render, Groq, Vercel, APK), its environment variables and the smoke test |
| [docs/MANUAL-TEST-GUIDE.md](docs/MANUAL-TEST-GUIDE.md) | An end-to-end manual test of the whole system, with a checklist |
| [docs/DEMO-AGRONOMIST.md](docs/DEMO-AGRONOMIST.md) | A step-by-step demo of every page the field agronomist has |
| [docs/COMPONENT-OWNERSHIP.md](docs/COMPONENT-OWNERSHIP.md) | Which files make up each component |
| [docs/STARTER-TASKS.md](docs/STARTER-TASKS.md) | Starter tasks for each student |
| [docs/handover/](docs/handover/) | Study notes on the key designs: the validator, the approval transaction, triage, stock, weather and V8, harvest and collection, outbreaks, payments, JWT |
| [docs/design/](docs/design/) | Design tokens, the UI brief and image credits |

---

## Credits

Weather by [Open-Meteo](https://open-meteo.com/). Map tiles © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright).
Home page video by Andi Farruku on [Pexels](https://www.pexels.com/video/green-plants-on-the-field-7983392/).
Photographs: see [docs/design/IMAGE-CREDITS.md](docs/design/IMAGE-CREDITS.md).
