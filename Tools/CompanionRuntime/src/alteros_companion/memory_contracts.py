"""Strict, provider-neutral records for local AlterOS player memory."""

from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator


class MemoryModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, frozen=True)


class MemoryConsent(MemoryModel):
    playerId: str = Field(min_length=1, max_length=128)
    enabled: bool
    revision: int = Field(ge=0)


class MemoryEvidence(MemoryModel):
    playerId: str = Field(min_length=1, max_length=128)
    sessionId: str = Field(default="", max_length=128)
    sourceId: str = Field(min_length=1, max_length=128)
    actor: Literal["player", "game", "alterra", "assistant", "third_party"]
    scope: Literal["real_world_claim", "in_game_experience", "fictional", "unclassified"]
    sourceKind: Literal[
        "player_message", "player_checkin", "onboarding", "profile_input",
        "player_confirmation", "game_event", "generated_dialogue", "assistant_message",
    ]
    text: str = Field(min_length=1, max_length=512)
    eventAtUtc: datetime | None
    explicitlyConfirmed: bool

    @field_validator("eventAtUtc", mode="before")
    @classmethod
    def parse_timestamp(cls, value):
        if isinstance(value, str):
            return datetime.fromisoformat(value.replace("Z", "+00:00"))
        return value

    @field_validator("eventAtUtc")
    @classmethod
    def require_aware_utc(cls, value: datetime | None) -> datetime | None:
        if value is not None and (value.tzinfo is None or value.utcoffset() is None):
            raise ValueError("event timestamp must include a timezone")
        if value is not None and value.utcoffset().total_seconds() != 0:
            raise ValueError("event timestamp must be UTC")
        return value


class MemoryProposal(MemoryModel):
    category: str = Field(min_length=1, max_length=80)
    key: str = Field(min_length=1, max_length=80, pattern=r"^[a-z0-9_.-]+$")
    value: str = Field(min_length=1, max_length=512)
    scope: Literal["real_world_claim", "in_game_experience", "fictional"]
    sourceId: str = Field(min_length=1, max_length=128)
    evidenceQuote: str = Field(min_length=1, max_length=512)


class MemoryRecord(MemoryModel):
    memoryId: str
    playerId: str
    category: str
    key: str
    value: str
    actor: str
    scope: str
    sourceKind: str
    sourceId: str
    sourceText: str
    eventAtUtc: datetime | None
    recordedAtUtc: datetime
    validUntilUtc: datetime | None
    freshUntilUtc: datetime | None
    status: Literal["active", "superseded"]
    supersedesId: str | None


class MemoryPage(MemoryModel):
    status: Literal["ok"]
    items: tuple[MemoryRecord, ...]
    nextCursor: str
    hasMore: bool


class MemoryContext(MemoryModel):
    core: tuple[MemoryRecord, ...]
    current: tuple[MemoryRecord, ...]
    recalled: tuple[MemoryRecord, ...]
    suppressedSourceIds: tuple[str, ...]


class MemoryOperationResult(MemoryModel):
    status: str
    reasonCode: str = ""
    memory: MemoryRecord | None = None
    consent: MemoryConsent | None = None
    context: MemoryContext | None = None
    page: MemoryPage | None = None
    lastSequence: int | None = None
    count: int | None = None
    stepId: str = ""
    completed: bool | None = None
