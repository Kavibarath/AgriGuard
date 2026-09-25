"""
Typed input/output contracts for the four agents (§9.2).

Each agent has a distinct Pydantic output model, and every model response is validated against
it before the graph moves on. A model that returns prose, invents a field or drops a required one
fails here — loudly and early — rather than leaking malformed data into a prescription.
"""

from __future__ import annotations

from datetime import date
from enum import StrEnum
from typing import Annotated

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
