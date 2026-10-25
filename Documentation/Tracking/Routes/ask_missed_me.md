---
title: "Route ask_missed_me"
route_id: ask_missed_me
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: ask_missed_me

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/ask_missed_me/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/ask]], [[Semantics/Topics/personal/memory]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.ask_missed_me_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_missed_me/ask_missed_me_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_missed_me/ask_missed_me_001.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_missed_me / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `f1e617322f2705c16249aee128390a187ec4ee3b7ea12bb264ddd0d08e7c7390` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- did you miss me
- you missed me
- miss me

### generic.ask_missed_me_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_missed_me/ask_missed_me_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_missed_me/ask_missed_me_002.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_missed_me / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `f32e02e5158b9e7c59996363c737b8c219cfdb0508c0cdcf7abd951706b6b27f` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- did you miss me
- did u miss me
- do you miss me

### generic.ask_missed_me_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_missed_me/ask_missed_me_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_missed_me/ask_missed_me_003.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_missed_me / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `68db05714ae97a59ca8b55bfd151090bb70636b74d7a736d32c7b299a235184a` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- did you miss me
- you miss me
- did you wait for me
