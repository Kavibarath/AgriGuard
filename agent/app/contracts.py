"""
Typed input/output contracts for the four agents (§9.2).

Each agent has a distinct Pydantic output model, and every model response is validated against
it before the graph moves on. A model that returns prose, invents a field or drops a required one
fails here — loudly and early — rather than leaking malformed data into a prescription.
"""

from __future__ import annotations

import re
from datetime import date
from enum import StrEnum
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints

ShortText = Annotated[str, StringConstraints(max_length=300, strip_whitespace=True)]


class AgentRole(StrEnum):
    COORDINATOR = "Coordinator"
    DIAGNOSIS = "Diagnosis"
    ACTION = "Action"
    VALIDATION = "Validation"


class RunOutcome(StrEnum):
    PENDING_APPROVAL = "PendingApproval"
    REJECTED = "Rejected"
    FAILED = "Failed"
    # The Coordinator handed the case to an agronomist on purpose: not a failure.
    MANUAL_REVIEW = "ManualReview"


class StrictModel(BaseModel):
    """Rejects unknown fields: a model inventing `"dose_ml": 900` must not pass silently."""

    model_config = ConfigDict(extra="forbid")


# ── Coordinator ─────────────────────────────────────────────────────────────
class PlanStep(StrictModel):
    seq: int = Field(ge=1, le=10)
    agent: AgentRole
    goal: ShortText
    success_criteria: ShortText


class Plan(StrictModel):
    steps: list[PlanStep] = Field(min_length=1, max_length=10)


class TriageRoute(StrEnum):
    TREAT = "TREAT"
    AGRONOMIST = "AGRONOMIST"


class Triage(StrictModel):
    """
    The Coordinator's second output, after the diagnosis: can a product treat this, and what can
    the farmer do meanwhile?
    """

    route: TriageRoute
    reason: ShortText
    # Non-chemical steps only: sanitation, spacing, watering, removing infected plants.
    farmer_advice: list[ShortText] = Field(min_length=1, max_length=4)


# Words and amounts that belong in a prescription, not in general advice. A tip that contains any
# of them is dropped: the only chemical advice a farmer gets is a validated, approved prescription.
_CHEMICAL_ADVICE = re.compile(
    r"(fungicide|insecticide|pesticide|herbicide|bactericide|miticide|acaricide|\bspray|"
    r"\b\d+(\.\d+)?\s*(ml|l|g|kg|litres?|liters?|grams?)\b|"
    # "Apply the approved product as directed": a product is only ever named in a prescription.
    r"\b(apply|use|treat with)\b.{0,30}\b(product|chemical|treatment)s?\b|\bapproved product)",
    re.IGNORECASE,
)


def is_safe_advice(tip: str) -> bool:
    return _CHEMICAL_ADVICE.search(tip) is None


class TriageDecision(StrictModel):
    """What the run actually does after triage, and who decided it (§9.2 "decide terminal outcome")."""

    route: TriageRoute
    reason: ShortText
    # "rules" when a hard stop decided (no approved product, weak diagnosis);
    # "coordinator" when the model did.
    decided_by: Literal["rules", "coordinator"]
    farmer_advice: list[ShortText] = Field(default_factory=list, max_length=4)


# ── Diagnosis ───────────────────────────────────────────────────────────────
class PathogenCandidate(StrictModel):
    pathogen_code: Annotated[str, StringConstraints(max_length=40, strip_whitespace=True)]
    confidence: float = Field(ge=0.0, le=1.0)
    evidence: list[ShortText] = Field(min_length=1, max_length=6)


class Diagnosis(StrictModel):
    candidates: list[PathogenCandidate] = Field(min_length=1, max_length=5)
    primary_pathogen_code: Annotated[str, StringConstraints(max_length=40, strip_whitespace=True)]
    reasoning: ShortText


# ── Action ──────────────────────────────────────────────────────────────────
class PrescriptionProposal(StrictModel):
    product_id: str
    dose_per_hectare: float = Field(gt=0)
    total_quantity: float = Field(gt=0)
    spray_date: date
    dealer_id: str | None = None
    justification: ShortText


# ── Validation ──────────────────────────────────────────────────────────────
class RuleFinding(StrictModel):
    code: str
    name: str
    status: str
    severity: str
    message: str
    evidence: str | None = None


class Verdict(StrictModel):
    """The deterministic validator's answer, as returned by the backend tool."""

    outcome: str  # Approved | Revise | Rejected
    summary: str
    results: list[RuleFinding]

    @property
    def failures(self) -> list[RuleFinding]:
        return [r for r in self.results if r.status == "Failed"]

    def revision_guidance(self) -> str:
        """What the Action agent is told to fix. Only the failures, never the whole verdict."""
        return "\n".join(f"- {f.code} ({f.severity}): {f.message}" for f in self.failures)


class ReviewDecision(StrEnum):
    PASS = "PASS"
    REVISE = "REVISE"
    REJECT = "REJECT"


# The only decision a review may state for each verdict. The model restates it; it never chooses it.
DECISION_FOR_VERDICT: dict[str, ReviewDecision] = {
    "Approved": ReviewDecision.PASS,
    "Revise": ReviewDecision.REVISE,
    "Rejected": ReviewDecision.REJECT,
}

RuleCode = Annotated[str, StringConstraints(pattern=r"^V([1-9]|1[01])$")]


class RuleFix(StrictModel):
    rule_code: RuleCode
    problem: ShortText
    fix: ShortText
    # A concrete value when one would fix it: "dose_per_hectare: 2.0", "spray_date: 2026-10-02".
    suggested_value: ShortText | None = None


class SafetyReview(StrictModel):
    """
    The Validation agent's own output: its reading of the deterministic verdict. It explains and
    suggests; it has no say in the outcome, which is taken from the verdict alone.
    """

    decision: ReviewDecision
    explanation: ShortText
    fixes: list[RuleFix] = Field(default_factory=list, max_length=11)

    def problem_with(self, verdict: Verdict) -> str | None:
        """
        Why this review cannot be trusted for this verdict, or None if it can. A review that
        disagrees with the rule engine, or talks about rules that did not fail, is discarded:
        a model that has been steered (by injected text, say) must not be able to reword the
        verdict into something else.
        """
        expected = DECISION_FOR_VERDICT.get(verdict.outcome)
        if expected is None:
            return f"The verdict's outcome '{verdict.outcome}' is not one the review can restate."
        if self.decision != expected:
            return f"The review said {self.decision} but the rule engine's verdict is {verdict.outcome}."

        failed = {f.code for f in verdict.failures}
        cited = {fix.rule_code for fix in self.fixes}
        if extra := sorted(cited - failed):
            return f"The review cites rules that did not fail: {', '.join(extra)}."
        if expected == ReviewDecision.REVISE and not self.fixes:
            return "The review gave no fix for a verdict that asks for a revision."
        if expected == ReviewDecision.PASS and self.fixes:
            return "The review suggested fixes for a proposal that passed every rule."
        return None

    def guidance(self) -> str:
        """The fixes, as lines the Action agent is shown after the rule engine's own messages."""
        return "\n".join(
            f"- {f.rule_code}: {f.fix}" + (f" (suggested {f.suggested_value})" if f.suggested_value else "")
            for f in self.fixes
        )


def revision_guidance(verdict: Verdict, review: SafetyReview | None) -> str:
    """
    What the Action agent is told after a Revise verdict. The rule engine's messages always come
    first and are never replaced: the Validation agent's fixes can only add to them.
    """
    guidance = verdict.revision_guidance()
    if review is not None and review.fixes:
        guidance += f"\nThe Validation agent suggests:\n{review.guidance()}"
    return guidance


# ── Run I/O ─────────────────────────────────────────────────────────────────
class RunRequest(StrictModel):
    """What the backend posts to start a run. Ids only — the agent fetches the rest via tools."""

    run_id: str
    case_id: str
    objective: ShortText
    correlation_id: str | None = None
    # Appended by an agronomist asking for a revision, so the next attempt has their steer.
    reviewer_note: ShortText | None = None


class RunResult(StrictModel):
    run_id: str
    outcome: RunOutcome
    proposal: PrescriptionProposal | None = None
    verdict: Verdict | None = None
    diagnosis: Diagnosis | None = None
    plan: Plan | None = None
    failure_reason: str | None = None
    revisions: int = 0
    # The Validation agent's review of the final verdict, when it passed the consistency check.
    safety_review: SafetyReview | None = None
    # The Coordinator's triage after the diagnosis; None when the run ended before it.
    triage: TriageDecision | None = None
