---
title: "Route sit_with_me"
route_id: sit_with_me
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: sit_with_me

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/sit_with_me/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/request]], [[Semantics/Topics/relationship/companionship]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.sit_with_me_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/sit_with_me/sit_with_me_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/sit_with_me/sit_with_me_001.json` |
| Source / schema | generic / 1 |
| Route / priority | sit_with_me / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `3003dece89b6824a3b2cc3835f4af8982c929571933ca825a74bcd4007832bca` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- sit with me
- stay with me
- keep me company

### generic.sit_with_me_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/sit_with_me/sit_with_me_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/sit_with_me/sit_with_me_002.json` |
| Source / schema | generic / 1 |
| Route / priority | sit_with_me / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `8c5dbce2b869f712fd5bfc697375cf39e31b3b35f877dbbb28579e7d6b825343` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- sit with me
- stay with me
- can you stay with me

### generic.sit_with_me_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/sit_with_me/sit_with_me_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/sit_with_me/sit_with_me_003.json` |
| Source / schema | generic / 1 |
| Route / priority | sit_with_me / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `5f3ee1e4ec867f0fe33b3ac181befdb8f61bc8ac39799e0f8b8d45890655df73` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- sit with me
- keep me company
- be with me for a bit

### generic.sit_with_me_005

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/sit_with_me/sit_with_me_005.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/sit_with_me/sit_with_me_005.json` |
| Source / schema | generic / 1 |
| Route / priority | sit_with_me / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `007e5523c7ca364d6254c8705aec2a98b60c1df250aa7b89cd17fb910a0d053a` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- just stay here
- stay here with me
- sit here for a while
- be nearby
- stay nearby
