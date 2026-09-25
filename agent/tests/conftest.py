"""Stubs that let the graph be tested without Ollama, a database or the backend."""

from __future__ import annotations

from datetime import UTC, date, datetime, timedelta
from typing import Any

import pytest
from pydantic import BaseModel

from app.config import Settings
from app.contracts import AgentRole, Diagnosis, PathogenCandidate, Plan, PlanStep, PrescriptionProposal
from app.llm import LlmError
from app.tools import ToolClient

TOMORROW = (datetime.now(UTC).date() + timedelta(days=1)).isoformat()


@pytest.fixture
def settings() -> Settings:
    return Settings(api_key="test-key", api_base_url="http://backend.test", max_revisions=2)


class StubLlm:
    """
    Returns canned objects per schema. `failures` makes a schema fail a given number of times
    first, so the repair loop can be exercised.
    """

    def __init__(self, responses: dict[type[BaseModel], Any] | None = None) -> None:
        self.responses: dict[type[BaseModel], Any] = responses or {}
        self.calls: list[str] = []
        self.failures: dict[type[BaseModel], int] = {}
        self.unreachable = False
        self.model_name = "stub"

    async def generate(self, schema: type[BaseModel], system: str, user: str, on_repair: Any = None) -> Any:
        self.calls.append(schema.__name__)
        if self.unreachable:
            raise LlmError("The language model is unreachable: stub")

        remaining = self.failures.get(schema, 0)
        if remaining:
            self.failures[schema] = remaining - 1
            if on_repair is not None:
                await on_repair(1, "stubbed schema failure")
            raise LlmError(f"The model did not return valid {schema.__name__}")

        value = self.responses.get(schema)
        if value is None:
            raise AssertionError(f"StubLlm has no canned response for {schema.__name__}")
        return value(user) if callable(value) and not isinstance(value, BaseModel) else value


class StubTools:
    """
    Stands in for the backend's tool endpoints while still enforcing the real allow-list, so a
    test proves the control rather than bypassing it.
    """

    def __init__(self, responses: dict[str, Any] | None = None) -> None:
        self.responses: dict[str, Any] = {**DEFAULT_TOOL_RESPONSES, **(responses or {})}
        self.calls: list[tuple[str, str]] = []
        self.failing: set[str] = set()

    def factory(self, settings: Settings) -> Any:
        def make(role: AgentRole) -> Any:
            return _RoleBoundStub(self, role, settings)

        return make


class _RoleBoundStub:
    def __init__(self, parent: StubTools, role: AgentRole, settings: Settings) -> None:
        self._parent = parent
        self._role = role
        # Reuse the real client purely for its allow-list check, then answer from the stub.
        self._guard = ToolClient(role, None, settings, "test")  # type: ignore[arg-type]

    async def call(self, tool: str, **params: Any) -> Any:
        from app.tools import ALLOW_LIST, ToolError, ToolNotAllowedError

        if tool not in ALLOW_LIST.get(self._role, frozenset()):
            raise ToolNotAllowedError(f"{self._role} may not call '{tool}'.")

        self._parent.calls.append((self._role.value, tool))
        if tool in self._parent.failing:
            raise ToolError(f"Tool '{tool}' failed after 3 attempts: stubbed outage")

        return self._parent.responses[tool]


DEFAULT_TOOL_RESPONSES: dict[str, Any] = {
    "get_case_detail": {
        "caseId": "case-1",
        "cropCycleId": "cycle-1",
        "plotId": "plot-1",
        "cropId": "crop-tomato",
        "districtId": "district-1",
        "cropName": "Tomato",
        "stage": "Flowering",
        "sownDate": "2026-07-01",
        "areaHectares": 0.8,
        "symptomCodes": ["leaf_brown_patches", "leaf_water_soaked_lesions"],
        "farmerNote": "Brown patches spreading fast after the rain.",
        "candidatePathogens": [
            {"code": "LATE_BLIGHT", "commonName": "Late blight"},
            {"code": "EARLY_BLIGHT", "commonName": "Early blight"},
        ],
    },
    "get_crop_history": {"summary": "No chemical applications recorded this cycle."},
    "get_regional_outbreak_signal": {
        "summary": "Late blight pressure high in this district (7 cases/14 days)."
    },
    "search_approved_products": {
        "products": [
            {
                "productId": "product-mancozeb",
                "productName": "Mancozeb 80 WP",
                "minDosePerHectare": 1.5,
                "maxDosePerHectare": 2.5,
            }
        ]
    },
    "get_plot_safety_profile": {
        "harvestDate": "2026-11-01",
        "productWindows": [
            {"productId": "product-mancozeb", "canSprayToday": True, "lastSafeSprayDate": "2026-10-25"}
        ],
    },
    "check_stock_availability": {"availableQuantity": 10.0},
    "get_product_pricing": {"unitPrice": 2400.0, "packSize": 1.0},
    "validate_prescription": {
        "outcome": "Approved",
        "summary": "All 11 checked rules passed.",
        "results": [
            {
                "code": "V5",
                "name": "Pre-harvest interval is respected",
                "status": "Passed",
                "severity": "Reject",
                "message": "Clears the interval.",
                "evidence": None,
            }
        ],
    },
}


def verdict(
    outcome: str, *, code: str = "V3", severity: str = "Revise", message: str = "Dose out of range."
) -> dict[str, Any]:
    return {
        "outcome": outcome,
        "summary": f"{outcome}: {code}.",
        "results": [
            {
                "code": code,
                "name": "Rule",
                "status": "Failed" if outcome != "Approved" else "Passed",
                "severity": severity,
                "message": message,
                "evidence": f"{code}=failed",
            }
        ],
    }


@pytest.fixture
def plan() -> Plan:
    return Plan(
        steps=[
            PlanStep(
                seq=1,
                agent=AgentRole.DIAGNOSIS,
                goal="Identify the disease",
                success_criteria="A ranked list",
            ),
            PlanStep(
                seq=2, agent=AgentRole.ACTION, goal="Propose a treatment", success_criteria="A proposal"
            ),
            PlanStep(seq=3, agent=AgentRole.VALIDATION, goal="Check the rules", success_criteria="A verdict"),
        ]
    )


@pytest.fixture
def diagnosis() -> Diagnosis:
    return Diagnosis(
        candidates=[
            PathogenCandidate(
                pathogen_code="LATE_BLIGHT", confidence=0.82, evidence=["water-soaked lesions"]
            ),
            PathogenCandidate(pathogen_code="EARLY_BLIGHT", confidence=0.3, evidence=["brown spots"]),
        ],
        primary_pathogen_code="LATE_BLIGHT",
        reasoning="Water-soaked lesions after rain during flowering fit late blight.",
    )


@pytest.fixture
def proposal() -> PrescriptionProposal:
    return PrescriptionProposal(
        product_id="product-mancozeb",
        dose_per_hectare=2.0,
        total_quantity=1.6,
        spray_date=date.fromisoformat(TOMORROW),
        justification="Protectant fungicide approved for late blight on tomato.",
    )


@pytest.fixture
def llm(plan: Plan, diagnosis: Diagnosis, proposal: PrescriptionProposal) -> StubLlm:
    return StubLlm({Plan: plan, Diagnosis: diagnosis, PrescriptionProposal: proposal})
