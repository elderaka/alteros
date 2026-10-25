---
title: "Route player_tired"
route_id: player_tired
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_tired

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_tired/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]], [[Semantics/Topics/state/tired]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_tired_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_tired/player_tired_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_tired/player_tired_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_tired / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `1a7e0bdb096bfd83cac57b2d3968e18e6e1c880846342f96f4b0306c0f82f10b` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am tired
- i'm tired
- tired
- so tired
- i feel tired

### generic.player_tired_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_tired/player_tired_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_tired/player_tired_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_tired / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `57298b280532ac221231bf12d2d238c2442d170fabd79f886c19ea404b47f9b7` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am exhausted
- i'm exhausted
- exhausted
- i am sleepy
- sleepy

### generic.player_tired_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_tired/player_tired_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_tired/player_tired_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_tired / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `9247afae6251629342562cf78178b221b5c5323486bb80fa61fde69762918866` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i have no energy
- no energy
- i am drained
- i'm drained
- drained

### generic.player_tired_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_tired/player_tired_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_tired/player_tired_004.json` |
| Source / schema | generic / 1 |
| Route / priority | player_tired / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 5 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `3c3290f25f269849d0961e2c6ad1f018ee323eff253236803ab730b9ee735605` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- my brain is tired
- brain tired
- mentally tired
- my head is tired
- too tired to think
