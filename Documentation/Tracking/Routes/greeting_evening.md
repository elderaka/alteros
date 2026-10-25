---
title: "Route greeting_evening"
route_id: greeting_evening
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: greeting_evening

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/greeting_evening/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/greet]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.greeting_evening_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_evening/greeting_evening_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_evening/greeting_evening_001.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_evening / 1 |
| startHour / endHour | 17 / 22 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `a23abb90cd66df3e62176ee28a2b1d67d637fbfe05c1289614b9be1b7ab79562` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good evening
- evening
- good evening alterra

### generic.greeting_evening_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_evening/greeting_evening_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_evening/greeting_evening_002.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_evening / 1 |
| startHour / endHour | 17 / 22 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `5ef2afeba3d4512fb071d06e94cf3fec84bbe6150d8c48e1fe2252a8b761562c` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good evening
- evening
- evening alterra

### generic.greeting_evening_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_evening/greeting_evening_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_evening/greeting_evening_003.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_evening / 1 |
| startHour / endHour | 17 / 22 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `21666cde7115ccd3acb4724fbbf68ffba9353a6aa7ecabe883b6c6ac51a081e4` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good evening
- evening
- hey evening

### generic.greeting_evening_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_evening/greeting_evening_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_evening/greeting_evening_004.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_evening / 1 |
| startHour / endHour | 17 / 22 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `c8f6e2f1cb0a4b96468eae7932d083104d83297cf5fdf80ce11b67b108914331` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- quiet evening
- evening again
- good evening again
- blue hour
- evening check in
