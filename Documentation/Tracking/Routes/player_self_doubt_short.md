---
title: "Route player_self_doubt_short"
route_id: player_self_doubt_short
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: player_self_doubt_short

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/player_self_doubt_short/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.player_self_doubt_short_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_self_doubt_short/player_self_doubt_short_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_self_doubt_short/player_self_doubt_short_001.json` |
| Source / schema | generic / 1 |
| Route / priority | player_self_doubt_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `ed071a4d49737874997252b921d5607337dc76bfeefdc6a8e67697ccdf363c4b` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i cannot do this
- i can't do this
- cant do this
- i cannot
- i can't

### generic.player_self_doubt_short_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_self_doubt_short/player_self_doubt_short_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_self_doubt_short/player_self_doubt_short_002.json` |
| Source / schema | generic / 1 |
| Route / priority | player_self_doubt_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `be700f29f7aa9cf6e35ea71e8e16563be8bea3b9b4850b40b121753d7d5203bd` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am useless
- i'm useless
- useless
- i am bad at this
- i'm bad at this

### generic.player_self_doubt_short_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/player_self_doubt_short/player_self_doubt_short_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/player_self_doubt_short/player_self_doubt_short_003.json` |
| Source / schema | generic / 1 |
| Route / priority | player_self_doubt_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `59c88e8298db803922acb5f0b94673d6736e2a96bbbee843d54e1856223eb534` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am failing
- i'm failing
- failing
- i keep failing
- i messed up
