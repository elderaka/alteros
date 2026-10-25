"""Strict protocol v3 envelope and operation-specific keyless memory commands."""

from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime
from pathlib import PurePosixPath, PureWindowsPath
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, SecretStr, ValidationError, field_validator, model_validator

from .contracts import ConversationJobV2, MAX_TURN_BYTES
from .memory_contracts import MemoryEvidence, MemoryProposal


MAX_ENVELOPE_BYTES = 256 * 1024
MAX_SOURCE_SYNC_EVENTS = 32
MAX_SOURCE_SYNC_TEXT_BYTES = 16 * 1024


class MemoryProtocolError(ValueError):
    """A sanitized v3 framing or schema error."""


class StrictMemoryModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, frozen=True)


class MemoryBootstrapV3(StrictMemoryModel):
    apiKey: SecretStr = Field(repr=False, max_length=4096)
    playerId: str = Field(min_length=1, max_length=128)
    memoryRoot: str = Field(min_length=1, max_length=1024)

    @field_validator("memoryRoot")
    @classmethod
    def check_memory_root(cls, value: str) -> str:
        validate_memory_root(value)
        return value


class ConsentDataV3(StrictMemoryModel):
    enabled: bool | None = None


class SourceSyncEventV3(StrictMemoryModel):
    sequence: int = Field(ge=1)
    evidence: MemoryEvidence


class SourceSyncDataV3(StrictMemoryModel):
    afterSequence: int = Field(ge=0)
    throughSequence: int = Field(ge=0)
    events: tuple[SourceSyncEventV3, ...] = Field(max_length=MAX_SOURCE_SYNC_EVENTS)

    @model_validator(mode="after")
    def validate_cursor_range(self) -> "SourceSyncDataV3":
        if self.throughSequence < self.afterSequence or any(event.sequence <= self.afterSequence or event.sequence > self.throughSequence for event in self.events):
            raise ValueError("source cursor range is invalid")
        if any(left.sequence >= right.sequence for left, right in zip(self.events, self.events[1:])):
            raise ValueError("source events are not ordered")
        return self


class ProfileSetDataV3(StrictMemoryModel):
    proposal: MemoryProposal
    evidence: MemoryEvidence


class ConfirmDataV3(StrictMemoryModel):
    proposal: MemoryProposal
    evidence: MemoryEvidence
    confirmation: MemoryEvidence


class ContextDataV3(StrictMemoryModel):
    query: str = Field(max_length=2048)


class ListDataV3(StrictMemoryModel):
    cursor: str = Field(max_length=256)
    limit: int = Field(ge=1, le=100)
    includeSuperseded: bool = False


class ForgetDataV3(StrictMemoryModel):
    memoryId: str = Field(min_length=1, max_length=128)


class OnboardingDataV3(StrictMemoryModel):
    stepId: str = Field(min_length=1, max_length=64)
    completed: bool


class OnboardingReadDataV3(StrictMemoryModel):
    pass


class QuestionAckDataV3(StrictMemoryModel):
    intent: Literal["preferred_name", "interest", "shared_activity_preference"]
    sessionId: str = Field(min_length=1, max_length=128)
    eventId: str = Field(min_length=1, max_length=128)
    presentedAtUtc: datetime

    @field_validator("presentedAtUtc", mode="before")
    @classmethod
    def parse_timestamp(cls, value: Any) -> Any:
        return _parse_timestamp(value)

    @field_validator("presentedAtUtc")
    @classmethod
    def require_utc(cls, value: datetime) -> datetime:
        if value.tzinfo is None or value.utcoffset() is None or value.utcoffset().total_seconds() != 0:
            raise ValueError("timestamp must be timezone-aware UTC")
        return value


_DATA_TYPES = {
    "consent": ConsentDataV3,
    "source_sync": SourceSyncDataV3,
    "profile_set": ProfileSetDataV3,
    "confirm": ConfirmDataV3,
    "context": ContextDataV3,
    "list": ListDataV3,
    "forget": ForgetDataV3,
    "onboarding": OnboardingDataV3,
    "onboarding_read": OnboardingReadDataV3,
    "question_ack": QuestionAckDataV3,
}


class MemoryCommandV3(StrictMemoryModel):
    operation: Literal["consent", "source_sync", "profile_set", "confirm", "context", "list", "forget", "onboarding", "onboarding_read", "question_ack"]
    playerId: str = Field(min_length=1, max_length=128)
    consentRevision: int = Field(ge=0)
    nowUtc: datetime
    data: Any

    @model_validator(mode="before")
    @classmethod
    def parse_operation_data(cls, values: dict[str, Any]) -> dict[str, Any]:
        if not isinstance(values, dict):
            raise ValueError("invalid memory command")
        operation = values.get("operation")
        data_type = _DATA_TYPES.get(operation) if isinstance(operation, str) else None
        if data_type is None:
            raise ValueError("unsupported memory operation")
        values = dict(values)
        data_json = json.dumps(values.get("data"), ensure_ascii=False, separators=(",", ":"))
        values["data"] = data_type.model_validate_json(data_json, strict=True)
        return values

    @field_validator("nowUtc", mode="before")
    @classmethod
    def parse_timestamp(cls, value: Any) -> Any:
        return _parse_timestamp(value)

    @field_validator("nowUtc")
    @classmethod
    def require_utc(cls, value: datetime) -> datetime:
        if value.tzinfo is None or value.utcoffset() is None or value.utcoffset().total_seconds() != 0:
            raise ValueError("timestamp must be timezone-aware UTC")
        return value


class MemoryHeaderV3(StrictMemoryModel):
    protocolVersion: Literal[3]
    type: Literal["bootstrap", "memory", "turn", "cancel", "shutdown"]
    requestId: str = Field(min_length=1, max_length=128)


class MemoryTurnPolicyV3(StrictMemoryModel):
    playerId: str = Field(min_length=1, max_length=128)
    consentRevision: int = Field(ge=0)
    nowUtc: datetime
    currentPlayerSourceId: str = Field(min_length=1, max_length=128)
    focusAllowed: bool | None
    quiet: bool | None
    explicitRecall: bool
    authorizedQuestionIntent: Literal["preferred_name", "interest", "shared_activity_preference"] | None
    memoryPolicyRequired: bool
    presentedHistorySourceIds: tuple[str, ...] = Field(max_length=12)

    @field_validator("nowUtc", mode="before")
    @classmethod
    def parse_turn_timestamp(cls, value: Any) -> Any:
        return _parse_timestamp(value)

    @field_validator("nowUtc")
    @classmethod
    def require_turn_utc(cls, value: datetime) -> datetime:
        if value.tzinfo is None or value.utcoffset() is None or value.utcoffset().total_seconds() != 0:
            raise ValueError("timestamp must be timezone-aware UTC")
        return value


class MemoryTurnDataV3(StrictMemoryModel):
    conversation: ConversationJobV2
    memory: MemoryTurnPolicyV3

    @model_validator(mode="after")
    def validate_history_source_alignment(self) -> "MemoryTurnDataV3":
        if len(self.memory.presentedHistorySourceIds) != len(self.conversation.presentedHistory):
            raise ValueError("history sources are not aligned")
        return self


@dataclass(frozen=True)
class MemoryEnvelopeV3:
    protocolVersion: int
    type: str
    requestId: str
    payload: MemoryBootstrapV3 | MemoryCommandV3 | MemoryTurnDataV3 | None


def validate_memory_root(value: str) -> str:
    if not value or ".." in value.replace("\\", "/").split("/"):
        raise MemoryProtocolError("invalid memory root")
    win = PureWindowsPath(value)
    posix = PurePosixPath(value)
    if not ((win.is_absolute() and win.drive) or posix.is_absolute()):
        raise MemoryProtocolError("invalid memory root")
    if win.name != "player-memory.sqlite3" or win.parent.name != "Memory":
        raise MemoryProtocolError("invalid memory root")
    return value


def decode_memory_line(line: bytes) -> MemoryEnvelopeV3:
    if not line or len(line) > MAX_ENVELOPE_BYTES + 2 or not line.endswith(b"\n"):
        raise MemoryProtocolError("invalid protocol line framing")
    body = line[:-1]
    if body.endswith(b"\r"):
        body = body[:-1]
    if not body or len(body) > MAX_ENVELOPE_BYTES:
        raise MemoryProtocolError("invalid protocol line size")
    try:
        data = json.loads(body.decode("utf-8", errors="strict"), object_pairs_hook=_unique_object)
        if not isinstance(data, dict) or set(data) != {"protocolVersion", "type", "requestId", "payload"}:
            raise MemoryProtocolError("invalid protocol message")
        header_data = {key: data[key] for key in ("protocolVersion", "type", "requestId") if key in data}
        header = MemoryHeaderV3.model_validate(header_data, strict=True)
        payload_data = data["payload"]
        if header.type == "bootstrap":
            payload_json = json.dumps(payload_data, ensure_ascii=False, separators=(",", ":"))
            payload = MemoryBootstrapV3.model_validate_json(payload_json, strict=True)
        elif header.type == "memory":
            payload_json = json.dumps(payload_data, ensure_ascii=False, separators=(",", ":"))
            payload = MemoryCommandV3.model_validate_json(payload_json, strict=True)
            if payload.operation == "source_sync":
                total = sum(len(event.evidence.text.encode("utf-8")) for event in payload.data.events)
                if total > MAX_SOURCE_SYNC_TEXT_BYTES:
                    raise MemoryProtocolError("source sync exceeds limit")
        elif header.type == "turn":
            if len(body) > MAX_TURN_BYTES:
                raise MemoryProtocolError("memory turn exceeds limit")
            payload_json = json.dumps(payload_data, ensure_ascii=False, separators=(",", ":"))
            payload = MemoryTurnDataV3.model_validate_json(payload_json, strict=True)
        else:
            if payload_data != {}:
                raise MemoryProtocolError("invalid control payload")
            payload = None
        return MemoryEnvelopeV3(3, header.type, header.requestId, payload)
    except MemoryProtocolError:
        raise
    except (UnicodeError, json.JSONDecodeError, ValidationError, TypeError, KeyError, ValueError):
        raise MemoryProtocolError("invalid protocol message") from None


def encode_memory_message(message: dict[str, Any]) -> bytes:
    try:
        encoded = json.dumps(message, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8", errors="strict")
        if not encoded or len(encoded) > MAX_ENVELOPE_BYTES:
            raise MemoryProtocolError("invalid protocol message size")
        if not isinstance(message, dict) or message.get("protocolVersion") != 3:
            raise MemoryProtocolError("invalid protocol message")
        if not isinstance(message.get("requestId"), str) or not 1 <= len(message["requestId"]) <= 128:
            raise MemoryProtocolError("invalid protocol message")
        if message.get("type") not in {"ready", "result", "error"}:
            raise MemoryProtocolError("invalid protocol message")
        return encoded + b"\n"
    except MemoryProtocolError:
        raise
    except (UnicodeError, TypeError, ValueError):
        raise MemoryProtocolError("invalid protocol message") from None


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise MemoryProtocolError("duplicate protocol property")
        result[key] = value
    return result


def _parse_timestamp(value: Any) -> Any:
    if isinstance(value, str):
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    return value
