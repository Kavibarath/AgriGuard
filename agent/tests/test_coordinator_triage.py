"""
The Coordinator's triage after the diagnosis (§9.2 "decide terminal outcome"): hard stops in code
first, then the model's judgement, which may escalate but never overrule a stop.
"""

from __future__ import annotations

from typing import Any

from app.config import Settings
from app.contracts import (
    Diagnosis,
    PathogenCandidate,
    RunOutcome,
    RunRequest,
    Triage,
    TriageRoute,
    is_safe_advice,
)
from app.runner import execute_run
from tests.conftest import DEFAULT_TOOL_RESPONSES, StubLlm, StubTools
from tests.test_graph import RecordingReporter, run

WILT_PROFILE = {
    **DEFAULT_TOOL_RESPONSES["get_pathogen_profile"],
    "code": "BACTERIAL_WILT",
    "commonName": "Bacterial wilt",
    "type": "BacterialDisease",
    "chemicalControl": False,
    "approvedProducts": 0,
}


def triage_events(reporter: RecordingReporter) -> list[dict[str, Any]]:
    return [p["payload"] for n, p in reporter.events if n == "TriageDecided"]


def action_ran(reporter: RecordingReporter) -> bool:
    return any(n == "StepStarted" and p["agentRole"] == "Action" for n, p in reporter.events)


class TestHardStops:
    async def test_no_approved_product_goes_to_an_agronomist_even_if_the_model_says_treat(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        # The stub model says TREAT (the default); the facts say nothing can be prescribed.
        state, reporter = await run(llm, StubTools({"get_pathogen_profile": WILT_PROFILE}), settings)

        assert state["outcome"] == RunOutcome.MANUAL_REVIEW
        assert "No approved product controls Bacterial wilt on Tomato" in state["failure_reason"]
        decided = triage_events(reporter)[0]
        assert decided["decided_by"] == "rules"
        assert decided["overridden"] is True
        assert decided["model_route"] == "TREAT"
        # Nothing was drafted, so nothing was held or validated.
        assert not action_ran(reporter)

    async def test_an_uncertain_diagnosis_goes_to_an_agronomist(
        self, llm: StubLlm, settings: Settings, diagnosis: Diagnosis
    ) -> None:
        llm.responses[Diagnosis] = diagnosis.model_copy(
            update={
                "candidates": [
                    PathogenCandidate(
                        pathogen_code="LATE_BLIGHT", confidence=0.35, evidence=["brown patches"]
                    )
                ]
            }
        )

        state, _ = await run(llm, StubTools(), settings)

        assert state["outcome"] == RunOutcome.MANUAL_REVIEW
        assert "35% confidence" in state["failure_reason"]

    async def test_a_pathogen_outside_the_catalogue_goes_to_an_agronomist(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        unknown = {**DEFAULT_TOOL_RESPONSES["get_pathogen_profile"], "known": False, "chemicalControl": False}

        state, _ = await run(llm, StubTools({"get_pathogen_profile": unknown}), settings)

        assert state["outcome"] == RunOutcome.MANUAL_REVIEW
        assert "not in the pathogen catalogue" in state["failure_reason"]


class TestTheCoordinatorsJudgement:
    async def test_a_treatable_diagnosis_goes_on_to_the_action_agent(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        state, reporter = await run(llm, StubTools(), settings)

        assert state["outcome"] == RunOutcome.PENDING_APPROVAL
        assert state["triage"].decided_by == "coordinator"
        assert action_ran(reporter)

    async def test_the_model_may_be_more_cautious_than_the_rules(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        # Treatable on paper, but the farmer's note describes something no product fixes.
        llm.responses[Triage] = Triage(
            route=TriageRoute.AGRONOMIST,
            reason="The whole field collapsed overnight; that needs a visit, not a spray.",
            farmer_advice=["Keep people and animals out of the field until the agronomist visits."],
        )

        state, reporter = await run(llm, StubTools(), settings)

        assert state["outcome"] == RunOutcome.MANUAL_REVIEW
        assert triage_events(reporter)[0]["decided_by"] == "coordinator"
        assert not action_ran(reporter)

    async def test_the_triage_hears_the_hard_stop_and_the_farmer_note(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        await run(llm, StubTools({"get_pathogen_profile": WILT_PROFILE}), settings)

        prompt = next(user for schema, user in llm.prompts if schema == "Triage")
        assert "A rule requires route AGRONOMIST" in prompt
        assert "<farmer_note>" in prompt

    async def test_without_the_model_the_hard_stops_still_decide(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        llm.failures[Triage] = 99

        state, reporter = await run(llm, StubTools({"get_pathogen_profile": WILT_PROFILE}), settings)

        assert state["outcome"] == RunOutcome.MANUAL_REVIEW
        assert triage_events(reporter)[0]["farmer_advice"] == []


class TestFarmerAdvice:
    async def test_advice_naming_a_chemical_or_a_dose_never_reaches_the_farmer(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        llm.responses[Triage] = Triage(
            route=TriageRoute.AGRONOMIST,
            reason="Needs an agronomist.",
            farmer_advice=[
                "Pull out wilted plants and burn them away from the field.",
                "Spray a copper fungicide at 2 g per litre.",
                "Do not replant tomato in this bed this season.",
            ],
        )

        state, reporter = await run(llm, StubTools(), settings)

        assert state["triage"].farmer_advice == [
            "Pull out wilted plants and burn them away from the field.",
            "Do not replant tomato in this bed this season.",
        ]
        assert triage_events(reporter)[0]["advice_dropped"] == 1

    def test_the_advice_filter(self) -> None:
        assert is_safe_advice("Water at the base of the plant in the morning.")
        assert not is_safe_advice("Use an insecticide on the undersides of the leaves.")
        assert not is_safe_advice("Mix 30 ml into the tank.")
        assert not is_safe_advice("Spray neem oil weekly.")


async def test_the_hand_off_and_the_advice_reach_the_backend(llm: StubLlm, settings: Settings) -> None:
    reporter = RecordingReporter()

    result = await execute_run(
        RunRequest(run_id="run-1", case_id="case-1", objective="o"),
        llm,  # type: ignore[arg-type]
        None,  # type: ignore[arg-type]
        settings,
        reporter,  # type: ignore[arg-type]
        StubTools({"get_pathogen_profile": WILT_PROFILE}).factory(settings),
    )

    assert result.outcome == RunOutcome.MANUAL_REVIEW
    assert result.triage is not None
    assert result.triage.farmer_advice
    assert result.proposal is None
    # A deliberate hand-off is reported as finished, not as a failure.
    assert "RunCompleted" in reporter.types()
    assert "RunFailed" not in reporter.types()
