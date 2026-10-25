"""Fail-closed assembly of bounded local memory and source-aware short history."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import sqlite3
from typing import Any, Iterable

from .memory_contracts import MemoryConsent, MemoryContext
from .memory_policy import validate_evidence
from .memory_repository import MemoryRepository, MemoryStorageError


class MemoryContextUnavailable(RuntimeError):
    """A required local-memory policy could not be safely applied."""


@dataclass(frozen=True)
class MemoryTurnPolicy:
    playerId: str
    consentRevision: int
    nowUtc: datetime
    currentPlayerSourceId: str
    memoryPolicyRequired: bool
    explicitRecall: bool = False
    presentedHistorySourceIds: tuple[str, ...] = ()
    authorizedQuestionIntent: str | None = None

    def __post_init__(self) -> None:
        if not self.playerId or len(self.playerId) > 128 or self.consentRevision < 0:
            raise ValueError("invalid memory turn policy")
        if self.nowUtc.tzinfo is None or self.nowUtc.utcoffset() != timezone.utc.utcoffset(self.nowUtc):
            raise ValueError("memory turn time must be UTC")


@dataclass(frozen=True)
class MemoryTurnContext:
    context: MemoryContext
    presentedHistory: tuple[Any, ...]
    allowedCallbackIds: tuple[str, ...]
    eligibleProposalSourceIds: tuple[str, ...]
    authorizedQuestionIntent: str | None
    droppedHistoryCount: int
    available: bool


def build_memory_context(repository: MemoryRepository | None, job: Any,
                         policy: MemoryTurnPolicy) -> MemoryTurnContext:
    """Read consent-bound context, then remove suppressed or untraceable history.

    The result is a separate prompt-data value; it is deliberately never folded into
    persona/character rules. When a save has opted into memory policy, missing source
    IDs are omitted rather than risking inclusion of a forgotten transcript event.
    """
    conversation = _conversation(job)
    original = _history(conversation)
    source_ids = policy.presentedHistorySourceIds
    if source_ids and len(source_ids) != len(original):
        if policy.memoryPolicyRequired:
            raise MemoryContextUnavailable("history_source_mismatch")
        source_ids = ()
    if repository is None:
        if policy.memoryPolicyRequired:
            raise MemoryContextUnavailable("memory_unavailable")
        return MemoryTurnContext(_empty_context(), original, (), (), policy.authorizedQuestionIntent, 0, False)
    try:
        consent = repository.get_consent()
        if consent.playerId != policy.playerId:
            raise MemoryContextUnavailable("wrong_player")
        if consent.revision != policy.consentRevision and policy.memoryPolicyRequired:
            raise MemoryContextUnavailable("consent_revision_conflict")
        if consent.revision != policy.consentRevision:
            consent = MemoryConsent(playerId=consent.playerId, enabled=False, revision=consent.revision)
        query = _query(conversation)
        context = repository.read_context(query, consent, policy.nowUtc)
    except MemoryContextUnavailable:
        raise
    except (MemoryStorageError, OSError, ValueError, sqlite3.Error):
        if policy.memoryPolicyRequired:
            raise MemoryContextUnavailable("memory_unavailable") from None
        return MemoryTurnContext(_empty_context(), original, (), (), policy.authorizedQuestionIntent, 0, False)

    suppressed_ids = set(context.suppressedSourceIds)
    kept: list[Any] = []
    dropped = 0
    for index, item in enumerate(original):
        source_id = source_ids[index] if len(source_ids) == len(original) else _field(item, "sourceId", "")
        if policy.memoryPolicyRequired and not source_id:
            dropped += 1
            continue
        if source_id and source_id in suppressed_ids:
            dropped += 1
            continue
        kept.append(item)
    callbacks = tuple(record.memoryId for record in context.recalled) if policy.explicitRecall and consent.enabled else ()
    current_source = repository.get_source_evidence(policy.currentPlayerSourceId) if consent.enabled else None
    eligible_sources = (policy.currentPlayerSourceId,) if current_source is not None and validate_evidence(current_source) is None else ()
    return MemoryTurnContext(context, tuple(kept), callbacks, eligible_sources,
                             policy.authorizedQuestionIntent, dropped, True)


def _empty_context() -> MemoryContext:
    return MemoryContext(core=(), current=(), recalled=(), suppressedSourceIds=())


def _history(job: Any) -> tuple[Any, ...]:
    raw = _field(job, "presentedHistory", ())
    return tuple(raw) if isinstance(raw, Iterable) and not isinstance(raw, (str, bytes, dict)) else ()


def _conversation(job: Any) -> Any:
    return _field(job, "conversation", job)


def _query(job: Any) -> str:
    request = _field(job, "request", None)
    text = _field(request, "text", "")
    return text if isinstance(text, str) else ""


def _field(value: Any, name: str, default: Any) -> Any:
    if isinstance(value, dict):
        return value.get(name, default)
    return getattr(value, name, default) if value is not None else default
