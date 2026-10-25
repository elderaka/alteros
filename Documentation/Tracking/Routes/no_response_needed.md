---
title: "Route no_response_needed"
route_id: no_response_needed
kind: authored-route-snapshot
implementation: implemented
verification: not-run
snapshot: 2026-10-15
tags: [alteros, dialogue, route]
---
# Route: no_response_needed

Related: [[Tracking/Dialogue Catalogue]], [[Tracking/State and Effects]], [[Tracking/Runtime Observability]].

## Ownership and review

- Source directory: `Assets/StreamingAssets/Dialogue/Generic/no_response_needed/`.
- 4 definition(s); values below are extracted from JSON, not a new gameplay test.
- Canonical Intent/Topic/Tone IDs and universal/local anchor assignments: not represented as separate fields in the current schema. Do not infer them from the route name or character expression.
- Existing semantic notes mentioning this route: [[Semantics/Intents/repair]]. These are review references, not validated runtime bindings.
- Stat effects / Agent tool calls: no dedicated fields in the current canonical schema. This does not establish that surrounding presentation code has no side effects.
- Speech: explicit audio-key counts below do not prove cache coverage; current playback can derive cache keys.
- Review gaps: canonical mappings, negative/ambiguous trigger cases, local specificity, actual eligibility enforcement, speech coverage, and end-to-end playback.

## Definitions and declared conditions

Hours and context below are declared data, not proof the consumer enforces all fields. `-1` is retained verbatim rather than inventing a time condition. Current route selection uses stable variant ordering; see `Assets/AlterOS/Dialogue/AuthoredDialogueRouteCatalog.cs`. [[SessionZero]] records the time-gated playback-success gap.

### generic.no_response_needed_001

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/no_response_needed/no_response_needed_001.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/no_response_needed/no_response_needed_001.json` |
| Source / schema | generic / 1 |
| Route / priority | no_response_needed / 0.85 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `948b6c72a1199149d6b775c5350da3fea5dac1af2fd63b54d5645a742e577cde` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- ok
- okay
- yeah
- yep
- sure

### generic.no_response_needed_002

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/no_response_needed/no_response_needed_002.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/no_response_needed/no_response_needed_002.json` |
| Source / schema | generic / 1 |
| Route / priority | no_response_needed / 0.85 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 2 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `6078bbb951356b946b43bc4ba094f870ed01297d1ef5a4a526862a69f89dd2e7` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- mm
- mhm
- uh huh
- got it
- alright

### generic.no_response_needed_003

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/no_response_needed/no_response_needed_003.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/no_response_needed/no_response_needed_003.json` |
| Source / schema | generic / 1 |
| Route / priority | no_response_needed / 0.85 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 1 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `8e96b0f855e40ce955d0da52b725ecf8e4464588094010e0e569432dc7e0435c` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- k
- kk
- fine
- ok then
- gotcha

### generic.no_response_needed_004

[Open source JSON](file:///E:/Pleiades/alteros/Assets/StreamingAssets/Dialogue/Generic/no_response_needed/no_response_needed_004.json)

| Field | Extracted value |
| --- | --- |
| Repository path | `Assets/StreamingAssets/Dialogue/Generic/no_response_needed/no_response_needed_004.json` |
| Source / schema | generic / 1 |
| Route / priority | no_response_needed / 0.85 |
| startHour / endHour | -1 / -1 |
| requiredContext | [] |
| blockedContext | [] |
| Nodes / choices | 1 / 0 |
| Explicit nonempty audioKey nodes | 0 |
| SHA-256 | `a0a129daaf8d961b994544431d709385a863de22726f670f4da93f90620ade85` |

Trigger examples (current flat per-variant anchors; not the future universal catalogue):

- m
- mmkay
- okay okay
- yup
- roger
