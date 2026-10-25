---
title: "Route greeting_latenight"
route_id: greeting_latenight
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: greeting_latenight

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/greeting_latenight/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/greet]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.greeting_latenight_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_latenight/greeting_latenight_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_latenight/greeting_latenight_001.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_latenight / 1 |
| startHour / endHour | 22 / 5 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `22ffb458c74f03fffdf9436c9a2ed8f630a6eb069fa39dad6ca62915990c3cb3` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- late night
- good late night
- hey late night

### generic.greeting_latenight_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_latenight/greeting_latenight_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_latenight/greeting_latenight_002.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_latenight / 1 |
| startHour / endHour | 22 / 5 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `2892493f97d8f1431695713a076d7062b3ae69fc86d8db949796b6a46c863536` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- late night
- still awake
- you awake

### generic.greeting_latenight_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_latenight/greeting_latenight_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_latenight/greeting_latenight_003.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_latenight / 1 |
| startHour / endHour | 22 / 5 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `153264e525ada943243c95c61d7ca1b92c956e594ffb16349d4b7e6313451e42` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- late night
- night chat
- night check in
