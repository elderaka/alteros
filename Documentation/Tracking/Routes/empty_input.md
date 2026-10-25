---
title: "Route empty_input"
route_id: empty_input
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: empty_input

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/empty_input/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/fallback]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.empty_input_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/empty_input/empty_input_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/empty_input/empty_input_001.json` |
| Source / schema | generic / 1 |
| Route / priority | empty_input / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `281fab3dd4badcd2dc226d2cf5f928daaf99b621312336f3f9731f64781a1d6a` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- .
- ...
- ?
- ??

### generic.empty_input_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/empty_input/empty_input_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/empty_input/empty_input_002.json` |
| Source / schema | generic / 1 |
| Route / priority | empty_input / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `a6d3170401c7be2c38aba5acf967301a244b479ade6596dd669432e8e3398451` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- ....
- ???
- !!!
- -

### generic.empty_input_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/empty_input/empty_input_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/empty_input/empty_input_003.json` |
| Source / schema | generic / 1 |
| Route / priority | empty_input / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `c0226e5eea5ccdbd01843e0bd37625c7da201bab1cacc5f64d34f8d9bbadabf9` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- ...?
- ???
