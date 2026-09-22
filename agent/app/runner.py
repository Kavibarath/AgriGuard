"""
Executes one run: builds the graph's dependencies, enforces the overall time limit, reports
progress to the backend, and converts every possible failure into a recorded safe failure.

Nothing here raises to the caller. A run that cannot finish still ends with a status, a reason
and a timeline — "failed silently" is the one outcome the system must never produce (§9.6).
"""

from __future__ import annotations

import asyncio
from typing import Any

import httpx
import structlog

from .config import Settings
from .contracts import AgentRole, RunOutcome, RunRequest, RunResult
from .graph import GraphDependencies, build_graph
from .llm import LlmError, StructuredLlm
from .tools import ToolClient, ToolError, ToolNotAllowedError

log = structlog.get_logger(__name__)


class BackendReporter:
    """Posts timeline events and the final result back to ASP.NET Core, which owns the state."""

    def __init__(self, client: httpx.AsyncClient, settings: Settings, run_id: str) -> None:
        self._client = client
        self._settings = settings
        self._run_id = run_id

    async def emit(self, event_type: str, payload: dict[str, Any]) -> None:
        body = {"eventType": event_type, **payload}
        try:
            await self._client.post(
                f"{self._settings.api_base_url}/internal/agent-runs/{self._run_id}/events",
                json=body,
                headers=self._headers(),
                timeout=self._settings.tool_timeout_seconds,
            )
        except httpx.HTTPError as error:
            # Losing an event must not abort a run that is otherwise fine.
            log.warning("event_post_failed", run_id=self._run_id, event=event_type, error=str(error))

    async def complete(self, result: RunResult) -> None:
        try:
            await self._client.post(
                f"{self._settings.api_base_url}/internal/agent-runs/{self._run_id}/result",
                json=result.model_dump(mode="json"),
                headers=self._headers(),
                timeout=self._settings.tool_timeout_seconds,
            )
        except httpx.HTTPError as error:
            log.error("result_post_failed", run_id=self._run_id, error=str(error))

    def _headers(self) -> dict[str, str]:
        return {"X-Agent-Key": self._settings.api_key, "X-Correlation-Id": f"agent-{self._run_id}"}


async def execute_run(
    request: RunRequest,
    llm: StructuredLlm,
    http: httpx.AsyncClient,
    settings: Settings,
    reporter: BackendReporter | None = None,
    tool_factory: Any = None,
) -> RunResult:
    """`reporter` and `tool_factory` are injection points for tests; production passes neither."""
    reporter = reporter or BackendReporter(http, settings, request.run_id)

    if tool_factory is None:

        def tool_factory(role: AgentRole) -> ToolClient:
            return ToolClient(role, http, settings, request.run_id, reporter.emit)

    deps = GraphDependencies(llm=llm, settings=settings, tool_factory=tool_factory, emit=reporter.emit)
    graph = build_graph(deps)

    initial: dict[str, Any] = {
        "run_id": request.run_id,
        "case_id": request.case_id,
        "objective": request.objective,
        "reviewer_note": request.reviewer_note,
        "revisions": 0,
    }

    try:
        # The whole run is bounded, not just its parts: a graph that stalls between nodes still
        # has to end (§9.3).
        state = await asyncio.wait_for(graph.ainvoke(initial), timeout=settings.run_timeout_seconds)
        result = RunResult(
            run_id=request.run_id,
            outcome=state.get("outcome", RunOutcome.FAILED),
            proposal=state.get("proposal"),
            verdict=state.get("verdict"),
            diagnosis=state.get("diagnosis"),
            plan=state.get("plan"),
            failure_reason=state.get("failure_reason"),
            revisions=state.get("revisions", 0),
        )
    except TimeoutError:
        result = _failed(request, f"The run exceeded its {settings.run_timeout_seconds:.0f}-second limit.")
    except ToolNotAllowedError as error:
        # An agent reaching outside its allow-list is a control failure worth shouting about.
        log.error("tool_not_allowed", run_id=request.run_id, error=str(error))
        result = _failed(request, f"Blocked tool call: {error}")
    except ToolError as error:
        result = _failed(request, f"A required tool did not respond: {error}")
    except LlmError as error:
        result = _failed(request, str(error))
    except Exception as error:
        log.exception("run_crashed", run_id=request.run_id)
        result = _failed(request, f"Unexpected failure: {type(error).__name__}: {error}")

    if result.outcome == RunOutcome.FAILED:
        await reporter.emit("RunFailed", {"payload": {"reason": result.failure_reason}})
    else:
        await reporter.emit("RunCompleted", {"payload": {"outcome": result.outcome.value}})

    await reporter.complete(result)
    log.info("run_finished", run_id=request.run_id, outcome=result.outcome.value, revisions=result.revisions)
    return result


def _failed(request: RunRequest, reason: str) -> RunResult:
    return RunResult(run_id=request.run_id, outcome=RunOutcome.FAILED, failure_reason=reason)
