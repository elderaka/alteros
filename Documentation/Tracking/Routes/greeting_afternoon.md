---
title: "Route greeting_afternoon"
route_id: greeting_afternoon
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: greeting_afternoon

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/greeting_afternoon/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/greet]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.greeting_afternoon_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_afternoon/greeting_afternoon_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_afternoon/greeting_afternoon_001.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_afternoon / 1 |
| startHour / endHour | 12 / 17 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `9067a3d53b2b0d5ba178ec30e01ae07885b4a365d7d3f59b646cc447cabc5c67` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good afternoon
- afternoon
- good afternoon alterra

### generic.greeting_afternoon_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_afternoon/greeting_afternoon_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_afternoon/greeting_afternoon_002.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_afternoon / 1 |
| startHour / endHour | 12 / 17 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `aa8bc562559aae716622d4b42063042e76befb31bfec6631725f5076468bfec8` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good afternoon
- afternoon
- hey this afternoon

### generic.greeting_afternoon_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_afternoon/greeting_afternoon_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_afternoon/greeting_afternoon_003.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_afternoon / 1 |
| startHour / endHour | 12 / 17 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `42c713eab7b8394b17fe44617882fed1440cb14dc969182ba0e0c41020dc3400` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- good afternoon
- afternoon alterra
- hello afternoon
