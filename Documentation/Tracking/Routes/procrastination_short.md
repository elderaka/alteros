---
title: "Route procrastination_short"
route_id: procrastination_short
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: procrastination_short

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/procrastination_short/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]], [[Semantics/Topics/activity/work]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.procrastination_short_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/procrastination_short/procrastination_short_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/procrastination_short/procrastination_short_001.json` |
| Source / schema | generic / 1 |
| Route / priority | procrastination_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `eba0f3b19b763a30748c8c604f5c9899dacd7d88d09596003ed49b6170e8e8d5` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am procrastinating
- i'm procrastinating
- procrastinating
- i keep procrastinating
- i am avoiding it

### generic.procrastination_short_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/procrastination_short/procrastination_short_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/procrastination_short/procrastination_short_002.json` |
| Source / schema | generic / 1 |
| Route / priority | procrastination_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `c0594f082b80c4b1669ee93581eac9fb83031dccdbea58d928ab3fd1a7116f24` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i keep avoiding work
- avoiding work
- i am putting it off
- i'm putting it off
- putting it off

### generic.procrastination_short_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/procrastination_short/procrastination_short_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/procrastination_short/procrastination_short_003.json` |
| Source / schema | generic / 1 |
| Route / priority | procrastination_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 6 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `fbfe215124afa616cefafdf2635370c9ddd5cd2ca61d448c47f4c1750e2b214d` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i keep delaying
- delaying again
- i am stuck before starting
- stuck before starting
- avoiding the first step
