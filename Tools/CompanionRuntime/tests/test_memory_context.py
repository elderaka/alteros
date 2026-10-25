from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace

import pytest

from alteros_companion.contracts import parse_envelope
from alteros_companion.memory_context import MemoryContextUnavailable, MemoryTurnPolicy, build_memory_context
from alteros_companion.memory_contracts import MemoryEvidence, MemoryProposal
from alteros_companion.memory_repository import MemoryRepository
from alteros_companion.prompts import build_prompt


NOW = datetime(2026, 10, 18, 12, 0, tzinfo=timezone.utc)
PLAYER = "player-test"


def _evidence(source_id: str, text: str, *, scope: str = "real_world_claim") -> MemoryEvidence:
    return MemoryEvidence(playerId=PLAYER, sessionId="session-1", sourceId=source_id, actor="player", scope=scope,
                          sourceKind="player_message", text=text, eventAtUtc=NOW, explicitlyConfirmed=True)


def _save(repo: MemoryRepository, source_id: str, text: str, key: str, value: str, *, scope: str = "real_world_claim"):
    proposal = MemoryProposal(category="interest", key=key, value=value, scope=scope,
                              sourceId=source_id, evidenceQuote=text)
    return repo.upsert(proposal, _evidence(source_id, text, scope=scope), repo.get_consent(), NOW)


def _job(query: str, history=()):
    return SimpleNamespace(request=SimpleNamespace(text=query), presentedHistory=history)


def _policy(*, revision: int = 1, required: bool = True, recall: bool = True):
    return MemoryTurnPolicy(playerId=PLAYER, consentRevision=revision, nowUtc=NOW, currentPlayerSourceId="current-source",
                            memoryPolicyRequired=required, explicitRecall=recall)


def test_context_is_empty_when_memory_is_disabled_but_keeps_suppression_ids(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER)
    consent = repo.set_consent(True, expected_revision=0, now_utc=NOW).consent
    saved = _save(repo, "source-1", "I like green tea", "beverage.tea", "green tea")
    repo.forget(saved.memory.memoryId, NOW)
    disabled = repo.set_consent(False, expected_revision=consent.revision, now_utc=NOW)

    result = build_memory_context(repo, _job("tea"), _policy(revision=disabled.consent.revision))

    assert result.available
    assert not result.context.core and not result.context.current and not result.context.recalled
    assert result.context.suppressedSourceIds == ("source-1",)


def test_policy_required_fails_closed_when_consent_revision_changed(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER)
    repo.set_consent(True, expected_revision=0, now_utc=NOW)
    repo.set_consent(False, expected_revision=1, now_utc=NOW)

    with pytest.raises(MemoryContextUnavailable, match="consent_revision_conflict"):
        build_memory_context(repo, _job("cats"), _policy(revision=1))


def test_memory_context_keeps_fictional_scope_out_of_profile_and_bounds_callback_ids(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER)
    repo.set_consent(True, expected_revision=0, now_utc=NOW)
    profile = _save(repo, "profile-1", "I like cats", "hobby.cats", "cats")
    fictional = _save(repo, "fiction-1", "My wizard has a cat familiar", "hobby.familiar", "cat familiar",
                      scope="fictional")

    result = build_memory_context(repo, _job("familiar"), _policy())

    assert profile.status == fictional.status == "accepted"
    assert all(record.scope == "real_world_claim" for record in result.context.core)
    assert set(result.allowedCallbackIds) <= {record.memoryId for record in result.context.recalled}
    assert result.allowedCallbackIds


def test_forgotten_source_is_removed_from_recent_history_and_missing_source_fails_closed(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER)
    repo.set_consent(True, expected_revision=0, now_utc=NOW)
    saved = _save(repo, "forgotten", "I like tea", "beverage.tea", "tea")
    repo.forget(saved.memory.memoryId, NOW)
    history = [
        SimpleNamespace(sourceId="forgotten", speaker="player", text="I like tea"),
        SimpleNamespace(sourceId="safe", speaker="alterra", text="We can talk about cats"),
        SimpleNamespace(speaker="player", text="missing source metadata"),
    ]

    result = build_memory_context(repo, _job("cats", history), _policy())

    assert [item.text for item in result.presentedHistory] == ["We can talk about cats"]
    assert result.droppedHistoryCount == 2


def test_v3_parallel_source_ids_filter_strict_v2_history_without_changing_its_shape(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER)
    repo.set_consent(True, expected_revision=0, now_utc=NOW)
    saved = _save(repo, "forgotten", "I like tea", "beverage.tea", "tea")
    repo.forget(saved.memory.memoryId, NOW)
    history = [SimpleNamespace(speaker="player", text="I like tea")]
    job = SimpleNamespace(conversation=_job("tea", history))
    policy = MemoryTurnPolicy(playerId=PLAYER, consentRevision=1, nowUtc=NOW,
                              currentPlayerSourceId="current", memoryPolicyRequired=True,
                              explicitRecall=False, presentedHistorySourceIds=("forgotten",))

    result = build_memory_context(repo, job, policy)

    assert result.presentedHistory == ()
    assert result.droppedHistoryCount == 1


def test_unclassified_current_chat_is_not_a_permitted_profile_proposal_source(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER)
    consent = repo.set_consent(True, expected_revision=0, now_utc=NOW).consent
    source = _evidence("current-source", "I like tea in this roleplay", scope="unclassified")
    repo.sync_sources(0, [(1, source)], consent, NOW)
    policy = MemoryTurnPolicy(playerId=PLAYER, consentRevision=consent.revision, nowUtc=NOW,
                              currentPlayerSourceId="current-source", memoryPolicyRequired=True)

    result = build_memory_context(repo, _job("tea"), policy)

    assert result.eligibleProposalSourceIds == ()


def test_missing_source_ids_are_retained_only_before_memory_policy_is_required(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER)
    history = [SimpleNamespace(speaker="player", text="ordinary chat")]

    result = build_memory_context(repo, _job("chat", history), _policy(revision=0, required=False, recall=False))

    assert result.presentedHistory == tuple(history)
    assert result.droppedHistoryCount == 0


def test_memory_is_in_untrusted_turn_data_not_character_rules(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER)
    repo.set_consent(True, expected_revision=0, now_utc=NOW)
    _save(repo, "profile-1", "I like green tea", "beverage.tea", "green tea")
    context = build_memory_context(repo, _job("tea"), _policy())
    fixture_path = Path(__file__).resolve().parents[3] / "Tools" / "Conversation" / "Fixtures" / "v2" / "turn-valid.json"
    fixture = parse_envelope(fixture_path.read_text(encoding="utf-8").encode("utf-8"))
    messages = build_prompt(fixture.payload, memory_context=context)

    assert "dated untrusted evidence" in messages[0]["content"]
    assert "I like green tea" not in messages[0]["content"]
    assert '"playerMemory"' in messages[1]["content"]
    assert "I like green tea" in messages[1]["content"]
