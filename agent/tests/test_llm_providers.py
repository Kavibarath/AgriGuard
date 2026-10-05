"""The hosted (OpenAI-compatible) provider and the provider factory, over a stubbed HTTP transport."""

from __future__ import annotations

import json

import httpx
import pytest
import respx
from pydantic import BaseModel

from app.config import Settings
from app.llm import (
    LlmError,
    OllamaProvider,
    OpenAICompatibleProvider,
    StructuredLlm,
    create_provider,
)

BASE = "https://llm.test/openai/v1"
COMPLETIONS = f"{BASE}/chat/completions"


class Verdict(BaseModel):
    label: str
    score: float


def hosted_settings(**overrides: object) -> Settings:
    values: dict[str, object] = {
        "llm_provider": "openai-compatible",
        "llm_base_url": BASE + "/",
        "llm_api_key": "test-llm-key",
        "llm_model": "test-model",
        "llm_num_predict": 321,
        "llm_temperature": 0.1,
    }
    values.update(overrides)
    return Settings(**values)  # type: ignore[arg-type]


def completion(content: str) -> httpx.Response:
    return httpx.Response(200, json={"choices": [{"message": {"role": "assistant", "content": content}}]})


class SleepRecorder:
    def __init__(self) -> None:
        self.waits: list[float] = []

    async def __call__(self, seconds: float) -> None:
        self.waits.append(seconds)


@respx.mock
async def test_sends_a_json_mode_chat_completion_with_the_key() -> None:
    route = respx.post(COMPLETIONS).mock(return_value=completion('{"label":"ok","score":1}'))
    async with httpx.AsyncClient() as client:
        text = await OpenAICompatibleProvider(client, hosted_settings()).complete("be brief", "the case")

    assert text == '{"label":"ok","score":1}'
    request = route.calls.last.request
    assert request.headers["Authorization"] == "Bearer test-llm-key"
    body = json.loads(request.content)
    assert body["model"] == "test-model"
    assert body["response_format"] == {"type": "json_object"}
    assert body["max_tokens"] == 321
    assert body["messages"] == [
        {"role": "system", "content": "be brief"},
        {"role": "user", "content": "the case"},
    ]


@respx.mock
async def test_structured_output_validates_and_repairs_through_the_hosted_provider() -> None:
    route = respx.post(COMPLETIONS).mock(
        side_effect=[completion('{"label":"ok"}'), completion('{"label":"ok","score":0.9}')]
    )
    settings = hosted_settings()
    repairs: list[int] = []

    async def on_repair(attempt: int, _error: str) -> None:
        repairs.append(attempt)

    async with httpx.AsyncClient() as client:
        llm = StructuredLlm(OpenAICompatibleProvider(client, settings), settings)
        verdict = await llm.generate(Verdict, "system", "user", on_repair=on_repair)

    assert verdict == Verdict(label="ok", score=0.9)
    assert repairs == [1]
    # The schema instruction carries the word JSON, which hosted JSON mode requires.
    first = json.loads(route.calls[0].request.content)
    assert "JSON" in first["messages"][0]["content"]
    assert "rejected" in json.loads(route.calls[1].request.content)["messages"][1]["content"]


@respx.mock
async def test_a_rate_limit_is_waited_out_using_retry_after() -> None:
    respx.post(COMPLETIONS).mock(
        side_effect=[httpx.Response(429, headers={"retry-after": "3"}), completion("{}")]
    )
    sleep = SleepRecorder()
    async with httpx.AsyncClient() as client:
        text = await OpenAICompatibleProvider(client, hosted_settings(), sleep=sleep).complete("s", "u")

    assert text == "{}"
    assert sleep.waits == [3.0]


@respx.mock
async def test_rate_limit_waits_are_capped_and_backed_off_without_retry_after() -> None:
    respx.post(COMPLETIONS).mock(
        side_effect=[
            httpx.Response(429, headers={"retry-after": "120"}),
            httpx.Response(429),
            completion("{}"),
        ]
    )
    sleep = SleepRecorder()
    settings = hosted_settings(llm_max_retry_wait_seconds=10.0)
    async with httpx.AsyncClient() as client:
        await OpenAICompatibleProvider(client, settings, sleep=sleep).complete("s", "u")

    assert sleep.waits == [10.0, 4.0]


@respx.mock
async def test_gives_up_after_the_rate_limit_retries() -> None:
    route = respx.post(COMPLETIONS).mock(return_value=httpx.Response(429, text="slow down"))
    sleep = SleepRecorder()
    async with httpx.AsyncClient() as client:
        provider = OpenAICompatibleProvider(client, hosted_settings(llm_rate_limit_retries=2), sleep=sleep)
        with pytest.raises(LlmError, match="429"):
            await provider.complete("s", "u")

    assert route.call_count == 3
    assert len(sleep.waits) == 2


@respx.mock
async def test_a_server_error_is_an_llm_error_without_retry() -> None:
    route = respx.post(COMPLETIONS).mock(return_value=httpx.Response(503, text="overloaded"))
    async with httpx.AsyncClient() as client:
        with pytest.raises(LlmError, match="503"):
            await OpenAICompatibleProvider(client, hosted_settings()).complete("s", "u")

    assert route.call_count == 1


@respx.mock
async def test_an_unreachable_provider_is_an_llm_error() -> None:
    respx.post(COMPLETIONS).mock(side_effect=httpx.ConnectError("no route"))
    async with httpx.AsyncClient() as client:
        with pytest.raises(LlmError, match="unreachable"):
            await OpenAICompatibleProvider(client, hosted_settings()).complete("s", "u")


@respx.mock
async def test_a_body_that_is_not_a_chat_completion_is_an_llm_error() -> None:
    respx.post(COMPLETIONS).mock(return_value=httpx.Response(200, json={"error": "odd"}))
    async with httpx.AsyncClient() as client:
        with pytest.raises(LlmError, match="unexpected response"):
            await OpenAICompatibleProvider(client, hosted_settings()).complete("s", "u")


@respx.mock
async def test_reachability_lists_models_and_spends_no_tokens() -> None:
    models = respx.get(f"{BASE}/models").mock(return_value=httpx.Response(200, json={"data": []}))
    completions = respx.post(COMPLETIONS)
    async with httpx.AsyncClient() as client:
        provider = OpenAICompatibleProvider(client, hosted_settings())
        assert await provider.is_reachable()

        models.mock(return_value=httpx.Response(401))
        assert not await provider.is_reachable()

    assert models.calls.last.request.headers["Authorization"] == "Bearer test-llm-key"
    assert not completions.called


async def test_the_factory_defaults_to_ollama() -> None:
    async with httpx.AsyncClient() as client:
        assert isinstance(create_provider(client, Settings()), OllamaProvider)


async def test_the_factory_builds_the_hosted_provider() -> None:
    async with httpx.AsyncClient() as client:
        assert isinstance(create_provider(client, hosted_settings()), OpenAICompatibleProvider)


@pytest.mark.parametrize(
    ("overrides", "message"),
    [
        ({"llm_api_key": ""}, "AGENT_LLM_API_KEY"),
        ({"llm_base_url": ""}, "AGENT_LLM_BASE_URL"),
        ({"llm_provider": "hosted"}, "Unknown AGENT_LLM_PROVIDER"),
    ],
)
async def test_the_factory_refuses_an_incomplete_configuration(
    overrides: dict[str, object], message: str
) -> None:
    async with httpx.AsyncClient() as client:
        with pytest.raises(ValueError, match=message):
            create_provider(client, hosted_settings(**overrides))


def test_the_hosted_key_never_appears_in_the_settings_repr() -> None:
    assert "test-llm-key" not in repr(hosted_settings())


def test_liveness_answers_without_touching_the_model() -> None:
    from fastapi.testclient import TestClient

    from app.main import app

    with respx.mock(assert_all_called=False) as mock, TestClient(app) as client:
        response = client.get("/health/live")

    assert response.status_code == 200
    assert response.json() == {"status": "healthy"}
    assert not mock.calls
