"""Deterministic source, scope, category and freshness policy for memory."""

from __future__ import annotations

from datetime import datetime, timedelta

from .memory_contracts import MemoryEvidence, MemoryProposal


ALLOWED_CATEGORIES = frozenset({
    "preferred_name", "interest", "dislike", "interaction_preference",
    "shared_activity_preference", "boundary", "active_goal", "recent_activity",
    "temporary_state", "routine",
})
CONFIRMATION_REQUIRED = frozenset({"interaction_preference", "shared_activity_preference", "boundary", "routine"})
PLAYER_SOURCE_KINDS = frozenset({"player_message", "player_checkin", "onboarding", "profile_input", "player_confirmation"})


def validate_proposal(proposal: MemoryProposal) -> str | None:
    if proposal.category not in ALLOWED_CATEGORIES:
        return "category_not_allowed"
    if not proposal.value.strip() or not proposal.evidenceQuote.strip():
        return "empty_value"
    if proposal.category == "preferred_name" and len(proposal.value) > 80:
        return "value_too_long"
    if proposal.category != "preferred_name" and len(proposal.value) > 512:
        return "value_too_long"
    if len(proposal.key) > 80:
        return "key_too_long"
    return None


def validate_evidence(evidence: MemoryEvidence) -> str | None:
    if evidence.scope == "unclassified":
        return "source_scope_unclassified"
    if evidence.eventAtUtc is None and evidence.sourceKind not in {"profile_input", "onboarding"}:
        return "event_time_unknown"
    if evidence.actor == "game":
        if evidence.sourceKind != "game_event" or evidence.scope != "in_game_experience" or not evidence.explicitlyConfirmed:
            return "source_not_authoritative"
        return None
    if evidence.actor != "player" or evidence.sourceKind not in PLAYER_SOURCE_KINDS:
        return "source_not_authoritative"
    if evidence.scope == "in_game_experience" and evidence.sourceKind not in {"onboarding", "profile_input"}:
        return "source_scope_mismatch"
    if evidence.scope == "real_world_claim" and evidence.sourceKind == "game_event":
        return "source_scope_mismatch"
    return None


def freshness_deadline(category: str, event_at_utc: datetime | None) -> datetime | None:
    if event_at_utc is None:
        return None
    if category == "recent_activity":
        return event_at_utc + timedelta(hours=24)
    if category == "temporary_state":
        return event_at_utc + timedelta(hours=2)
    return None


def requires_confirmation(category: str, evidence: MemoryEvidence) -> bool:
    if category in CONFIRMATION_REQUIRED:
        return evidence.sourceKind not in {"profile_input", "onboarding"} and not evidence.explicitlyConfirmed
    return False

