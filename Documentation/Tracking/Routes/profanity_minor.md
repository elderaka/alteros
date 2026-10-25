---
title: "Route profanity_minor"
route_id: profanity_minor
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: profanity_minor

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/profanity_minor/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/boundary]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.profanity_minor_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/profanity_minor/profanity_minor_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/profanity_minor/profanity_minor_001.json` |
| Source / schema | generic / 1 |
| Route / priority | profanity_minor / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `34a9d31999c1b3ca497832e1d1f7df48301b091d06ed56127195820b56ded82b` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- fuck
- shit
- crap
- damn
- lol fuck
- wtf
- ffs
- fuck this

### generic.profanity_minor_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/profanity_minor/profanity_minor_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/profanity_minor/profanity_minor_002.json` |
| Source / schema | generic / 1 |
| Route / priority | profanity_minor / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `49dc4d1bb416ee2a96b4724ce1772fa5d9239e0ef31a08db644a00afec760040` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- this is shit
- this sucks
- dammit
- god damn it
- ah shit
- oh fuck
