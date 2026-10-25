from __future__ import annotations

import asyncio
import json
from pathlib import Path

import httpx
import pytest

from alteros_companion.provider import OpenRouterProvider


REPO_ROOT = Path(__file__).resolve().parents[3]
VALID_JUDGMENT = json.loads(
    (REPO_ROOT / "Tools" / "Conversation" / "Fixtures" / "v2" / "result-valid.json").read_text(encoding="utf-8")
)["payload"]["judgment"]
MESSAGES = [
    {"role": "system", "content": "System policy."},
    {"role": "user", "content": "Turn data."},
]


def completion(content: str, *, finish_reason: str = "stop", message_fields: dict | None = None) -> dict:
    message = {"role": "assistant", "content": content}
    if message_fields:
        message.update(message_fields)
    return {
        "id": "chatcmpl-test",
        "choices": [{"index": 0, "message": message, "finish_reason": finish_reason}],
        "usage": {"prompt_tokens": 91, "completion_tokens": 33, "total_tokens": 124, "cost": 0.0012},
    }


def run_provider(handler, *, body: bytes | None = None):
    transport = httpx.MockTransport(handler)
    return asyncio.run(OpenRouterProvider(transport=transport).generate(MESSAGES, "synthetic-api-key-marker"))


def test_openrouter_sends_one_pinned_structured_request_without_key_in_body() -> None:
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json=completion(json.dumps(VALID_JUDGMENT, ensure_ascii=False)))

    result = run_provider(handler)

    assert len(seen) == 1
    request = seen[0]
    body = json.loads(request.content)
    assert request.url == "https://openrouter.ai/api/v1/chat/completions"
    assert request.headers["authorization"] == "Bearer synthetic-api-key-marker"
    assert body["model"] == "deepseek/deepseek-v4.1-flash"
    assert body["max_completion_tokens"] == 4096
    assert body["stream"] is False
    assert body["provider"]["require_parameters"] is True
    assert body["response_format"] == {"type": "json_object"}
    assert request.extensions["timeout"]["read"] == 45.0
    assert "tools" not in body
    assert "synthetic-api-key-marker" not in request.content.decode("utf-8")
    assert result.error is None
    assert result.judgment.kind == "existing_uncovered"
    assert result.usage.inputTokens == 91
    assert result.usage.outputTokens == 33
    assert result.usage.costUsd == pytest.approx(0.0012)


def test_memory_turn_uses_one_bounded_structured_response_wrapper():
    seen: list[httpx.Request] = []
    memory_response = {"judgment": VALID_JUDGMENT, "memoryProposals": [],
                       "questionProposal": None, "usedCallbackIds": []}

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json=completion(json.dumps(memory_response, ensure_ascii=False)))

    result = asyncio.run(OpenRouterProvider(transport=httpx.MockTransport(handler)).generate_memory(
        MESSAGES, "synthetic-api-key-marker"))
    body = json.loads(seen[0].content)

    assert len(seen) == 1
    assert body["response_format"] == {"type": "json_object"}
    assert result.error is None
    assert result.judgment.kind == "existing_uncovered"
    assert result.memoryProposals == ()
    assert result.questionProposal is None
    assert result.usedCallbackIds == ()


def test_scenario_stage_provider_validates_typed_content_and_keeps_usage_on_rejection() -> None:
    root = Path(__file__).resolve().parents[3]
    fixture_root = root / "Tools" / "ScenarioRuntime" / "Fixtures" / "v4"
    head = json.loads((fixture_root / "head-valid.json").read_text(encoding="utf-8"))["payload"]
    main = json.loads((fixture_root / "main-cats.json").read_text(encoding="utf-8"))["payload"]
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        stage = "head" if len(seen) == 1 else "main"
        content = {"beats": head["beats"]} if stage == "head" else {
            "scenario": main["scenario"], "segmentStepId": main["segmentStepId"], "topicOffers": [],
            "memoryProposals": [], "questionProposal": None, "usedCallbackIds": [],
        }
        return httpx.Response(200, json=completion(json.dumps(content, ensure_ascii=False)))

    provider = OpenRouterProvider(transport=httpx.MockTransport(handler))
    head_result = asyncio.run(provider.generate_scenario_stage(MESSAGES, "synthetic-api-key-marker", "head"))
    main_result = asyncio.run(provider.generate_scenario_stage(MESSAGES, "synthetic-api-key-marker", "main"))
    assert head_result.error is None and len(head_result.scenarioContent.beats) == 1
    assert main_result.error is None and main_result.scenarioContent.scenario.topicId == "cats"
    assert len(seen) == 2
    assert all("synthetic-api-key-marker" not in request.content.decode("utf-8") for request in seen)

    invalid = OpenRouterProvider(transport=httpx.MockTransport(
        lambda _: httpx.Response(200, json=completion('{"beats":[]}'))))
    rejected = asyncio.run(invalid.generate_scenario_stage(MESSAGES, "synthetic-api-key-marker", "head"))
    assert rejected.error.code == "invalid_reply"
    assert rejected.usage.totalTokens == 124
    assert rejected.usage.costUsd == pytest.approx(0.0012)


@pytest.mark.parametrize("memory_turn", [False, True])
def test_conversation_requests_pin_digitalocean_and_disable_reasoning(memory_turn: bool) -> None:
    seen: list[httpx.Request] = []
    reply = {"judgment": VALID_JUDGMENT, "memoryProposals": [],
             "questionProposal": None, "usedCallbackIds": []} if memory_turn else VALID_JUDGMENT

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json=completion(json.dumps(reply)))

    provider = OpenRouterProvider(transport=httpx.MockTransport(handler))
    generate = provider.generate_memory if memory_turn else provider.generate
    result = asyncio.run(generate(MESSAGES, "synthetic-api-key-marker"))

    assert result.error is None
    assert len(seen) == 1
    body = json.loads(seen[0].content)
    assert body["provider"] == {"only": ["digitalocean"], "allow_fallbacks": False,
                                "require_parameters": True}
    assert body["reasoning"] == {"enabled": False}
    assert body["response_format"] == {"type": "json_object"}


@pytest.mark.parametrize(
    ("status", "code", "retryable"),
    [
        (401, "provider_auth", False),
        (402, "provider_payment_required", False),
        (429, "provider_rate_limited", True),
        (500, "provider_unavailable", True),
        (400, "schema_unsupported", False),
    ],
)
def test_provider_http_failures_are_mapped_without_retries_or_raw_body(status: int, code: str, retryable: bool) -> None:
    calls = 0

    def handler(request: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        return httpx.Response(status, text="upstream synthetic-api-key-marker details")

    result = run_provider(handler)

    assert calls == 1
    assert result.judgment is None
    assert result.error.code == code
    assert result.error.retryable is retryable
    assert "synthetic-api-key-marker" not in str(result.error)


def test_provider_timeout_after_send_is_not_retried() -> None:
    calls = 0

    def handler(request: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        raise httpx.ReadTimeout("synthetic-api-key-marker timeout", request=request)

    result = run_provider(handler)

    assert calls == 1
    assert result.error.code == "provider_timeout"
    assert "synthetic-api-key-marker" not in str(result.error)


@pytest.mark.parametrize(
    ("response", "code", "diagnostic_code"),
    [
        (httpx.Response(200, content=b"not-json synthetic-api-key-marker"), "invalid_reply", "response_json"),
        (httpx.Response(200, json=completion("{}", message_fields={"refusal": "synthetic-api-key-marker"})), "provider_refusal", None),
        (httpx.Response(200, json=completion(json.dumps(VALID_JUDGMENT), finish_reason="length")), "invalid_reply", "provider_termination"),
        (httpx.Response(200, json=completion("{}", message_fields={"tool_calls": [{"id": "x"}]})), "invalid_reply", "provider_termination"),
    ],
)
def test_provider_rejects_invalid_refused_truncated_and_tool_call_outputs(
    response: httpx.Response, code: str, diagnostic_code: str | None
) -> None:
    result = run_provider(lambda request: response)

    assert result.judgment is None
    assert result.error.code == code
    assert result.error.diagnosticCode == diagnostic_code
    assert "synthetic-api-key-marker" not in str(result.error)


@pytest.mark.parametrize(
    ("response", "expected_diagnostic"),
    [
        (httpx.Response(200, content=b"not-json"), "response_json"),
        (httpx.Response(200, json={"choices": []}), "response_envelope"),
        (httpx.Response(200, json=completion(json.dumps({"kind": "not-a-kind"}))), "judgment_schema"),
        (httpx.Response(200, json=completion(json.dumps(VALID_JUDGMENT), finish_reason="length")), "provider_termination"),
        (httpx.Response(200, json={**completion(json.dumps(VALID_JUDGMENT)), "usage": {}}), "usage_metadata"),
    ],
)
def test_invalid_reply_diagnostics_identify_safe_failure_stage(
    response: httpx.Response, expected_diagnostic: str
) -> None:
    result = run_provider(lambda request: response)

    assert result.error.code == "invalid_reply"
    assert result.error.diagnosticCode == expected_diagnostic
    assert "synthetic-api-key-marker" not in str(result.error)


def test_result_error_serializes_only_the_safe_diagnostic_category() -> None:
    from alteros_companion.__main__ import _result_message
    from alteros_companion.contracts import ErrorPayloadV2, TimingV2, UsageV2
    from alteros_companion.provider import ProviderResult

    result = ProviderResult(judgment=None, usage=UsageV2(inputTokens=0, outputTokens=0,
        totalTokens=0, costUsd=None), timing=TimingV2(providerMs=1, totalMs=1),
        error=ErrorPayloadV2(code="invalid_reply", retryable=False, diagnosticCode="output_format"))

    message = _result_message("request-1", result)

    assert message["payload"] == {
        "code": "invalid_reply", "retryable": False, "diagnosticCode": "output_format"
    }

    with pytest.raises(ValueError):
        ErrorPayloadV2(code="provider_auth", retryable=False, diagnosticCode="output_format")
