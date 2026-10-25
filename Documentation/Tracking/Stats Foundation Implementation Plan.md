# Stats Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. User approved inline execution on 2026-10-16. Source implementation and supplemental 16/16 checks are finished; Unity import/full Editor-suite acceptance is blocked. See [[Verification/2026-10-16 Stats Foundation]]. Do not reimplement completed source tasks.

**Goal:** Give the approved six needs and three emotional axes a validated, missing-aware data contract usable by synthetic tests without altering player state or the concurrent Playground.

**Architecture:** Put the small immutable catalogue and validated snapshot in the existing AlterOS.Runtime assembly. Keep the legacy MonoBehaviour store and serialization intact; a separate scene-side adapter produces read-only snapshots. The Playground may later consume this contract through a coordinated integration task, not a copied router or implicit schema change.

**Tech Stack:** Unity 6000.4.4f1, C#, existing AlterOS.Runtime and AlterOS.Editor.Tests assemblies, NUnit; no new packages/framework.

**Spec:** [[Stat Catalogue]], [[Stats and Mood]], [[Live Conversation Journal]]; current evidence: [[Stats Revamp Audit]]. This is slice 1, not the full simulation/routine implementation.

## Global Constraints

- Work in `E:\Pleiades\alteros`; `pleiades.os` is archive-only.
- Six needs: energy, hydration, satiety, engagement, connection and physical comfort. Satisfaction ranges 0..100. Proposed axis contract -1..1 requires review with this plan.
- Preserve legacy serialized altera keys, scene references and all existing saves. No player-save load/write from tests or debug snapshot construction.
- Never equate missing/unsupported values with zero; zero is valid on both scales.
- No generic LLM set-stat API, arbitrary model deltas or automatic thresholds/need triggers.
- No scene/prefab/.meta hand editing. New imported assets/metas are managed by Unity.
- Other Luna session owns Playground files and shared dialogue/semantic changes; do not modify those files in this slice.
- No new ticker, default/recovery rates, offline catch-up, mood-label replacement, routine learner, UI or anchor rebake.

## Review Focus

- Missing hydration in legacy saves remains unknown, not severe dehydration.
- NaN/infinity and out-of-range values cannot silently enter valid snapshots.
- Unknown actor/ID and typos cannot mutate or impersonate an Alterra stat.
- Snapshot edits cannot change live store, save file or derived mood.
- Concurrent Playground progress does not get overwritten or claimed as tested here.

## Proposed file ownership

- Create `Assets/AlterOS/Stats/StatCatalogue.cs`: immutable nine-entry definition lookup; no Unity lifecycle.
- Create `Assets/AlterOS/Stats/StatSnapshot.cs`: validated present values only, independent of MonoBehaviours.
- Create `Assets/Scripts/Progression/CompanionStatSnapshotBuilder.cs`: one-way legacy read adapter; no reverse synchronization or new persistent authority.
- Tests: `Assets/AlterOS/Tests/Editor/StatCatalogueTests.cs`, `StatSnapshotTests.cs`, `CompanionStatSnapshotBuilderTests.cs`.
- Existing `AlteraProgressionStore.cs` is a consumer/source, not edited in slice 1.

## Task 1: Define the reviewed catalogue

**Interfaces:** `StatCatalogue.TryGet(string id, out StatDefinition definition)`;
`StatCatalogue.Definitions` as `IReadOnlyList<StatDefinition>`.
`StatDefinition` exposes read-only `Id`, `Minimum`, `Maximum`; no mutable setters.

- [ ] Write failing `StatCatalogueTests`: exactly six needs and three axes;
  unique IDs; hydration range 0..100; emotion valence range -1..1; unknown
  ID returns false. Confirm catalogue collection cannot be externally modified.
- [ ] Run EditMode filter `AlterOS.Tests.StatCatalogueTests`; require red for
  absent API, not an unrelated compiler/environment failure.
- [ ] Implement only the nine proposed IDs from [[Stat Catalogue]]. Use ordinal
  exact IDs, not arbitrary normalization/aliases; put legacy mappings in the adapter.
- [ ] Re-run the filter; require all named assertions passing.
- [ ] Review scoped diff; checkpoint only owned files if repository commit state
  permits and the user requests commits. Never stage the initial whole-project snapshot.

## Task 2: Validated synthetic snapshots

**Interfaces:** `StatSnapshot.TrySet(string id, float value, out string error)`;
`StatSnapshot.TryGet(string id, out float value)`; `StatSnapshot.Clone()`.
TrySet here is a fixture/data boundary, not a registered Agent capability.

- [ ] Write failing `StatSnapshotTests`: missing hydration returns false;
  explicit zero returns true; valid boundaries persist; reject -1/101 for needs,
  -1.01/1.01 for axes, NaN, infinities and unknown IDs without mutating prior
  value; modifying a clone leaves original unchanged. No default filling/clamping.
- [ ] Run EditMode filter `AlterOS.Tests.StatSnapshotTests`; confirm intended red.
- [ ] Implement catalogue validation and private value storage; TryGet false
  means unknown, regardless of its out-parameter default. Do not expose mutable storage.
- [ ] Re-run catalogue + snapshot tests; require pass and no unrelated API changes.
- [ ] Review/checkpoint only this task's owned files under the same commit constraint.

## Task 3: Read-only legacy adapter and compatibility gate

**Interface:** `CompanionStatSnapshotBuilder.Build(AlteraStatsState legacy)`
returns an independent `StatSnapshot` and does not access MonoBehaviour lifecycle/files.
This adapter lives in the existing default Unity assembly, which can reference
AlterOS.Runtime; do not make the runtime assembly reference Assembly-CSharp.

- [ ] Write failing `CompanionStatSnapshotBuilderTests` using the existing
  Editor-test reflection pattern for default-assembly types; build a synthetic
  legacy DTO (no AddComponent/Awake/real save). Energy=70, engagement=50,
  attention=10, reassurance=20, comfort=30: map energy/engagement exactly;
  hydration/satiety/connection/physical_comfort/axes remain unknown. Null DTO
  produces an empty snapshot. Changes to snapshot do not alter DTO fields.
- [ ] Add invalid legacy-value tests: NaN/out-of-range energy is omitted rather
  than clamped or mapped to another stat. No mood/availability setter is called.
- [ ] Run the filter; confirm red before implementing the pure adapter. Preserve
  legacy comfort until its old meaning is explicitly migrated; no silent rebranding.
- [ ] Implement the two safe source mappings only; no reflection in production,
  duplicated persistent needs state or edits to legacy GetStat/SetStat APIs.
- [ ] Run all three new filters and the existing relevant Editor test assembly;
  confirm compile/import and test results separately. Do not trigger another
  session's Unity tests/domain reload concurrently; stop and coordinate if busy.
- [ ] Update [[Stat Catalogue]], [[Stats Revamp Audit]], [[Feature Register]],
  [[Maintenance]] and root TODO with actual supported IDs/source mappings and
  evidence. Record that canonical dialogue/Playground numeric gating is still absent.
- [ ] Review/checkpoint owned files; stop. Hand off snapshot contract and tests
  for a separately approved Playground integration or persistent-state migration.

## Verification and stopping gate

Use the connected intended AlterOS Editor test tools to run named EditMode
filters and assembly `AlterOS.Editor.Tests`; preserve results/job IDs. If Editor
access is unavailable/busy, report tests not run, never substitute a compile
guess or fabricate a command. No Play Mode, paid provider calls, bake or full
room build is required for this pure contract slice.

Success means known/unknown, ID/range validation, immutable definitions and
read-only mapping are tested. It does not mean needs tick, routines adapt,
mood displays work or Topic selection consumes new stats.

## Subsequent plans - not executable tasks in this slice

Persistent migration must handle corrupt/future-version saves, backups and
atomic replacement before introducing new values/defaults. Real-time simulation
must separate wall-clock context from elapsed time and needs from actual
activity effects. Routine adaptation requires evidence provenance, opt-in,
manual pins, transparent bounded changes and explicit unknown/done/skipped
statuses. Draft those plans only after resolving their parameters.

## Plan review calls

Assistant proposals requiring approval with this plan: exact nine IDs/ranges
from the draft, initial slice limited to snapshots, safe mapping of only energy
and engagement, and leaving old comfort unmapped. No biological defaults/rates
or arbitrary reminder schedule has been chosen. Self-reviewed for requirement,
interface, assembly boundary, failure-test coverage and scope consistency.
