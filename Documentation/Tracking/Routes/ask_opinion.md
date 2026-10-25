---
title: "Route ask_opinion"
route_id: ask_opinion
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: ask_opinion

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/ask_opinion/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/ask]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.ask_opinion_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_opinion/ask_opinion_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_opinion/ask_opinion_001.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_opinion / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `e946054ac4b89ac620ab892cf0df6c7569f9f59d00b4181d4ac4a628aa4d2292` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what do you think
- what do you think alterra
- your opinion
- what is your opinion
- tell me what you think

### generic.ask_opinion_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_opinion/ask_opinion_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_opinion/ask_opinion_002.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_opinion / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `4f0d6eb34b2da34367a644899cf23058a4f6462ef97dee715a822a1d601c35f5` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- do you have thoughts
- any thoughts
- what are your thoughts
- do you think so
- tell me your thoughts
