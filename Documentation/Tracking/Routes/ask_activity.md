---
title: "Route ask_activity"
route_id: ask_activity
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: ask_activity

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/ask_activity/`.
- 5 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/ask]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.ask_activity_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity_001.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_activity / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `ed119239b051611967e52a788d3ca1d23ee54b7ff3aec8c0650dc2a92f2522e7` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what are you doing
- what are you up to
- what are you working on

### generic.ask_activity_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity_002.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_activity / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `9d3e102f39f327ad79baf368c3c39e9e2b4ef838b3af07fc555a8f1bc96ed763` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what are you doing
- what are you up to
- what are you working on

### generic.ask_activity_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity_003.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_activity / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `8789266e9b4c951e970ca64b33f9aeffe0d08eb47f06dc2135b9def66ef1c5c5` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what are you doing
- what are you up to
- what are you working on

### generic.ask_activity_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity_004.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_activity / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `0458d1d93f4e85cb261ff1ead4960efe61226d7f4f8323eb905c06fd2835a5f8` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what were you doing
- what have you been doing
- what are you up to now
- busy with anything
- what are you thinking about now

### generic.ask_activity

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_activity/ask_activity.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_activity / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `e4e8f3dfeeaf4779c51b0fcf82308439f819726c6e17a78a93b7bbe13894d86d` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what are you doing
- what are you up to
