---
title: Player Context Evidence
tags: [alteros, design, player, sentiment]
updated: 2026-10-16
status: proposed-contract
---
# Player Context Evidence

Related: [[Stat Catalogue]], [[Stats and Mood]], [[Design Decisions]], [[Stats Foundation Implementation Plan]].

## Separate from Alterra's numerical stats

Player hidden variables were not removed from scope of the product. They are
not among the nine Alterra definitions in the first foundation slice because
inference needs provenance, uncertainty and expiry, not an authoritative float.
No runtime evidence class, classifier or Playground signal integration is
implemented by this note. "Hidden" means absent from the normal HUD, not
undisclosed profiling; permission/disclosure controls remain required.

## Proposed record for a later bounded slice

- Signal ID and actor: player-specific valence or frustration evidence, not
  Alterra's emotional axis and not legacy player stress/reliability meters.
- Optional value: missing/expired means unknown, never zero/neutral by default.
- Confidence: assessed separately; range and acceptance threshold require validation.
- Source kind/reference: submitted text observation, explicit self-report or
  synthetic fixture. Store a minimal private reference, not shared raw chat.
- Observation timestamp and expiry: UTC instants; expire independently from
  local day/night, with TTL/calibration still to be chosen.

Explicit focus/quiet/consent and pinned preferences are separate authoritative
settings. A text classifier cannot overwrite them, infer meal/sleep completion,
mint effects or decide whether an interruption is permitted. No permanent
"angry player" score or relationship penalty follows from a negative signal.

## Playground integration boundary

A future Playground extension may inject labelled synthetic evidence with
fixed evaluation time/confidence/expiry to test applicable rules. Distinguish
that from real sentiment inference. Test absent/expired/uncertain evidence and
explicit focus overriding inference. New signal data must not be accepted
through StatSnapshot.TrySet, which deliberately rejects unknown/player IDs.

Do not treat the current legacy `player.stress` field as verified emotion or
map it automatically to this contract. Model choice, languages, calibration,
retention and executable policy need their own approved implementation task.
