---
title: "Route player_sad_short"
route_id: player_sad_short
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_sad_short

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_sad_short/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]], [[Semantics/Topics/state/sad]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_sad_short_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_sad_short/player_sad_short_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_sad_short/player_sad_short_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_sad_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `5dce9836d1c9c5897b99b1fd6b587e3d93eb9fb1a44840dbc0124ae3b47ff999` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am sad
- i'm sad
- sad
- i feel sad
- bad day

### generic.player_sad_short_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_sad_short/player_sad_short_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_sad_short/player_sad_short_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_sad_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `cf3b219c93a018e6ab6fd0d14e6cb4b33d5b1b7664825f2467aaf9e03d9db3b3` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am not okay
- i'm not okay
- not okay
- i feel bad
- i feel awful

### generic.player_sad_short_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_sad_short/player_sad_short_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_sad_short/player_sad_short_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_sad_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `4f427ab08aad2f3f396052e5a75444981b9230a224190c5410adbe3f58865b94` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i want to cry
- i feel like crying
- crying
- i cried
- rough day
