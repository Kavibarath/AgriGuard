"""
FastAPI surface. Two endpoints: start a run, and report health.

Runs are accepted and executed in the background — a four-node graph on CPU inference takes
around a minute, far longer than an HTTP request should be held open. Progress is visible
immediately in the React console, because every step posts to the backend as it happens.
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

import httpx
import structlog
from fastapi import BackgroundTasks, FastAPI, Header, HTTPException, status

from .config import Settings, get_settings
from .contracts import RunRequest
from .llm import StructuredLlm, create_provider
from .runner import execute_run

log = structlog.get_logger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    """One shared HTTP client for the process: connection reuse matters on a free-tier host."""
    settings = get_settings()
    async with httpx.AsyncClient() as client:
        app.state.http = client
        app.state.settings = settings
        app.state.provider = create_provider(client, settings)
        app.state.llm = StructuredLlm(app.state.provider, settings)
        log.info("agent_service_started", provider=settings.llm_provider, model=settings.llm_model)
        yield


app = FastAPI(title="AgriGuard Agent Service", version="0.1.0", lifespan=lifespan)


def _authorise(settings: Settings, provided: str | None) -> None:
    """
    The backend is the only legitimate caller. Without this, anything that can reach the service
    could start runs against real cases.
    """
    if not settings.api_key:
        raise HTTPException(status.HTTP_500_INTERNAL_SERVER_ERROR, "Agent key is not configured.")
    if provided != settings.api_key:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Invalid agent key.")


@app.get("/health/live")
async def health_live() -> dict[str, str]:
    """The process is up. Calls nothing, so the host's frequent probes never spend model quota."""
    return {"status": "healthy"}


@app.get("/health")
async def health() -> dict[str, Any]:
    settings: Settings = app.state.settings
    reachable = await app.state.provider.is_reachable()

    # Degraded rather than unhealthy: the service is up and will fail runs safely if asked.
    return {
        "status": "healthy" if reachable else "degraded",
        "provider": settings.llm_provider,
        "model": settings.llm_model,
        "llmReachable": reachable,
    }


@app.post("/runs", status_code=status.HTTP_202_ACCEPTED)
async def start_run(
    request: RunRequest,
    background: BackgroundTasks,
    x_agent_key: str | None = Header(default=None, alias="X-Agent-Key"),
) -> dict[str, str]:
    settings: Settings = app.state.settings
    _authorise(settings, x_agent_key)

    background.add_task(execute_run, request, app.state.llm, app.state.http, settings)
    log.info("run_accepted", run_id=request.run_id, case_id=request.case_id)

    return {"runId": request.run_id, "status": "accepted"}
