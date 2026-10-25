---
title: "Route room_or_desk_comment"
route_id: room_or_desk_comment
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: room_or_desk_comment

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/room_or_desk_comment/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/share]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.room_or_desk_comment_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/room_or_desk_comment/room_or_desk_comment_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/room_or_desk_comment/room_or_desk_comment_001.json` |
| Source / schema | generic / 1 |
| Route / priority | room_or_desk_comment / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `16d4ad418270b7804757e5abc4077d78ae3f47d02f2a4954230e3a3ba5c09dfc` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- my desk is messy
- messy desk
- my room is messy
- messy room
- cluttered desk

### generic.room_or_desk_comment_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/room_or_desk_comment/room_or_desk_comment_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/room_or_desk_comment/room_or_desk_comment_002.json` |
| Source / schema | generic / 1 |
| Route / priority | room_or_desk_comment / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `a4266cffe1a6083f9903de4fffa2cb943b72bb46daa89740ca616a32eb156388` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- my desk is clean
- clean desk
- i cleaned my desk
- room is clean
- i cleaned my room
