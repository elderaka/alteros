---
title: "Route apology"
route_id: apology
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: apology

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/apology/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/repair]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.apology_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/apology/apology_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/apology/apology_001.json` |
| Source / schema | generic / 1 |
| Route / priority | apology / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `77c8d7669529a295caecaedfa0f87da89dcd6b099866439b4d84d1c4785767b1` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- sorry
- i'm sorry
- im sorry
- my bad

### generic.apology_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/apology/apology_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/apology/apology_002.json` |
| Source / schema | generic / 1 |
| Route / priority | apology / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `9cb22cada9974c2c10fc086e311f1779cf824681921bd0d2800e17a34f7170c6` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- sorry
- i'm sorry
- im sorry
- my bad

### generic.apology_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/apology/apology_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/apology/apology_003.json` |
| Source / schema | generic / 1 |
| Route / priority | apology / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `ed2221d37984819a003b28ddc58b4720870301e51fee2fb86a86b31f6863a195` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- sorry again
- i am really sorry
- i'm really sorry
- my bad again
- i did not mean that
