---
title: Topic Runtime Playground Handoff
tags: [alteros, topics, handoff]
reviewed: 2026-10-22
status: integration-partial
---
# Topic runtime: Playground handoff

Related: [[Topic Playground Handoff]], [[Topic Runtime Catalogue Handoff]],
[[Topic Runtime Implementation Plan]]. The Playground integration is additive;
`Assets/AlterOS/Topics` remains read-only.

## Shared API

New public types live in `AlterOS.Topics` under `Assets/AlterOS/Topics`.
Use this runtime from both CLI and window; do not copy selection/lifecycle logic.

```csharp
var errors = TopicDefinitionValidator.Validate(definition);
// Stop on errors. Resolve a stable occurrence ID, not a label hash.
var instance = TopicInstance.Create(definition, occurrenceId, "authored");
var result = TopicScenarioSelector.Select(definition, instance, context,
    history, nowUtc, draw);
if (result.SelectedScenario != null)
{
    var handle = history.BeginPlayback(definition, instance,
        result.SelectedScenario.scenarioId, nowUtc);
    // Only after the presentation adapter actually delivers a node:
    history.ReportPresentedNode(handle, deliveredNodeId, nowUtc);
    // When the actual branch ends successfully, not when routing succeeds:
    history.Complete(handle, completionUtc);
}
```

The snippet assumes all delivered nodes are reported in order. Complete rejects
undelivered/nonterminal paths. Choices with an empty next target are exit-capable;
the trusted adapter must only complete after an actual exit choice. Use
`Cancel(handle)` or `Fail(handle)` on interruption/failure. Superseded or foreign
handles cannot consume a successor. Handles freeze graph transitions and policy;
Consecutive same-node acknowledgements return Duplicate without another durable
effect, but advance the monotonic timestamp fence (including self-loop delivery).
selection does not freeze mutable definitions, so don't mutate between selection
and BeginPlayback. All calls belong to one game-owned thread.

`TopicContext`: `stats` (`StatSnapshot`), `localHour` (-1 unknown), explicit
boolean `facts`, `objectsComplete`, `objects` (`TopicObjectSnapshot`: id, kind,
flags, numbers). Preserve absent vs zero. Empty complete inventory means no
object; incomplete inventory means unknown unless a match is proven. Player
evidence is not an Alterra stat; adapters may supply bounded explicit facts but
must preserve uncertainty/expiry and user settings. No sentiment inference here.

Expose candidate `Gate` tree/Truth/Reason/bound object, effective weight, applied
modifiers, winning priority and supplied draw from `TopicSelectionResult`.
No selection is a normal result; the caller owns fallback. Explicit deterministic
draw in [0,1) enables parity. Seed generation belongs to the adapter, not core.

## Detached history and durability

Playground defaults to `new TopicHistory()` per isolated session. Do not touch
player saves. For explicit scratch persistence use `TopicHistoryRepository.Load`
and `Save` with a caller-owned path. Failure has `Success=false`, error, and no
replacement empty history. Missing file is a successful empty load with Missing.
After mutation `history.IsDirty` / outcome `RequiresSave` stays true until Save
succeeds, including duplicate completion after a failed save. In-memory consumed
does not mean durably saved. Save immediately after a consumption checkpoint
before reporting durable completion. Persist retries; do not replay rewards.

History stores metadata only, no transcript/keys/generated text. It has a separate
schemaVersion1, 4MiB/10000-record limits. Same-directory temp write/flush/replace
keeps a `.bak` on overwrite. Malformed/future-version and stale snapshots fail
closed without resetting originals. No automatic backup recovery. One writer
owns the file; fingerprint conflict detection is not multi-process locking or
cross-device synchronization. No auto-creation of parent directories.

## Playground integration (2026-10-16)

`TopicPlaygroundSession.QueryTopicFamilySteps` now owns the family path called by
both the Editor window and batch command. Stage 1 embeds the natural-language
query and each family's shared anchors with the warmed GTE runner. The initial
matcher uses maximum cosine similarity, the existing `0.45` minimum and stable
topic-ID tie ordering; diagnostics include each family's best anchor and
support count. It does not read or write baked GTE anchors, and no rebake was
performed. Stage 2 converts the same detached `TopicPlaygroundContext` into a
`TopicContext`, then calls the shared `TopicPlaygroundTopicHarness` and
`TopicScenarioSelector`. Direct family selection uses the same Stage 2 path but
reports `semanticRoutingAttempted=false` and is not semantic-routing proof.

The integration uses synthetic files only:

- `Tools/TopicPlayground/Fixtures/topic-runtime-v1/Topics/beverage.drink.json`
- `Tools/TopicPlayground/Fixtures/topic-runtime-v1/Topics/state.rest.json`
- `Tools/TopicPlayground/Fixtures/topic-runtime-v1/deterministic-parity.json`

These fixtures are outside `StreamingAssets`. They exercise needs, emotional
axes as inputs, explicit facts, complete/incomplete object snapshots,
same-object property binding, shared-anchor family ranking, priority tiers,
weights, modifiers, occurrence history and dialogue traversal. They are not
migrated or published production catalogue content.

The shared context builder accepts list-based facts and object properties with
explicit `isKnown` markers, preserving JSON serialization. Unknown facts are
omitted from `TopicContext.facts`; unknown object properties are omitted from
that object's dictionaries; unknown never becomes false or zero. Player
sentiment/evidence is not represented as a StatCatalogue ID or slider. Local
hour remains synthetic and independent from event timestamps.

Use `topicMode` values `generic-route`, `semantic-family`, or `direct-family`.
Omitted/empty mode keeps the old generic route path. Family scenarios add
`topicFixtureSet`, `topicFamilyId` (direct mode only), `instanceId`, `source`,
optional `replayPolicy`, `draw` in `[0,1)`, ISO-8601 `selectionTimeUtc`,
`facts`, `objectsComplete`, and `objects`. Batch `steps` additionally support
`present`, `complete`, `cancel`, `fail`, `retry`/`reselect` (same occurrence),
and `newOccurrence` with a different `instanceId`; `secondsAfterStart` is
finite, nonnegative, and relative to the scenario's base time. `choose` and
`continue` move the preview cursor only; `present` explicitly acknowledges a
delivered node. In family modes, `expectedRouteId` compares against the selected
scenario ID.

The window's **Load scenario JSON into window** reads that same DTO and can run
the scripted steps. Both window and CLI call the same session step executor,
selection rules, context validator, and lifecycle adapter. No live progression,
saves, system clock, activity, player-signal inference, LLM or TTS are involved.

Batch invocation (run only after the active project Editor is safely closed):

```powershell
& 'Tools/TopicPlayground/topic-playground.ps1' `
  -Scenario 'Tools/TopicPlayground/Fixtures/topic-runtime-v1/deterministic-parity.json'
```

The launcher deliberately refuses while the project Editor is open. The
parity fixture uses `direct-family`, so it tests identical Stage2 inputs and
diagnostics; it does not demonstrate Stage1 semantic routing.

History defaults to detached in-memory. Optional persistence is opt-in and
requires a caller-selected file beneath `%TEMP%/AlterOS-TopicPlayground`; other
paths, including player saves and `StreamingAssets`, are rejected. Save errors
remain `PENDING SAVE (not durable)` and keep `TopicHistory.IsDirty`. The sample
parity file leaves `scratchHistoryPath` empty.

## Scenario runner preview (2026-10-19)

The additive Scenario Conversation slice now has two fixture entry points:

- In the Topic Playground Editor window, **Load Scenario fixture** opens a JSON
  file under `Tools/ScenarioRuntime/Fixtures/Playground/`. It can run without
  loading GTE and applies one fixture event at a time with the shared
  `ScenarioPlaygroundSession.Apply` method.
- The headless command is `Tools/ScenarioRuntime/scenario-playground.ps1
  -Fixture <path>`. It uses the same scenario/event/time DTO and session method,
  emits a `SCENARIO_PLAYGROUND_JSON` report, and refuses to launch while the
  project Editor is open.

Fixtures cover a cat Ask/answer/closing, drawing and coding with clearly
simulated activities, and a player-ended diversion. The selected-authored-Topic
preview uses `AuthoredScenarioAdapter` but does not touch TopicHistory; the
separate family preview remains the authoritative test for Topic acknowledgement,
consumption checkpoint, cancellation and completion. Scenario preview never
calls an LLM, provider, TTS or gameplay activity. `.nodes` stay authored Topic
content; conversion occurs in memory and does not rebake semantic anchors.

Source evidence: Scenario suite47/47, changed Playground suite3/3, existing
family suite13/13 and unified conversation suite161/161. Unity imported the
scripts and five focused adapter/Playground EditMode tests passed. The Console
still records two serialization-depth diagnostics for recursive
`TopicCondition.children` in the existing `m_TopicFamilies` EditorWindow field.
Visual rendering, live GTE and batch CLI execution remain unverified. See
[[Scenario Conversation Handoff]] for the resume gates.

## Current evidence and next gate

- `Tools/TopicPlayground/Check-TopicPlayground.ps1 -Filter TopicPlayground`:
  15/15 focused NUnit source checks pass; it compiles the changed runtime,
  Editor/window, command, and fingerprint helper sources. Existing `CS0649`
  warnings are emitted for DTO fields. The supplied draw remains double
  precision, validates `[0,1)`, and preserves the near-boundary selection.
- 2026-10-22 responsiveness fix: the repaint path reads a cached fingerprint
  snapshot; a cancellable background worker streams the model-file hash, and an
  exact-path asset import/delete/move invalidation keeps it current. See
  [[../Verification/2026-10-22 Topic Playground Responsiveness]].
- Unity 6000.4.4f1 imported scripts and compiled with 0 Console errors. Focused
  EditMode fingerprint test passed 1/1 (job
`47bb00ad67ac46a8bff31432167393a8`). The active `ModelTest` scene was kept
  open; no scene/prefab edits, batch-mode launch, or full Editor suite were run.
- The stat-context-v1 31/31 and Topic Runtime 33/33 source results above are
  earlier recorded checks, not rerun in this responsiveness slice. The recorded
  full Unity suite (105/105) also predates this integration.
- `.superpowers/sdd/Stats Foundation Implementation Plan/Check-Stats.ps1`:
  31/31 existing stat-context-v1 checks passed after the earlier integration.
- `Tools/TopicRuntime/Check-TopicRuntime.ps1`: 33/33; `Assets/AlterOS/Topics`
  was not modified; this suite was not rerun for the responsiveness fix.
- Not verified: actual GTE family inference, runtime JSON import via the live
  Editor, scripted window/CLI report equality, visual/repaint profiling, and
  live gameplay. Synthetic-vector ranking tests are not GTE inference. The
  actual 611 MB model was not hashed and no inference was run for this check.
- Scratch persistence rejects reparse-point ancestors and rechecks the explicit
  temp-only target before every save. A path-validation failure remains visibly
  `PENDING SAVE (not durable)` and leaves history dirty. The code compiles; no
  adversarial filesystem-link integration test was run.

Next gate: after the correct Editor reconnects and imports current scripts, run
focused EditMode and full Editor suites, then load `deterministic-parity.json`
in the window and pass the same file to the documented CLI. Compare per-run
selected scenario, candidate traces, lifecycle, history and draw. Verify live
GTE inference and visual behavior separately. Stop before gameplay loaders or
production catalogue content are changed.

## Acceptance fixtures for this integration

- `TopicPlaygroundFamilyTests` covers unknown vs zero, unknown emotion-axis
  values, context validation, same-object dispenser rejection, fixture
  isolation, weighted draw/modifier traces, once/checkpoint cancellation,
  independent new occurrence, completion-time cooldown, and failed scratch
  persistence remaining dirty.
- Scenario JSON and equivalent window-control DTOs produce the same selected
  scenario, winning priority, draw and candidate diagnostics through the shared
  runtime harness. Actual Editor-window/CLI execution parity remains pending.
- The exact-double regression test selects `casual_a` immediately below the
  2/3 weight boundary; float-rounding the same draw selects `casual_b`.
- Keep existing stat-context-v1 fixtures/schema working. New family context is
  a separate adapter contract, not a silent reinterpretation of old scenarios.
