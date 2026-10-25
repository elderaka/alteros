---
title: Stats Revamp Audit
tags: [alteros, audit, stats]
reviewed: 2026-10-16
status: source-reviewed
---
# Stats Revamp Audit

Related: [[Stat Catalogue]], [[Stats and Mood]], [[State and Effects]], [[Stats Foundation Implementation Plan]], [[Topic Playground Implementation Plan]].

## Scope and evidence

Follow-up: first-slice catalogue/snapshot/adapter source now exists with 16/16
supplemental checks; Unity import/full-suite validation is blocked. See
[[Verification/2026-10-16 Stats Foundation]]. The findings below describe the
pre-slice baseline; persistent needs, ticker replacement and routines remain open.

Focused read-only source/serialized-reference review of progression, effects,
time, trigger context, UI consumers and the other session's emerging Playground.
No player saves were opened, code/Unity assets changed, tests/build/import run,
or live Editor state inspected. This report does not establish runtime behavior.
The Playground files are concurrent work; source presence is not a completion claim.

## Current foundation

- `Assets/Scripts/Progression/AlteraProgressionStore.cs`: schemaVersion 4 JSON
  save, Alterra/player/environment state, tasks/flags/custom stats, mutation APIs,
  Changed event and trigger-context export. The file currently has about 29 KB
  of responsibilities; preserve its scene-facing identity rather than rewrite it.
- `ProgressionTickSystem.cs`: 60-second default scaled-time coroutine, passive
  recovery/decay and derived-state refresh. No ticker GUID attachment found in
  inspected Scenes/Prefabs or other direct script references/creation.
- `StatMoodResolver.cs`: ordered need/relationship-to-mood and availability
  mapping. It is not the proposed emotional-axis model or activity availability.
- `DialogueEffectApplier.cs`: immediate sequence/node/choice deltas/effects.
  No running-activity participation/checkpoint authority or idempotency ledger.
- `Assets/Scripts/Dialogue/DialogueData.cs`: TriggerContext.TryGetStat and
  numeric conditions can fail on missing values. Reuse that distinction.
- HUD reads legacy mood/bond/credits. Full needs/mood-details view is absent.
- New `Assets/AlterOS/Editor/TopicPlaygroundSession.cs` takes text + localHour;
  `AuthoredDialogueVariantSelector` currently checks canonical time windows,
  not the new needs. Existing legacy numeric conditions do not prove support
  in this canonical/Playground path.

## Prioritized gaps

| ID | Priority / confidence | Observation and impact | Next action / verification |
| --- | --- | --- | --- |
| SR-01 | High planning priority; confirmed source gap | Hydration/satiety/connection and emotional axes are absent as dedicated state; generic customStats can hold arbitrary keys but do not define semantics | Add explicit catalogue + known/unknown snapshot first; test IDs/ranges/missing values |
| SR-02 | Medium; confirmed source mismatch, activation unverified | Ticker uses WaitForSeconds and configured tick length, not measured real elapsed time; passive energy recovery ignores actual sleep/rest | Replace only in a later simulation slice with explicit elapsed-time/event rules; fake-clock tests before wiring |
| SR-03 | Medium; confirmed | Mood resolver overwrites mood AND availability; direct mood effects may later be replaced by refresh | Separate emotional model from activity permissions; do not reinterpret old mood strings as measured axes |
| SR-04 | High migration priority; confirmed code pattern, no reproduced corruption | Direct overwrite save; load failure falls back to default state; EnsureState bumps schema number without explicit field migration | Synthetic-save migration/backup/atomic replacement tests before introducing persistent fields; retain malformed originals |
| SR-05 | Medium; confirmed source risk | SetStat accepts unknown keys via custom stats; unknown target can fall through to Alterra built-ins; missing reads default to zero; direct State remains mutable | New typed boundary rejects unknown IDs/nonfinite values, uses TryGet, and is not a model-facing set-stat tool; retain compatibility until callers migrate |
| SR-06 | Medium; confirmed absence in inspected systems | Routine records, observations, adaptive schedule, pin/override and reminder evidence are absent; lastUpdatedUtc is a save timestamp, not simulation/routine progress | Separate routine project after foundation; do not infer skipped meals from missing chat |
| SR-07 | Medium; confirmed integration gap | Playground context is currently localHour only; stat UI alone would imply unsupported gates | Coordinate a separately scoped canonical condition/snapshot extension after its current slice; no parallel edits now |

Existing clamped meters, notification events and missing-aware trigger reads
are useful reuse points. Preserve player tasks/credits/environment and legacy
serialization; no broad deletion follows from these findings.

## Recommended slices

1. Catalogue and validated synthetic/read-only snapshots (bounded plan linked above).
2. Persistent typed needs/emotion state with explicit legacy migration and safe saves.
3. Real-time needs/activity events, self-care decisions and bounded away catch-up.
4. Emotional appraisal/resolution and matching authored Canvas display/cue rules.
5. Adaptive player routines with opt-in learning, direct edits/pins, transparent
   bounded adaptation, expiry/unknown evidence and focus-aware reminders.

Only slice 1 has an implementation plan here. Rates, defaults, exact alias
policy beyond safe mappings, routine adaptation bounds, reminder cadence and
catch-up budget remain decisions for their own bounded plans. Do not invent
biology-based universal rates from the previous research.
