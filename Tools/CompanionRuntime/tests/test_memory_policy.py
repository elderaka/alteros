from __future__ import annotations

from datetime import datetime, timedelta, timezone

import pytest
from pydantic import ValidationError

from alteros_companion.memory_contracts import MemoryEvidence, MemoryProposal
from alteros_companion.memory_policy import (
    ALLOWED_CATEGORIES,
    freshness_deadline,
    validate_evidence,
    validate_proposal,
)


NOW = datetime(2026, 10, 18, 12, 0, tzinfo=timezone.utc)


@pytest.mark.parametrize("category", ["health", "financial", "precise_location", "credential", "diagnosis"])
def test_sensitive_categories_are_not_memory_categories(category):
    item = MemoryProposal(category=category, key="x", value="secret", scope="real_world_claim",
                          sourceId="msg", evidenceQuote="secret")
    assert category not in ALLOWED_CATEGORIES
    assert validate_proposal(item) == "category_not_allowed"


def test_unknown_and_unzoned_times_fail_closed():
    assert validate_evidence(MemoryEvidence(playerId="p", sourceId="s", actor="player", scope="real_world_claim",
                                            sourceKind="player_message", text="hello", eventAtUtc=None,
                                            explicitlyConfirmed=False)) == "event_time_unknown"
    with pytest.raises(ValueError):
        MemoryEvidence(playerId="p", sourceId="s", actor="player", scope="real_world_claim",
                       sourceKind="player_message", text="hello", eventAtUtc=datetime(2026, 10, 18),
                       explicitlyConfirmed=False)


def test_expiry_is_host_derived_and_does_not_accept_model_time():
    activity = MemoryEvidence(playerId="p", sourceId="e", actor="game", scope="in_game_experience",
                              sourceKind="game_event", text="completed chess", eventAtUtc=NOW,
                              explicitlyConfirmed=True)
    temporary = MemoryEvidence(playerId="p", sourceId="s", actor="player", scope="real_world_claim",
                               sourceKind="player_checkin", text="I feel tired", eventAtUtc=NOW,
                               explicitlyConfirmed=False)
    assert freshness_deadline("recent_activity", NOW) == NOW + timedelta(hours=24)
    assert freshness_deadline("temporary_state", NOW) == NOW + timedelta(hours=2)
    assert "validUntilUtc" not in MemoryProposal.model_fields
    assert validate_evidence(activity) is None
    assert validate_evidence(temporary) is None


def test_game_scope_requires_completed_engine_event_not_model_narration():
    valid = MemoryEvidence(playerId="p", sourceId="event", actor="game", scope="in_game_experience",
                           sourceKind="game_event", text="chess completed", eventAtUtc=NOW,
                           explicitlyConfirmed=True)
    invalid = MemoryEvidence(playerId="p", sourceId="reply", actor="alterra", scope="in_game_experience",
                             sourceKind="generated_dialogue", text="we played chess", eventAtUtc=NOW,
                             explicitlyConfirmed=True)
    assert validate_evidence(valid) is None
    assert validate_evidence(invalid) == "source_not_authoritative"


def test_unclassified_player_chat_is_preserved_as_provenance_but_never_extractable():
    source = MemoryEvidence(playerId="p", sourceId="chat-1", actor="player", scope="unclassified",
                            sourceKind="player_message", text="I like tea, in this fictional scene", eventAtUtc=NOW,
                            explicitlyConfirmed=False)

    assert validate_evidence(source) == "source_scope_unclassified"


def test_preferred_name_has_tighter_limit_and_all_values_are_bounded():
    long_name = MemoryProposal(category="preferred_name", key="profile.preferred_name", value="x" * 81,
                               scope="real_world_claim", sourceId="s", evidenceQuote="x" * 81)
    assert validate_proposal(long_name) == "value_too_long"
    with pytest.raises(ValidationError):
        MemoryProposal(category="interest", key="hobby.x", value="x" * 513,
                       scope="real_world_claim", sourceId="s", evidenceQuote="x" * 513)
