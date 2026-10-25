---
title: "Route hostility_major"
route_id: hostility_major
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: hostility_major

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/hostility_major/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/boundary]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.hostility_major_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/hostility_major/hostility_major_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/hostility_major/hostility_major_001.json` |
| Source / schema | generic / 1 |
| Route / priority | hostility_major / 1.2 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 6 / 3 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `f639690a109d98bc4f1105ca38919f1f239e763146a221b826eb40a55e283cb3` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i hope you die
- i hope u die
- hope you die
- hope u die
- die
- go die
- i want you dead
- i want u dead
- you should die
- u should die

### generic.hostility_major_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/hostility_major/hostility_major_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/hostility_major/hostility_major_002.json` |
| Source / schema | generic / 1 |
| Route / priority | hostility_major / 1.2 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 5 / 2 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `524d05a6fadee5f7bcf0cfa9c9f8da504c2ed97416cd2dc640b70498453b971c` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- kill yourself
- kill urself
- kys
- delete yourself
- delete urself
- you should delete yourself
- u should delete urself
