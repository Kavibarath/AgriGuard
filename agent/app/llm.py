"""
LLM access, with the two properties the workflow depends on: structured output, and a bounded
repair loop when the model does not produce it.

The provider is pluggable (ADR: Ollama locally and for the viva, a hosted model in the cloud
where there is no GPU) because the demo must not depend on a machine we do not control.
"""

from __future__ import annotations

import json
from typing import Any, Protocol, TypeVar

import httpx
import structlog
from pydantic import BaseModel, ValidationError

from .config import Settings

log = structlog.get_logger(__name__)

TModel = TypeVar("TModel", bound=BaseModel)


class LlmError(RuntimeError):
    """The model was unreachable, too slow, or produced nothing usable after repair attempts."""


class LlmProvider(Protocol):
    async def complete(self, system: str, user: str) -> str: ...

    @property
    def model_name(self) -> str: ...


class OllamaProvider:
    """
    Local inference. `format="json"` makes Ollama emit bare JSON with no prose or code fences,
    which removes the whole class of "strip the markdown first" parsing bugs.

    Runs CPU-only on the demo machine (the GPU driver crashes on this card), which is why the
    prompts are short and `num_predict` is capped: about 23 tokens/second is the budget.
    """

    def __init__(self, client: httpx.AsyncClient, settings: Settings) -> None:
        self._client = client
        self._settings = settings

    @property
    def model_name(self) -> str:
        return self._settings.llm_model

    async def complete(self, system: str, user: str) -> str:
        payload = {
            "model": self._settings.llm_model,
            "system": system,
            "prompt": user,
            "format": "json",
            "stream": False,
            "options": {
                "temperature": self._settings.llm_temperature,
                "num_predict": self._settings.llm_num_predict,
            },
        }

        try:
            response = await self._client.post(
                f"{self._settings.ollama_base_url}/api/generate",
                json=payload,
                timeout=self._settings.llm_timeout_seconds,
            )
        except httpx.HTTPError as error:
            # Covers the viva demo of stopping Ollama mid-run (golden case G8).
            raise LlmError(f"The language model is unreachable: {error}") from error

        if response.status_code >= 400:
            raise LlmError(f"Model returned {response.status_code}: {response.text[:200]}")

        return str(response.json().get("response", ""))


class StructuredLlm:
    """Wraps a provider and guarantees a validated Pydantic object, or raises."""

    def __init__(self, provider: LlmProvider, settings: Settings) -> None:
        self._provider = provider
        self._settings = settings

    @property
    def model_name(self) -> str:
        return self._provider.model_name

    async def generate(
        self,
        schema: type[TModel],
        system: str,
        user: str,
        on_repair: Any = None,
    ) -> TModel:
        """
        Asks for JSON matching `schema`. On invalid output the model is shown its own mistake and
        asked again, at most `schema_repair_attempts` times (§9.3). Repairs are recorded, because
        a model that needs two attempts every run is a finding, not a detail.
        """
        instruction = (
            f"{system}\n\n"
            "Reply with a single JSON object and nothing else. It must match this JSON schema:\n"
            f"{json.dumps(schema.model_json_schema(), separators=(',', ':'))}"
        )
        prompt = user
        last_error = ""

        for attempt in range(self._settings.schema_repair_attempts + 1):
            raw = await self._provider.complete(instruction, prompt)
            try:
                return schema.model_validate_json(raw)
            except ValidationError as error:
                last_error = _summarise(error)
                log.warning(
                    "llm_schema_invalid", schema=schema.__name__, attempt=attempt + 1, error=last_error
                )
                if on_repair is not None:
                    await on_repair(attempt + 1, last_error)
                # Show the model exactly what was wrong; vague retries tend to repeat the fault.
                prompt = (
                    f"{user}\n\n"
                    f"Your previous reply was rejected: {last_error}\n"
                    "Return only valid JSON matching the schema."
                )

        raise LlmError(
            f"The model did not return valid {schema.__name__} after "
            f"{self._settings.schema_repair_attempts + 1} attempts: {last_error}"
        )


def _summarise(error: ValidationError) -> str:
    """One short line per problem — long validator dumps make the next prompt worse, not better."""
    problems = [f"{'.'.join(str(p) for p in e['loc']) or 'root'}: {e['msg']}" for e in error.errors()[:5]]
    return "; ".join(problems)
