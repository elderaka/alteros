---
title: "Route returning"
route_id: returning
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: returning

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/returning/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/greet]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.returning_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/returning/returning_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/returning/returning_001.json` |
| Source / schema | generic / 1 |
| Route / priority | returning / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `279e8b0df09b76ea702b3b43ddc4817fc2bf7584f53cac2dd005d612b41ae007` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i'm back
- im back
- i am back
- i came back
- back again

### generic.returning_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/returning/returning_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/returning/returning_002.json` |
| Source / schema | generic / 1 |
| Route / priority | returning / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `e33ae4353ca35d0be6d159faff0b630baa64e097c27b7b757dbdac438f1a3c96` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i'm back
- im back
- i am back
- i came back
- back again

### generic.returning_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/returning/returning_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/returning/returning_003.json` |
| Source / schema | generic / 1 |
| Route / priority | returning / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `e76ebd48f47068498f37daf79a5130b5c6060e99c8398286b9a3b7fd03c4ee00` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am back again
- i'm back again
- back again
- returned again
- came back again
