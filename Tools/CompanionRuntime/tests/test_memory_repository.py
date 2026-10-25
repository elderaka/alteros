from __future__ import annotations

from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sqlite3

import pytest

from alteros_companion.memory_contracts import MemoryConsent, MemoryEvidence, MemoryProposal
from alteros_companion.memory_repository import MemoryRepository, MemoryStorageError


PLAYER = "player-test"
NOW = datetime(2026, 10, 18, 12, 0, tzinfo=timezone.utc)


def test_authored_first_meeting_has_skippable_bilingual_allowlisted_steps():
    path = Path(__file__).resolve().parents[3] / "Assets" / "StreamingAssets" / "Dialogue" / "Onboarding" / "first_meeting.json"
    catalogue = json.loads(path.read_text(encoding="utf-8"))

    assert catalogue["schemaVersion"] == 1
    assert catalogue["id"] == "onboarding.first_meeting"
    assert [step["stepId"] for step in catalogue["steps"]] == [
        "consent", "preferred_name", "interest", "shared_activity_preference"]
    assert all(step["speechText"].strip() and step["displayText"].strip() for step in catalogue["steps"])
    assert {choice["choiceId"] for choice in catalogue["steps"][0]["choices"]} == {"allow", "skip"}


def enabled_repo(path):
    repo = MemoryRepository(path, PLAYER)
    consent = repo.set_consent(True, expected_revision=0, now_utc=NOW)
    assert consent.status == "accepted"
    return repo, consent.consent


def evidence(source_id, text, *, actor="player", scope="real_world_claim", kind="player_message",
             event_at=NOW, confirmed=False, player_id=PLAYER, session_id=""):
    return MemoryEvidence(
        playerId=player_id, sessionId=session_id, sourceId=source_id, actor=actor, scope=scope, sourceKind=kind,
        text=text, eventAtUtc=event_at, explicitlyConfirmed=confirmed,
    )


def proposal(source_id, quote, *, category="interest", key="hobby.cats", value="cats"):
    return MemoryProposal(category=category, key=key, value=value, scope="real_world_claim",
                          sourceId=source_id, evidenceQuote=quote)


def test_memory_roundtrips_across_repository_restart(tmp_path):
    path = tmp_path / "memory.sqlite3"
    repo, consent = enabled_repo(path)
    result = repo.upsert(proposal("msg-1", "I like cats"), evidence("msg-1", "I like cats"), consent, NOW)
    assert result.status == "accepted", result.reasonCode
    memory_id = result.memory.memoryId

    reopened = MemoryRepository(path, PLAYER)
    page = reopened.list_page("", 20)
    assert page.status == "ok"
    assert [item.memoryId for item in page.items] == [memory_id]
    assert page.items[0].value == "cats"


def test_same_source_proposal_is_idempotent_after_replay(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("msg-1", "I like cats")
    item = proposal("msg-1", "I like cats")

    first = repo.upsert(item, source, consent, NOW)
    replay = repo.upsert(item, source, consent, NOW + timedelta(minutes=1))

    assert first.status == "accepted"
    assert replay.status == "duplicate"
    assert len(repo.list_page("", 20).items) == 1


@pytest.mark.parametrize("actor,scope,kind", [
    ("assistant", "real_world_claim", "player_message"),
    ("third_party", "real_world_claim", "player_message"),
    ("game", "real_world_claim", "game_event"),
])
def test_non_player_or_wrong_scope_source_cannot_create_profile_fact(tmp_path, actor, scope, kind):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("source", "I like cats", actor=actor, scope=scope, kind=kind)
    result = repo.upsert(proposal("source", "I like cats"), source, consent, NOW)
    assert result.status == "rejected"
    assert result.reasonCode == "source_not_authoritative"
    assert repo.list_page("", 20).items == ()


def test_game_event_can_support_only_a_scoped_recent_activity(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("event-1", "Completed chess activity", actor="game", scope="in_game_experience",
                      kind="game_event", confirmed=True)
    proposal_as_interest = MemoryProposal(category="interest", key="hobby.chess", value="chess",
                                          scope="in_game_experience", sourceId="event-1", evidenceQuote="chess")
    result = repo.upsert(proposal_as_interest, source, consent, NOW)
    assert result.status == "rejected"
    assert result.reasonCode == "source_scope_mismatch"


def test_correction_supersedes_only_matching_preference_key(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    first_source = evidence("msg-1", "I like coffee with milk")
    first = repo.upsert(
        proposal("msg-1", "I like coffee with milk", category="interest", key="beverage.coffee", value="coffee with milk"),
        first_source, consent, NOW,
    )
    other = repo.upsert(
        proposal("msg-2", "I love tea too", category="interest", key="beverage.tea", value="tea"),
        evidence("msg-2", "I love tea too"), consent, NOW,
    )
    corrected = repo.upsert(
        proposal("msg-3", "Actually, black coffee", category="interest", key="beverage.coffee", value="black coffee"),
        evidence("msg-3", "Actually, black coffee"), consent, NOW + timedelta(days=1),
    )

    records = {item.memoryId: item for item in repo.list_page("", 20, include_superseded=True).items}
    assert first.status == other.status == corrected.status == "accepted"
    assert records[first.memory.memoryId].status == "superseded"
    assert corrected.memory.supersedesId == first.memory.memoryId
    assert records[other.memory.memoryId].status == "active"
    assert corrected.memory.value == "black coffee"


def test_multiple_interests_are_preserved_instead_of_replacing_profile(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    for source_id, key, value, quote in [
        ("m1", "hobby.cats", "cats", "I like cats"),
        ("m2", "hobby.books", "books", "I like books"),
        ("m3", "hobby.gardening", "gardening", "I like gardening"),
    ]:
        assert repo.upsert(proposal(source_id, quote, key=key, value=value),
                           evidence(source_id, quote), consent, NOW).status == "accepted"
    values = {item.value for item in repo.list_page("", 20).items}
    assert values == {"cats", "books", "gardening"}


def test_recent_activity_is_current_before_24h_then_historical_only(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    occurred = NOW - timedelta(hours=24)
    source = evidence("event-1", "We played chess together", actor="game", scope="in_game_experience",
                      kind="game_event", event_at=occurred, confirmed=True)
    item = MemoryProposal(category="recent_activity", key="activity.chess", value="played chess",
                          scope="in_game_experience", sourceId="event-1", evidenceQuote="played chess")
    result = repo.upsert(item, source, consent, NOW)
    assert result.status == "accepted", result.reasonCode

    before = repo.read_context("chess", consent, occurred + timedelta(hours=24) - timedelta(microseconds=1))
    boundary = repo.read_context("chess", consent, occurred + timedelta(hours=24))
    assert len(before.current) == 1
    assert boundary.current == ()
    assert len(boundary.recalled) == 1
    assert boundary.recalled[0].memoryId == result.memory.memoryId


def test_temporary_state_expires_after_two_hours_but_remains_recallable(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("checkin-1", "I feel tired today", event_at=NOW)
    item = MemoryProposal(category="temporary_state", key="state.energy", value="tired",
                          scope="real_world_claim", sourceId="checkin-1", evidenceQuote="tired")
    result = repo.upsert(item, source, consent, NOW)
    assert result.status == "accepted", result.reasonCode

    at_boundary = repo.read_context("tired", consent, NOW + timedelta(hours=2))
    assert at_boundary.current == ()
    assert [record.memoryId for record in at_boundary.recalled] == [result.memory.memoryId]


def test_retrieval_does_not_refresh_event_or_record_times(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    event_at = NOW - timedelta(hours=1)
    source = evidence("event-1", "We played chess", actor="game", scope="in_game_experience",
                      kind="game_event", event_at=event_at, confirmed=True)
    item = MemoryProposal(category="recent_activity", key="activity.chess", value="chess",
                          scope="in_game_experience", sourceId="event-1", evidenceQuote="chess")
    stored = repo.upsert(item, source, consent, NOW)
    first = repo.read_context("chess", consent, NOW + timedelta(hours=25)).recalled[0]
    second = repo.read_context("chess", consent, NOW + timedelta(days=3)).recalled[0]
    assert first.eventAtUtc == second.eventAtUtc == event_at
    assert first.recordedAtUtc == second.recordedAtUtc == stored.memory.recordedAtUtc
    assert first.validUntilUtc == second.validUntilUtc


def test_memory_off_suppresses_context_but_still_returns_forgotten_source_ids(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    stored = repo.upsert(proposal("msg-1", "I like cats"), evidence("msg-1", "I like cats"), consent, NOW)
    assert repo.forget(stored.memory.memoryId, NOW).status == "forgotten"
    disabled = repo.set_consent(False, expected_revision=consent.revision, now_utc=NOW + timedelta(minutes=1))

    context = repo.read_context("cats", disabled.consent, NOW + timedelta(minutes=2))
    assert context.core == context.current == context.recalled == ()
    assert context.suppressedSourceIds == ("msg-1",)


def test_unconfirmed_routine_stays_pending_until_player_confirmation(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    item = MemoryProposal(category="routine", key="morning.drink", value="tea",
                          scope="real_world_claim", sourceId="msg-1", evidenceQuote="I drink tea every morning")
    source = evidence("msg-1", "I drink tea every morning")

    pending = repo.upsert(item, source, consent, NOW)
    assert pending.status == "pending_confirmation"
    assert repo.list_page("", 20).items == ()

    confirmation = evidence("confirm-1", "Yes, remember that routine", kind="player_confirmation", confirmed=True)
    confirmed = repo.confirm(item, source, confirmation, consent, NOW + timedelta(minutes=1))
    assert confirmed.status == "accepted"
    assert confirmed.memory.value == "tea"


def test_quote_must_be_present_in_trusted_source(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    result = repo.upsert(proposal("msg-1", "I hate dogs"), evidence("msg-1", "I love dogs"), consent, NOW)
    assert result.status == "rejected"
    assert result.reasonCode == "evidence_quote_missing"


def test_wrong_player_source_and_wrong_consent_revision_are_rejected(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    other_player = evidence("msg-1", "I like cats", player_id="player-other")
    wrong_owner = repo.upsert(proposal("msg-1", "I like cats"), other_player, consent, NOW)
    newer_consent = MemoryConsent(playerId=PLAYER, enabled=True, revision=consent.revision + 1)
    stale = repo.upsert(proposal("msg-2", "I like books", key="hobby.books", value="books"),
                        evidence("msg-2", "I like books"), newer_consent, NOW)
    assert wrong_owner.reasonCode == "wrong_player"
    assert stale.reasonCode == "consent_revision_conflict"
    assert repo.list_page("", 20).items == ()


def test_forgotten_source_replay_cannot_recreate_memory(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    stored = repo.upsert(proposal("msg-1", "I like cats"), evidence("msg-1", "I like cats"), consent, NOW)
    repo.forget(stored.memory.memoryId, NOW + timedelta(minutes=1))

    replay = repo.upsert(proposal("msg-1", "I like cats"), evidence("msg-1", "I like cats"), consent, NOW + timedelta(minutes=2))
    assert replay.status == "rejected"
    assert replay.reasonCode == "source_suppressed"
    assert repo.list_page("", 20).items == ()


def test_source_conflict_is_not_silently_overwritten(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    original = evidence("msg-1", "I like cats")
    repo.upsert(proposal("msg-1", "I like cats"), original, consent, NOW)
    changed = evidence("msg-1", "I hate cats")
    conflict = repo.upsert(proposal("msg-1", "I hate cats", value="dislike cats"), changed, consent, NOW)
    assert conflict.status == "rejected"
    assert conflict.reasonCode == "source_conflict"


def test_paging_is_bounded_and_cursor_stable(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    for index in range(7):
        text = f"I enjoy hobby {index}"
        repo.upsert(proposal(f"m{index}", text, key=f"hobby.{index}", value=f"hobby {index}"),
                    evidence(f"m{index}", text), consent, NOW + timedelta(seconds=index))
    first = repo.list_page("", 3)
    second = repo.list_page(first.nextCursor, 3)
    third = repo.list_page(second.nextCursor, 3)
    assert len(first.items) == 3 and first.hasMore
    assert len(second.items) == 3 and second.hasMore
    assert len(third.items) == 1 and not third.hasMore
    assert len({item.memoryId for item in first.items + second.items + third.items}) == 7


def test_memory_context_respects_item_and_combined_character_budgets(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    long_value = "hobby memory " + ("quiet words " * 34)
    for index in range(8):
        source_id = f"profile-{index}"
        text = f"I like hobby {index} " + ("quiet words " * 30)
        repo.upsert(MemoryProposal(category="interest", key=f"hobby.{index}", value=long_value,
            scope="real_world_claim", sourceId=source_id, evidenceQuote=text),
            evidence(source_id, text), consent, NOW + timedelta(seconds=index))
    for index in range(8):
        source_id = f"activity-{index}"
        text = f"Completed study activity {index} " + ("quiet words " * 30)
        repo.upsert(MemoryProposal(category="recent_activity", key=f"activity.{index}", value=long_value,
            scope="in_game_experience", sourceId=source_id, evidenceQuote=text),
            evidence(source_id, text, actor="game", scope="in_game_experience", kind="game_event", confirmed=True),
            consent, NOW + timedelta(minutes=index))

    context = repo.read_context("study activity hobby", consent, NOW + timedelta(minutes=20))
    groups = (context.core, context.current, context.recalled)
    total_characters = sum(len(item.value) + len(item.sourceText) for group in groups for item in group)

    assert len(context.core) <= 4
    assert len(context.current) <= 3
    assert len(context.recalled) <= 3
    assert total_characters <= 3000


def test_search_values_are_parameterized_and_percent_is_literal_in_like_fallback(tmp_path):
    repo = MemoryRepository(tmp_path / "memory.sqlite3", PLAYER, enable_fts5=False)
    consent = repo.set_consent(True, expected_revision=0, now_utc=NOW).consent
    source = evidence("m1", "We played 100% chess", actor="game", scope="in_game_experience",
                      kind="game_event", confirmed=True)
    item = MemoryProposal(category="recent_activity", key="activity.chess", value="100% chess",
                          scope="in_game_experience", sourceId="m1", evidenceQuote="100% chess")
    repo.upsert(item, source, consent, NOW)
    historical_time = NOW + timedelta(hours=25)
    assert len(repo.read_context("100%", consent, historical_time).recalled) == 1
    assert repo.read_context("% OR 1=1 --", consent, historical_time).recalled == ()


def test_corrupt_database_is_not_reinitialized_or_overwritten(tmp_path):
    path = tmp_path / "memory.sqlite3"
    damaged = b"not a sqlite database; keep original bytes"
    path.write_bytes(damaged)
    with pytest.raises(MemoryStorageError) as error:
        MemoryRepository(path, PLAYER)
    assert error.value.code == "memory_unavailable"
    assert path.read_bytes() == damaged


def test_zero_is_an_explicit_value_and_missing_record_stays_unknown(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    item = MemoryProposal(category="active_goal", key="goal.daily_minutes", value="0",
                          scope="real_world_claim", sourceId="m1", evidenceQuote="I want zero minutes")
    repo.upsert(item, evidence("m1", "I want zero minutes"), consent, NOW)
    context = repo.read_context("unrelated", consent, NOW)
    assert [record.value for record in context.core] == ["0"]
    empty = MemoryRepository(tmp_path / "empty.sqlite3", PLAYER).read_context("goal", consent, NOW)
    assert empty.core == empty.current == empty.recalled == ()


def test_source_sync_acknowledges_one_bounded_page_atomically_and_refuses_stale_cursor(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    synced = repo.sync_sources(0, [(1, evidence("source-1", "I like green tea"))], consent, NOW)
    stale = repo.sync_sources(0, [(1, evidence("source-1", "I like green tea"))], consent, NOW)
    assert synced.status == "accepted"
    assert synced.lastSequence == 1
    assert synced.count == 1
    assert stale.reasonCode == "source_sequence_conflict"


def test_source_sync_empty_zero_cursor_reads_checkpoint_for_resume(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    repo.sync_sources(0, [(1, evidence("source-1", "I like tea"))], consent, NOW)
    checkpoint = repo.sync_sources(0, [], consent, NOW)
    assert checkpoint.status == "accepted"
    assert checkpoint.lastSequence == 1
    assert checkpoint.count == 0


def test_source_sync_initializes_checkpoint_without_backfill_and_advances_across_noncontent_rows(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    initialized = repo.sync_sources(0, [], consent, NOW, through_sequence=12)
    replay_initialize = repo.sync_sources(0, [], consent, NOW, through_sequence=99)
    source = evidence("new-message", "I like tea", session_id="session-1")
    page = repo.sync_sources(12, [(15, source)], consent, NOW, through_sequence=20)

    assert initialized.status == "accepted" and initialized.lastSequence == 12
    assert replay_initialize.lastSequence == 12
    assert page.status == "accepted" and page.lastSequence == 20 and page.count == 1


def test_source_sync_rejects_checkpoint_before_last_included_event(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    result = repo.sync_sources(0, [(4, evidence("source-4", "I like tea"))], consent, NOW,
                              through_sequence=3)
    assert result.status == "rejected"
    assert result.reasonCode == "source_sequence_conflict"


def test_source_sync_allows_gaps_for_noncontent_transcript_rows(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("node-3", "I like green tea", session_id="session-1")
    synced = repo.sync_sources(0, [(3, source)], consent, NOW)
    assert synced.status == "accepted"
    assert synced.lastSequence == 3


def test_unclassified_synced_chat_cannot_be_promoted_to_a_real_world_profile_fact(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("chat-1", "I like tea, in this fictional scene", scope="unclassified")

    synced = repo.sync_sources(0, [(1, source)], consent, NOW)
    proposal_result = repo.upsert(proposal("chat-1", "I like tea", key="beverage.tea", value="tea"),
                                  source, consent, NOW)

    assert synced.status == "accepted"
    assert synced.count == 1
    assert proposal_result.status == "pending_confirmation"
    assert proposal_result.reasonCode == "scope_confirmation_required"
    assert repo.list_page("", 10).items == ()


def test_unclassified_chat_requires_explicit_player_confirmation_before_profile_memory(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("chat-1", "I like tea", scope="unclassified")
    candidate = proposal("chat-1", "I like tea", key="beverage.tea", value="tea")
    pending = repo.upsert(candidate, source, consent, NOW)
    confirmation = evidence("confirmation-1", "Yes, please remember that I like tea", kind="player_confirmation",
                            confirmed=True, scope="real_world_claim", event_at=NOW + timedelta(minutes=1))

    accepted = repo.confirm(candidate, source, confirmation, consent, NOW + timedelta(minutes=1))

    assert pending.status == "pending_confirmation"
    assert pending.reasonCode == "scope_confirmation_required"
    assert accepted.status == "accepted"
    assert accepted.memory.scope == "real_world_claim"
    assert accepted.memory.sourceId == "chat-1"


def test_source_presence_lookup_is_player_bound_and_does_not_refresh_memory_freshness(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("current-source", "I had a strange day", scope="unclassified")
    repo.sync_sources(0, [(1, source)], consent, NOW)

    assert repo.has_source("current-source") is True
    assert repo.has_source("another-player-source") is False


def test_source_evidence_lookup_preserves_unclassified_scope_and_actor(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("chat-source", "My wizard likes tea", scope="unclassified", session_id="session-1")
    repo.sync_sources(0, [(1, source)], consent, NOW)

    restored = repo.get_source_evidence("chat-source")

    assert restored == source
    assert restored.scope == "unclassified"


def test_schema_v1_database_adds_source_cursor_without_rebuilding_memory_tables(tmp_path):
    path = tmp_path / "memory.sqlite3"
    repo, consent = enabled_repo(path)
    saved = repo.upsert(proposal("source-1", "I like green tea", key="beverage.tea", value="green tea"),
                        evidence("source-1", "I like green tea"), consent, NOW)
    with sqlite3.connect(path) as connection:
        connection.execute("DROP TABLE source_sync_state")
        connection.execute("ALTER TABLE source_events DROP COLUMN session_id")
        connection.execute("PRAGMA user_version=1")
    reopened = MemoryRepository(path, PLAYER)
    assert reopened.list_page("", 10).items[0].memoryId == saved.memory.memoryId
    assert reopened.sync_sources(0, [], consent, NOW).lastSequence == 0


def test_source_sync_after_forget_never_restores_source_excerpt(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    source = evidence("source-1", "I like green tea")
    stored = repo.upsert(proposal("source-1", "I like green tea"), source, consent, NOW)
    repo.forget(stored.memory.memoryId, NOW + timedelta(minutes=1))

    replay = repo.sync_sources(0, [(1, source)], consent, NOW + timedelta(minutes=2))
    assert replay.status == "accepted"
    assert replay.count == 0
    assert "source-1" in repo.read_context("tea", consent, NOW + timedelta(minutes=3)).suppressedSourceIds
    assert repo.upsert(proposal("source-1", "I like green tea"), source, consent, NOW + timedelta(minutes=4)).reasonCode == "source_suppressed"


def test_onboarding_progress_and_question_ack_are_idempotent_host_records(tmp_path):
    repo, consent = enabled_repo(tmp_path / "memory.sqlite3")
    first = repo.save_onboarding_progress("preferred_name", False, NOW)
    same = repo.save_onboarding_progress("preferred_name", False, NOW + timedelta(minutes=1))
    question = repo.record_question_ack("interest", "session-1", "event-1", NOW)
    duplicate = repo.record_question_ack("interest", "session-1", "event-1", NOW + timedelta(minutes=1))
    assert first.status == same.status == "accepted"
    assert first.stepId == "preferred_name"
    assert first.completed is False
    assert question.status == "accepted"
    assert duplicate.status == "duplicate"


def test_onboarding_progress_can_be_read_after_repository_restart_without_advancing(tmp_path):
    repo = MemoryRepository(tmp_path / "Memory" / "player-memory.sqlite3", "player-test")
    repo.save_onboarding_progress("interest", False, NOW)

    reopened = MemoryRepository(tmp_path / "Memory" / "player-memory.sqlite3", "player-test")
    progress = reopened.get_onboarding_progress()

    assert progress.status == "accepted"
    assert progress.stepId == "interest"
    assert progress.completed is False
    assert reopened.get_onboarding_progress().stepId == "interest"
