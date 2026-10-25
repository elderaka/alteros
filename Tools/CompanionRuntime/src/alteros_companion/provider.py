"""One bounded, non-streaming OpenRouter chat-completion request."""

from __future__ import annotations

import json
import math
import time
from dataclasses import dataclass
from typing import Any

import httpx
from pydantic import ValidationError

from .contracts import (
    ConversationJudgmentV2,
    ErrorPayloadV2,
    MemoryTurnResponseV3,
    TimingV2,
    UsageV2,
)
from .scenario_contracts import ScenarioHeadContentV4, ScenarioMainContentV4


MODEL_ID = "deepseek/deepseek-v4.1-flash"
CHAT_COMPLETIONS_URL = "https://openrouter.ai/api/v1/chat/completions"
MAX_COMPLETION_TOKENS = 4096
MAX_PROVIDER_RESPONSE_BYTES = 128 * 1024
PROVIDER_TIMEOUT_SECONDS = 45.0


@dataclass(frozen=True)
class ProviderResult:
    judgment: ConversationJudgmentV2 | None
    usage: UsageV2
    timing: TimingV2
    error: ErrorPayloadV2 | None = None
    memoryProposals: tuple[Any, ...] = ()
    questionProposal: str | None = None
    usedCallbackIds: tuple[str, ...] = ()
    scenarioContent: ScenarioHeadContentV4 | ScenarioMainContentV4 | None = None


class OpenRouterProvider:
    def __init__(self, transport: httpx.AsyncBaseTransport | None = None) -> None:
        self._transport = transport

    async def generate(self, messages: list[dict[str, str]], credential: str) -> ProviderResult:
        return await self._generate(messages, credential, memory_turn=False)

    async def generate_memory(self, messages: list[dict[str, str]], credential: str) -> ProviderResult:
        return await self._generate(messages, credential, memory_turn=True)

    async def generate_scenario_stage(self, messages: list[dict[str, str]], credential: str, stage: str) -> ProviderResult:
        if stage not in {"head", "main"}:
            return self._failure("invalid_job", False, time.monotonic())
        return await self._generate(messages, credential, scenario_stage=stage)

    async def _generate(self, messages: list[dict[str, str]], credential: str, *, memory_turn: bool = False,
                        scenario_stage: str | None = None) -> ProviderResult:
        started = time.monotonic()
        if not credential or not credential.strip():
            return self._failure("provider_auth", False, started)
        if not isinstance(messages, list) or not messages or any(
            not isinstance(item, dict) or set(item) != {"role", "content"}
            or item["role"] not in {"system", "user"} or not isinstance(item["content"], str)
            for item in messages
        ):
            return self._failure("invalid_job", False, started)

        request_body = {
            "model": MODEL_ID,
            "messages": messages,
            "max_completion_tokens": MAX_COMPLETION_TOKENS,
            "stream": False,
            "provider": {"only": ["digitalocean"], "allow_fallbacks": False, "require_parameters": True},
            "reasoning": {"enabled": False},
            "response_format": {"type": "json_object"},
        }
        transport = self._transport or httpx.AsyncHTTPTransport(retries=0)
        try:
            async with httpx.AsyncClient(transport=transport, timeout=PROVIDER_TIMEOUT_SECONDS, trust_env=False) as client:
                async with client.stream(
                    "POST",
                    CHAT_COMPLETIONS_URL,
                    headers={"Authorization": "Bearer " + credential, "Content-Type": "application/json"},
                    json=request_body,
                ) as response:
                    provider_ms = _elapsed_ms(started)
                    if response.status_code in (401, 403):
                        return self._failure("provider_auth", False, started, provider_ms)
                    if response.status_code == 402:
                        return self._failure("provider_payment_required", False, started, provider_ms)
                    if response.status_code == 408:
                        return self._failure("provider_timeout", True, started, provider_ms)
                    if response.status_code == 429:
                        return self._failure("provider_rate_limited", True, started, provider_ms)
                    if response.status_code == 400:
                        return self._failure("schema_unsupported", False, started, provider_ms)
                    if response.status_code >= 500:
                        return self._failure("provider_unavailable", True, started, provider_ms)
                    if response.status_code >= 400:
                        return self._failure("provider_unavailable", False, started, provider_ms)
                    raw = await _read_response_bounded(response)
        except httpx.TimeoutException:
            return self._failure("provider_timeout", True, started)
        except httpx.HTTPError:
            return self._failure("provider_unavailable", True, started)
        except _ResponseTooLarge:
            return self._failure("invalid_reply", False, started, diagnostic_code="output_format")
        except Exception:
            return self._failure("internal_error", False, started)

        provider_ms = _elapsed_ms(started)
        try:
            payload = json.loads(raw)
        except (UnicodeDecodeError, json.JSONDecodeError):
            return self._failure("invalid_reply", False, started, provider_ms, "response_json")
        usage: UsageV2 | None = None
        if isinstance(payload, dict):
            try:
                usage = _parse_usage(payload.get("usage"))
            except (TypeError, ValueError, ValidationError):
                pass
        if not isinstance(payload, dict) or not isinstance(payload.get("choices"), list) or not payload["choices"]:
            return self._failure("invalid_reply", False, started, provider_ms, "response_envelope", usage)
        choice = payload["choices"][0]
        if not isinstance(choice, dict) or not isinstance(choice.get("message"), dict):
            return self._failure("invalid_reply", False, started, provider_ms, "response_envelope", usage)
        message = choice["message"]
        finish_reason = choice.get("finish_reason")
        if message.get("refusal") or finish_reason == "content_filter":
            return self._failure("provider_refusal", False, started, provider_ms, usage=usage)
        if message.get("tool_calls") or finish_reason != "stop" or not isinstance(message.get("content"), str):
            return self._failure("invalid_reply", False, started, provider_ms, "provider_termination", usage)
        try:
            content = json.loads(message["content"])
        except (UnicodeDecodeError, json.JSONDecodeError):
            return self._failure("invalid_reply", False, started, provider_ms, "response_json", usage)
        try:
            if memory_turn:
                parsed = MemoryTurnResponseV3.model_validate(content, strict=True)
                judgment = parsed.judgment
                proposals = tuple(parsed.memoryProposals)
                question = parsed.questionProposal
                callbacks = tuple(parsed.usedCallbackIds)
                scenario_content = None
            elif scenario_stage == "head":
                scenario_content = ScenarioHeadContentV4.model_validate(content, strict=True)
                judgment, proposals, question, callbacks = None, (), None, ()
            elif scenario_stage == "main":
                scenario_content = ScenarioMainContentV4.model_validate(content, strict=True)
                judgment, proposals, question, callbacks = None, (), None, ()
            else:
                judgment = ConversationJudgmentV2.model_validate(content, strict=True)
                proposals = ()
                question = None
                callbacks = ()
                scenario_content = None
        except ValidationError:
            return self._failure("invalid_reply", False, started, provider_ms, "judgment_schema", usage)
        if usage is None:
            return self._failure("invalid_reply", False, started, provider_ms, "usage_metadata")
        total_ms = _elapsed_ms(started)
        return ProviderResult(judgment=judgment, usage=usage, timing=TimingV2(providerMs=provider_ms, totalMs=total_ms),
                              memoryProposals=proposals, questionProposal=question, usedCallbackIds=callbacks,
                              scenarioContent=scenario_content)

    @staticmethod
    def _failure(code: str, retryable: bool, started: float, provider_ms: int | None = None,
                 diagnostic_code: str | None = None, usage: UsageV2 | None = None) -> ProviderResult:
        elapsed = _elapsed_ms(started)
        return ProviderResult(
            judgment=None,
            usage=usage or UsageV2(inputTokens=0, outputTokens=0, totalTokens=0, costUsd=None),
            timing=TimingV2(providerMs=elapsed if provider_ms is None else provider_ms, totalMs=elapsed),
            error=ErrorPayloadV2(code=code, retryable=retryable, diagnosticCode=diagnostic_code),
        )


class _ResponseTooLarge(Exception):
    pass


async def _read_response_bounded(response: httpx.Response) -> bytes:
    chunks: list[bytes] = []
    total = 0
    async for chunk in response.aiter_bytes():
        total += len(chunk)
        if total > MAX_PROVIDER_RESPONSE_BYTES:
            raise _ResponseTooLarge()
        chunks.append(chunk)
    return b"".join(chunks)


def _parse_usage(value: Any) -> UsageV2:
    if not isinstance(value, dict):
        raise ValueError("usage unavailable")
    input_tokens = value.get("prompt_tokens")
    output_tokens = value.get("completion_tokens")
    total_tokens = value.get("total_tokens")
    if any(type(item) is not int or item < 0 for item in (input_tokens, output_tokens, total_tokens)):
        raise ValueError("usage unavailable")
    raw_cost = value.get("cost")
    cost: float | None = None
    if type(raw_cost) in (int, float) and math.isfinite(raw_cost) and raw_cost >= 0:
        cost = float(raw_cost)
    return UsageV2(inputTokens=input_tokens, outputTokens=output_tokens, totalTokens=total_tokens, costUsd=cost)


def _elapsed_ms(started: float) -> int:
    return max(0, int((time.monotonic() - started) * 1000))
