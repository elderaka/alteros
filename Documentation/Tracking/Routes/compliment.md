---
title: "Route compliment"
route_id: compliment
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: compliment

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/compliment/`.
- 3 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/relationship]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.compliment_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/compliment/compliment_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/compliment/compliment_001.json` |
| Source / schema | generic / 1 |
| Route / priority | compliment / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `7dcceca82d35850a4da87a9e399736c4addbde2b15e0e7b6135cc53391185b1e` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- you look cute
- you are cute
- you're cute
- you look nice
- you are pretty

### generic.compliment_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/compliment/compliment_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/compliment/compliment_002.json` |
| Source / schema | generic / 1 |
| Route / priority | compliment / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `b1cd017d6105c53a830981d581025f44b7728640fa578d4fae34c1102dfde834` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- you look cute
- you are cute
- you're cute
- you look nice
- you are pretty

### generic.compliment_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/compliment/compliment_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/compliment/compliment_003.json` |
| Source / schema | generic / 1 |
| Route / priority | compliment / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 9 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `d5927b99dbbb62dceb1ce25bd8a7c71c8c56169257c8f076696cc2fe1b6e0fd5` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- you are lovely
- you are adorable
- you are precious
- you are sweet
- you are really cute
