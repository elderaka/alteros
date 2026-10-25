from __future__ import annotations

from datetime import datetime, timedelta, timezone

from alteros_companion.memory_contracts import MemoryEvidence, MemoryProposal
from alteros_companion.memory_repository import MemoryRepository


def test_synthetic_onboarding_correction_expiry_forget_and_source_replay_lifecycle(tmp_path):
    now = datetime(2026, 10, 18, 12, 0, tzinfo=timezone.utc)
    repo = MemoryRepository(tmp_path / "Memory" / "player-memory.sqlite3", "fixture-player")
    consent = repo.set_consent(True, expected_revision=0, now_utc=now).consent

    def save(source_id, text, category, key, value, *, scope="real_world_claim", kind="profile_input",
             actor="player", confirmed=False):
        evidence = MemoryEvidence(playerId="fixture-player", sessionId="fixture-session", sourceId=source_id,
            actor=actor, scope=scope, sourceKind=kind, text=text, eventAtUtc=now, explicitlyConfirmed=confirmed)
        proposal = MemoryProposal(category=category, key=key, value=value, scope=scope,
            sourceId=source_id, evidenceQuote=text)
        return evidence, repo.upsert(proposal, evidence, consent, now)

    save("name", "Call me Rei", "preferred_name", "profile.name", "Rei", kind="onboarding")
    save("interest", "I enjoy cozy games", "interest", "hobby.games", "cozy games", kind="onboarding")

    _, activity_result = save("study", "Completed a study session", "recent_activity", "activity.study",
        "studied together", scope="in_game_experience", kind="game_event", actor="game", confirmed=True)
    assert activity_result.status == "accepted"
    after_25_hours = repo.read_context("study session", consent, now + timedelta(hours=25))
    assert after_25_hours.current == ()
    assert any(item.sourceId == "study" for item in after_25_hours.recalled)

    save("tea", "I prefer tea", "interest", "drink.preference", "tea")
    _, coffee = save("coffee", "I prefer coffee now", "interest", "drink.preference", "coffee")
    assert coffee.status == "accepted"
    current_profile = repo.read_context("coffee", consent, now)
    assert any(item.value == "coffee" and item.status == "active" for item in current_profile.core)
    assert not any(item.value == "tea" for item in current_profile.core)

    _, fictional = save("roleplay", "In our story, I am a wizard who likes tea", "interest", "story.wizard",
        "fictional wizard likes tea", scope="fictional", kind="player_message")
    assert fictional.status == "accepted"
    assert not any(item.key == "story.wizard" for item in repo.read_context("wizard", consent, now).core)

    _, cancelled_result = save("cancelled-activity", "Study activity cancelled", "recent_activity",
        "activity.cancelled", "studied", scope="in_game_experience", kind="game_event", actor="game")
    assert cancelled_result.status == "rejected"

    first_ack = repo.record_question_ack("interest", "fixture-session", "question-1", now)
    duplicate_ack = repo.record_question_ack("interest", "fixture-session", "question-1", now)
    assert first_ack.status == "accepted"
    assert duplicate_ack.status == "duplicate"

    assert repo.forget(coffee.memory.memoryId, now + timedelta(minutes=1)).status == "forgotten"
    reopened = MemoryRepository(tmp_path / "Memory" / "player-memory.sqlite3", "fixture-player")
    replay = reopened.sync_sources(0, [(1, MemoryEvidence(playerId="fixture-player", sessionId="fixture-session",
        sourceId="coffee", actor="player", scope="real_world_claim", sourceKind="profile_input",
        text="I prefer coffee now", eventAtUtc=now, explicitlyConfirmed=False))], consent,
        now + timedelta(minutes=2), through_sequence=1)
    context = reopened.read_context("coffee", consent, now + timedelta(minutes=3))
    assert replay.status == "accepted" and replay.count == 0
    assert not any(item.value == "coffee" for item in context.core + context.current + context.recalled)
    assert "coffee" in context.suppressedSourceIds
