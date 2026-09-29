"""
The Validation agent's own reasoning (SafetyReview): it explains the deterministic verdict and
suggests fixes, but can never change where the run goes.
"""

from __future__ import annotations

from typing import Any

import pytest

from app.config import Settings
from app.contracts import (
    PrescriptionProposal,
    ReviewDecision,
    RuleFix,
    RunOutcome,
    RunRequest,
    SafetyReview,
    Verdict,
    revision_guidance,
)
from app.runner import execute_run
from tests.conftest import StubLlm, StubTools, verdict
from tests.test_graph import RecordingReporter, run


def reviews(reporter: RecordingReporter) -> list[dict[str, Any]]:
    return [payload["payload"] for name, payload in reporter.events if name == "SafetyReviewed"]


class TestTheReviewExplainsTheVerdict:
    async def test_a_passing_proposal_gets_an_explanation_and_reaches_the_result(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        reporter = RecordingReporter()
        result = await execute_run(
            RunRequest(run_id="run-1", case_id="case-1", objective="o"),
            llm,  # type: ignore[arg-type]
            None,  # type: ignore[arg-type]
            settings,
            reporter,  # type: ignore[arg-type]
            StubTools().factory(settings),
        )

        assert result.outcome == RunOutcome.PENDING_APPROVAL
        assert result.safety_review is not None
        assert result.safety_review.decision == ReviewDecision.PASS
        assert reviews(reporter)[0]["accepted"] is True

    async def test_the_review_quotes_the_rules_table(self, llm: StubLlm, settings: Settings) -> None:
        tools = StubTools()

        await run(llm, tools, settings)

        assert ("Validation", "get_rule_limits") in tools.calls
        prompt = next(user for schema, user in llm.prompts if schema == "SafetyReview")
        assert "dose 1.5 to 2.5 kg/ha; pre-harvest interval 7 days" in prompt
        assert "Your decision must be: PASS" in prompt

    async def test_its_fixes_are_added_to_what_the_action_agent_is_told(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        tools = StubTools({"validate_prescription": verdict("Revise", code="V3", message="Dose too high.")})

        await run(llm, tools, settings)

        second_proposal = [user for schema, user in llm.prompts if schema == "PrescriptionProposal"][1]
        # The rule engine's own message first, never replaced; the agent's fix after it.
        assert "- V3 (Revise): Dose too high." in second_proposal
        assert (
            "The Validation agent suggests:\n- V3: Bring it within the limit. (suggested 2.0)"
            in second_proposal
        )


class TestTheReviewCannotOverrideTheVerdict:
    async def test_a_review_that_passes_a_revisable_proposal_is_discarded(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        # A model steered into saying "all fine" about a proposal the rules sent back.
        llm.responses[SafetyReview] = SafetyReview(
            decision=ReviewDecision.PASS, explanation="Looks safe to me."
        )
        tools = StubTools({"validate_prescription": verdict("Revise")})

        state, reporter = await run(llm, tools, settings)

        # The route came from the verdict: revised, then given up on. Never pending approval.
        assert state["outcome"] == RunOutcome.FAILED
        assert state["safety_review"] is None
        discarded = reviews(reporter)[0]
        assert discarded["accepted"] is False
        assert "said PASS but the rule engine's verdict is Revise" in discarded["reason"]

    async def test_a_review_that_cites_a_rule_which_passed_is_discarded(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        llm.responses[SafetyReview] = SafetyReview(
            decision=ReviewDecision.REVISE,
            explanation="Change the product.",
            fixes=[RuleFix(rule_code="V2", problem="Not approved.", fix="Use another product.")],
        )
        tools = StubTools({"validate_prescription": verdict("Revise", code="V3")})

        _, reporter = await run(llm, tools, settings)

        assert "cites rules that did not fail: V2" in reviews(reporter)[0]["reason"]
        # The Action agent is still told what the rule engine said, and nothing from the discarded review.
        second_proposal = [user for schema, user in llm.prompts if schema == "PrescriptionProposal"][1]
        assert "V3 (Revise)" in second_proposal
        assert "Use another product." not in second_proposal

    async def test_the_run_goes_on_when_the_model_cannot_review(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        # The verdict never depends on the model, so neither does the run's outcome.
        llm.failures[SafetyReview] = 99

        state, reporter = await run(llm, StubTools(), settings)

        assert state["outcome"] == RunOutcome.PENDING_APPROVAL
        assert "could not review the verdict" in reviews(reporter)[0]["reason"]

    async def test_without_the_rule_limits_the_review_still_happens(
        self, llm: StubLlm, settings: Settings
    ) -> None:
        tools = StubTools()
        tools.failing.add("get_rule_limits")

        state, _ = await run(llm, tools, settings)

        assert state["safety_review"] is not None
        prompt = next(user for schema, user in llm.prompts if schema == "SafetyReview")
        assert "Rule limits: unavailable." in prompt


class TestConsistencyRules:
    def _verdict(self, outcome: str, *failed: str) -> Verdict:
        return Verdict.model_validate(
            {
                "outcome": outcome,
                "summary": outcome,
                "results": [
                    {"code": code, "name": "Rule", "status": "Failed", "severity": "Revise", "message": "m"}
                    for code in failed
                ],
            }
        )

    @pytest.mark.parametrize(
        ("outcome", "decision"),
        [
            ("Approved", ReviewDecision.PASS),
            ("Revise", ReviewDecision.REVISE),
            ("Rejected", ReviewDecision.REJECT),
        ],
    )
    def test_the_decision_must_restate_the_verdict(self, outcome: str, decision: ReviewDecision) -> None:
        fixes = [] if decision == ReviewDecision.PASS else [RuleFix(rule_code="V3", problem="p", fix="f")]
        failed = () if decision == ReviewDecision.PASS else ("V3",)

        assert (
            SafetyReview(decision=decision, explanation="e", fixes=fixes).problem_with(
                self._verdict(outcome, *failed)
            )
            is None
        )

    def test_a_revise_verdict_needs_at_least_one_fix(self) -> None:
        review = SafetyReview(decision=ReviewDecision.REVISE, explanation="e")

        assert "no fix" in (review.problem_with(self._verdict("Revise", "V3")) or "")

    def test_a_passing_verdict_has_nothing_to_fix(self) -> None:
        review = SafetyReview(
            decision=ReviewDecision.PASS,
            explanation="e",
            fixes=[RuleFix(rule_code="V3", problem="p", fix="f")],
        )

        # Refused either way: no rule failed, so any fix cites a rule that passed.
        assert review.problem_with(self._verdict("Approved")) is not None

    def test_a_rule_code_outside_v1_to_v11_is_not_valid_output(self) -> None:
        with pytest.raises(ValueError):
            RuleFix(rule_code="V12", problem="p", fix="f")

    def test_without_a_review_the_guidance_is_the_rule_engine_s_alone(self) -> None:
        v = self._verdict("Revise", "V3")

        assert revision_guidance(v, None) == v.revision_guidance()


def test_the_proposal_fixture_is_valid(proposal: PrescriptionProposal) -> None:
    # Guards the review prompt's inputs: it quotes the dose, total and spray date.
    assert proposal.dose_per_hectare == 2.0
