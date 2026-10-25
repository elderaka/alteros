---
title: "Route ask_task_help_short"
route_id: ask_task_help_short
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: ask_task_help_short

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/ask_task_help_short/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/request]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.ask_task_help_short_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_task_help_short/ask_task_help_short_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_task_help_short/ask_task_help_short_001.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_task_help_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `45fbb280310272bc4c8fea228e301974c09657fef043ce45956b51ab2b102b82` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- help me with a task
- help me with this task
- can you help me start
- help me start this
- help me begin

### generic.ask_task_help_short_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_task_help_short/ask_task_help_short_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_task_help_short/ask_task_help_short_002.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_task_help_short / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `034fefdf740247a0ba8ac5a12275b0e0f7abf231e21baa75f51d21dfed39c620` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i do not know where to start
- i don't know where to start
- where do i start
- what is the first step
- help me choose a first step
