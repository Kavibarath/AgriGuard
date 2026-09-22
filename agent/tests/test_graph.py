"""The graph's control flow: the happy path, the revise loop, and every way a run can end."""

from __future__ import annotations

from typing import Any

import pytest

from app.config import Settings
from app.contracts import PrescriptionProposal, RunOutcome, RunRequest
from app.graph import GraphDependencies, build_graph
from app.runner import execute_run
from tests.conftest import StubLlm, StubTools, verdict


class RecordingReporter:
    """Captures what the backend would have been told."""

    def __init__(self) -> None:
        self.events: list[tuple[str, dict[str, Any]]] = []
        self.result: Any = None

    async def emit(self, event_type: str, payload: dict[str, Any]) -> None:
        self.events.append((event_type, payload))

    async def complete(self, result: Any) -> None:
        self.result = result

    def types(self) -> list[str]:
        return [name for name, _ in self.events]


async def run(
    llm: StubLlm, tools: StubTools, settings: Settings, **overrides: Any
) -> tuple[Any, RecordingReporter]:
    reporter = RecordingReporter()
    deps = GraphDependencies(
        llm=llm, settings=settings, tool_factory=tools.factory(settings), emit=reporter.emit
    )
    graph = build_graph(deps)
    state = await graph.ainvoke(
        {
            "run_id": "run-1",
            "case_id": "case-1",
            "objective": "Resolve crop-health case case-1",
            "revisions": 0,
            **overrides,
        }
    )
    return state, reporter


class TestHappyPath:
    async def test_runs_all_four_agents_in_order(self, llm: StubLlm, settings: Settings) -> None:
        tools = StubTools()

        state, reporter = await run(llm, tools, settings)

        assert state["outcome"] == RunOutcome.PENDING_APPROVAL
        # Each agent ran exactly once, in the planned order.
        assert [payload["agentRole"] for name, payload in reporter.events if name == "StepStarted"] == [
            "Coordinator",
            "Diagnosis",
            "Action",
            "Validation",
        ]

    async def test_stops_for_a_human_rather_than_acting(self, llm: StubLlm, settings: Settings) -> None:
        state, reporter = await run(llm, StubTools(), settings)

        # The run's best possible outcome is "please look at this", never "done".
        assert state["outcome"] == RunOutcome.PENDING_APPROVAL
        assert "ApprovalRequested" in reporter.types()
        assert "RunCompleted" not in reporter.types()  # emitted by the runner, not the graph

    async def test_the_proposal_carries_the_diagnosis_forward(self, llm: StubLlm, settings: Settings) -> None:
        state, _ = await run(llm, StubTools(), settings)

        assert state["diagnosis"].primary_pathogen_code == "LATE_BLIGHT"
        assert state["proposal"].product_id == "product-mancozeb"


class TestAllowList:
    async def test_each_agent_only_calls_its_own_tools(self, llm: StubLlm, settings: Settings) -> None:
        tools = StubTools()

        await run(llm, tools, settings)

        by_role: dict[str, set[str]] = {}
        for role, tool in tools.calls:
            by_role.setdefault(role, set()).add(tool)

        assert by_role["Coordinator"] == {"get_case_detail"}
        assert by_role["Validation"] == {"validate_prescription"}
        # The headline control: the Diagnosis agent never touches stock or products.
        assert "check_stock_availability" not in by_role.get("Diagnosis", set())
        assert "search_approved_products" not in by_role.get("Diagnosis", set())

    async def test_a_tool_outside_the_allow_list_is_refused(self, settings: Settings) -> None:
        from app.contracts import AgentRole
        from app.tools import ToolNotAllowedError

        tools = StubTools()
        diagnosis_client = tools.factory(settings)(AgentRole.DIAGNOSIS)

        with pytest.raises(ToolNotAllowedError, match="may not call 'check_stock_availability'"):
            await diagnosis_client.call("check_stock_availability", productId="x")


class TestReviseLoop:
    async def test_a_revisable_failure_sends_the_proposal_back_to_the_action_agent(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        tools = StubTools({"validate_prescription": verdict("Revise")})
        tools.responses["validate_prescription"] = verdict("Revise")

        state, reporter = await run(llm, tools, settings)

        # Two revisions allowed, so Action runs three times in total before giving up.
        action_steps = [p for n, p in reporter.events if n == "StepStarted" and p["agentRole"] == "Action"]
        assert len(action_steps) == 3
        assert state["revisions"] == settings.max_revisions

    async def test_gives_up_safely_instead_of_looping_forever(self, llm: StubLlm, settings: Settings) -> None:
        tools = StubTools({"validate_prescription": verdict("Revise")})

        state, _ = await run(llm, tools, settings)

        assert state["outcome"] == RunOutcome.FAILED
        assert "after 2 revisions" in state["failure_reason"]

    async def test_a_corrected_second_attempt_is_approved(self, llm: StubLlm, settings: Settings) -> None:
        tools = StubTools()
        verdicts = [verdict("Revise"), {"outcome": "Approved", "summary": "All rules passed.", "results": []}]

        class Sequenced(StubTools):
            async def _next(self) -> Any:
                return verdicts.pop(0) if verdicts else verdicts[-1]

        # Simpler: swap the canned response after the first validation call.
        original = tools.responses["validate_prescription"]
        tools.responses["validate_prescription"] = verdict("Revise")

        calls = {"n": 0}
        factory = tools.factory(settings)

        def counting_factory(role: Any) -> Any:
            client = factory(role)
            inner_call = client.call

            async def call(tool: str, **params: Any) -> Any:
                if tool == "validate_prescription":
                    calls["n"] += 1
                    if calls["n"] > 1:
                        tools.responses["validate_prescription"] = original
                return await inner_call(tool, **params)

            client.call = call  # type: ignore[method-assign]
            return client

        reporter = RecordingReporter()
        deps = GraphDependencies(
            llm=llm, settings=settings, tool_factory=counting_factory, emit=reporter.emit
        )
        state = await build_graph(deps).ainvoke(
            {"run_id": "run-1", "case_id": "case-1", "objective": "o", "revisions": 0}
        )

        assert state["outcome"] == RunOutcome.PENDING_APPROVAL
        assert state["revisions"] == 1

    async def test_revision_guidance_names_only_the_failed_rules(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        tools = StubTools({"validate_prescription": verdict("Revise", code="V3", message="Dose too high.")})

        state, _ = await run(llm, tools, settings)

        guidance = state["verdict"].revision_guidance()
        assert "V3" in guidance
        assert "Dose too high." in guidance


class TestTerminalOutcomes:
    async def test_a_rejection_is_terminal_with_no_retry(self, llm: StubLlm, settings: Settings) -> None:
        tools = StubTools(
            {
                "validate_prescription": verdict(
                    "Rejected", code="V5", severity="Reject", message="Needs 14 days before harvest."
                )
            }
        )

        state, reporter = await run(llm, tools, settings)

        assert state["outcome"] == RunOutcome.REJECTED
        assert "Needs 14 days" in state["failure_reason"]
        # Action ran once: a reject is not negotiable.
        assert len([p for n, p in reporter.events if n == "StepStarted" and p["agentRole"] == "Action"]) == 1


class TestSafeFailure:
    async def test_an_unreachable_model_fails_the_run_without_a_proposal(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        # Golden case G8: Ollama stopped mid-run.
        llm.unreachable = True
        reporter = RecordingReporter()

        result = await execute_run(
            RunRequest(run_id="run-1", case_id="case-1", objective="o"),
            llm,  # type: ignore[arg-type]
            None,  # type: ignore[arg-type]
            settings,
            reporter,  # type: ignore[arg-type]
            StubTools().factory(settings),
        )

        assert result.outcome == RunOutcome.FAILED
        assert "unreachable" in result.failure_reason
        assert result.proposal is None
        assert "RunFailed" in reporter.types()

    async def test_a_failing_tool_ends_the_run_with_a_recorded_reason(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        tools = StubTools()
        tools.failing.add("get_plot_safety_profile")

        reporter = RecordingReporter()
        deps = GraphDependencies(
            llm=llm, settings=settings, tool_factory=tools.factory(settings), emit=reporter.emit
        )
        graph = build_graph(deps)

        from app.tools import ToolError

        with pytest.raises(ToolError):
            await graph.ainvoke({"run_id": "r", "case_id": "c", "objective": "o", "revisions": 0})

    async def test_no_approved_product_is_a_safe_failure_not_a_guess(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        # Every product blocked by the safety rules: the agent must not invent an alternative.
        tools = StubTools(
            {
                "get_plot_safety_profile": {
                    "harvestDate": "2026-11-01",
                    "productWindows": [{"productId": "product-mancozeb", "canSprayToday": False}],
                }
            }
        )
        reporter = RecordingReporter()

        result = await execute_run(
            RunRequest(run_id="run-1", case_id="case-1", objective="o"),
            llm,  # type: ignore[arg-type]
            None,  # type: ignore[arg-type]
            settings,
            reporter,  # type: ignore[arg-type]
            tools.factory(settings),
        )

        assert result.outcome == RunOutcome.FAILED
        assert "No approved product" in result.failure_reason


class TestTimeline:
    async def test_every_tool_call_is_recorded_for_the_audit_trail(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        reporter = RecordingReporter()
        deps = GraphDependencies(
            llm=llm,
            settings=settings,
            tool_factory=StubTools().factory(settings),
            emit=reporter.emit,
        )
        await build_graph(deps).ainvoke({"run_id": "r", "case_id": "c", "objective": "o", "revisions": 0})

        assert "PlanCreated" in reporter.types()
        assert "ValidationResult" in reporter.types()

    async def test_the_validation_verdict_reaches_the_timeline_verbatim(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        tools = StubTools({"validate_prescription": verdict("Rejected", code="V5", severity="Reject")})

        _, reporter = await run(llm, tools, settings)

        payload = next(p for n, p in reporter.events if n == "ValidationResult")
        assert payload["payload"]["outcome"] == "Rejected"
        assert payload["payload"]["results"][0]["code"] == "V5"


class TestReviewerNote:
    async def test_an_agronomists_revision_note_reaches_the_action_agent(
        self, llm: StubLlm, settings: Settings, proposal: PrescriptionProposal
    ) -> None:
        seen: dict[str, str] = {}

        async def capture(schema: Any, system: str, user: str, on_repair: Any = None) -> Any:
            if schema is PrescriptionProposal:
                seen["prompt"] = user
            return await StubLlm.generate(llm, schema, system, user, on_repair)

        llm.generate = capture  # type: ignore[method-assign]

        await run(llm, StubTools(), settings, reviewer_note="Prefer a contact fungicide, not a systemic one.")

        assert "contact fungicide" in seen["prompt"]
        assert "<reviewer_note>" in seen["prompt"]
