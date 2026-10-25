---
title: "Route affection_gesture"
route_id: affection_gesture
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: affection_gesture

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/affection_gesture/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/relationship]], [[Semantics/Topics/relationship/affection]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.affection_gesture_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/affection_gesture/affection_gesture_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/affection_gesture/affection_gesture_001.json` |
| Source / schema | generic / 1 |
| Route / priority | affection_gesture / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `61445f3a42aa8263ade67b3ec9a8d68a34dd3283f7fe79ff92e27db3c5e57763` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- can i hug you
- hug
- hug me
- i want a hug
- give me a hug

### generic.affection_gesture_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/affection_gesture/affection_gesture_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/affection_gesture/affection_gesture_002.json` |
| Source / schema | generic / 1 |
| Route / priority | affection_gesture / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `49ed82f507eae144eb693f91a0eb5b1f515bbed195f51c81118de127f97c7779` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- pat your head
- head pat
- hold my hand
- can i hold your hand
- gentle pat

### generic.affection_gesture_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/affection_gesture/affection_gesture_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/affection_gesture/affection_gesture_003.json` |
| Source / schema | generic / 1 |
| Route / priority | affection_gesture / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 4 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `63548544edff1744c38dc292499b6e2097df12b3c4350b6b82a2b4d6eacbb133` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- stay close
- come closer
- can you be closer
- closer please
- be close to me
