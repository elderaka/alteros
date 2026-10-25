---
title: "Route gratitude"
route_id: gratitude
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: gratitude

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/gratitude/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/relationship]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.gratitude_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/gratitude/gratitude_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/gratitude/gratitude_001.json` |
| Source / schema | generic / 1 |
| Route / priority | gratitude / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `357778bcccbfe19c5042f5e429a415152ee5767ffe14b9d26bdc2168d61ce6a3` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- thank you
- thanks
- ty
- appreciate it
- thank you alterra

### generic.gratitude_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/gratitude/gratitude_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/gratitude/gratitude_002.json` |
| Source / schema | generic / 1 |
| Route / priority | gratitude / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `39790e1cd9403fc8f3c6372eb8713332350454a4fb1b9296cde7e86ee2a9da1b` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- thank you
- thanks
- ty
- appreciate it
- thank you alterra

### generic.gratitude_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/gratitude/gratitude_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/gratitude/gratitude_003.json` |
| Source / schema | generic / 1 |
| Route / priority | gratitude / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `17cd3f82b77c0c8695f399b449465b25eb354aa445a53830981993df95a98121` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- thanks again
- thank you again
- really thanks
- thanks for being here
- thank you for staying
