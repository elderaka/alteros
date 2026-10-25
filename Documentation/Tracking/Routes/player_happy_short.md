---
title: "Route player_happy_short"
route_id: player_happy_short
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_happy_short

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_happy_short/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_happy_short_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_happy_short/player_happy_short_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_happy_short/player_happy_short_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_happy_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `bf58bfcc562bff5ecfc5a96dc05efe0d4dc30491b299bf98a47edf96adfaf257` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am happy
- i'm happy
- happy
- i feel happy
- good day

### generic.player_happy_short_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_happy_short/player_happy_short_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_happy_short/player_happy_short_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_happy_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `436e887a763eaee2016a488503e1b30a4d1ff00a44a6ffd5dc1d08c52abfbdc1` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am excited
- i'm excited
- excited
- good news
- i have good news

### generic.player_happy_short_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_happy_short/player_happy_short_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_happy_short/player_happy_short_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_happy_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `b98663b524cc84221d340455fbcc7231e829a79761a344ffd491b428befb88bc` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i feel good
- feeling good
- today is good
- i am in a good mood
- i'm in a good mood

### generic.player_happy_short_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_happy_short/player_happy_short_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_happy_short/player_happy_short_004.json` |
| Source / schema | generic / 1 |
| Route / priority | player_happy_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `da8bfadfea3685bedc8ef9b32415a4aafbfb1d175228f2d60ddb33c9de71be26` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i feel lighter
- feeling lighter
- i feel better
- better now
- things feel better
