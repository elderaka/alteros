---
title: "Route missed_you"
route_id: missed_you
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: missed_you

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/missed_you/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/relationship]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.missed_you_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/missed_you/missed_you_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/missed_you/missed_you_001.json` |
| Source / schema | generic / 1 |
| Route / priority | missed_you / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `a6bc043956b445e6e1145fc7e2b1f0079ea98f6eee0cd2aec8c65299b2c94c47` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i missed you
- missed you
- i miss you

### generic.missed_you_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/missed_you/missed_you_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/missed_you/missed_you_002.json` |
| Source / schema | generic / 1 |
| Route / priority | missed_you / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `f464355b5be8eb4dca482e081de991dc323144f0f2d1f2d575507d6172525d19` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i missed you
- missed you
- i was missing you

### generic.missed_you_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/missed_you/missed_you_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/missed_you/missed_you_003.json` |
| Source / schema | generic / 1 |
| Route / priority | missed_you / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `ef05d4d6b08753256f3815b0cea60bfa439e123fcdd4d3fdfa482e239ad24d49` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i missed you
- missed you
- i really missed you

### generic.missed_you_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/missed_you/missed_you_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/missed_you/missed_you_004.json` |
| Source / schema | generic / 1 |
| Route / priority | missed_you / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 9 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `7595de4ceda90396dcf79bd3715a3558ad2cbee61d68d999270b89798f70783c` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i missed being here
- missed this place
- missed talking to you
- i missed our room
- missed sitting with you
