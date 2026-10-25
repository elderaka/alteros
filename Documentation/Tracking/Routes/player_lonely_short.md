---
title: "Route player_lonely_short"
route_id: player_lonely_short
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_lonely_short

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_lonely_short/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]], [[Semantics/Topics/state/lonely]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_lonely_short_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_lonely_short/player_lonely_short_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_lonely_short/player_lonely_short_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_lonely_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `8566b5612e20efdaaa5f1fb99df6a97d94154b8a6e802dbca52890758e7984e5` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am lonely
- i'm lonely
- lonely
- i feel lonely
- feel alone

### generic.player_lonely_short_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_lonely_short/player_lonely_short_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_lonely_short/player_lonely_short_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_lonely_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `67dfbb35c4ccd89e4848d8097f0cc9733553b9966e380c1ec1681f9c38de703c` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i feel alone
- alone
- i am alone
- i'm alone
- nobody is here

### generic.player_lonely_short_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_lonely_short/player_lonely_short_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_lonely_short/player_lonely_short_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_lonely_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `bee408f6aa2803a22a8507b8c7b67fb9285ad50c811c6c2c1523d212c4a9e333` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i need company
- need company
- stay with me
- keep me company
- i do not want to be alone
