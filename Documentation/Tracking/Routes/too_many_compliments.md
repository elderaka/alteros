---
title: "Route too_many_compliments"
route_id: too_many_compliments
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: too_many_compliments

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/too_many_compliments/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/relationship]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.too_many_compliments_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/too_many_compliments/too_many_compliments_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/too_many_compliments/too_many_compliments_001.json` |
| Source / schema | generic / 1 |
| Route / priority | too_many_compliments / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `b3caad1935115ff37718f4fa9eca1e449f77fb456728e07ad1aabda7d623c6f9` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- you are cute again
- so cute again
- you are too cute
- cute cute cute
- you are beautiful again

### generic.too_many_compliments_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/too_many_compliments/too_many_compliments_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/too_many_compliments/too_many_compliments_002.json` |
| Source / schema | generic / 1 |
| Route / priority | too_many_compliments / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `e6902f68cc523f0aefc8ca40a0e2c9a137d54ffe8edeb4912a09dd53bcf1e02c` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i keep calling you cute
- complimenting you again
- another compliment
- you are cute for the third time
- more compliments
