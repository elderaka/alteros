---
title: Activity Sequence Catalog v1
tags: [alteros, verification, activities, reusable-content]
updated: 2026-10-23
---
# Activity Sequence Catalog v1

Status: Activities catalog and reservation-token executor fix round 1 passed Main's
scoped source review. The shared factory, descriptor metadata, source composer-
argument validation, normalized builder, and runtime binding-provenance check are
implemented. This does not release the DBMS Content projection, runtime primitive
handlers, Unity host wiring, or gameplay acceptance.

## Shared API

```csharp
ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
ActivityPrimitiveRegistry primitives = catalog.Primitives;
ReadOnlyCollection<ActivitySequenceComposerDescriptor> composers = catalog.Composers;
ActivitySequenceBuilder builder = catalog.CreateBuilder();
```

Each primitive descriptor is immutable and includes `primitiveId`, version,
operation kind, required capability ID (empty only where the reservation/function
host check is the authority), typed arguments, typed results, and optional entity
provenance. `ActivitySequenceCatalog.CreateBuilder()` passes the same registry and
composer metadata used by authoring/build validation.

`ActivitySequenceSource` adds `composerArguments`. Exactly one source remains
required: `steps` XOR registered `composerId`. Composer arguments are typed literal
values, legal only with a composer, and validated against immutable composer ID,
version, and argument descriptors before trusted C# composition. Both the composer
output and authored steps pass through the same `ActivitySequenceValidator`.

The installed generic composer is `sequence.wait_then_complete.v1`, descriptor
version `1`, with required `durationSeconds: double [0,300]`. It composes a bounded
`wait` followed by `complete`; it performs no Unity or game action itself.

## Canonical primitive descriptors

All primitive descriptor versions are `1`.

| Primitive | Operation | Required capability | Arguments | Results |
| --- | --- | --- | --- | --- |
| `reserve_nearest` | Pure | `furniture.reserve_nearest` | Required `useId:text`; optional `functionId:enum` (`power.set`, `dispenser.collect_mock`) | `targetToken:entity`, kind `activity_target` |
| `move_to` | Pure | None; host validates reservation token | Required `target:entity`, kind `activity_target`, binding produced by `reserve_nearest` | `arrived:bool` |
| `face` | Pure | None; host validates reservation token | Same required reservation-bound `target` | None |
| `bark` | Presentation | `dialogue.bark` | Required `dialogueId:text`, a release-local content key, never raw dialogue text | `delivered:bool` |
| `wait` | Pure | None | Required `durationSeconds:double [0,300]` | None |
| `invoke_furniture` | FurnitureFunction | None; host validates exact reservation and registered function | Required reservation-bound `target`; required literal `functionId:enum` (`power.set`, `dispenser.collect_mock`); optional `desiredState:enum` (`Off`, `On`) | `functionStatus:enum` (`applied`, `no_change`, `replayed`, `unavailable`, `conflict`, `failed`, `completed`, `cancelled`) |
| `move_near_player` | Pure | `movement.near_player` | None; movement tuning remains host-owned | `arrived:bool` |
| `checkpoint` | Pure | `scenario.checkpoint` | Required `checkpointId:text`; host accepts registered Scenario checkpoint IDs only | `recorded:bool` |
| `complete` | Pure | `scenario.complete` | None | None |

`invoke_furniture` applies one v1 conditional rule: `power.set` requires
`desiredState`; `dispenser.collect_mock` forbids it. `accepted` and `running` are
transient presentation lifecycle states and are not final `functionStatus` results.

`move_to`, `face`, and `invoke_furniture` cannot take a literal scene ID as target
authority. Their argument descriptors require an `activity_target` result binding
from `reserve_nearest`; output validation also enforces that entity kind. The runtime
port must still verify token ownership, exact instance/use-point/revision and live
reservation. A descriptor or capability ID alone grants no permission.

Checkpoint is an identifier-only Scenario event; these primitives expose no stat or
currency delta. No author-supplied navigation tuning, reflection, raw function JSON,
or arbitrary effect payload is supported in this v1 catalog.

## Content and runtime integration boundary

- The DBMS compiler must map `composerArguments` into `ActivitySequenceSource` and
  use the shared catalog builder for source and package/build validation. Unknown
  composers, missing composer metadata, invalid arguments, or missing registry must
  fail closed.
- `dialogueId` must resolve to one exact Dialogue revision in the selected
  snapshot/release and be included in its pinned closure. Runtime resolution is
  only through the pinned Action release; no latest/global lookup is supported.
- No Content parser/schema change, primitive handler, Activity host/Scenario change,
  Editor wiring, or provider call was included in this slice.

## Verification

- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1 -Filter ActivitySequence` —
  26 passed, 0 failed.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` — 93 passed, 0 failed.
- The harness compiles the selected C# source before executing tests. It emits
  existing nullable-field warnings from fixture/import DTOs; no source compile
  errors were reported in either passing run.
- `Tools/ContentData/Check-ContentData.ps1 -Group ReusableDefinitions` compiled
  source but could not invoke its child harness: it resolved a non-existent
  `C:\Windows\System32\WindowsPowerShell\v1.0\pwsh.exe` path. No result from that
  ContentData group is claimed.
- A later fresh Scenario harness rerun, after concurrent DBMS source edits, stopped
  before tests at `Assets/AlterOS/Content/ContentPackageValidator.cs(355,21)` with
  `CS0103: The name 'Int' does not exist in the current context`. No Activities
  catalog source changed after the passing 93/93 run; the current full-source
  compile is therefore blocked by this separate Content error.
- Unity Editor import/compilation, Play Mode, actual furniture operations, bark
  delivery, and player-visible acceptance were not run.

## Reservation token executor fix round 1

The first runtime probe built a valid `reserve_nearest -> move_to` sequence with an
empty `ScenarioRuntimeSnapshot.objects` list. `reserve_nearest` completed with an
`entity(activity_target, reservation-token-1)` result, but only the reserve call
reached the primitive port; `move_to` failed with `sequence_entity_unavailable`.

Cause: `ActivitySequenceExecutor.ResolveArguments` applied
`FlowValue.IsAvailableIn(startContext)` to every resolved entity, including a
sequence-created reservation token that cannot exist in the immutable start-time
scene snapshot.

The executor now keeps producer/result-descriptor provenance with sequence result
bindings. It waives the start-snapshot entity check only for a bound
`targetToken:entity(activity_target)` produced by `reserve_nearest` and consumed by
an argument whose descriptor requires that exact producer and entity kind. The
value is passed unchanged to the primitive port for live reservation/owner/revision
validation. Literal IDs, unknown bindings, another producer's entity, and a
wrong-kind reservation output remain rejected before movement dispatch.

Focused verification: `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1 -Filter
ActivitySequence` passed 31/31 on 2026-10-23. No broad suite rerun, Unity Editor,
host implementation, or Play Mode check was performed for this fix.

Scoped review passed in `reviews/ActivityCatalog-2/review.md`: the original
regression is addressed, no new findings were reported, and the reviewer verified
five targeted regressions plus two ordinary-entity probes. Work is released at the
Activities source boundary and idle pending full W2 integration. DBMS pinned-release
integration and the previously documented ContentData child-process path remain
separate.
