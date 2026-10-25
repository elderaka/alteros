---
title: "Route greeting_general"
route_id: greeting_general
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: greeting_general

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/greeting_general/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/greet]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.greeting_general_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_general/greeting_general_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_general/greeting_general_001.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_general / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `6e0d05957eddefac30b60a3e4ccb669cd464d5960baca40736fce044beb869ef` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- hello
- hi
- hey
- hey alterra
- hello alterra

### generic.greeting_general_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_general/greeting_general_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_general/greeting_general_002.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_general / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `2c930c34f3727ae4ad47bea168464d7a8f5c0668b02312d78de1d8786b4634f0` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- hello
- hi
- hey
- hey alterra
- hello alterra

### generic.greeting_general_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/greeting_general/greeting_general_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/greeting_general/greeting_general_003.json` |
| Source / schema | generic / 1 |
| Route / priority | greeting_general / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `2960a9ee550d6f2cc05eba74db313ba4a29d886b77337548893929a463d16748` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- hello
- hi
- hey
- hey alterra
- hello alterra
