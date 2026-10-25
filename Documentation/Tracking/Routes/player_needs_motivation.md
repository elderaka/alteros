---
title: "Route player_needs_motivation"
route_id: player_needs_motivation
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_needs_motivation

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_needs_motivation/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/request]], [[Semantics/Topics/state/motivated]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_needs_motivation_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_needs_motivation/player_needs_motivation_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_needs_motivation/player_needs_motivation_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_needs_motivation / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `d59432b182b50c9314a161664f2b7242bcfd8403937e0d38b0cd0aea8620e706` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- motivate me
- i need motivation
- give me motivation
- encourage me
- i need encouragement

### generic.player_needs_motivation_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_needs_motivation/player_needs_motivation_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_needs_motivation/player_needs_motivation_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_needs_motivation / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `c18902f9dc97cdc885bd2c632d7c7aca2ef945799aa3cc2e20bba40c9d9fa623` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- help me start
- help me get started
- i need to start
- make me start
- push me a little

### generic.player_needs_motivation_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_needs_motivation/player_needs_motivation_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_needs_motivation/player_needs_motivation_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_needs_motivation / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `4aa77cde9a016ca70c812b49156a3ce5158627a4b7fb9dfed656b088f2e07cf5` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- hype me up
- give me a push
- i need a push
- make me productive
- help me focus
