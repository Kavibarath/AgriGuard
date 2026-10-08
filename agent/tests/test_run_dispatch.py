"""POST /runs: the backend retries while a sleeping cloud instance wakes, so a repeat must be harmless."""

from __future__ import annotations

from typing import Any

import pytest
from fastapi.testclient import TestClient

import app.main as main
from app.config import Settings

KEY = "test-only-agent-key-0123456789abcdef"


@pytest.fixture
def started(monkeypatch: pytest.MonkeyPatch) -> list[str]:
    """Records each run the service starts, instead of running the graph."""
    runs: list[str] = []

    async def fake_execute_run(request: Any, *_: Any) -> None:
        runs.append(request.run_id)

    monkeypatch.setattr(main, "execute_run", fake_execute_run)
    return runs


def post_run(client: TestClient, run_id: str, key: str = KEY) -> Any:
    return client.post(
        "/runs",
        json={"run_id": run_id, "case_id": "case-1", "objective": "Diagnose and propose"},
        headers={"X-Agent-Key": key},
    )


def test_a_repeated_run_id_is_acknowledged_but_started_once(started: list[str]) -> None:
    with TestClient(main.app) as client:
        main.app.state.settings = Settings(api_key=KEY)
        first = post_run(client, "run-1")
        second = post_run(client, "run-1")

    assert first.status_code == second.status_code == 202
    assert second.json() == {"runId": "run-1", "status": "accepted"}
    assert started == ["run-1"]


def test_different_runs_each_start(started: list[str]) -> None:
    with TestClient(main.app) as client:
        main.app.state.settings = Settings(api_key=KEY)
        post_run(client, "run-a")
        post_run(client, "run-b")

    assert started == ["run-a", "run-b"]


def test_a_refused_request_is_not_remembered(started: list[str]) -> None:
    with TestClient(main.app) as client:
        main.app.state.settings = Settings(api_key=KEY)
        assert post_run(client, "run-x", key="wrong").status_code == 401
        assert post_run(client, "run-x").status_code == 202

    assert started == ["run-x"]


def test_only_the_most_recent_run_ids_are_remembered(
    started: list[str], monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(main, "ACCEPTED_RUNS_REMEMBERED", 2)
    with TestClient(main.app) as client:
        main.app.state.settings = Settings(api_key=KEY)
        for run_id in ("r1", "r2", "r3", "r1"):
            post_run(client, run_id)

    # r1 had been forgotten by the time it came again, so it started a second time.
    assert started == ["r1", "r2", "r3", "r1"]
