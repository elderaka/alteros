---
title: "Route ask_memory"
route_id: ask_memory
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: ask_memory

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/ask_memory/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/ask]], [[Semantics/Topics/personal/memory]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.ask_memory_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_memory/ask_memory_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_memory/ask_memory_001.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_memory / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `8a1b7f12e18e48d818d78b7bd22d45b8ff106f4d087e977001b4e45031e584f2` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- do you remember
- remember me
- do you remember me
- remember that
- you remember

### generic.ask_memory_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_memory/ask_memory_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_memory/ask_memory_002.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_memory / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `c417e0ac93e46283931326da38de4d7c1dfa063a7ff36fd0272a1b2299fdcff1` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what do you remember
- what do you know about me
- do you know me
- what have you remembered
- remember anything

### generic.ask_memory_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_memory/ask_memory_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_memory/ask_memory_003.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_memory / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 8 / 6 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `7239b441480e45f5c37ebca072f0fce5d6817a5162cf28f234dca72aa5f4650d` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what should you remember
- can you remember this
- remember this about me
- i want this remembered
- this matters to me
