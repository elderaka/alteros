---
title: "Route player_overworking"
route_id: player_overworking
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_overworking

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_overworking/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]], [[Semantics/Topics/activity/work]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_overworking_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_overworking/player_overworking_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_overworking/player_overworking_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_overworking / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `9ff7bfa8322c7f5715c1f67490c2488ea5d639947b26bb6b8b7f0df7694d9122` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am still working
- i'm still working
- still working
- i keep working
- working too much

### generic.player_overworking_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_overworking/player_overworking_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_overworking/player_overworking_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_overworking / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `e0384d8f66fe82e122b94c8b811f72c1f424ba949758fb17351566d10bd5f350` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i cannot stop working
- i can't stop working
- cant stop working
- i should keep working
- need to keep working

### generic.player_overworking_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_overworking/player_overworking_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_overworking/player_overworking_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_overworking / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `9be907a09fdcc8148a0375469215188d05ce7ecb7a46785c7106e4ed293a261e` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i have been working all day
- working all day
- i worked too long
- worked too long
- overworking
