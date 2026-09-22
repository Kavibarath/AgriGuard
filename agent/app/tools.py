"""
The agent's only door to data (§9.3).

Three properties matter here, and each is enforced in code rather than by convention:

1. **No database.** Every fact comes from an allow-listed `/internal/tools/*` endpoint on the
   ASP.NET Core API, authenticated with a shared key. This service holds no connection string.
2. **Per-agent allow-lists.** A client is bound to one agent role, and a call to a tool outside
   that role's list raises before any HTTP happens. The Diagnosis agent *cannot* check stock.
3. **No privileged actions.** `reserve_stock` and `issue_prescription` are deliberately absent:
   they are executed by the backend after a human approves. The agent can only ever propose.
"""

from __future__ import annotations

import asyncio
from collections.abc import Awaitable, Callable
from typing import Any

import httpx
import structlog

from .config import Settings
from .contracts import AgentRole

log = structlog.get_logger(__name__)

# Which tools each agent may call. Disjoint by design — this table is the control.
ALLOW_LIST: dict[AgentRole, frozenset[str]] = {
    AgentRole.COORDINATOR: frozenset({"get_case_detail"}),
    AgentRole.DIAGNOSIS: frozenset(
        {"get_case_detail", "get_crop_history", "get_weather_forecast", "get_regional_outbreak_signal"}
    ),
    AgentRole.ACTION: frozenset(
        {
            "search_approved_products",
            "get_plot_safety_profile",
            "check_stock_availability",
            "get_product_pricing",
        }
    ),
    AgentRole.VALIDATION: frozenset({"validate_prescription"}),
}

# Tool name → (HTTP method, path on the backend).
TOOL_ROUTES: dict[str, tuple[str, str]] = {
    "get_case_detail": ("GET", "/internal/tools/case-detail"),
    "get_crop_history": ("GET", "/internal/tools/crop-history"),
    "get_weather_forecast": ("GET", "/internal/tools/weather-forecast"),
    "get_regional_outbreak_signal": ("GET", "/internal/tools/outbreak-signal"),
    "search_approved_products": ("GET", "/internal/tools/approved-products"),
    "get_plot_safety_profile": ("GET", "/internal/tools/plot-safety-profile"),
    "check_stock_availability": ("GET", "/internal/tools/stock-availability"),
    "get_product_pricing": ("GET", "/internal/tools/product-pricing"),
    "validate_prescription": ("POST", "/internal/tools/validate-prescription"),
}


class ToolError(RuntimeError):
    """A tool call that could not be completed. Always recorded; never silently swallowed."""


class ToolNotAllowedError(ToolError):
    """An agent asked for a tool outside its allow-list — a bug or an attack, never routine."""


EventSink = Callable[[str, dict[str, Any]], Awaitable[None]]


class ToolClient:
    """Bound to one agent role. Create a new one per node; never share across roles."""

    def __init__(
        self,
        role: AgentRole,
        client: httpx.AsyncClient,
        settings: Settings,
        run_id: str,
        emit: EventSink | None = None,
    ) -> None:
        self._role = role
        self._client = client
        self._settings = settings
        self._run_id = run_id
        self._emit = emit

    async def call(self, tool: str, **params: Any) -> Any:
        allowed = ALLOW_LIST.get(self._role, frozenset())
        if tool not in allowed:
            # Refused before any network call: the allow-list is a wall, not a warning.
            raise ToolNotAllowedError(
                f"{self._role} may not call '{tool}'. Allowed: {sorted(allowed) or 'none'}."
            )

        method, path = TOOL_ROUTES[tool]
        attempts = self._settings.tool_retries + 1
        last_error: Exception | None = None

        for attempt in range(1, attempts + 1):
            started = asyncio.get_running_loop().time()
            try:
                response = await self._request(method, path, params)
                duration_ms = int((asyncio.get_running_loop().time() - started) * 1000)
                await self._record("ToolCalled", tool, duration_ms, {"attempt": attempt})
                return response
            except (httpx.HTTPError, ToolError) as error:
                last_error = error
                duration_ms = int((asyncio.get_running_loop().time() - started) * 1000)
                await self._record("ToolFailed", tool, duration_ms, {"attempt": attempt, "error": str(error)})
                log.warning("tool_failed", tool=tool, attempt=attempt, error=str(error))
                if attempt < attempts:
                    # Exponential backoff: 0.5 s, 1 s, 2 s …
                    await asyncio.sleep(0.5 * (2 ** (attempt - 1)))

        raise ToolError(f"Tool '{tool}' failed after {attempts} attempts: {last_error}")

    async def _request(self, method: str, path: str, params: dict[str, Any]) -> Any:
        headers = {"X-Agent-Key": self._settings.api_key, "X-Correlation-Id": f"agent-{self._run_id}"}
        url = f"{self._settings.api_base_url}{path}"
        timeout = self._settings.tool_timeout_seconds

        if method == "GET":
            response = await self._client.get(url, params=params, headers=headers, timeout=timeout)
        else:
            response = await self._client.post(url, json=params, headers=headers, timeout=timeout)

        if response.status_code >= 400:
            # The body may carry a ProblemDetails explanation; keep it short for the timeline.
            raise ToolError(f"{method} {path} returned {response.status_code}: {response.text[:200]}")

        return response.json()

    async def _record(self, event_type: str, tool: str, duration_ms: int, payload: dict[str, Any]) -> None:
        if self._emit is None:
            return
        await self._emit(
            event_type,
            {"agentRole": self._role.value, "toolName": tool, "durationMs": duration_ms, "payload": payload},
        )
