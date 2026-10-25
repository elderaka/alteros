---
title: "Route sleep_nudge"
route_id: sleep_nudge
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: sleep_nudge

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/sleep_nudge/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/request]], [[Semantics/Topics/activity/sleep]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.sleep_nudge_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/sleep_nudge/sleep_nudge_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/sleep_nudge/sleep_nudge_001.json` |
| Source / schema | generic / 1 |
| Route / priority | sleep_nudge / 1 |
| startHour / endHour | 21 / 6 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `f1f59969ec018c87df97b81e02018b4332725236ac23a3be1fc8cb1dfdeb7c6d` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i should sleep
- need to sleep
- i need sleep
- i should go to bed
- go to bed

### generic.sleep_nudge_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/sleep_nudge/sleep_nudge_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/sleep_nudge/sleep_nudge_002.json` |
| Source / schema | generic / 1 |
| Route / priority | sleep_nudge / 1 |
| startHour / endHour | 22 / 6 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `6552b23c956eb110bc24007a5f0feed9433ffa5872f20deb238018b935645cf9` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i am still awake
- i'm still awake
- still awake
- i cannot sleep
- i can't sleep
