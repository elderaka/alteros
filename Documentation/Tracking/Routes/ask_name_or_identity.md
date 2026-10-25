---
title: "Route ask_name_or_identity"
route_id: ask_name_or_identity
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: ask_name_or_identity

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/ask_name_or_identity/`.
- 2 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/ask]], [[Semantics/Topics/personal/identity]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.ask_name_or_identity_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_name_or_identity/ask_name_or_identity_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_name_or_identity/ask_name_or_identity_001.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_name_or_identity / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `934cb09145472dbe2de7e4e3d85853d341edc0fe8897c653d801d52643a137d0` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- who are you
- what are you
- what is your name
- who is alterra
- tell me about yourself

### generic.ask_name_or_identity_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/ask_name_or_identity/ask_name_or_identity_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/ask_name_or_identity/ask_name_or_identity_002.json` |
| Source / schema | generic / 1 |
| Route / priority | ask_name_or_identity / 1 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 3 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `1c819caaafec187cda24c2c3d823db3cc132eff14b46d5602341b9a112ed72b8` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- what should i call you
- can i call you alterra
- your name is alterra
- are you alterra
- alterra is your name
