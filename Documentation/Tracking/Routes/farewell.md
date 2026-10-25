---
title: "Route farewell"
route_id: farewell
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: farewell

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/farewell/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/farewell]], [[Semantics/Intents/greet]], [[Semantics/Topics/activity/sleep]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.farewell_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/farewell/farewell_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/farewell/farewell_001.json` |
| Source / schema | generic / 1 |
| Route / priority | farewell / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `9b97968b38da3a3f911769a6e53d49ada6e0e13474bd977bbc0a8d31773cca3c` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- bye
- goodbye
- see you
- see you later
- i have to go

### generic.farewell_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/farewell/farewell_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/farewell/farewell_002.json` |
| Source / schema | generic / 1 |
| Route / priority | farewell / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `a6bb09fae1e56b951ca5614fe1808f0e780dcd316b47cc1d34e07be06fd73c70` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- bye
- goodbye
- see you
- see you later
- i have to go
