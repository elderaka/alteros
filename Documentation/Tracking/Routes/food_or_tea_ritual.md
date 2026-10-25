---
title: "Route food_or_tea_ritual"
route_id: food_or_tea_ritual
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: food_or_tea_ritual

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/food_or_tea_ritual/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/request]], [[Semantics/Topics/beverage/tea]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.food_or_tea_ritual_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/food_or_tea_ritual/food_or_tea_ritual_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/food_or_tea_ritual/food_or_tea_ritual_001.json` |
| Source / schema | generic / 1 |
| Route / priority | food_or_tea_ritual / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `678fe1e96890e14544668bbd2252764e74742d3805dc3cb5544450f2496ff64b` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- make tea
- tea time
- i want tea
- let us have tea
- let's have tea

### generic.food_or_tea_ritual_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/food_or_tea_ritual/food_or_tea_ritual_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/food_or_tea_ritual/food_or_tea_ritual_002.json` |
| Source / schema | generic / 1 |
| Route / priority | food_or_tea_ritual / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `15ae601b64f3a321ef430452180596869b23550068639539fb126ff592758785` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am hungry
- i'm hungry
- hungry
- need food
- i need food
