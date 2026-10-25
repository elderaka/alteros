---
title: "Route ask_status"
route_id: ask_status
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: ask_status

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/ask_status/`.
- 5 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/ask]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.ask_status_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status_001.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_status / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `d6e19819cdf6609bffacbd4f47ca9c3361f834ccf9ed9f8016134cfd8eb21b4d` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- how are you
- how are you doing
- how do you feel
- you okay
- are you okay

### generic.ask_status_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status_002.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_status / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `be6d743f02c942c511a4e452f6ec0b7e34ba2133274018cdfff0a1960c576854` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- how are you
- how are you doing
- how do you feel
- you okay
- are you okay

### generic.ask_status_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status_003.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_status / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `1157f6c8d83657ba4a43a606c591cb4a3ffbda848d2703a92049ca4cae2234dd` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- how are you
- how are you doing
- how do you feel
- you okay
- are you okay

### generic.ask_status_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status_004.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_status / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 4 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `1bdc0204879e3b8e907ad8217ae3ab8fd6767a27a2163ac5688cf6792aeaf65e` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- how are you really
- how are you feeling really
- are you really okay
- tell me how you are
- how is alterra

### generic.ask_status

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_status/ask_status.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_status / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `4ce41e33d3e4caaf799b8a72b435062d90c6bfc1f0374bca9923728469059477` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- how are you
- how are you doing
- how do you feel
- you okay
