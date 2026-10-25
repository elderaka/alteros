"""Transactional, keyless SQLite repository for local player memory."""

from __future__ import annotations

import base64
from contextlib import nullcontext
import hashlib
import json
import re
import sqlite3
import threading
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from .memory_contracts import (
    MemoryConsent,
    MemoryContext,
    MemoryEvidence,
    MemoryOperationResult,
    MemoryPage,
    MemoryProposal,
    MemoryRecord,
)
from .memory_policy import freshness_deadline, requires_confirmation, validate_evidence, validate_proposal


SCHEMA_VERSION = 3
MAX_PAGE_SIZE = 100
MAX_CONTEXT_ITEMS = 10
MAX_CONTEXT_CHARACTERS = 3000
CORE_CATEGORIES = (
    "preferred_name", "boundary", "interaction_preference", "shared_activity_preference",
    "interest", "dislike", "active_goal", "routine",
)
TOKEN_PATTERN = re.compile(r"[^\w]+", re.UNICODE)


class MemoryStorageError(RuntimeError):
    def __init__(self, code: str = "memory_unavailable") -> None:
        super().__init__(code)
        self.code = code


class MemoryRepository:
    def __init__(self, db_path: str | Path, player_id: str, *, enable_fts5: bool = True) -> None:
        if not player_id or len(player_id) > 128:
            raise ValueError("invalid player identity")
        self._path = Path(db_path)
        if not self._path.is_absolute():
            raise ValueError("memory database path must be absolute")
        self._player_id = player_id
        self._lock = threading.RLock()
        self._fts_enabled = False
        self._check_existing_file()
        self._path.parent.mkdir(parents=True, exist_ok=True)
        try:
            self._initialize(enable_fts5)
        except MemoryStorageError:
            raise
        except sqlite3.Error as exc:
            raise MemoryStorageError() from None

    @property
    def player_id(self) -> str:
        return self._player_id

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self._path, timeout=3.0, isolation_level=None)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys=ON")
        connection.execute("PRAGMA busy_timeout=3000")
        return connection

    def _check_existing_file(self) -> None:
        if not self._path.exists() or self._path.stat().st_size == 0:
            return
        uri = f"file:{self._path.as_posix()}?mode=ro"
        try:
            with sqlite3.connect(self._path.as_uri() + "?mode=ro", uri=True, timeout=2.0) as connection:
                check = connection.execute("PRAGMA quick_check").fetchone()
                version = connection.execute("PRAGMA user_version").fetchone()[0]
            if not check or check[0] != "ok" or version not in (0, 1, 2, SCHEMA_VERSION):
                raise MemoryStorageError()
        except MemoryStorageError:
            raise
        except sqlite3.Error:
            raise MemoryStorageError() from None

    def _initialize(self, enable_fts5: bool) -> None:
        with self._connect() as connection:
            version = connection.execute("PRAGMA user_version").fetchone()[0]
            if version not in (0, 1, 2, SCHEMA_VERSION):
                raise MemoryStorageError()
            if version == 0:
                try:
                    connection.executescript("""
                        BEGIN IMMEDIATE;
                        CREATE TABLE consent_state (
                            player_id TEXT PRIMARY KEY,
                            enabled INTEGER NOT NULL CHECK(enabled IN (0,1)),
                            revision INTEGER NOT NULL CHECK(revision >= 0),
                            updated_at_utc TEXT NOT NULL
                        );
                        CREATE TABLE source_events (
                            player_id TEXT NOT NULL,
                            source_id TEXT NOT NULL,
                            actor TEXT NOT NULL,
                            scope TEXT NOT NULL,
                            source_kind TEXT NOT NULL,
                            excerpt TEXT NOT NULL,
                            event_at_utc TEXT,
                            explicitly_confirmed INTEGER NOT NULL CHECK(explicitly_confirmed IN (0,1)),
                            content_hash TEXT NOT NULL,
                            PRIMARY KEY(player_id, source_id)
                        );
                        CREATE TABLE memories (
                            memory_id TEXT PRIMARY KEY,
                            player_id TEXT NOT NULL,
                            category TEXT NOT NULL,
                            memory_key TEXT NOT NULL,
                            value TEXT NOT NULL,
                            actor TEXT NOT NULL,
                            scope TEXT NOT NULL,
                            source_kind TEXT NOT NULL,
                            source_id TEXT NOT NULL,
                            event_at_utc TEXT,
                            recorded_at_utc TEXT NOT NULL,
                            valid_until_utc TEXT,
                            fresh_until_utc TEXT,
                            status TEXT NOT NULL CHECK(status IN ('active','superseded')),
                            supersedes_id TEXT,
                            proposal_hash TEXT NOT NULL,
                            UNIQUE(player_id, source_id, category, memory_key, value),
                            FOREIGN KEY(player_id, source_id) REFERENCES source_events(player_id, source_id),
                            FOREIGN KEY(supersedes_id) REFERENCES memories(memory_id)
                        );
                        CREATE INDEX memories_profile_idx ON memories(player_id, status, category, memory_key);
                        CREATE INDEX memories_page_idx ON memories(player_id, recorded_at_utc, memory_id);
                        CREATE TABLE memory_evidence (
                            memory_id TEXT NOT NULL,
                            player_id TEXT NOT NULL,
                            source_id TEXT NOT NULL,
                            role TEXT NOT NULL,
                            excerpt TEXT NOT NULL,
                            PRIMARY KEY(memory_id, source_id),
                            FOREIGN KEY(memory_id) REFERENCES memories(memory_id) ON DELETE CASCADE,
                            FOREIGN KEY(player_id, source_id) REFERENCES source_events(player_id, source_id)
                        );
                        CREATE TABLE suppression (
                            player_id TEXT NOT NULL,
                            source_id TEXT NOT NULL,
                            category TEXT NOT NULL,
                            memory_key TEXT NOT NULL,
                            forgotten_at_utc TEXT NOT NULL,
                            PRIMARY KEY(player_id, source_id, category, memory_key)
                        );
                        CREATE TABLE onboarding_progress (
                            player_id TEXT PRIMARY KEY,
                            step_id TEXT NOT NULL,
                            completed INTEGER NOT NULL CHECK(completed IN (0,1)),
                            updated_at_utc TEXT NOT NULL
                        );
                        CREATE TABLE question_history (
                            player_id TEXT NOT NULL,
                            intent TEXT NOT NULL,
                            session_id TEXT NOT NULL,
                            event_id TEXT NOT NULL,
                            presented_at_utc TEXT NOT NULL,
                            PRIMARY KEY(player_id, event_id)
                        );
                        PRAGMA user_version=1;
                        COMMIT;
                    """)
                    version = 1
                except sqlite3.Error:
                    connection.rollback()
                    raise
            if version == 1:
                try:
                    connection.executescript("""
                        BEGIN IMMEDIATE;
                        CREATE TABLE source_sync_state (
                            player_id TEXT PRIMARY KEY,
                            last_sequence INTEGER NOT NULL CHECK(last_sequence >= 0)
                        );
                        PRAGMA user_version=2;
                        COMMIT;
                    """)
                except sqlite3.Error:
                    connection.rollback()
                    raise
                version = 2
            if version == 2:
                try:
                    connection.executescript("""
                        BEGIN IMMEDIATE;
                        ALTER TABLE source_events ADD COLUMN session_id TEXT NOT NULL DEFAULT '';
                        PRAGMA user_version=3;
                        COMMIT;
                    """)
                except sqlite3.Error:
                    connection.rollback()
                    raise
            if enable_fts5:
                try:
                    connection.execute("CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(memory_id UNINDEXED, player_id UNINDEXED, value, source_text, tokenize='unicode61')")
                    self._fts_enabled = True
                    rows = connection.execute("SELECT memory_id, player_id, value FROM memories WHERE status='active'").fetchall()
                    for row in rows:
                        source = self._source_excerpt(connection, row["player_id"], row["memory_id"])
                        self._index_memory(connection, row["memory_id"], row["player_id"], row["value"], source)
                except sqlite3.OperationalError:
                    self._fts_enabled = False

    @staticmethod
    def _source_excerpt(connection: sqlite3.Connection, player_id: str, memory_id: str) -> str:
        row = connection.execute("""SELECT e.excerpt FROM memory_evidence e JOIN memories m ON m.memory_id=e.memory_id
                                     WHERE m.player_id=? AND m.memory_id=? AND e.role='source' ORDER BY e.source_id LIMIT 1""",
                                 (player_id, memory_id)).fetchone()
        return row[0] if row else ""

    def get_consent(self) -> MemoryConsent:
        with self._lock, self._connect() as connection:
            row = connection.execute("SELECT enabled, revision FROM consent_state WHERE player_id=?", (self._player_id,)).fetchone()
            return MemoryConsent(playerId=self._player_id, enabled=bool(row["enabled"]), revision=row["revision"]) if row else MemoryConsent(playerId=self._player_id, enabled=False, revision=0)

    def set_consent(self, enabled: bool, *, expected_revision: int, now_utc: datetime) -> MemoryOperationResult:
        now = _utc(now_utc)
        with self._lock, self._connect() as connection:
            try:
                connection.execute("BEGIN IMMEDIATE")
                row = connection.execute("SELECT enabled, revision FROM consent_state WHERE player_id=?", (self._player_id,)).fetchone()
                old_revision = row["revision"] if row else 0
                old_enabled = bool(row["enabled"]) if row else False
                if expected_revision != old_revision:
                    connection.rollback()
                    return MemoryOperationResult(status="rejected", reasonCode="consent_revision_conflict")
                if row and old_enabled == enabled:
                    connection.commit()
                    return MemoryOperationResult(status="accepted", consent=MemoryConsent(playerId=self._player_id, enabled=enabled, revision=old_revision))
                revision = old_revision + 1
                connection.execute("""INSERT INTO consent_state(player_id,enabled,revision,updated_at_utc) VALUES(?,?,?,?)
                                    ON CONFLICT(player_id) DO UPDATE SET enabled=excluded.enabled,revision=excluded.revision,updated_at_utc=excluded.updated_at_utc""",
                                   (self._player_id, int(enabled), revision, _stamp(now)))
                connection.commit()
                return MemoryOperationResult(status="accepted", consent=MemoryConsent(playerId=self._player_id, enabled=enabled, revision=revision))
            except sqlite3.Error:
                connection.rollback()
                return MemoryOperationResult(status="error", reasonCode="memory_unavailable")

    def upsert(self, proposal: MemoryProposal, evidence: MemoryEvidence, consent: MemoryConsent,
               now_utc: datetime) -> MemoryOperationResult:
        return self._write_memory(proposal, evidence, consent, now_utc, None)

    def upsert_batch(self, items: Iterable[tuple[MemoryProposal, MemoryEvidence]], consent: MemoryConsent,
                     now_utc: datetime) -> list[MemoryOperationResult]:
        """Apply a validated proposal batch atomically; any rejected item rolls back all writes."""
        batch = list(items)
        if not batch:
            return []
        results: list[MemoryOperationResult] = []
        with self._lock, self._connect() as connection:
            try:
                connection.execute("BEGIN IMMEDIATE")
                for proposal, evidence in batch:
                    result = self._write_memory(proposal, evidence, consent, now_utc, None, connection)
                    results.append(result)
                    if result.status not in {"accepted", "duplicate", "pending_confirmation"}:
                        connection.rollback()
                        return results
                connection.commit()
                return results
            except sqlite3.Error:
                connection.rollback()
                return [MemoryOperationResult(status="error", reasonCode="memory_unavailable")]

    def confirm(self, proposal: MemoryProposal, evidence: MemoryEvidence, confirmation: MemoryEvidence,
                consent: MemoryConsent, now_utc: datetime) -> MemoryOperationResult:
        if (confirmation.playerId != self._player_id or confirmation.actor != "player" or
                confirmation.sourceKind != "player_confirmation" or not confirmation.explicitlyConfirmed):
            return MemoryOperationResult(status="rejected", reasonCode="confirmation_not_authoritative")
        if proposal.evidenceQuote not in evidence.text:
            return MemoryOperationResult(status="rejected", reasonCode="evidence_quote_missing")
        return self._write_memory(proposal, evidence, consent, now_utc, confirmation)

    def _write_memory(self, proposal: MemoryProposal, evidence: MemoryEvidence, consent: MemoryConsent,
                      now_utc: datetime, confirmation: MemoryEvidence | None,
                      connection: sqlite3.Connection | None = None) -> MemoryOperationResult:
        now = _utc(now_utc)
        reason = validate_proposal(proposal)
        if reason:
            return MemoryOperationResult(status="rejected", reasonCode=reason)
        if evidence.playerId != self._player_id or consent.playerId != self._player_id:
            return MemoryOperationResult(status="rejected", reasonCode="wrong_player")
        if proposal.sourceId != evidence.sourceId:
            return MemoryOperationResult(status="rejected", reasonCode="source_scope_mismatch")
        needs_scope_confirmation = evidence.scope == "unclassified" and proposal.scope != "unclassified"
        if proposal.scope != evidence.scope and not needs_scope_confirmation:
            return MemoryOperationResult(status="rejected", reasonCode="source_scope_mismatch")
        reason = validate_evidence(evidence)
        if reason and not (reason == "source_scope_unclassified" and needs_scope_confirmation):
            return MemoryOperationResult(status="rejected", reasonCode=reason)
        if evidence.scope == "in_game_experience" and proposal.category != "recent_activity":
            return MemoryOperationResult(status="rejected", reasonCode="source_scope_mismatch")
        if proposal.evidenceQuote not in evidence.text:
            return MemoryOperationResult(status="rejected", reasonCode="evidence_quote_missing")
        if needs_scope_confirmation and confirmation is None:
            return MemoryOperationResult(status="pending_confirmation", reasonCode="scope_confirmation_required")
        if requires_confirmation(proposal.category, evidence) and confirmation is None:
            return MemoryOperationResult(status="pending_confirmation", reasonCode="explicit_confirmation_required")
        if confirmation and (validate_evidence(confirmation) or confirmation.scope != proposal.scope):
            return MemoryOperationResult(status="rejected", reasonCode="confirmation_not_authoritative")
        if not consent.enabled:
            return MemoryOperationResult(status="rejected", reasonCode="memory_disabled")

        proposal_hash = _hash({"category": proposal.category, "key": proposal.key,
                               "value": proposal.value, "scope": proposal.scope,
                               "sourceId": proposal.sourceId})
        source_records = [evidence] + ([confirmation] if confirmation else [])
        owns_transaction = connection is None
        lock_context = self._lock
        connection_context = self._connect() if owns_transaction else nullcontext(connection)
        with lock_context, connection_context as connection:
            try:
                if owns_transaction:
                    connection.execute("BEGIN IMMEDIATE")
                consent_row = connection.execute("SELECT enabled,revision FROM consent_state WHERE player_id=?", (self._player_id,)).fetchone()
                if not consent_row or not consent_row["enabled"] or consent_row["revision"] != consent.revision:
                    if owns_transaction: connection.rollback()
                    return MemoryOperationResult(status="rejected", reasonCode="consent_revision_conflict")
                suppressed = connection.execute("""SELECT 1 FROM suppression WHERE player_id=? AND source_id=?
                                                    AND category=? AND memory_key=?""",
                                                (self._player_id, proposal.sourceId, proposal.category, proposal.key)).fetchone()
                if suppressed:
                    if owns_transaction: connection.rollback()
                    return MemoryOperationResult(status="rejected", reasonCode="source_suppressed")
                for source in source_records:
                    if not self._insert_source(connection, source):
                        if owns_transaction: connection.rollback()
                        return MemoryOperationResult(status="rejected", reasonCode="source_conflict")
                duplicate = connection.execute("""SELECT * FROM memories WHERE player_id=? AND source_id=?
                                                   AND category=? AND memory_key=? AND value=?""",
                                               (self._player_id, proposal.sourceId, proposal.category, proposal.key, proposal.value)).fetchone()
                if duplicate:
                    if owns_transaction: connection.commit()
                    return MemoryOperationResult(status="duplicate", memory=self._record(connection, duplicate))
                superseded = connection.execute("""SELECT * FROM memories WHERE player_id=? AND category=? AND memory_key=?
                                                   AND scope=? AND status='active' ORDER BY recorded_at_utc DESC,memory_id DESC LIMIT 1""",
                                                (self._player_id, proposal.category, proposal.key, proposal.scope)).fetchone()
                if superseded:
                    connection.execute("UPDATE memories SET status='superseded' WHERE memory_id=?", (superseded["memory_id"],))
                    self._delete_index(connection, superseded["memory_id"])
                memory_id = uuid.uuid4().hex
                valid_until = freshness_deadline(proposal.category, evidence.eventAtUtc)
                source_text = evidence.text[:512]
                connection.execute("""INSERT INTO memories(memory_id,player_id,category,memory_key,value,actor,scope,source_kind,
                                    source_id,event_at_utc,recorded_at_utc,valid_until_utc,fresh_until_utc,status,supersedes_id,proposal_hash)
                                    VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                                   (memory_id, self._player_id, proposal.category, proposal.key, proposal.value,
                                    evidence.actor, proposal.scope, evidence.sourceKind, evidence.sourceId,
                                    _stamp(evidence.eventAtUtc) if evidence.eventAtUtc else None, _stamp(now),
                                    _stamp(valid_until) if valid_until else None, _stamp(valid_until) if valid_until else None,
                                    "active", superseded["memory_id"] if superseded else None, proposal_hash))
                connection.execute("INSERT INTO memory_evidence(memory_id,player_id,source_id,role,excerpt) VALUES(?,?,?,?,?)",
                                   (memory_id, self._player_id, evidence.sourceId, "source", source_text))
                if confirmation:
                    connection.execute("INSERT INTO memory_evidence(memory_id,player_id,source_id,role,excerpt) VALUES(?,?,?,?,?)",
                                       (memory_id, self._player_id, confirmation.sourceId, "confirmation", confirmation.text[:512]))
                self._index_memory(connection, memory_id, self._player_id, proposal.value, source_text)
                row = connection.execute("SELECT * FROM memories WHERE memory_id=?", (memory_id,)).fetchone()
                if owns_transaction: connection.commit()
                return MemoryOperationResult(status="accepted", memory=self._record(connection, row))
            except sqlite3.Error:
                if owns_transaction: connection.rollback()
                return MemoryOperationResult(status="error", reasonCode="memory_unavailable")

    def _insert_source(self, connection: sqlite3.Connection, evidence: MemoryEvidence) -> bool:
        digest = _hash({"playerId": evidence.playerId, "sessionId": evidence.sessionId, "sourceId": evidence.sourceId, "actor": evidence.actor,
                        "scope": evidence.scope, "sourceKind": evidence.sourceKind, "text": evidence.text,
                        "eventAtUtc": _stamp(evidence.eventAtUtc) if evidence.eventAtUtc else None,
                        "explicitlyConfirmed": evidence.explicitlyConfirmed})
        row = connection.execute("SELECT content_hash FROM source_events WHERE player_id=? AND source_id=?",
                                 (self._player_id, evidence.sourceId)).fetchone()
        if row:
            return row["content_hash"] == digest
        connection.execute("""INSERT INTO source_events(player_id,session_id,source_id,actor,scope,source_kind,excerpt,event_at_utc,
                              explicitly_confirmed,content_hash) VALUES(?,?,?,?,?,?,?,?,?,?)""",
                           (self._player_id, evidence.sessionId, evidence.sourceId, evidence.actor, evidence.scope, evidence.sourceKind,
                            evidence.text[:512], _stamp(evidence.eventAtUtc) if evidence.eventAtUtc else None,
                            int(evidence.explicitlyConfirmed), digest))
        return True

    def has_source(self, source_id: str) -> bool:
        if not source_id or len(source_id) > 128:
            return False
        with self._lock, self._connect() as connection:
            return connection.execute("SELECT 1 FROM source_events WHERE player_id=? AND source_id=? LIMIT 1",
                                      (self._player_id, source_id)).fetchone() is not None

    def get_source_evidence(self, source_id: str) -> MemoryEvidence | None:
        if not source_id or len(source_id) > 128:
            return None
        with self._lock, self._connect() as connection:
            row = connection.execute("SELECT * FROM source_events WHERE player_id=? AND source_id=? LIMIT 1",
                                     (self._player_id, source_id)).fetchone()
            if row is None:
                return None
            return MemoryEvidence(playerId=row["player_id"], sessionId=row["session_id"], sourceId=row["source_id"],
                actor=row["actor"], scope=row["scope"], sourceKind=row["source_kind"], text=row["excerpt"],
                eventAtUtc=_parse_time(row["event_at_utc"]), explicitlyConfirmed=bool(row["explicitly_confirmed"]))

    def forget(self, memory_id: str, now_utc: datetime) -> MemoryOperationResult:
        now = _utc(now_utc)
        with self._lock, self._connect() as connection:
            try:
                connection.execute("BEGIN IMMEDIATE")
                row = connection.execute("SELECT * FROM memories WHERE memory_id=? AND player_id=?", (memory_id, self._player_id)).fetchone()
                if not row:
                    connection.rollback()
                    return MemoryOperationResult(status="not_found")
                evidence_rows = connection.execute("SELECT source_id FROM memory_evidence WHERE memory_id=?", (memory_id,)).fetchall()
                for item in evidence_rows:
                    connection.execute("INSERT OR IGNORE INTO suppression(player_id,source_id,category,memory_key,forgotten_at_utc) VALUES(?,?,?,?,?)",
                                       (self._player_id, item["source_id"], row["category"], row["memory_key"], _stamp(now)))
                self._delete_index(connection, memory_id)
                connection.execute("DELETE FROM memories WHERE memory_id=?", (memory_id,))
                connection.execute("DELETE FROM source_events WHERE player_id=? AND source_id NOT IN (SELECT source_id FROM memory_evidence WHERE player_id=?)",
                                   (self._player_id, self._player_id))
                connection.commit()
                return MemoryOperationResult(status="forgotten")
            except sqlite3.Error:
                connection.rollback()
                return MemoryOperationResult(status="error", reasonCode="memory_unavailable")

    def sync_sources(self, after_sequence: int, events: Iterable[tuple[int, MemoryEvidence]],
                     consent: MemoryConsent, now_utc: datetime, *, through_sequence: int | None = None) -> MemoryOperationResult:
        now = _utc(now_utc)
        page = tuple(events)
        if after_sequence < 0 or (through_sequence is not None and through_sequence < 0):
            return MemoryOperationResult(status="rejected", reasonCode="source_sequence_conflict")
        if len(page) > 32 or sum(len(item.text.encode("utf-8")) for _, item in page) > 16 * 1024:
            return MemoryOperationResult(status="rejected", reasonCode="source_sync_too_large")
        if consent.playerId != self._player_id:
            return MemoryOperationResult(status="rejected", reasonCode="wrong_player")
        with self._lock, self._connect() as connection:
            try:
                connection.execute("BEGIN IMMEDIATE")
                state = connection.execute("SELECT last_sequence FROM source_sync_state WHERE player_id=?", (self._player_id,)).fetchone()
                current = state["last_sequence"] if state else 0
                consent_row = connection.execute("SELECT enabled,revision FROM consent_state WHERE player_id=?", (self._player_id,)).fetchone()
                if not consent_row or consent_row["revision"] != consent.revision or consent_row["enabled"] != consent.enabled:
                    connection.rollback()
                    return MemoryOperationResult(status="rejected", reasonCode="consent_revision_conflict", lastSequence=current)
                if not page and after_sequence == 0 and state is not None:
                    connection.commit()
                    return MemoryOperationResult(status="accepted", lastSequence=current, count=0)
                if state is None and after_sequence != 0:
                    connection.rollback()
                    return MemoryOperationResult(status="rejected", reasonCode="source_sequence_conflict", lastSequence=0)
                if state is None and not page:
                    checkpoint = through_sequence or 0
                    connection.execute("INSERT INTO source_sync_state(player_id,last_sequence) VALUES(?,?)",
                                       (self._player_id, checkpoint))
                    connection.commit()
                    return MemoryOperationResult(status="accepted", lastSequence=checkpoint, count=0)
                if after_sequence != current:
                    connection.rollback()
                    return MemoryOperationResult(status="rejected", reasonCode="source_sequence_conflict", lastSequence=current)
                previous_sequence = current
                stored_count = 0
                for sequence, evidence in page:
                    if sequence <= previous_sequence or evidence.playerId != self._player_id:
                        connection.rollback()
                        reason = "wrong_player" if evidence.playerId != self._player_id else "source_sequence_conflict"
                        return MemoryOperationResult(status="rejected", reasonCode=reason, lastSequence=current)
                    suppressed = connection.execute("SELECT 1 FROM suppression WHERE player_id=? AND source_id=? LIMIT 1",
                                                   (self._player_id, evidence.sourceId)).fetchone()
                    if consent.enabled and not suppressed and not self._insert_source(connection, evidence):
                        connection.rollback()
                        return MemoryOperationResult(status="rejected", reasonCode="source_conflict", lastSequence=current)
                    if consent.enabled and not suppressed:
                        stored_count += 1
                    previous_sequence = sequence
                last_sequence = previous_sequence if through_sequence is None else through_sequence
                if last_sequence < previous_sequence or last_sequence < current:
                    connection.rollback()
                    return MemoryOperationResult(status="rejected", reasonCode="source_sequence_conflict", lastSequence=current)
                connection.execute("""INSERT INTO source_sync_state(player_id,last_sequence) VALUES(?,?)
                    ON CONFLICT(player_id) DO UPDATE SET last_sequence=excluded.last_sequence""",
                                   (self._player_id, last_sequence))
                connection.commit()
                return MemoryOperationResult(status="accepted", lastSequence=last_sequence, count=stored_count)
            except sqlite3.Error:
                connection.rollback()
                return MemoryOperationResult(status="error", reasonCode="memory_unavailable")

    def save_onboarding_progress(self, step_id: str, completed: bool, now_utc: datetime) -> MemoryOperationResult:
        if step_id not in {"consent", "preferred_name", "interest", "shared_activity_preference", "complete", "skipped"}:
            return MemoryOperationResult(status="rejected", reasonCode="invalid_onboarding_step")
        now = _utc(now_utc)
        with self._lock, self._connect() as connection:
            try:
                connection.execute("""INSERT INTO onboarding_progress(player_id,step_id,completed,updated_at_utc) VALUES(?,?,?,?)
                    ON CONFLICT(player_id) DO UPDATE SET step_id=excluded.step_id,completed=excluded.completed,updated_at_utc=excluded.updated_at_utc""",
                                   (self._player_id, step_id, int(completed), _stamp(now)))
                return MemoryOperationResult(status="accepted", stepId=step_id, completed=completed)
            except sqlite3.Error:
                return MemoryOperationResult(status="error", reasonCode="memory_unavailable")

    def get_onboarding_progress(self) -> MemoryOperationResult:
        """Read the single durable onboarding cursor without changing it."""
        with self._lock, self._connect() as connection:
            try:
                row = connection.execute("SELECT step_id,completed FROM onboarding_progress WHERE player_id=?",
                                         (self._player_id,)).fetchone()
                if row is None:
                    return MemoryOperationResult(status="accepted")
                return MemoryOperationResult(status="accepted", stepId=row["step_id"], completed=bool(row["completed"]))
            except sqlite3.Error:
                return MemoryOperationResult(status="error", reasonCode="memory_unavailable")

    def record_question_ack(self, intent: str, session_id: str, event_id: str,
                            presented_at_utc: datetime) -> MemoryOperationResult:
        if intent not in {"preferred_name", "interest", "shared_activity_preference"}:
            return MemoryOperationResult(status="rejected", reasonCode="invalid_question_intent")
        if not session_id or len(session_id) > 128 or not event_id or len(event_id) > 128:
            return MemoryOperationResult(status="rejected", reasonCode="invalid_question_event")
        now = _utc(presented_at_utc)
        with self._lock, self._connect() as connection:
            try:
                cursor = connection.execute("INSERT OR IGNORE INTO question_history(player_id,intent,session_id,event_id,presented_at_utc) VALUES(?,?,?,?,?)",
                                            (self._player_id, intent, session_id, event_id, _stamp(now)))
                return MemoryOperationResult(status="accepted" if cursor.rowcount else "duplicate")
            except sqlite3.Error:
                return MemoryOperationResult(status="error", reasonCode="memory_unavailable")

    def read_context(self, query: str, consent: MemoryConsent, now_utc: datetime) -> MemoryContext:
        now = _utc(now_utc)
        if consent.playerId != self._player_id:
            return MemoryContext(core=(), current=(), recalled=(), suppressedSourceIds=())
        with self._lock, self._connect() as connection:
            suppressed = tuple(row[0] for row in connection.execute(
                "SELECT DISTINCT source_id FROM suppression WHERE player_id=? ORDER BY source_id", (self._player_id,)).fetchall())
            current_consent = connection.execute("SELECT enabled,revision FROM consent_state WHERE player_id=?", (self._player_id,)).fetchone()
            if (not consent.enabled or not current_consent or not current_consent["enabled"] or
                    current_consent["revision"] != consent.revision):
                return MemoryContext(core=(), current=(), recalled=(), suppressedSourceIds=suppressed)
            core_rows = connection.execute("""SELECT * FROM memories WHERE player_id=? AND status='active'
                AND scope='real_world_claim' AND category IN ('preferred_name','boundary','interaction_preference',
                'shared_activity_preference','interest','dislike','active_goal','routine')
                ORDER BY CASE category WHEN 'preferred_name' THEN 0 WHEN 'boundary' THEN 1 ELSE 2 END,
                recorded_at_utc DESC,memory_id ASC LIMIT 32""", (self._player_id,)).fetchall()
            now_s = _stamp(now)
            current_rows = connection.execute("""SELECT * FROM memories WHERE player_id=? AND status='active'
                AND category IN ('recent_activity','temporary_state') AND valid_until_utc IS NOT NULL AND valid_until_utc>?
                ORDER BY event_at_utc DESC,memory_id ASC LIMIT 20""", (self._player_id, now_s)).fetchall()
            core, used = self._budget_records(connection, core_rows, max_items=4, used=0)
            current, used = self._budget_records(connection, current_rows, max_items=3, used=used)
            excluded = {item.memoryId for item in core + current}
            recalled_rows = self._search(connection, query, excluded)
            recalled, _ = self._budget_records(connection, recalled_rows, max_items=3, used=used)
            return MemoryContext(core=tuple(core), current=tuple(current), recalled=tuple(recalled), suppressedSourceIds=suppressed)

    def _search(self, connection: sqlite3.Connection, query: str, excluded: set[str]) -> list[sqlite3.Row]:
        terms = _search_terms(query)
        if not terms:
            return []
        if self._fts_enabled:
            expression = " AND ".join('"' + term.replace('"', '""') + '"' for term in terms)
            rows = connection.execute("""SELECT m.*, e.excerpt AS source_text FROM memory_fts f
                JOIN memories m ON m.memory_id=f.memory_id JOIN memory_evidence e ON e.memory_id=m.memory_id AND e.role='source'
                WHERE memory_fts MATCH ? AND f.player_id=? AND m.status='active' ORDER BY m.event_at_utc DESC LIMIT 40""",
                                      (expression, self._player_id)).fetchall()
        else:
            clauses = " AND ".join("(lower(m.value) LIKE ? ESCAPE '\\' OR lower(e.excerpt) LIKE ? ESCAPE '\\')" for _ in terms)
            params: list[str] = []
            for term in terms:
                pattern = "%" + _escape_like(term.casefold()) + "%"
                params.extend((pattern, pattern))
            rows = connection.execute(f"""SELECT m.*, e.excerpt AS source_text FROM memories m
                JOIN memory_evidence e ON e.memory_id=m.memory_id AND e.role='source'
                WHERE m.player_id=? AND m.status='active' AND ({clauses})
                ORDER BY COALESCE(m.event_at_utc,m.recorded_at_utc) DESC,m.memory_id ASC LIMIT 40""",
                                  (self._player_id, *params)).fetchall()
        filtered = [row for row in rows if row["memory_id"] not in excluded]
        # A current state that has expired is historical recall; scopes remain explicit on each result.
        return sorted(filtered, key=lambda row: _relevance(row, terms), reverse=True)

    def _budget_records(self, connection: sqlite3.Connection, rows: Iterable[sqlite3.Row], *, max_items: int,
                        used: int) -> tuple[list[MemoryRecord], int]:
        result: list[MemoryRecord] = []
        for row in rows:
            if len(result) >= max_items:
                break
            excerpt = row["source_text"] if "source_text" in row.keys() else self._source_excerpt(connection, self._player_id, row["memory_id"])
            cost = len(row["value"]) + len(excerpt)
            if used + cost > MAX_CONTEXT_CHARACTERS:
                continue
            result.append(self._record(connection, row, source_text=excerpt))
            used += cost
        return result, used

    def _record(self, connection: sqlite3.Connection, row: sqlite3.Row, source_text: str | None = None) -> MemoryRecord:
        if source_text is None:
            source_text = self._source_excerpt(connection, self._player_id, row["memory_id"])
        return MemoryRecord(
            memoryId=row["memory_id"], playerId=row["player_id"], category=row["category"], key=row["memory_key"],
            value=row["value"], actor=row["actor"], scope=row["scope"], sourceKind=row["source_kind"],
            sourceId=row["source_id"], sourceText=source_text, eventAtUtc=_parse_time(row["event_at_utc"]),
            recordedAtUtc=_parse_time(row["recorded_at_utc"]), validUntilUtc=_parse_time(row["valid_until_utc"]),
            freshUntilUtc=_parse_time(row["fresh_until_utc"]), status=row["status"], supersedesId=row["supersedes_id"],
        )

    def list_page(self, cursor: str, limit: int, *, include_superseded: bool = False) -> MemoryPage:
        limit = max(1, min(int(limit), MAX_PAGE_SIZE))
        marker = _decode_cursor(cursor)
        status_clause = "" if include_superseded else "AND status='active'"
        with self._lock, self._connect() as connection:
            if marker:
                rows = connection.execute(f"""SELECT * FROM memories WHERE player_id=? {status_clause}
                    AND (recorded_at_utc,memory_id)>(?,?) ORDER BY recorded_at_utc,memory_id LIMIT ?""",
                                          (self._player_id, marker[0], marker[1], limit + 1)).fetchall()
            else:
                rows = connection.execute(f"SELECT * FROM memories WHERE player_id=? {status_clause} ORDER BY recorded_at_utc,memory_id LIMIT ?",
                                          (self._player_id, limit + 1)).fetchall()
            has_more = len(rows) > limit
            rows = rows[:limit]
            items = tuple(self._record(connection, row) for row in rows)
            next_cursor = _encode_cursor(rows[-1]["recorded_at_utc"], rows[-1]["memory_id"]) if rows else ""
            return MemoryPage(status="ok", items=items, nextCursor=next_cursor, hasMore=has_more)

    def _index_memory(self, connection: sqlite3.Connection, memory_id: str, player_id: str,
                      value: str, source_text: str) -> None:
        if self._fts_enabled:
            connection.execute("DELETE FROM memory_fts WHERE memory_id=?", (memory_id,))
            connection.execute("INSERT INTO memory_fts(memory_id,player_id,value,source_text) VALUES(?,?,?,?)",
                               (memory_id, player_id, value, source_text))

    def _delete_index(self, connection: sqlite3.Connection, memory_id: str) -> None:
        if self._fts_enabled:
            connection.execute("DELETE FROM memory_fts WHERE memory_id=?", (memory_id,))


def _utc(value: datetime) -> datetime:
    if value.tzinfo is None or value.utcoffset() is None or value.utcoffset().total_seconds() != 0:
        raise ValueError("timestamps must be timezone-aware UTC")
    return value.astimezone(timezone.utc)


def _stamp(value: datetime | None) -> str | None:
    return _utc(value).isoformat(timespec="microseconds").replace("+00:00", "Z") if value else None


def _parse_time(value: str | None) -> datetime | None:
    return datetime.fromisoformat(value.replace("Z", "+00:00")) if value else None


def _hash(value: dict[str, object]) -> str:
    payload = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def _search_terms(query: str) -> tuple[str, ...]:
    if not query:
        return ()
    return tuple(dict.fromkeys(term.casefold() for term in TOKEN_PATTERN.split(query) if len(term) > 1))[:12]


def _escape_like(value: str) -> str:
    return value.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")


def _relevance(row: sqlite3.Row, terms: tuple[str, ...]) -> tuple[int, str, str]:
    value = (row["value"] + " " + row["source_text"]).casefold()
    return (sum(term in value for term in terms), row["event_at_utc"] or row["recorded_at_utc"], row["memory_id"])


def _encode_cursor(timestamp: str, memory_id: str) -> str:
    value = json.dumps([timestamp, memory_id], separators=(",", ":")).encode("utf-8")
    return base64.urlsafe_b64encode(value).decode("ascii").rstrip("=")


def _decode_cursor(cursor: str) -> tuple[str, str] | None:
    if not cursor:
        return None
    try:
        value = json.loads(base64.urlsafe_b64decode(cursor + "=" * (-len(cursor) % 4)))
        if not isinstance(value, list) or len(value) != 2 or not all(isinstance(item, str) for item in value):
            raise ValueError
        return value[0], value[1]
    except (ValueError, TypeError, json.JSONDecodeError):
        raise ValueError("invalid page cursor") from None
