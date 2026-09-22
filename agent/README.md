# AgriGuard agent service

The controlled agentic workflow: four LangGraph agents that diagnose a crop problem and **propose**
a treatment for a human to approve. It never acts on its own.

```bash
py -3.12 -m venv .venv
.venv\Scripts\activate          # Windows;  source .venv/bin/activate elsewhere
pip install -e ".[dev]"

pytest -q                       # 27 tests, no model or database needed
ruff check . && mypy app
uvicorn app.main:app --port 8000 --reload
```

Configuration is environment variables with an `AGENT_` prefix (see `app/config.py`):
`AGENT_API_BASE_URL`, `AGENT_API_KEY`, `AGENT_LLM_MODEL`, `AGENT_OLLAMA_BASE_URL`.

## The graph

```
START → Coordinator → Diagnosis → Action → Validation
                          ▲          │
                          └──REVISE──┘   (at most 2)
Validation ── REJECT ─► safe failure, recorded
Validation ── PASS ───► stops; a human decides
```

| Agent | Does | May call |
|---|---|---|
| Coordinator | Plans the run | `get_case_detail` |
| Diagnosis | Ranks candidate pathogens with evidence | case, crop history, weather, outbreak signal |
| Action | Picks an approved product, dose and spray date | products, safety profile, stock, pricing |
| Validation | Submits the proposal to the deterministic C# validator | `validate_prescription` |

## What makes it *controlled*

- **No database.** Every fact arrives through an allow-listed `/internal/tools/*` call on the
  backend. This service has no connection string.
- **Per-agent allow-lists** (`app/tools.py`). A call outside a role's list raises before any HTTP
  happens — the Diagnosis agent physically cannot check stock.
- **No privileged tools.** `reserve_stock` and `issue_prescription` do not exist here. The backend
  performs them after a human approves.
- **Typed contracts** (`app/contracts.py`). Every model reply is validated against a Pydantic
  schema with `extra="forbid"`; invalid output is repaired at most twice, then the run fails safely.
- **Untrusted farmer text** is bounded, stripped, fenced and flagged (`app/prompts.py`) — and the
  real guarantee is that any proposal must still clear the C# validator.
- **Everything is bounded**: 20 s per tool, 90 s per model call, 5 minutes per run, 2 tool retries,
  2 schema repairs, 2 revision loops. Exceeding any of them ends the run with a recorded reason.

## Layout

```
app/
  config.py      settings (env, AGENT_ prefix)
  contracts.py   Pydantic I/O for each agent
  prompts.py     system prompts + untrusted-input handling
  llm.py         provider (Ollama) + structured output with repair
  tools.py       allow-listed tool client
  graph.py       the LangGraph state graph
  runner.py      run execution, limits, safe failure, backend reporting
  main.py        FastAPI: POST /runs, GET /health
tests/           graph flow, allow-lists, revise loop, safe failure, injection handling
```

## Local check against a real model

With Ollama running and `qwen2.5:7b` pulled, a full run takes about 35 seconds on CPU. The GPU is
deliberately unused on the demo machine (`OLLAMA_NUM_GPU=0`) because the driver crashes on it.
