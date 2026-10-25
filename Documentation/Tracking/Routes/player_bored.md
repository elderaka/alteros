---
title: "Route player_bored"
route_id: player_bored
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_bored

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_bored/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]], [[Semantics/Topics/state/bored]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_bored_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_bored/player_bored_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_bored/player_bored_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_bored / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `6d43a52410949368a7496f9d357cc2e1669bc9fb88d9c8c05b8b8c875df56a14` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am bored
- i'm bored
- bored
- so bored
- i feel bored

### generic.player_bored_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_bored/player_bored_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_bored/player_bored_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_bored / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `56040ae670d862c2362e5673432b515414d65a0455fb14f2670bf98e85a6589e` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- nothing to do
- i have nothing to do
- there is nothing to do
- i need something to do
- what should i do

### generic.player_bored_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_bored/player_bored_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_bored/player_bored_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_bored / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `15ee9692af06b5bf8e412a4de078daccb0c2db932b2c18c5bbe763a957e0ebff` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- entertain me
- i need entertainment
- make me less bored
- i am restless
- i'm restless

### generic.player_bored_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_bored/player_bored_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_bored/player_bored_004.json` |
| Source / schema | generic / 1 |
| Route / priority | player_bored / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `c62a8879fb6eb6d8df5da7d5d9856bc4575b1c0d456023588d9b01b481f6617e` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i need a tiny idea
- give me a tiny idea
- tiny idea please
- small idea
- something small to do
