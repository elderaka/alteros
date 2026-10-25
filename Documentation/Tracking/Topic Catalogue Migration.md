---
title: Topic Catalogue Migration
tags: [alteros, dialogue, topics, migration]
reviewed: 2026-10-16
---
# Authored Topic catalogue migration

Related: [[Dialogue Catalogue]], [[Topic Scenario Selection]],
[[Topic Runtime Catalogue Handoff]], [[../Verification/2026-10-16 Topic Catalogue Migration]].

## Staged result

The migration is staged at `Tools/TopicRuntime/StagedCatalogue/`. It is not
loaded by gameplay and is not under `StreamingAssets`.

`migration-map.json` is the exhaustive per-file map. Each row names the source
file, legacy dialogue ID/route, staged Topic/scenario, old priority and mapped
priority, old time window, and target family file. It covers all 161 source
definitions exactly once. Search this map for a route or source path; do not
infer migration from the older route snapshot.

| Legacy route | Staged Topic ID | Result |
| --- | --- | --- |
| All57 route IDs, including `greeting_general`, `greeting_morning`, `greeting_afternoon`, `greeting_evening`, and `greeting_latenight` | The same ID as the legacy route | Each route is one family. Its variants become scenarios with the union of that route's anchors. Distinct routes retain separate anchor banks. |

Totals: 57 legacy routes, 161 JSON definitions, 464 dialogue nodes, 773 anchor
occurrences and 678 distinct exact phrases within their legacy routes become 57
families and 161 scenarios. Exact duplicate phrases are deduplicated only within
the destination Topic. A source-coverage check confirms no phrase was lost.

Verification on 2026-10-16: migration unit tests 8/8; staged source integrity
check passed for 161/161 inputs; the actual C# `TopicDefinitionValidator`
accepted 57/57 families and all 161 scenarios. Topic runtime source checks 33/33
and existing stats/Playground source checks 31/31 pass. Unity Editor and player
runtime were not used for this migration.

The two source files whose dialogue ID does not follow the usual route/filename
pattern (`ask_activity.json`, `ask_status.json`) retain their original ID in
the map. Scenario identity follows the source filename stem, which is unique
within its destination family.

## Conversion decisions and gaps

- All staged authored families use `repeatable`, zero cooldown, and no
  consumption checkpoint. The legacy source does not mark milestones as
  one-time; none were guessed. All scenarios begin at equal base weight1.
- The five observed source priority values map monotonically to integer tiers:
  `0.85→0`, `1.0→1`, `1.1→2`, `1.15→3`, `1.2→4`. This preserves ordering in
  the new integer field; these values were not previously enforced by the
  current C# variant selector. Weight and priority effects need review in the
  Playground before any gameplay adoption.
- All20 authored per-file hour windows are carried to scenario `timeWindow`
  conditions. The two `sleep_nudge` windows remain distinct. The prior schema
  contains no populated stat eligibility, required-context or blocked-context
  gates. The converter fails closed if such fields appear in a future source
  file until an explicit mapping is made.
- The source schema has no menu label or Topic prompt. These remain blank in
  the staged definitions for editorial review; route names are not presented
  as player-facing copy.
- Dialogue node payloads, branches, choices, language, voice, audio keys and
  presentation fields are copied without edits. No sentiment, furniture or
  environment facts are invented.
- Anchors are an exact-string shared union per Topic. This verifies phrase
  coverage, not embedding quality. Time-of-day greeting routes stay separate
  because the current runtime has no scenario-local anchor field; combining
  their anchors would let a morning phrase retrieve evening scenarios (and
  vice versa). Any broader canonical grouping needs local-anchor support or
  routing evidence first. No vectors were baked.

## Shared generated-content boundary

Generated personal dialogue uses the same version1 `TopicDefinition` and
scenario/node shape after validation. Its runtime occurrence is a
`TopicInstance` with source `generated`, which defaults to whole-instance
`once` consumption. Generated records, anchors and dialogue remain private to
the player's runtime catalogue; this staged authored directory contains no
player-generated content.

The generation/dedup/index/embedding/TTS/publication pipeline is not implemented
by this migration. A generated candidate is not routable merely because it
exists or has a ready label; a producer must validate it, assign stable IDs,
complete its semantic indexing and publication gates, then make it available
to selection. Model output cannot introduce executable actions or arbitrary
stat/currency effects.

## Reproduce and validate

From the project root:

```powershell
python Tools/TopicRuntime/migrate_authored_catalogue.py --output Tools/TopicRuntime/StagedCatalogue-repro
python Tools/TopicRuntime/migrate_authored_catalogue.py --verify
python Tools/TopicRuntime/migrate_authored_catalogue.py --verify --output Tools/TopicRuntime/StagedCatalogue-repro
pwsh -NoProfile -File Tools/TopicRuntime/Validate-TopicCatalogue.ps1
python -m unittest discover -s Tools/TopicRuntime/tests -v
```

Generation refuses to overwrite nonempty output. Use a fresh `--output`
directory when reproducing; the verifier and C# validator default to the
checked-in `StagedCatalogue` directory, or accept an explicit path.
`--verify` checks exact source-file coverage, mapped IDs, dialogue node payloads,
anchor coverage, priority ranks, replay defaults and time windows without
changing the files. The PowerShell command deserializes every family into the
actual C# `TopicDefinition` and runs `TopicDefinitionValidator`, then checks
that every scenario has one mapping row. It compiles against the current
installed Unity references without opening Unity.

Neither validation command updates the live loader, the route snapshots, baked
vectors or production dialogue. Playground integration is the next check;
after parity and content review, gameplay integration can be planned separately.
