---
title: "Route insult_boundary_light"
route_id: insult_boundary_light
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: insult_boundary_light

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/insult_boundary_light/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/boundary]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.insult_boundary_light_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/insult_boundary_light/insult_boundary_light_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/insult_boundary_light/insult_boundary_light_001.json` |
| Source / schema | generic / 1 |
| Route / priority | insult_boundary_light / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `56aa3de8add01dfa7ccf8a96f7cb26799ea7ab054d6d37f792f5e115e3e62a67` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- you are annoying
- you are stupid
- shut up
- you are useless
- you are dumb

### generic.insult_boundary_light_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/insult_boundary_light/insult_boundary_light_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/insult_boundary_light/insult_boundary_light_002.json` |
| Source / schema | generic / 1 |
| Route / priority | insult_boundary_light / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `701a20d40ce45a98c066a7c5dff42ad0f2f66fcfec8c46aa95d739d2dbc55a72` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- you suck
- that was useless
- you are bad
- bad assistant
- you are irritating
