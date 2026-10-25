---
title: "Route goodnight"
route_id: goodnight
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: goodnight

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/goodnight/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/farewell]], [[Semantics/Topics/activity/sleep]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.goodnight_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/goodnight/goodnight_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/goodnight/goodnight_001.json` |
| Source / schema | generic / 1 |
| Route / priority | goodnight / 1 |
| startHour / endHour | 20 / 6 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `0ae10635cbdbad76efe839df7780e64a98506771dabd03e2d0fa98e4ee38b194` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good night
- goodnight
- night
- sleep well

### generic.goodnight_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/goodnight/goodnight_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/goodnight/goodnight_002.json` |
| Source / schema | generic / 1 |
| Route / priority | goodnight / 1 |
| startHour / endHour | 20 / 6 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `19946ceb427553ed92ae5aecc67248baa6c182e255729823210ef681f144f101` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good night
- goodnight
- night alterra
- i am going to sleep

### generic.goodnight_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/goodnight/goodnight_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/goodnight/goodnight_003.json` |
| Source / schema | generic / 1 |
| Route / priority | goodnight / 1 |
| startHour / endHour | 20 / 6 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `b2241d474ab733efa406c38bf91613156c3115b9a503a3b29b44017319008f19` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good night
- goodnight
- gn
- sweet dreams

### generic.goodnight_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/goodnight/goodnight_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/goodnight/goodnight_004.json` |
| Source / schema | generic / 1 |
| Route / priority | goodnight / 1 |
| startHour / endHour | 20 / 6 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `25831f923251309de083dc846d90dc255f300e6ecce4343262d23f8d7c1dcd01` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- sleep time
- i will sleep now
- going to sleep now
- night for real
- really goodnight
