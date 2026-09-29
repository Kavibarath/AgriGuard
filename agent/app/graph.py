"""
The agent graph (§9.1).

    START → Coordinator → Diagnosis → Coordinator (triage) → Action → Validation
                                              │                 ▲          │
                                              │                 └──REVISE──┘   (at most `max_revisions` loops)
                                              └─ AGRONOMIST ─► handed to a person, with advice for the farmer
    Validation ── REJECT ─► safe failure, recorded
    Validation ── PASS ───► the run stops and waits for a human

The Coordinator triages the diagnosis before anything is prescribed. Hard stops in code decide
first: no approved product for this pathogen on this crop, or a diagnosis below
`min_diagnosis_confidence`, always go to an agronomist. Otherwise the model decides, and it may
choose to escalate: it can be more cautious than the rules, never less.

The Validation agent never decides the route: the deterministic C# verdict does. It then reads
that verdict against the rule limits and explains it (SafetyReview), and its fixes are added to
what the Action agent is told on a revision. A review that disagrees with the verdict is discarded.

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
    DECISION_FOR_VERDICT,
    AgentRole,
    Diagnosis,
    Plan,
    PrescriptionProposal,
    ReviewDecision,
    RunOutcome,
    SafetyReview,
    Triage,
    TriageDecision,
    TriageRoute,
    Verdict,
    is_safe_advice,
    revision_guidance,
)
from .llm import LlmError, StructuredLlm
from .prompts import (
    ACTION_SYSTEM,
    COORDINATOR_SYSTEM,
    DIAGNOSIS_SYSTEM,
    TRIAGE_SYSTEM,
    VALIDATION_SYSTEM,
    fence,
    sanitise_farmer_note,
)
from .tools import EventSink, ToolClient, ToolError

log = structlog.get_logger(__name__)


def primary_confidence(diagnosis: Diagnosis) -> float:
    """The confidence the Diagnosis agent gave its own primary pick (0 if it did not rank it)."""
    return next(
        (c.confidence for c in diagnosis.candidates if c.pathogen_code == diagnosis.primary_pathogen_code),
        0.0,
    )


def describe_limits(limits: dict[str, Any] | None) -> str:
    """The rules-table row, in one line, for the Validation agent to quote."""
    if limits is None:
        return "Rule limits: unavailable."
    if not limits.get("approved"):
        return f"Rule limits: {limits.get('productName')} is not approved for {limits.get('cropName')}."
    unit = "L" if limits.get("unit") == "Litre" else "kg"
    restricted = "; restricted product." if limits.get("isRestricted") else "."
    return (
        f"Rule limits for {limits.get('productName')} on {limits.get('cropName')}: dose "
        f"{limits.get('minDosePerHectare')} to {limits.get('maxDosePerHectare')} {unit}/ha; "
        f"pre-harvest interval {limits.get('preHarvestIntervalDays')} days; at most "
        f"{limits.get('maxApplicationsPerCycle')} applications a cycle, "
        f"{limits.get('minDaysBetweenApplications')} days apart; rainfast after "
        f"{limits.get('rainfastHours')} hours{restricted}"
    )


def describe_recent_weather(weather: dict[str, Any]) -> str:
    """One line of weather evidence for the Diagnosis agent; says so plainly when there is none."""
    if not weather.get("forecastAvailable"):
        return "Weather at the plot: unavailable."
    recent = (
        f"last 48 h {weather.get('recentRainMm')} mm rain, average humidity "
        f"{weather.get('recentHumidityPercent')}%"
        if weather.get("recentRainMm") is not None
        else "recent conditions unknown"
    )
    return f"Weather at the plot: {recent}. Coming days: {weather.get('summary', '')}"


class RunState(TypedDict, total=False):
    """Shared state threaded through the graph. Everything the console later shows is in here."""

    run_id: str
    case_id: str
    objective: str
    reviewer_note: str | None

    case: dict[str, Any]
    safety_profile: dict[str, Any]
    # The plot's forecast, fetched by Diagnosis and read by Action to pick a spray day.
    weather: dict[str, Any]
    plan: Plan
    diagnosis: Diagnosis
    # The Coordinator's decision after the diagnosis: treat, or hand to an agronomist.
    triage: TriageDecision
    proposal: PrescriptionProposal
    verdict: Verdict
    # The Validation agent's reading of the latest verdict; None when it was discarded or unavailable.
    safety_review: SafetyReview | None

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
        # Weather is evidence, not a precondition: wet, humid days favour fungal disease. If the
        # forecast cannot be had, the diagnosis goes ahead without it.
        try:
            weather = await tools.call("get_weather_forecast", plotId=case["plotId"], days=14)
        except ToolError:
            weather = {}

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
            f"{describe_recent_weather(weather)}\n"
            f"{fence('farmer_note', note or 'none')}",
        )

        await deps.emit(
            "StepCompleted",
            {"agentRole": AgentRole.DIAGNOSIS.value, "sequenceNo": 2, "payload": result.model_dump()},
        )
        return {**state, "diagnosis": result, "weather": weather}

    async def triage(state: RunState) -> RunState:
        await step(AgentRole.COORDINATOR, 3, "Decide whether a product can treat this")
        tools = deps.client_for(AgentRole.COORDINATOR)
        case = state["case"]
        diagnosis = state["diagnosis"]

        profile = await tools.call(
            "get_pathogen_profile", pathogenCode=diagnosis.primary_pathogen_code, cropId=case["cropId"]
        )
        confidence = primary_confidence(diagnosis)
        name, crop = profile.get("commonName", diagnosis.primary_pathogen_code), case.get("cropName")

        # Hard stops: facts, checked in code, that no model output can talk its way past.
        hard_stop: str | None = None
        if not profile.get("known", True):
            hard_stop = f"The diagnosis {diagnosis.primary_pathogen_code} is not in the pathogen catalogue."
        elif not profile.get("chemicalControl"):
            hard_stop = f"No approved product controls {name} on {crop}, so nothing can be prescribed."
        elif confidence < deps.settings.min_diagnosis_confidence:
            hard_stop = (
                f"The diagnosis is uncertain: {confidence:.0%} confidence in {name}, below the "
                f"{deps.settings.min_diagnosis_confidence:.0%} needed to treat."
            )

        note, _ = sanitise_farmer_note(case.get("farmerNote"), deps.settings.max_farmer_note_chars)
        proposed: Triage | None = None
        try:
            proposed = await deps.llm.generate(
                Triage,
                TRIAGE_SYSTEM,
                f"Crop: {crop} at stage {case.get('stage')}.\n"
                f"Diagnosis: {name} ({profile.get('type')}), {confidence:.0%} confidence. "
                f"{diagnosis.reasoning}\n"
                f"Approved products that target it on this crop: {profile.get('approvedProducts', 0)}.\n"
                + (f"A rule requires route AGRONOMIST: {hard_stop}\n" if hard_stop else "")
                + fence("farmer_note", note or "none"),
            )
        except LlmError as error:
            # Without the model there is no advice, but the hard stops still decide. With no hard
            # stop the run goes on to Action, which will need the model too and fail safely there.
            log.warning("triage_model_failed", run_id=state["run_id"], error=str(error))

        if hard_stop is not None:
            decision = TriageDecision(route=TriageRoute.AGRONOMIST, reason=hard_stop, decided_by="rules")
        elif proposed is not None:
            decision = TriageDecision(route=proposed.route, reason=proposed.reason, decided_by="coordinator")
        else:
            decision = TriageDecision(
                route=TriageRoute.TREAT,
                reason="No rule stops treatment; the model gave no view.",
                decided_by="rules",
            )

        tips = proposed.farmer_advice if proposed is not None else []
        decision.farmer_advice = [tip for tip in tips if is_safe_advice(tip)]

        payload = {
            **decision.model_dump(mode="json"),
            "model_route": proposed.route.value if proposed is not None else None,
            # The model said TREAT and a hard stop said no: recorded, as it is what the rules are for.
            "overridden": hard_stop is not None
            and proposed is not None
            and proposed.route == TriageRoute.TREAT,
            "advice_dropped": len(tips) - len(decision.farmer_advice),
        }
        await deps.emit("TriageDecided", {"agentRole": AgentRole.COORDINATOR.value, "payload": payload})
        await deps.emit(
            "StepCompleted", {"agentRole": AgentRole.COORDINATOR.value, "sequenceNo": 3, "payload": payload}
        )
        return {**state, "triage": decision}

    def route_after_triage(state: RunState) -> str:
        return "treat" if state["triage"].route == TriageRoute.TREAT else "agronomist"

    async def escalated(state: RunState) -> RunState:
        return {**state, "outcome": RunOutcome.MANUAL_REVIEW, "failure_reason": state["triage"].reason}

    async def action(state: RunState) -> RunState:
        revisions = state.get("revisions", 0)
        await step(AgentRole.ACTION, 4, "Propose a compliant treatment")
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

        # The same forecast judgement as rule V8: proposing any other day only earns a revision.
        weather = state.get("weather") or {}
        spray_days = [d["date"] for d in weather.get("days", []) if d.get("suitable")]
        if weather.get("forecastAvailable") and not spray_days:
            raise ToolError(f"No day in the forecast suits spraying. {weather.get('summary', '')}".strip())
        weather_line = (
            f"Days whose weather suits spraying (spray_date must be one of these): {', '.join(spray_days)}\n"
            if spray_days
            else "Weather forecast unavailable: choose the earliest safe date.\n"
        )

        guidance = ""
        if (verdict := state.get("verdict")) is not None:
            guidance = (
                "\nYour previous proposal was rejected by the safety rules. Fix exactly these "
                f"problems:\n{revision_guidance(verdict, state.get('safety_review'))}"
            )
        if reviewer := state.get("reviewer_note"):
            guidance += f"\nThe reviewing agronomist added: {fence('reviewer_note', reviewer)}"

        proposal = await deps.llm.generate(
            PrescriptionProposal,
            ACTION_SYSTEM,
            f"Diagnosis: {state['diagnosis'].primary_pathogen_code} "
            f"({state['diagnosis'].reasoning})\n"
            f"Plot area: {case['areaHectares']} hectares.\n"
            # The farm's date from the backend, so the proposal and the validator agree on "today"
            # (UTC is still yesterday in Sri Lanka before 05:30). Own clock only as a fallback.
            f"Today: {safety.get('today') or datetime.now(UTC).date().isoformat()}. "
            f"Planned harvest: {safety.get('harvestDate')}\n"
            f"{weather_line}"
            f"Approved products:\n{options}\n{guidance}",
        )

        await deps.emit(
            "StepCompleted",
            {
                "agentRole": AgentRole.ACTION.value,
                "sequenceNo": 4,
                "payload": {"proposal": proposal.model_dump(mode="json"), "revision": revisions},
            },
        )
        return {**state, "proposal": proposal, "safety_profile": safety}

    async def validation(state: RunState) -> RunState:
        await step(AgentRole.VALIDATION, 5, "Check the proposal against the safety rules")
        tools = deps.client_for(AgentRole.VALIDATION)
        proposal = state["proposal"]

        # The agent does not judge safety: it submits the proposal to deterministic C# and
        # reports what came back. This call is the whole of its authority here.
        raw = await tools.call(
            "validate_prescription",
            # Rule V10 checks the proposal belongs to the case this run is resolving.
            runId=state["run_id"],
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
        review = await review_verdict(state, verdict, tools)
        return {**state, "verdict": verdict, "safety_review": review}

    async def review_verdict(state: RunState, verdict: Verdict, tools: ToolClient) -> SafetyReview | None:
        """
        The Validation agent's own reasoning: it reads the verdict against the rule limits and
        explains it, turning each failure into a concrete fix. The route the run takes is already
        fixed by the verdict above; this can only add explanation and advice. A review that
        contradicts the verdict, or that the model cannot produce, is recorded and set aside.
        """
        proposal = state["proposal"]
        case = state["case"]

        # The limits make the fixes concrete ("use 2.0-2.5 kg/ha"). Without them the review is
        # vaguer, not wrong, so an unreachable tool does not stop it.
        limits: dict[str, Any] | None = None
        try:
            limits = await tools.call("get_rule_limits", productId=proposal.product_id, cropId=case["cropId"])
        except ToolError:
            limits = None

        expected = DECISION_FOR_VERDICT.get(verdict.outcome, ReviewDecision.REJECT)
        failures = "\n".join(
            f"- {f.code} {f.name} ({f.severity}): {f.message}" + (f" [{f.evidence}]" if f.evidence else "")
            for f in verdict.failures
        )
        try:
            review = await deps.llm.generate(
                SafetyReview,
                VALIDATION_SYSTEM,
                f"Verdict from the rule engine: {verdict.outcome}. {verdict.summary}\n"
                f"Your decision must be: {expected.value}\n"
                f"Failed rules:\n{failures or 'none'}\n"
                f"Proposal: product {proposal.product_id}, {proposal.dose_per_hectare} per hectare, "
                f"{proposal.total_quantity} in total over {case.get('areaHectares')} ha, "
                f"spray on {proposal.spray_date.isoformat()}. "
                f"Planned harvest: {state.get('safety_profile', {}).get('harvestDate', 'unknown')}.\n"
                f"{describe_limits(limits)}",
            )
        except LlmError as error:
            await record_review(accepted=False, reason=f"The model could not review the verdict: {error}")
            return None

        if (problem := review.problem_with(verdict)) is not None:
            log.warning("safety_review_discarded", run_id=state["run_id"], reason=problem)
            await record_review(accepted=False, reason=problem, review=review)
            return None

        await record_review(accepted=True, review=review)
        return review

    async def record_review(
        *, accepted: bool, reason: str | None = None, review: SafetyReview | None = None
    ) -> None:
        await deps.emit(
            "SafetyReviewed",
            {
                "agentRole": AgentRole.VALIDATION.value,
                "payload": {
                    "accepted": accepted,
                    "reason": reason,
                    "review": review.model_dump(mode="json") if review is not None else None,
                },
            },
        )

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
    graph.add_node("triage", triage)
    graph.add_node("escalated", escalated)
    graph.add_node("action", action)
    graph.add_node("validation", validation)
    graph.add_node("revise", increment_revision)
    graph.add_node("approved", approved)
    graph.add_node("rejected", rejected)
    graph.add_node("exhausted", exhausted)

    graph.add_edge(START, "coordinator")
    graph.add_edge("coordinator", "diagnosis")
    graph.add_edge("diagnosis", "triage")
    graph.add_conditional_edges("triage", route_after_triage, {"treat": "action", "agronomist": "escalated"})
    graph.add_edge("escalated", END)
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
