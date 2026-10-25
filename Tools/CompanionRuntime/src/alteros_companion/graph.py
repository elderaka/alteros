"""One-shot LangGraph pipeline for a single bounded conversation turn."""

from __future__ import annotations

import time
from dataclasses import dataclass, replace
from datetime import datetime, timezone
import sqlite3
from typing import Any, TypedDict

import httpx
from langgraph.graph import END, StateGraph
from pydantic import ValidationError

from .contracts import (
    ConversationJobV2,
    ErrorPayloadV2,
    TimingV2,
    UsageV2,
    validate_judgment,
)
from .prompts import PromptSetupError, build_prompt
from .provider import OpenRouterProvider, ProviderResult
from .memory_context import MemoryContextUnavailable, MemoryTurnContext, build_memory_context
from .memory_protocol import MemoryTurnDataV3
from .memory_repository import MemoryRepository, MemoryStorageError


class ConversationState(TypedDict, total=False):
    job: Any
    messages: list[dict[str, str]]
    provider_result: ProviderResult
    final_result: ProviderResult
    started_at: float


@dataclass(frozen=True)
class MemoryTurnExecution:
    result: ProviderResult | None
    errorCode: str = ""


def _failure(code: str, retryable: bool = False) -> ProviderResult:
    return ProviderResult(
        judgment=None,
        usage=UsageV2(inputTokens=0, outputTokens=0, totalTokens=0, costUsd=None),
        timing=TimingV2(providerMs=0, totalMs=0),
        error=ErrorPayloadV2(code=code, retryable=retryable),
    )


def _build_graph(provider: Any, credential: str, persona_path: str | None, memory_context: Any | None = None,
                 memory_policy: Any | None = None):
    async def validate_job(state: ConversationState) -> dict[str, Any]:
        try:
            raw_job = state["job"]
            if isinstance(raw_job, ConversationJobV2):
                raw_job = raw_job.model_dump(mode="python")
            validated = ConversationJobV2.model_validate(raw_job, strict=True)
            return {"job": validated}
        except (ValidationError, TypeError, ValueError, KeyError):
            return {"final_result": _failure("invalid_job")}

    def job_is_valid(state: ConversationState) -> str:
        return "build_prompt" if "final_result" not in state else "finish"

    async def build_prompt_node(state: ConversationState) -> dict[str, Any]:
        try:
            return {"messages": build_prompt(state["job"], persona_path=persona_path, memory_context=memory_context)}
        except PromptSetupError:
            return {"final_result": _failure("persona_unavailable")}

    def prompt_is_ready(state: ConversationState) -> str:
        return "call_provider" if "messages" in state else "finish"

    async def call_provider(state: ConversationState) -> dict[str, Any]:
        try:
            memory_generate = getattr(provider, "generate_memory", None) if memory_context is not None else None
            result = await (memory_generate(state["messages"], credential) if memory_generate is not None
                            else provider.generate(state["messages"], credential))
            return {"provider_result": result}
        except httpx.TimeoutException:
            return {"provider_result": _failure("provider_timeout", True)}
        except Exception:
            return {"provider_result": _failure("internal_error")}

    async def validate_result(state: ConversationState) -> dict[str, Any]:
        result = state.get("provider_result")
        if result is None:
            return {"final_result": _failure("internal_error")}
        if result.error is not None:
            return {"final_result": result}
        job = state["job"]
        errors = validate_judgment(job, result.judgment, frozenset(job.allowedExpressions))
        if memory_context is not None and not errors:
            eligible_sources = set(memory_context.eligibleProposalSourceIds)
            if any(proposal.sourceId not in eligible_sources or proposal.evidenceQuote not in job.request.text
                   for proposal in result.memoryProposals):
                # Bad memory metadata is discarded; it must not turn an otherwise
                # valid conversational response into a provider failure.
                result = ProviderResult(judgment=result.judgment, usage=result.usage, timing=result.timing,
                    error=result.error, questionProposal=result.questionProposal, usedCallbackIds=result.usedCallbackIds)
            if result.questionProposal is not None and (
                memory_policy is None or result.questionProposal != memory_policy.authorizedQuestionIntent
            ):
                errors.append("unauthorized_personal_question")
            if any(callback_id not in set(memory_context.allowedCallbackIds) for callback_id in result.usedCallbackIds):
                errors.append("unauthorized_memory_callback")
        if errors:
            return {"final_result": ProviderResult(
                judgment=None,
                usage=result.usage,
                timing=result.timing,
                error=ErrorPayloadV2(code="invalid_reply", retryable=False, diagnosticCode="judgment_validation"),
            )}
        return {"final_result": result}

    workflow = StateGraph(ConversationState)
    workflow.add_node("validate_job", validate_job)
    workflow.add_node("build_prompt", build_prompt_node)
    workflow.add_node("call_provider", call_provider)
    workflow.add_node("validate_result", validate_result)
    workflow.add_node("finish", lambda state: {})
    workflow.set_entry_point("validate_job")
    workflow.add_conditional_edges("validate_job", job_is_valid, {"build_prompt": "build_prompt", "finish": "finish"})
    workflow.add_conditional_edges("build_prompt", prompt_is_ready, {"call_provider": "call_provider", "finish": "finish"})
    workflow.add_edge("call_provider", "validate_result")
    workflow.add_edge("validate_result", "finish")
    workflow.add_edge("finish", END)
    return workflow.compile()


async def generate(job: ConversationJobV2, credential: str, provider: Any | None = None,
                   persona_path: str | None = None, memory_context: Any | None = None,
                   memory_policy: Any | None = None) -> ProviderResult:
    started = time.monotonic()
    graph = _build_graph(provider or OpenRouterProvider(), credential, persona_path, memory_context, memory_policy)
    state = await graph.ainvoke({"job": job, "started_at": started})
    result = state.get("final_result")
    if result is None:
        return _failure("internal_error")
    total_ms = max(result.timing.totalMs, int((time.monotonic() - started) * 1000))
    return replace(result, timing=TimingV2(providerMs=result.timing.providerMs, totalMs=total_ms))


async def generate_memory_turn(turn: MemoryTurnDataV3, repository: MemoryRepository, credential: str,
                              provider: Any | None = None, persona_path: str | None = None) -> MemoryTurnExecution:
    """Run one v3 turn with host-read local context and post-call policy revalidation."""
    try:
        policy = turn.memory
        consent = repository.get_consent()
        if consent.enabled and not repository.has_source(policy.currentPlayerSourceId):
            raise MemoryContextUnavailable("current_source_missing")
        memory_context = build_memory_context(repository, turn, policy)
        conversation = turn.conversation.model_copy(update={"presentedHistory": list(memory_context.presentedHistory)})
        result = await generate(conversation, credential, provider=provider, persona_path=persona_path,
                                memory_context=memory_context, memory_policy=policy)
        current = repository.get_consent()
        if current.revision != policy.consentRevision:
            raise MemoryContextUnavailable("consent_revision_conflict")
        fresh = repository.read_context(conversation.request.text, current, datetime.now(timezone.utc))
        if fresh.suppressedSourceIds != memory_context.context.suppressedSourceIds:
            raise MemoryContextUnavailable("suppression_changed")
        if result.error is None:
            for proposal in result.memoryProposals:
                evidence = repository.get_source_evidence(proposal.sourceId)
                if evidence is None or proposal.evidenceQuote not in evidence.text:
                    return MemoryTurnExecution(ProviderResult(judgment=None, usage=result.usage, timing=result.timing,
                        error=ErrorPayloadV2(code="invalid_reply", retryable=False)))
                stored = repository.upsert(proposal, evidence, current, datetime.now(timezone.utc))
                if stored.status not in {"accepted", "duplicate", "pending_confirmation"}:
                    return MemoryTurnExecution(ProviderResult(judgment=None, usage=result.usage, timing=result.timing,
                        error=ErrorPayloadV2(code="invalid_reply", retryable=False)))
        return MemoryTurnExecution(result)
    except MemoryContextUnavailable:
        return MemoryTurnExecution(None, "memory_unavailable")
    except (MemoryStorageError, OSError, ValueError, sqlite3.Error):
        return MemoryTurnExecution(None, "memory_unavailable")
