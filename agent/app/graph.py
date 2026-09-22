"""
The agent graph (§9.1).

    START → Coordinator → Diagnosis → Action → Validation
                              ▲          │
                              └──REVISE──┘   (at most `max_revisions` loops)
    Validation ── REJECT ─► safe failure, recorded
    Validation ── PASS ───► the run stops and waits for a human

An explicit state graph rather than a free-running ReAct loop: every transition is declared, so
the run can be drawn, replayed and explained. That is also why it is auditable — each node writes
an AgentRunStep and the tool calls land on the timeline.

**On the human gate.** LangGraph offers `interrupt()`, which parks a run inside its own
checkpointer. We do not use it: the EF-owned tables in PostgreSQL are the system of record
(§6), and a second, separate store of half-finished runs would be a second source of truth.
Instead the graph ends at the gate, the backend records `PendingApproval`, and an agronomist
asking for a revision starts a fresh invocation carrying their note. The pause therefore lives
in the database everyone else can already see.
"""

from __future__ import annotations

from collections.abc import Callable
from datetime import UTC, datetime
from typing import Any, TypedDict

import structlog
from langgraph.graph import END, START, StateGraph

from .config import Settings
from .contracts import (
    AgentRole,
    Diagnosis,
    Plan,
    PrescriptionProposal,
    RunOutcome,
    Verdict,
)
from .llm import LlmError, StructuredLlm
from .prompts import (
    ACTION_SYSTEM,
    COORDINATOR_SYSTEM,
    DIAGNOSIS_SYSTEM,
    fence,
    sanitise_farmer_note,
)
from .tools import EventSink, ToolClient, ToolError

log = structlog.get_logger(__name__)


class RunState(TypedDict, total=False):
    """Shared state threaded through the graph. Everything the console later shows is in here."""

    run_id: str
    case_id: str
    objective: str
    reviewer_note: str | None

    case: dict[str, Any]
    safety_profile: dict[str, Any]
    plan: Plan
    diagnosis: Diagnosis
    proposal: PrescriptionProposal
    verdict: Verdict

    revisions: int
    outcome: RunOutcome
    failure_reason: str | None
    injection_flags: list[str]


class GraphDependencies:
    """What the nodes need. Passed in so tests can substitute stubs for the model and the tools."""

    def __init__(
        self,
        llm: StructuredLlm,
        settings: Settings,
        tool_factory: Callable[[AgentRole], ToolClient],
        emit: EventSink,
    ) -> None:
        self.llm = llm
        self.settings = settings
        # role → ToolClient, so each node gets a client bound to its own allow-list.
        self.tools = tool_factory
        self.emit = emit

    def client_for(self, role: AgentRole) -> ToolClient:
        return self.tools(role)


def build_graph(deps: GraphDependencies) -> Any:
    """Wires the nodes and the one conditional edge (revise or finish)."""

    async def step(role: AgentRole, seq: int, goal: str) -> None:
        await deps.emit("StepStarted", {"agentRole": role.value, "sequenceNo": seq, "goal": goal})

    async def coordinator(state: RunState) -> RunState:
        await step(AgentRole.COORDINATOR, 1, "Plan the run")
        tools = deps.client_for(AgentRole.COORDINATOR)
        case = await tools.call("get_case_detail", caseId=state["case_id"])

        note, flags = sanitise_farmer_note(case.get("farmerNote"), deps.settings.max_farmer_note_chars)
        if flags:
            # Recorded, not blocked: the agronomist should know the note tried to steer the model.
            await deps.emit(
                "InjectionFlagged",
                {"agentRole": AgentRole.COORDINATOR.value, "payload": {"patterns": flags}},
            )
            log.warning("injection_flagged", run_id=state["run_id"], patterns=flags)

        plan = await deps.llm.generate(
            Plan,
            COORDINATOR_SYSTEM,
            f"Objective: {state['objective']}\n"
            f"Crop: {case.get('cropName')} at stage {case.get('stage')}\n"
            f"Reported symptoms: {', '.join(case.get('symptomCodes', [])) or 'none recorded'}\n"
            f"{fence('farmer_note', note or 'none')}",
        )

        await deps.emit(
            "PlanCreated", {"agentRole": AgentRole.COORDINATOR.value, "payload": plan.model_dump()}
        )
        await deps.emit("StepCompleted", {"agentRole": AgentRole.COORDINATOR.value, "sequenceNo": 1})

        return {**state, "case": case, "plan": plan, "injection_flags": flags}

    async def diagnosis(state: RunState) -> RunState:
        await step(AgentRole.DIAGNOSIS, 2, "Identify the most likely pest or disease")
        tools = deps.client_for(AgentRole.DIAGNOSIS)
        case = state["case"]

        history = await tools.call("get_crop_history", cropCycleId=case["cropCycleId"])
        outbreak = await tools.call(
            "get_regional_outbreak_signal", cropId=case["cropId"], districtId=case["districtId"]
        )

        note, _ = sanitise_farmer_note(case.get("farmerNote"), deps.settings.max_farmer_note_chars)
        candidates = case.get("candidatePathogens", [])

        result = await deps.llm.generate(
            Diagnosis,
            DIAGNOSIS_SYSTEM,
            f"Crop: {case.get('cropName')} at stage {case.get('stage')}, sown {case.get('sownDate')}.\n"
            f"Reported symptoms: {', '.join(case.get('symptomCodes', [])) or 'none recorded'}\n"
            f"Candidate pathogens (use these codes only): "
            f"{', '.join(f'{c["code"]}={c["commonName"]}' for c in candidates) or 'none'}\n"
            f"Recent treatments on this crop: {history.get('summary', 'none')}\n"
            f"District disease pressure: {outbreak.get('summary', 'no signal')}\n"
            f"{fence('farmer_note', note or 'none')}",
        )

        await deps.emit(
            "StepCompleted",
            {"agentRole": AgentRole.DIAGNOSIS.value, "sequenceNo": 2, "payload": result.model_dump()},
        )
        return {**state, "diagnosis": result}

    async def action(state: RunState) -> RunState:
        revisions = state.get("revisions", 0)
        await step(AgentRole.ACTION, 3, "Propose a compliant treatment")
        tools = deps.client_for(AgentRole.ACTION)
        case = state["case"]

        products = await tools.call(
            "search_approved_products",
            cropId=case["cropId"],
            pathogenCode=state["diagnosis"].primary_pathogen_code,
        )
        safety = await tools.call("get_plot_safety_profile", plotId=case["plotId"])

        # Blocked products are filtered out here as well as flagged in the prompt: the model
        # should not be choosing between options that the rules already refuse.
        windows = {w["productId"]: w for w in safety.get("productWindows", [])}
        options = [
            {
                "product_id": p["productId"],
                "name": p["productName"],
                "min_dose_per_hectare": p["minDosePerHectare"],
                "max_dose_per_hectare": p["maxDosePerHectare"],
                "last_safe_spray_date": windows.get(p["productId"], {}).get("lastSafeSprayDate"),
            }
            for p in products.get("products", [])
            if windows.get(p["productId"], {}).get("canSprayToday", True)
        ]

        if not options:
            raise ToolError("No approved product can be sprayed on this plot today.")

        guidance = ""
        if (verdict := state.get("verdict")) is not None:
            guidance = (
                "\nYour previous proposal was rejected by the safety rules. Fix exactly these "
                f"problems:\n{verdict.revision_guidance()}"
            )
        if reviewer := state.get("reviewer_note"):
            guidance += f"\nThe reviewing agronomist added: {fence('reviewer_note', reviewer)}"

        proposal = await deps.llm.generate(
            PrescriptionProposal,
            ACTION_SYSTEM,
            f"Diagnosis: {state['diagnosis'].primary_pathogen_code} "
            f"({state['diagnosis'].reasoning})\n"
            f"Plot area: {case['areaHectares']} hectares.\n"
            f"Today: {datetime.now(UTC).date().isoformat()}. "
            f"Planned harvest: {safety.get('harvestDate')}\n"
            f"Approved products:\n{options}\n{guidance}",
        )

        await deps.emit(
            "StepCompleted",
            {
                "agentRole": AgentRole.ACTION.value,
                "sequenceNo": 3,
                "payload": {"proposal": proposal.model_dump(mode="json"), "revision": revisions},
            },
        )
        return {**state, "proposal": proposal, "safety_profile": safety}

    async def validation(state: RunState) -> RunState:
        await step(AgentRole.VALIDATION, 4, "Check the proposal against the safety rules")
        tools = deps.client_for(AgentRole.VALIDATION)
        proposal = state["proposal"]

        # The agent does not judge safety: it submits the proposal to deterministic C# and
        # reports what came back. This call is the whole of its authority here.
        raw = await tools.call(
            "validate_prescription",
            cropCycleId=state["case"]["cropCycleId"],
            productId=proposal.product_id,
            dosePerHectare=proposal.dose_per_hectare,
            totalQuantity=proposal.total_quantity,
            sprayDate=proposal.spray_date.isoformat(),
            dealerId=proposal.dealer_id,
        )
        verdict = Verdict.model_validate(raw)

        await deps.emit(
            "ValidationResult",
            {"agentRole": AgentRole.VALIDATION.value, "payload": verdict.model_dump()},
        )
        return {**state, "verdict": verdict}

    def route_after_validation(state: RunState) -> str:
        verdict = state["verdict"]

        if verdict.outcome == "Approved":
            return "approved"
        if verdict.outcome == "Rejected":
            return "rejected"

        # Revise: loop back to Action, but only within the cap. A third failure is a safe
        # failure, not an endless conversation with a model that is not converging.
        if state.get("revisions", 0) >= deps.settings.max_revisions:
            return "exhausted"
        return "revise"

    async def approved(state: RunState) -> RunState:
        await deps.emit("ApprovalRequested", {"payload": {"summary": state["verdict"].summary}})
        return {**state, "outcome": RunOutcome.PENDING_APPROVAL}

    async def rejected(state: RunState) -> RunState:
        reasons = "; ".join(f.message for f in state["verdict"].failures)
        return {**state, "outcome": RunOutcome.REJECTED, "failure_reason": reasons}

    async def exhausted(state: RunState) -> RunState:
        return {
            **state,
            "outcome": RunOutcome.FAILED,
            "failure_reason": (
                f"Could not produce a compliant proposal after {deps.settings.max_revisions} revisions: "
                f"{state['verdict'].summary}"
            ),
        }

    async def increment_revision(state: RunState) -> RunState:
        return {**state, "revisions": state.get("revisions", 0) + 1}

    graph: Any = StateGraph(RunState)
    graph.add_node("coordinator", coordinator)
    graph.add_node("diagnosis", diagnosis)
    graph.add_node("action", action)
    graph.add_node("validation", validation)
    graph.add_node("revise", increment_revision)
    graph.add_node("approved", approved)
    graph.add_node("rejected", rejected)
    graph.add_node("exhausted", exhausted)

    graph.add_edge(START, "coordinator")
    graph.add_edge("coordinator", "diagnosis")
    graph.add_edge("diagnosis", "action")
    graph.add_edge("action", "validation")
    graph.add_conditional_edges(
        "validation",
        route_after_validation,
        {"approved": "approved", "rejected": "rejected", "revise": "revise", "exhausted": "exhausted"},
    )
    graph.add_edge("revise", "action")
    graph.add_edge("approved", END)
    graph.add_edge("rejected", END)
    graph.add_edge("exhausted", END)

    return graph.compile()


__all__ = ["GraphDependencies", "LlmError", "RunState", "ToolError", "build_graph"]
