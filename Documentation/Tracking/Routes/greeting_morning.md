---
title: "Route greeting_morning"
route_id: greeting_morning
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: greeting_morning

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/greeting_morning/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/greet]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.greeting_morning_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_morning/greeting_morning_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_morning/greeting_morning_001.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_morning / 1 |
| startHour / endHour | 5 / 12 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `5321047fdca3929e1a89ae25f39ad1e2803c266c21ca8e85ef73eb731384439b` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good morning
- morning
- good morning alterra

### generic.greeting_morning_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_morning/greeting_morning_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_morning/greeting_morning_002.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_morning / 1 |
| startHour / endHour | 5 / 12 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `0dbb9b69f6d4a3ff6deed5aa6e3f6c43ecced88dc3e9ce8fc6968c1809e1ad1f` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good morning
- morning
- good morning alterra

### generic.greeting_morning_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_morning/greeting_morning_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_morning/greeting_morning_003.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_morning / 1 |
| startHour / endHour | 5 / 12 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `02efb364c418e838ff6e60dd48a03626c9e9b3ef20e2e07c23cd0ab35e89b47e` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good morning
- morning
- good morning alterra

### generic.greeting_morning_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_morning/greeting_morning_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_morning/greeting_morning_004.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_morning / 1 |
| startHour / endHour | 5 / 12 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `54de49d7916b50ea949aeedd180d4c1ab519e0e8885242ce624d7dc26bb4c474` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- morning again
- good morning again
- another morning
- morning alterra again
- new morning
