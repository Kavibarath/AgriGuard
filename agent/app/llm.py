"""
LLM access, with the two properties the workflow depends on: structured output, and a bounded
repair loop when the model does not produce it.

The provider is pluggable (ADR: Ollama locally and for the viva, a hosted model in the cloud
where there is no GPU) because the demo must not depend on a machine we do not control.
"""

from __future__ import annotations

import asyncio
import json
from collections.abc import Awaitable, Callable
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

    async def is_reachable(self) -> bool: ...

    @property
    def model_name(self) -> str: ...


OLLAMA = "ollama"
OPENAI_COMPATIBLE = "openai-compatible"


def create_provider(client: httpx.AsyncClient, settings: Settings) -> LlmProvider:
    """
    Picks the provider from AGENT_LLM_PROVIDER. A misconfigured provider stops start-up, because
    a service that boots without a usable model would only fail later, mid-run, in front of a user.
    """
    if settings.llm_provider == OLLAMA:
        return OllamaProvider(client, settings)
    if settings.llm_provider == OPENAI_COMPATIBLE:
        missing = [
            name
            for name, value in (
                ("AGENT_LLM_BASE_URL", settings.llm_base_url),
                ("AGENT_LLM_API_KEY", settings.llm_api_key),
            )
            if not value
        ]
        if missing:
            raise ValueError(f"AGENT_LLM_PROVIDER={OPENAI_COMPATIBLE} needs {' and '.join(missing)}.")
        return OpenAICompatibleProvider(client, settings)
    raise ValueError(
        f"Unknown AGENT_LLM_PROVIDER '{settings.llm_provider}'. Use '{OLLAMA}' or '{OPENAI_COMPATIBLE}'."
    )


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

    async def is_reachable(self) -> bool:
        try:
            response = await self._client.get(f"{self._settings.ollama_base_url}/api/tags", timeout=3.0)
        except httpx.HTTPError:
            return False
        return response.status_code == 200


class OpenAICompatibleProvider:
    """
    Hosted inference over the OpenAI chat-completions API (Groq, Gemini's OpenAI endpoint, …),
    used where the deployment has no GPU. `response_format: json_object` is the equivalent of
    Ollama's `format="json"`: the model must emit one JSON object, so StructuredLlm's Pydantic
    validation and repair loop work unchanged. JSON mode requires the word "JSON" in the
    messages; StructuredLlm's instruction always contains it.

    Free tiers rate-limit per minute. A 429 is waited out (honouring Retry-After) a bounded number
    of times; after that the call fails like any other model error and the run ends safely.
    """

    def __init__(
        self,
        client: httpx.AsyncClient,
        settings: Settings,
        sleep: Callable[[float], Awaitable[None]] = asyncio.sleep,
    ) -> None:
        self._client = client
        self._settings = settings
        self._sleep = sleep

    @property
    def model_name(self) -> str:
        return self._settings.llm_model

    @property
    def _base_url(self) -> str:
        return self._settings.llm_base_url.rstrip("/")

    @property
    def _headers(self) -> dict[str, str]:
        return {"Authorization": f"Bearer {self._settings.llm_api_key}"}

    async def complete(self, system: str, user: str) -> str:
        payload = {
            "model": self._settings.llm_model,
            "messages": [
                {"role": "system", "content": system},
                {"role": "user", "content": user},
            ],
            "response_format": {"type": "json_object"},
            "temperature": self._settings.llm_temperature,
            "max_tokens": self._settings.llm_num_predict,
        }

        for attempt in range(self._settings.llm_rate_limit_retries + 1):
            try:
                response = await self._client.post(
                    f"{self._base_url}/chat/completions",
                    json=payload,
                    headers=self._headers,
                    timeout=self._settings.llm_timeout_seconds,
                )
            except httpx.HTTPError as error:
                raise LlmError(f"The language model is unreachable: {error}") from error

            if response.status_code == 429 and attempt < self._settings.llm_rate_limit_retries:
                wait = _retry_after(response, attempt, self._settings.llm_max_retry_wait_seconds)
                log.warning("llm_rate_limited", attempt=attempt + 1, wait_seconds=wait)
                await self._sleep(wait)
                continue
            if response.status_code >= 400:
                # The body never contains our key; providers echo only the error message.
                raise LlmError(f"Model returned {response.status_code}: {response.text[:200]}")
            return _message_content(response)

        raise AssertionError("unreachable: the last attempt either returns or raises")

    async def is_reachable(self) -> bool:
        """Lists models: authenticated, but spends no tokens from the daily quota."""
        try:
            response = await self._client.get(f"{self._base_url}/models", headers=self._headers, timeout=5.0)
        except httpx.HTTPError:
            return False
        return response.status_code == 200


def _retry_after(response: httpx.Response, attempt: int, cap: float) -> float:
    """Seconds to wait after a 429: the provider's Retry-After when given, else 2, 4, 8 … capped."""
    try:
        wait = float(response.headers.get("retry-after", ""))
    except ValueError:
        wait = 2.0 ** (attempt + 1)
    return max(0.0, min(wait, cap))


def _message_content(response: httpx.Response) -> str:
    """The first choice's text, or an LlmError for any body that is not a chat completion."""
    try:
        content = response.json()["choices"][0]["message"]["content"]
    except (ValueError, KeyError, IndexError, TypeError) as error:
        raise LlmError(f"The model returned an unexpected response: {response.text[:200]}") from error
    return str(content or "")


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
