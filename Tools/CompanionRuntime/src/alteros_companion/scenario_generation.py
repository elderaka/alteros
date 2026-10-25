"""Two bounded, independent provider calls for one v4 Scenario turn."""

from __future__ import annotations

import asyncio
import sqlite3
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Callable

from .contracts import TimingV2, UsageV2
from .memory_context import MemoryContextUnavailable, MemoryTurnContext, build_memory_context
from .memory_policy import validate_evidence, validate_proposal
from .memory_protocol import MemoryTurnDataV3
from .memory_repository import MemoryRepository, MemoryStorageError
from .prompts import PromptSetupError, build_scenario_prompt
from .provider import OpenRouterProvider, ProviderResult
from .scenario_contracts import (
    ScenarioCompleteEnvelopeV4,
    ScenarioCompletePayloadV4,
    ScenarioHeadContentV4,
    ScenarioHeadStagePayloadV4,
    ScenarioMainContentV4,
    ScenarioMainStagePayloadV4,
    ScenarioStageEnvelopeV4,
    ScenarioStageSummaryV4,
    ScenarioTurnPayloadV4,
    ScenarioUsageV4,
    validate_scenario_definition,
)


@dataclass(frozen=True)
class ScenarioStageProviderResult:
    content: Any
    usage: UsageV2
    timing: TimingV2
    error_code: str | None = None


@dataclass(frozen=True)
class ScenarioTurnReceipt:
    head: ScenarioStageSummaryV4
    main: ScenarioStageSummaryV4
    terminalReason: str


def _usage(value: UsageV2 | ScenarioUsageV4) -> ScenarioUsageV4:
    if isinstance(value, ScenarioUsageV4):
        return value
    return ScenarioUsageV4(**value.model_dump(mode="python"))


def _receipt(request_id: str, job: ScenarioTurnPayloadV4, head: ScenarioStageSummaryV4,
             main: ScenarioStageSummaryV4, terminal: str) -> tuple[ScenarioTurnReceipt, dict[str, Any]]:
    payload = ScenarioCompletePayloadV4(turnId=job.turnId, turnVersion=job.turnVersion, head=head, main=main,
                                        terminalReason=terminal)
    envelope = ScenarioCompleteEnvelopeV4(protocolVersion=4, type="scenario_complete", requestId=request_id,
                                          payload=payload)
    return ScenarioTurnReceipt(head=head, main=main, terminalReason=terminal), envelope.model_dump(mode="json")


def scenario_receipt_message(job: ScenarioTurnPayloadV4, receipt: ScenarioTurnReceipt) -> dict[str, Any]:
    return _receipt(job.turnId, job, receipt.head, receipt.main, receipt.terminalReason)[1]


def _summary(stage: str, status: str, usage: UsageV2 | ScenarioUsageV4) -> ScenarioStageSummaryV4:
    return ScenarioStageSummaryV4(stage=stage, status=status, usage=_usage(usage))


def _zero_usage() -> ScenarioUsageV4:
    return ScenarioUsageV4(inputTokens=0, outputTokens=0, totalTokens=0, costUsd=None)


def _as_stage_result(value: Any) -> ScenarioStageProviderResult:
    if isinstance(value, ScenarioStageProviderResult):
        return value
    if isinstance(value, ProviderResult):
        return ScenarioStageProviderResult(content=value.scenarioContent, usage=value.usage, timing=value.timing,
                                           error_code=value.error.code if value.error else None)
    raise ValueError("invalid provider result")


def _payload(turn_id: str, version: int, stage: str, content: Any,
             usage: UsageV2 | ScenarioUsageV4) -> ScenarioStageEnvelopeV4:
    stage_id = f"{turn_id[:112]}-{stage}"
    if stage == "head":
        head = content if isinstance(content, ScenarioHeadContentV4) else ScenarioHeadContentV4.model_validate(content, strict=True)
        body = ScenarioHeadStagePayloadV4(turnId=turn_id, turnVersion=version, stageId=stage_id, stage="head",
                                           status="ready", usage=_usage(usage), beats=head.beats)
    else:
        main = content if isinstance(content, ScenarioMainContentV4) else ScenarioMainContentV4.model_validate(content, strict=True)
        body = ScenarioMainStagePayloadV4(turnId=turn_id, turnVersion=version, stageId=stage_id, stage="main",
                                          status="ready", usage=_usage(usage), scenario=main.scenario,
                                          segmentStepId=main.segmentStepId, topicOffers=main.topicOffers,
                                          memoryProposals=main.memoryProposals, questionProposal=main.questionProposal,
                                          usedCallbackIds=main.usedCallbackIds)
    return ScenarioStageEnvelopeV4(protocolVersion=4, type="scenario_stage", requestId=turn_id, payload=body)


async def _prepare_memory(job: ScenarioTurnPayloadV4, repository: MemoryRepository | None) -> tuple[Any, MemoryTurnContext | None]:
    policy = job.memory
    if policy is None:
        return job, None
    if repository is None:
        if policy.memoryPolicyRequired:
            raise MemoryContextUnavailable("memory_unavailable")
        turn = MemoryTurnDataV3(conversation=job.conversation, memory=policy)
        context = build_memory_context(None, turn, policy)
    else:
        consent = repository.get_consent()
        if consent.playerId != policy.playerId:
            raise MemoryContextUnavailable("wrong_player")
        if consent.enabled and not repository.has_source(policy.currentPlayerSourceId):
            raise MemoryContextUnavailable("current_source_missing")
        turn = MemoryTurnDataV3(conversation=job.conversation, memory=policy)
        context = build_memory_context(repository, turn, policy)
    conversation = job.conversation.model_copy(update={"presentedHistory": list(context.presentedHistory)})
    return job.model_copy(update={"conversation": conversation}), context


def _memory_still_current(job: ScenarioTurnPayloadV4, repository: MemoryRepository | None,
                          context: MemoryTurnContext | None) -> bool:
    policy = job.memory
    if policy is None:
        return True
    if repository is None:
        return not policy.memoryPolicyRequired
    try:
        consent = repository.get_consent()
        if consent.revision != policy.consentRevision:
            return False
        fresh = repository.read_context(job.conversation.request.text, consent, datetime.now(timezone.utc))
        return context is not None and fresh.suppressedSourceIds == context.context.suppressedSourceIds
    except (MemoryStorageError, OSError, ValueError, sqlite3.Error):
        return False


def _validate_main_memory(job: ScenarioTurnPayloadV4, content: ScenarioMainContentV4,
                          repository: MemoryRepository | None, memory_context: MemoryTurnContext | None) -> bool:
    if not content.memoryProposals and content.questionProposal is None and not content.usedCallbackIds:
        return True
    if job.memory is None or repository is None or memory_context is None:
        return False
    if content.questionProposal and content.questionProposal != memory_context.authorizedQuestionIntent:
        return False
    if not set(content.usedCallbackIds).issubset(memory_context.allowedCallbackIds):
        return False
    eligible = set(memory_context.eligibleProposalSourceIds)
    staged: list[tuple[Any, Any]] = []
    for proposal in content.memoryProposals:
        if proposal.sourceId not in eligible or validate_proposal(proposal):
            return False
        evidence = repository.get_source_evidence(proposal.sourceId)
        if evidence is None or validate_evidence(evidence) or proposal.evidenceQuote not in evidence.text:
            return False
        staged.append((proposal, evidence))
    if not staged:
        return True
    results = repository.upsert_batch(staged, repository.get_consent(), datetime.now(timezone.utc))
    return len(results) == len(staged) and all(
        result.status in {"accepted", "duplicate", "pending_confirmation"} for result in results
    )


async def generate_scenario_turn(job: ScenarioTurnPayloadV4, credential: str,
                                 emit_stage: Callable[[ScenarioStageEnvelopeV4], None],
                                 provider: Any | None = None,
                                 memory_repository: MemoryRepository | None = None,
                                 persona_path: str | None = None) -> ScenarioTurnReceipt:
    """Run head and main concurrently; never retry or repair provider output."""
    provider = provider or OpenRouterProvider()
    empty = _zero_usage()
    if not isinstance(job, ScenarioTurnPayloadV4):
        raise ValueError("invalid Scenario turn")
    if not job.onlineEnabled:
        head, main = _summary("head", "skipped", empty), _summary("main", "skipped", empty)
        return _receipt(job.turnId, job, head, main, "failed")[0]
    if not credential or not credential.strip():
        head, main = _summary("head", "failed", empty), _summary("main", "failed", empty)
        return _receipt(job.turnId, job, head, main, "failed")[0]

    try:
        request_job, memory_context = await _prepare_memory(job, memory_repository)
        # Validate persona before starting either provider request.
        if persona_path is not None:
            from .prompts import load_persona
            load_persona(persona_path)
    except (MemoryContextUnavailable, MemoryStorageError, OSError, ValueError, sqlite3.Error, PromptSetupError):
        head, main = _summary("head", "failed", empty), _summary("main", "failed", empty)
        return _receipt(job.turnId, job, head, main, "failed")[0]

    async def run_stage(stage: str) -> tuple[ScenarioStageEnvelopeV4 | None, ScenarioStageSummaryV4]:
        result: ScenarioStageProviderResult | None = None
        try:
            messages = build_scenario_prompt(request_job, stage, persona_path, memory_context)
            raw = await provider.generate_scenario_stage(messages, credential, stage)
            result = _as_stage_result(raw)
            if result.error_code or result.content is None:
                return None, _summary(stage, "failed", result.usage)
            if not _memory_still_current(job, memory_repository, memory_context):
                return None, _summary(stage, "failed", result.usage)
            if stage == "head":
                envelope = _payload(job.turnId, job.turnVersion, stage, result.content, result.usage)
            else:
                content = result.content if isinstance(result.content, ScenarioMainContentV4) else ScenarioMainContentV4.model_validate(result.content, strict=True)
                validate_scenario_definition(content.scenario, job.runtime)
                if content.scenario.scenarioId:
                    raise ValueError("scenario identity must be host-assigned")
                if any((offer.topicId and offer.topicId not in job.runtime.knownTopicIds)
                       or (offer.intentId and offer.intentId not in job.runtime.knownIntentIds)
                       for offer in content.topicOffers):
                    raise ValueError("Topic offer is outside supplied host catalogue")
                if not _validate_main_memory(job, content, memory_repository, memory_context):
                    raise ValueError("unauthorized memory metadata")
                envelope = _payload(job.turnId, job.turnVersion, stage, content, result.usage)
            return envelope, _summary(stage, "ready", result.usage)
        except asyncio.CancelledError:
            raise
        except Exception:
            return None, _summary(stage, "failed", result.usage if result is not None else empty)

    tasks = {stage: asyncio.create_task(run_stage(stage)) for stage in ("head", "main")}
    summaries: dict[str, ScenarioStageSummaryV4] = {}
    pending = set(tasks.values())
    cancelled = False
    try:
        while pending:
            done, pending = await asyncio.wait(pending, return_when=asyncio.FIRST_COMPLETED)
            for task in done:
                stage = next(name for name, child in tasks.items() if child is task)
                envelope, summary = task.result()
                summaries[stage] = summary
                if envelope is not None:
                    emit_stage(envelope)
    except asyncio.CancelledError:
        cancelled = True
    finally:
        for task in pending:
            task.cancel()
        results = await asyncio.gather(*tasks.values(), return_exceptions=True)
        for stage, value in zip(tasks, results):
            if stage not in summaries:
                if isinstance(value, tuple):
                    summaries[stage] = value[1]
                else:
                    summaries[stage] = _summary(stage, "cancelled", empty)

    head, main = summaries["head"], summaries["main"]
    if cancelled:
        terminal = "cancelled"
    elif main.status == "ready":
        terminal = "completed"
    else:
        terminal = "failed"
    receipt, _ = _receipt(job.turnId, job, head, main, terminal)
    return receipt
