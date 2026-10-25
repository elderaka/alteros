---
title: "Route player_stressed"
route_id: player_stressed
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_stressed

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_stressed/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]], [[Semantics/Topics/state/stressed]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_stressed_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_stressed/player_stressed_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_stressed/player_stressed_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_stressed / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `94e350e405d91dd60d6cb8eb5a02b76232853d2454122fa61c58580c9146944f` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am stressed
- i'm stressed
- stressed
- so stressed
- i feel stressed

### generic.player_stressed_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_stressed/player_stressed_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_stressed/player_stressed_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_stressed / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 6 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `a453cbc443a590f34b96b76ba7d0f0d4cc68e29e3c1551c15ce65225f5529f81` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am overwhelmed
- i'm overwhelmed
- overwhelmed
- too much
- everything is too much

### generic.player_stressed_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_stressed/player_stressed_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_stressed/player_stressed_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_stressed / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `4ca5b122b91815dda2461ab48a6c2a211314a81dd7018638959ca92a08009563` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am anxious
- i'm anxious
- anxious
- i feel anxious
- i am tense

### generic.player_stressed_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_stressed/player_stressed_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_stressed/player_stressed_004.json` |
| Source / schema | generic / 1 |
| Route / priority | player_stressed / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 6 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `ba024cb4ffaf4e11efb82a0cf29920fc16089038bdb95fdc490f648f2251aa1f` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- my mind is loud
- too many thoughts
- thoughts are loud
- head is noisy
- everything is noisy
