---
title: "Route ask_preference"
route_id: ask_preference
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: ask_preference

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/ask_preference/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/ask]], [[Semantics/Topics/personal/preference]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.ask_preference_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_preference/ask_preference_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_preference/ask_preference_001.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_preference / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `3197db6dd352254aeb4f48949b37cc06bf3606f881cd4ba3ea7eb4705306a10e` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what do you like
- what are your likes
- what is your favorite
- favorite thing
- tell me what you like

### generic.ask_preference_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_preference/ask_preference_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_preference/ask_preference_002.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_preference / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `b6c6c7981ca2ebe5c6d57aa52697d61eb5e4c0c9022e186da56b61fb64c19bde` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what do you dislike
- what do you hate
- what are your dislikes
- least favorite
- what annoys you

### generic.ask_preference_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_preference/ask_preference_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_preference/ask_preference_003.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_preference / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 4 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `f01a79b758f8cd9b0088453edff536d4d6ebe30bea3f6f37abc3658a42786a09` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- favorite drink
- favorite snack
- favorite weather
- favorite music
- favorite book
