"""Strict provider-neutral v2 message contracts for the AlterOS sidecar."""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from decimal import Decimal
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, SecretStr, ValidationError, model_validator

from .memory_contracts import MemoryProposal


MAX_ENVELOPE_BYTES = 256 * 1024
MAX_TURN_BYTES = 64 * 1024
MAX_RESULT_BYTES = 128 * 1024
MAX_HISTORY_UTTERANCES = 12
MAX_HISTORY_CHARACTERS = 6000
MAX_CONTEXT_CHARACTERS = 10000
MAX_CHARACTER_RULE_CHARACTERS = 8000
MAX_REPLY_BEATS = 16
MAX_BEAT_CHARACTERS = 600
MAX_REPLY_CHARACTERS = 12000
MAX_SUGGESTIONS = 3


class ContractError(ValueError):
    """A sanitized protocol failure that never includes rejected input."""


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, frozen=True)


class EnvelopeHeader(StrictModel):
    protocolVersion: Literal[2]
    type: Literal["bootstrap", "ready", "turn", "result", "error", "cancel", "shutdown"]
    requestId: str = Field(min_length=1, max_length=128)


class BootstrapPayloadV2(StrictModel):
    apiKey: SecretStr = Field(repr=False, min_length=1, max_length=4096)


class ReadyPayloadV2(StrictModel):
    runtimeVersion: str = Field(min_length=1, max_length=64)
    modelId: str = Field(min_length=1, max_length=128)


class TurnRequestV2(StrictModel):
    sessionId: str = Field(min_length=1, max_length=128)
    channel: Literal["direct", "phone"]
    text: str = Field(min_length=1, max_length=2048)
    activeResponseId: str = Field(max_length=128)
    activeTopicId: str = Field(max_length=128)

    @model_validator(mode="after")
    def validate_active_response(self) -> "TurnRequestV2":
        if self.activeTopicId and not self.activeResponseId:
            raise ValueError("active topic requires an active response")
        return self


class TopicCandidateCardV2(StrictModel):
    topicId: str = Field(min_length=1, max_length=128)
    prompt: str = Field(max_length=512)
    examples: list[str] = Field(max_length=3)
    similarity: float
    supportCount: int = Field(ge=0)
    authoredAvailability: Literal["ready", "blocked"]

    @model_validator(mode="after")
    def validate_finite_similarity(self) -> "TopicCandidateCardV2":
        if not math.isfinite(self.similarity):
            raise ValueError("invalid candidate score")
        if any(len(example) > 256 or not example.strip() for example in self.examples):
            raise ValueError("invalid candidate example")
        return self


class CandidateSetV2(StrictModel):
    indexIdentity: str = Field(min_length=1, max_length=256)
    source: Literal["local_gte", "synthetic"]
    cards: list[TopicCandidateCardV2] = Field(max_length=8)

    @model_validator(mode="after")
    def validate_unique_topics(self) -> "CandidateSetV2":
        if len({card.topicId for card in self.cards}) != len(self.cards):
            raise ValueError("duplicate candidate Topic")
        return self


class CoverageScenarioV2(StrictModel):
    scenarioId: str = Field(min_length=1, max_length=128)
    coverageSummary: str = Field(min_length=1, max_length=400)
    experienceKey: str = Field(pattern=r"^[a-z0-9_.-]{1,128}$")


class CoverageTopicV2(StrictModel):
    topicId: str = Field(min_length=1, max_length=128)
    allowUncoveredDiscussion: bool
    authoredAvailability: Literal["ready", "blocked"]
    scenarios: list[CoverageScenarioV2] = Field(max_length=32)

    @model_validator(mode="after")
    def validate_scenarios(self) -> "CoverageTopicV2":
        ids = [scenario.scenarioId for scenario in self.scenarios]
        keys = [scenario.experienceKey for scenario in self.scenarios]
        if len(set(ids)) != len(ids) or len(set(keys)) != len(keys):
            raise ValueError("duplicate coverage scenario or experience key")
        if self.allowUncoveredDiscussion and not self.scenarios:
            raise ValueError("uncovered discussion requires coverage summaries")
        return self


class CoverageSnapshotV2(StrictModel):
    schemaVersion: Literal[1]
    topics: list[CoverageTopicV2] = Field(max_length=9)

    @model_validator(mode="after")
    def validate_unique_topics(self) -> "CoverageSnapshotV2":
        if len({topic.topicId for topic in self.topics}) != len(self.topics):
            raise ValueError("duplicate coverage Topic")
        return self


class PresentedUtteranceV2(StrictModel):
    speaker: Literal["player", "alterra"]
    source: Literal["player", "authored", "generated"]
    responseId: str = Field(max_length=128)
    nodeId: str = Field(max_length=64)
    text: str = Field(min_length=1, max_length=600)


class ConversationJobV2(StrictModel):
    request: TurnRequestV2
    candidates: CandidateSetV2
    coverage: CoverageSnapshotV2
    presentedHistory: list[PresentedUtteranceV2] = Field(max_length=MAX_HISTORY_UTTERANCES)
    characterRules: str = Field(min_length=1, max_length=MAX_CHARACTER_RULE_CHARACTERS)
    allowedExpressions: list[str] = Field(max_length=64)

    @model_validator(mode="after")
    def validate_context_budgets(self) -> "ConversationJobV2":
        history_chars = sum(len(item.text) for item in self.presentedHistory)
        if history_chars > MAX_HISTORY_CHARACTERS:
            raise ValueError("history exceeds context limit")
        context_chars = sum(len(card.prompt) + sum(len(example) for example in card.examples) for card in self.candidates.cards)
        context_chars += sum(len(item.coverageSummary) for topic in self.coverage.topics for item in topic.scenarios)
        if context_chars > MAX_CONTEXT_CHARACTERS:
            raise ValueError("candidate and coverage context exceeds limit")
        if len(set(self.allowedExpressions)) != len(self.allowedExpressions):
            raise ValueError("duplicate allowed expression")
        candidate_ids = {card.topicId for card in self.candidates.cards}
        if any(topic.topicId not in candidate_ids for topic in self.coverage.topics):
            raise ValueError("coverage Topic is outside candidate context")
        return self


class ConversationBeatV2(StrictModel):
    id: str = Field(min_length=1, max_length=64)
    text: str = Field(min_length=1, max_length=MAX_BEAT_CHARACTERS)
    displayText: str = Field(min_length=1, max_length=MAX_BEAT_CHARACTERS)
    language: str = Field(min_length=1, max_length=32)
    expression: str = Field(max_length=64)


class ConversationReplyV2(StrictModel):
    beats: list[ConversationBeatV2] = Field(min_length=1, max_length=MAX_REPLY_BEATS)

    @model_validator(mode="after")
    def validate_unique_beat_ids(self) -> "ConversationReplyV2":
        ids = [beat.id for beat in self.beats]
        if len(set(ids)) != len(ids):
            raise ValueError("duplicate reply beat id")
        if any(not beat.text.strip() or not beat.displayText.strip() for beat in self.beats):
            raise ValueError("bilingual beat text is required")
        return self


class SuggestedReplyV2(StrictModel):
    label: str = Field(min_length=1, max_length=80)
    prompt: str = Field(min_length=1, max_length=512)


class ConversationJudgmentV2(StrictModel):
    kind: Literal["existing", "existing_uncovered", "continue", "clarify", "novel_candidate"]
    topicId: str = Field(max_length=128)
    referencedTopicIds: list[str] = Field(max_length=8)
    reply: ConversationReplyV2 | None
    suggestedReplies: list[SuggestedReplyV2] = Field(max_length=MAX_SUGGESTIONS)

    @model_validator(mode="after")
    def validate_references(self) -> "ConversationJudgmentV2":
        if len(set(self.referencedTopicIds)) != len(self.referencedTopicIds):
            raise ValueError("duplicate Topic reference")
        return self


class MemoryTurnResponseV3(StrictModel):
    judgment: ConversationJudgmentV2
    memoryProposals: list[MemoryProposal] = Field(max_length=3)
    questionProposal: Literal["preferred_name", "interest", "shared_activity_preference"] | None
    usedCallbackIds: list[str] = Field(max_length=1)

    @model_validator(mode="after")
    def validate_unique_callback_ids(self) -> "MemoryTurnResponseV3":
        if len(set(self.usedCallbackIds)) != len(self.usedCallbackIds):
            raise ValueError("duplicate callback ID")
        return self


class UsageV2(StrictModel):
    inputTokens: int = Field(ge=0)
    outputTokens: int = Field(ge=0)
    totalTokens: int = Field(ge=0)
    costUsd: float | None

    @model_validator(mode="after")
    def validate_cost(self) -> "UsageV2":
        if self.costUsd is not None and (not math.isfinite(self.costUsd) or self.costUsd < 0):
            raise ValueError("invalid usage cost")
        return self


class TimingV2(StrictModel):
    providerMs: int = Field(ge=0)
    totalMs: int = Field(ge=0)


class ConversationResultV2(StrictModel):
    judgment: ConversationJudgmentV2
    usage: UsageV2
    timing: TimingV2


class ErrorPayloadV2(StrictModel):
    code: Literal[
        "invalid_job",
        "schema_unsupported",
        "provider_auth",
        "provider_payment_required",
        "provider_rate_limited",
        "provider_unavailable",
        "provider_timeout",
        "provider_refusal",
        "persona_unavailable",
        "invalid_reply",
        "internal_error",
    ]
    retryable: bool
    diagnosticCode: Literal[
        "output_format",
        "judgment_validation",
        "response_json",
        "response_envelope",
        "provider_termination",
        "judgment_schema",
        "usage_metadata",
    ] | None = None

    @model_validator(mode="after")
    def validate_diagnostic_code(self) -> "ErrorPayloadV2":
        if self.diagnosticCode is not None and self.code != "invalid_reply":
            raise ValueError("diagnostic code only applies to invalid replies")
        return self


class EmptyPayloadV2(StrictModel):
    pass


@dataclass(frozen=True)
class ParsedEnvelope:
    protocolVersion: int
    type: str
    requestId: str
    payload: StrictModel


_PAYLOAD_MODELS: dict[str, type[StrictModel]] = {
    "bootstrap": BootstrapPayloadV2,
    "ready": ReadyPayloadV2,
    "turn": ConversationJobV2,
    "result": ConversationResultV2,
    "error": ErrorPayloadV2,
    "cancel": EmptyPayloadV2,
    "shutdown": EmptyPayloadV2,
}


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ContractError("duplicate JSON property")
        result[key] = value
    return result


def _reject_nonfinite(value: str) -> None:
    raise ContractError("nonfinite JSON number")


def parse_envelope(raw: str | bytes) -> ParsedEnvelope:
    try:
        encoded = raw.encode("utf-8", errors="strict") if isinstance(raw, str) else bytes(raw)
        if not encoded or len(encoded) > MAX_ENVELOPE_BYTES:
            raise ContractError("protocol envelope size is invalid")
        value = json.loads(
            encoded.decode("utf-8", errors="strict"),
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_nonfinite,
        )
        if not isinstance(value, dict) or set(value) != {"protocolVersion", "type", "requestId", "payload"}:
            raise ContractError("protocol envelope shape is invalid")
        header = EnvelopeHeader.model_validate({key: value[key] for key in ("protocolVersion", "type", "requestId")}, strict=True)
        payload_model = _PAYLOAD_MODELS[header.type]
        payload = payload_model.model_validate(value["payload"], strict=True)
        payload_bytes = json.dumps(value["payload"], ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")
        payload_limit = MAX_TURN_BYTES if header.type == "turn" else MAX_RESULT_BYTES if header.type == "result" else MAX_ENVELOPE_BYTES
        if len(payload_bytes) > payload_limit:
            raise ContractError("protocol payload size is invalid")
        return ParsedEnvelope(header.protocolVersion, header.type, header.requestId, payload)
    except ContractError:
        raise
    except (UnicodeError, json.JSONDecodeError, ValidationError, TypeError, ValueError, KeyError) as exc:
        raise ContractError("invalid protocol envelope") from None


def validate_judgment(job: ConversationJobV2, judgment: ConversationJudgmentV2,
                      allowed_expressions: set[str] | frozenset[str]) -> list[str]:
    errors: list[str] = []
    if job is None or judgment is None:
        return ["invalid_job_or_judgment"]
    candidates = {card.topicId: card for card in job.candidates.cards}
    coverage = {topic.topicId: topic for topic in job.coverage.topics}
    active_topic = job.request.activeTopicId
    references = set(judgment.referencedTopicIds)
    known_ids = set(candidates)
    if active_topic:
        known_ids.add(active_topic)
    if any(reference not in known_ids for reference in references):
        errors.append("unknown_reference")

    if judgment.kind in {"existing", "existing_uncovered"}:
        candidate = candidates.get(judgment.topicId)
        if candidate is None and judgment.topicId != active_topic:
            errors.append("unknown_selected_topic")
        if judgment.topicId not in references:
            errors.append("selected_topic_not_referenced")
        if judgment.kind == "existing":
            if candidate is None or candidate.authoredAvailability != "ready":
                errors.append("authored_topic_unavailable")
            if judgment.reply is not None:
                errors.append("authored_judgment_contains_reply")
        else:
            policy_topic = coverage.get(judgment.topicId)
            if policy_topic is None or not policy_topic.allowUncoveredDiscussion:
                errors.append("uncovered_discussion_not_allowed")
            if judgment.reply is None:
                errors.append("generated_reply_required")
    else:
        if judgment.topicId:
            errors.append("generated_judgment_selected_canonical_topic")
        if judgment.reply is None:
            errors.append("generated_reply_required")
        if judgment.kind == "continue":
            if not job.request.activeResponseId or not active_topic or active_topic not in references:
                errors.append("continuation_not_host_bound")
        elif judgment.kind == "novel_candidate" and active_topic:
            active_card = candidates.get(active_topic)
            if active_card is None or active_card.authoredAvailability != "ready":
                errors.append("active_topic_unavailable_for_novel_branch")

    for reference in references:
        if judgment.kind == "continue" and reference == active_topic:
            continue
        if judgment.kind == "existing_uncovered" and reference == judgment.topicId:
            continue
        card = candidates.get(reference)
        if card is None or card.authoredAvailability != "ready":
            errors.append("referenced_topic_unavailable")

    if judgment.reply is not None:
        beats = judgment.reply.beats
        total_chars = 0
        seen_ids: set[str] = set()
        for beat in beats:
            if beat.language != "japanese":
                errors.append("speech_language_must_be_japanese")
            if not beat.text.strip() or not beat.displayText.strip():
                errors.append("bilingual_text_required")
            if beat.id in seen_ids:
                errors.append("duplicate_beat_id")
            seen_ids.add(beat.id)
            if beat.expression and beat.expression not in allowed_expressions:
                errors.append("unsupported_expression")
            total_chars += len(beat.text) + len(beat.displayText)
        if total_chars > MAX_REPLY_CHARACTERS:
            errors.append("reply_text_limit_exceeded")

    return sorted(set(errors))


def is_response_owned_by(result: ParsedEnvelope, turn: ParsedEnvelope, current_session_id: str) -> bool:
    if result is None or turn is None or result.type != "result" or turn.type != "turn":
        return False
    job = turn.payload
    return (
        isinstance(job, ConversationJobV2)
        and result.requestId == turn.requestId
        and bool(current_session_id)
        and job.request.sessionId == current_session_id
    )
