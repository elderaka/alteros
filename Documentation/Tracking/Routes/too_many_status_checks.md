---
title: "Route too_many_status_checks"
route_id: too_many_status_checks
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: too_many_status_checks

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/too_many_status_checks/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/boundary]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.too_many_status_checks_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/too_many_status_checks/too_many_status_checks_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/too_many_status_checks/too_many_status_checks_001.json` |
| Source / schema | generic / 1 |
| Route / priority | too_many_status_checks / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `86b6bcd71b1f5a5f89caa0d4edf9242726003f873977fbdf57d32069ec1b654b` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- are you okay again
- how are you again
- checking on you again
- are you still okay
- you still okay

### generic.too_many_status_checks_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/too_many_status_checks/too_many_status_checks_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/too_many_status_checks/too_many_status_checks_002.json` |
| Source / schema | generic / 1 |
| Route / priority | too_many_status_checks / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 4 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `eb4a547cabda7b70747e74a490ec2166fa82be89bd7dab6b185b93d7a045e81c` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- i keep asking how you are
- asking how you are again
- too many how are you
- status check again
- another status check
