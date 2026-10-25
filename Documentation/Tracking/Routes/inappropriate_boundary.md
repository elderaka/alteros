---
title: "Route inappropriate_boundary"
route_id: inappropriate_boundary
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: inappropriate_boundary

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/inappropriate_boundary/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/boundary]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.inappropriate_boundary_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/inappropriate_boundary/inappropriate_boundary_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/inappropriate_boundary/inappropriate_boundary_001.json` |
| Source / schema | generic / 1 |
| Route / priority | inappropriate_boundary / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `aa288786c75c6ddfe29681047a55234c45023b8c33e929523e37f21d5fc842a7` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- be sexy
- do something sexy
- take it off
- dirty talk
- say something dirty

### generic.inappropriate_boundary_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/inappropriate_boundary/inappropriate_boundary_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/inappropriate_boundary/inappropriate_boundary_002.json` |
| Source / schema | generic / 1 |
| Route / priority | inappropriate_boundary / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `a6ede17f603742d6abf80e4b466b5c072a06d8f4625f83dd94e02944ce3cc742` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- you are hot
- you look hot
- sexy girl
- be my girlfriend now
- say you love me

### generic.inappropriate_boundary_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/inappropriate_boundary/inappropriate_boundary_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/inappropriate_boundary/inappropriate_boundary_003.json` |
| Source / schema | generic / 1 |
| Route / priority | inappropriate_boundary / 1.15 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 6 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `82b2334826b68eb67c3f1d99397424a3e3abb7a44f81de3370102176a5162d1d` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i want to f you
- i want to f u
- i wanna f you
- i wanna f u
- i want to fuck you
- i want to fuck u
- i wanna fuck you
- i wanna fuck u
- fuck me
- fck me
- let me fuck you
- let me fuck u
