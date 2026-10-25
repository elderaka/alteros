---
title: Topic Playground Handoff
tags: [alteros, handoff, dialogue, testing]
updated: 2026-10-22
status: partial-implementation
---
# Topic Playground Handoff

Related: [[Home]], [[Live Conversation Journal]], [[Stats and Mood]], [[Dialogue Catalogue]], [[Semantic Index]], [[SessionZero]].

## Purpose and authorization boundary

The approved bounded slice is a Twine-like Topic Playground: enter natural
language, supply supported synthetic context, inspect the actual authored
response and selection reasons, then traverse choices and continuations. It has
a Unity Editor window and a repeatable headless command, neither of which loads
the room during use.

The implementation is present, but acceptance is partial. Do not expand it into
the full stats/activity redesign or mark Session Zero complete.

## Current implementation and evidence (2026-10-16)

- Shared production rules now own confidence/fallback, local-hour eligibility,
  stable authored-variant selection, and canonical node traversal.
- The Editor window is available at `AlterOS > Dialogue > Topic Playground`.
  The batch launcher is `Tools/TopicPlayground/topic-playground.ps1`; sample
  scenario: `Tools/TopicPlayground/sample-scenario.json`.
- New Topic family integration is tracked in [[Topic Runtime Playground Handoff]].
  It adds `semantic-family` and clearly labeled `direct-family` test modes while
  preserving the old route/stat-context-v1 path. Both surfaces use the same
  session, context validation, shared family step executor, canonical selector
  and history lifecycle. Fixtures remain outside StreamingAssets.
- Latest source adds synthetic stats and a versioned numeric eligibility
  contract; see “Stat-foundation extension” below. Sentiment, activities,
  autonomous triggers, LLM, Agent tools, TTS and generated dialogue remain
  outside this slice.
- The current routing threshold/fallback are `0.45` and `unclear_short`,
  matching the present ModelTest serialized values. The tool uses shared code
  defaults rather than loading a scene override. The window shows score, best
  similarity, support count and fallback reason, but does not yet spell out the
  numeric threshold; the production policy has no winner-margin gate.
- Starting a new query clears the prior transcript and reroutes; restarting the
  exact same selected variant without inference is not exposed.
- The earlier Unity `AlterOS.Editor.Tests` run reported 36 passed, 0 failed;
  it predates the stat-foundation and family extensions. The current
  `Check-TopicPlayground.ps1` compiles the changed runtime and Editor sources
  and reports 13/13 focused tests; `Check-Stats.ps1` reports 31/31 legacy
  context regressions. These are source checks, not Unity import or Test Runner
  evidence. The registered `alteros@48a268a4` currently reports stale state and
  external changes dirty; the active ModelTest scene was preserved.
- The PowerShell script parses and sample JSON validates. The headless command
  intentionally refuses to start while the project Editor is open; it was not
  run so the existing unsaved Editor scene was not closed or replaced.
- Actual window/CLI report parity and a live GTE family query remain open.
  Both surfaces now call the same context builder, `QueryTopicFamilySteps` and
  `ExecuteFamilyScenarioStep`, and can load
  `Tools/TopicPlayground/Fixtures/topic-runtime-v1/deterministic-parity.json`.
  This is shared-code/source evidence only until both surfaces are run with that
  file and selected scenario plus diagnostics are compared.

## Responsiveness fix (2026-10-22)

See [[../Verification/2026-10-22 Topic Playground Responsiveness]] for the dated
source, compile, and targeted-test evidence.

`TopicPlaygroundWindow.OnGUI` no longer hashes the pinned GTE model. The session
starts one cancellable background SHA-256 over the model file using a 1 MiB
streaming buffer. The UI and command report read cached fingerprint/status only;
the window shows `Calculating...`, the completed digest, or an `Unavailable:`
error. Unity's asset postprocessor invalidates the cache only when the exact
model asset is imported, deleted, or moved. A change during hashing cancels and
discards the stale generation before calculating the new file. File length and
last-write time are checked before/after each hash to avoid publishing an
identity from a file that changed during the read. The CLI retains its
`modelFingerprint` field and adds `modelFingerprintStatus`; the fingerprint is
empty while pending/failed rather than being replaced with status text.

Verification: `Tools/TopicPlayground/Check-TopicPlayground.ps1 -Filter
TopicPlayground` passed 15/15; Unity 6000.4.4f1 script compilation completed
with 0 errors; focused EditMode job
`47bb00ad67ac46a8bff31432167393a8` passed 1/1. The regression uses temporary
files to verify repeated cached-display reads while the file is exclusively
locked, content invalidation/re-hash, visible failure, and retry. The actual
611,121,320-byte model was not hashed, and no GTE inference, rebake, provider
call, visual window inspection, or Editor repaint/Profiler comparison was run.
This is not a live UI performance or CLI/window parity pass. The Editor's
existing incremental model-load and query callbacks were inspected and left
unchanged; individual model-load/warm-up or inference steps may still occupy an
Editor update frame and need separate measurement.

## Stat-foundation extension (2026-10-16)

- `StatCatalogue` supplies every control ID and range. Each value is explicitly
  known or unknown; known zero is distinct from unknown. Reset clears stats and
  local-hour context. Named controls are explicitly synthetic presets.
- The context is detached. The Editor window never reads the system clock;
  local hour defaults to `-1` (unknown). No save, progression, mood, activity,
  environment, or player-evidence state is read or written. Emotional axes are
  input values only.
- `TopicPlaygroundContextBuilder` in the shared stat assembly validates both
  Editor controls and scenario JSON. `localHour` accepts `-1` or `0..23`;
  `fixtureSet` accepts only empty or `stat-context-v1`. Stat IDs, duplicate
  entries, known-value finiteness and catalogue bounds use the same
  `SyntheticStatSnapshotBuilder` diagnostics in both paths.
- Canonical JSON may contain `routing.eligibility`:

  ```json
  { "schemaVersion": 1, "all": [
    { "statId": "alterra.hydration", "operation": "at_most", "value": 25 }
  ] }
  ```

  Version 1 combines `all` with AND and supports inclusive `at_most` and
  `at_least` only. A missing value is unknown and rejects that condition;
  explicit zero is evaluated normally. Unknown IDs/operators/versions,
  `any`/`none`/`not` rule forms and nonempty legacy `requiredContext` /
  `blockedContext` tags are reported as unsupported. Invalid thresholds are
  reported as invalid. Each evaluated
  candidate exposes the consumed value (or unknown), threshold, outcome and
  reason in both the window and structured command report.
- Production uses the same selector/evaluator and read-only legacy adapter;
  currently only legacy energy and engagement can be known. Other catalogue
  values stay unknown. Player-context evidence is not accepted as a stat ID.
- Synthetic dialogue lives only under
  `Tools/TopicPlayground/Fixtures/stat-context-v1/Generic/`; it is not shipped
  authored content and does not enter StreamingAssets or the GTE index. It
  proves low hydration, high hydration and mixed emotional-axis cases. Uncovered
  routes, including fallback, still resolve from authored Generic. No semantic
  anchors changed and GTE was not rebaked.
- Sample scenario demonstrates `fixtureSet`, `stats`, and unknown hour.
  `expectedRouteId` stays empty: no semantic silver label is being asserted.
- Supplemental source checks cover unknown versus zero, need/axis boundaries,
  nonfinite/out-of-range inputs, unknown/rejected/unsupported conditions,
  context cloning and matching context validation. Separate EditMode fixtures
  cover context-dependent selection, fixture isolation, authored fallback, and
  sample scenario JSON against equivalent synthetic control inputs; they are
  added but not run after this extension because the Editor bridge is stale.
  Source checks do not prove Unity import, real GTE inference,
  command/window runtime parity, or visual layout.

## Project identity and reading order

Work only in `E:\Pleiades\alteros`. The chat may start in
`E:\Pleiades\pleiades.os`, which is archive-only. Read root `AGENTS.md` first.

Then read `Documentation/Home.md`, `Architecture.md`, `UnityProjectContext.md`,
the relevant `SessionZero.md` evidence, this brief, and the linked Live/Stats
notes. `Tracking/Feature Register.md` describes coverage, not executable rules.

## Implemented reuse points

- `Tools/SemanticLab/README.md`, `semantic_lab.py`, `semantic_router.py`:
  existing standalone GUI/CLI, routing diagnostics, silver cases and model setup.
  README benchmark values are historical, not fresh performance claims.
- `Tools/SemanticLab/bake_gte_anchors.py` and
  `Assets/AlterOS/Editor/GteSemanticToolbar.cs`: existing bake paths.
- `Assets/AlterOS/Semantics/GteSemanticRuntime.cs`, `DialogueRouteIndex.cs`,
  `GteSemanticQueryEncoder.cs`: shipped GTE/index loading, shared tokenization
  and inference input preparation, embedding and ranking behavior.
- `Assets/AlterOS/Dialogue/DialogueDefinition.cs`, `DialogueValidator.cs`,
  `DialoguePlayback.cs`, `AuthoredDialogueRouteCatalog.cs`: shared JSON contract,
  validation, deterministic node traversal and stable variant discovery.
- `Assets/Scripts/Dialogue/DialogueRouter.cs`, `DialogueManager.cs` and the
  actual condition evaluators: runtime variant eligibility, fallback and conversion.
- `Assets/StreamingAssets/Dialogue/Generic/<routeId>/*.json`: authored content.
- `Assets/AlterOS/Tests/`: existing route recovery and Canvas tests. Inspect
  current assemblies/test counts rather than copying historical totals.

`Tools/DialogueCoreCheck.cs` uses node/choice names that differ from the current
canonical model in the inspected source; verify compatibility before reusing it.
Do not present that old script as a currently passing headless runner.

## Slice one - scope

1. Load and validate real Generic JSON without modifying content.
2. Submit text, run semantic ranking, apply actual supported eligibility and
   variant selection, and resolve the same fallback policy as the game.
3. Present selected text/display text, supported choices, current node and
   completion. Support continue/choose; starting another query clears the
   visible transcript and starts a fresh route. Restarting the exact same
   selected variant without rerouting is not exposed yet.
4. Supply explicit synthetic local hour and catalogued stats. Unknown remains
   distinct from zero. Only versioned, implemented rules may consume values;
   unsupported context fields must be visible.
5. Explain scores/anchors, threshold/margin behavior actually used, considered
   variants, rejection reasons, chosen source/node, and fallback/no-content reason.
6. Expose that behavior through a command with structured results/nonzero
   failure status, and a Unity Editor window with transcript/context/diagnostics.
7. Allow a scenario to replay query/context/choice steps and assert expected
   route, variant, node or fallback. Never invent golden labels or equate a
   provisional silver-label match with human-approved correctness.

Scenario `steps` are ordered objects: `{"action":"continue"}` or
`{"action":"choose","index":0}`. Choice indexes are zero-based. An invalid
step is a scenario failure. `expectedRouteId` is optional; leave it empty for
exploratory probes that have not been human-labelled.

## Shared behavior, not a second router

Both surfaces must exercise the same production selection/eligibility/traversal
rules, not copied Python/C# condition implementations. Inspect whether minimal
pure-C# extraction is needed. Choose a viable headless host from installed tools;
Unity batch mode without graphics is acceptable if needed, but document that
it requires Unity. A standalone CLI must not be claimed unless actually runnable.

Existing Python inference can support research, but different export precision,
tokenization, pooling or scoring must be reported as a different backend, not
silent parity with Sentis. Include backend/model/index fingerprints in results.
Keep production behavior unchanged except for justified shared-rule extraction.

Model readiness, missing model/index and malformed content need explicit errors.
Do not fabricate a successful route when semantic inference was skipped. Label
fixture-only tests separately from real GTE inference.

## Baking and catalogue policy

Start with current pinned GTE model and baked anchors. A new window, context
fixture, stat threshold or mood label does not itself require rebaking.
Validate index/content compatibility; anchor/index changes require the existing
baker and regression checks. Do not rebake automatically on every query or
download/replace model packages unnecessarily.

Review affected catalogue coverage and conditions as tests reveal gaps. Do not
migrate all Intent/Tone/Topic IDs, rewrite the corpus or turn design notes into
runtime configuration as a prerequisite for the playground.

## Explicitly outside slice one

- Paid Live/LLM requests, tools, phone UI, TTS/audio and background generation.
- Full needs/emotion simulation, activity state machine, greeting queue,
  furniture use-point runtime or furnishing/house expansion.
- Autonomous state-triggered conversation. Later add a distinct event input;
  a synthetic state filter is not an autonomous trigger implementation.
- Animation/physical input/scene validation or proof of playable room behavior.
- New agent framework, imported Twine runtime, broad namespace migration.

The mood display is locked at a presentation level; stat IDs, emotion update
rules and thresholds are unfinished. The playground must expose unsupported
features honestly instead of filling those gaps with hidden assumptions.

## Acceptance and stopping conditions

- [ ] Run the documented command and sample in batch mode. The launcher and
  scenario exist and pass static parsing, but Unity must be closed first; the
  actual batch execution and exit code remain unverified.
- [x] The Editor menu opens the playground without entering Play Mode/loading
  the room (confirmed by Editor window metadata; no visual screenshot review).
- [x] Shared fixtures cover confidence fallback, eligible-next variant choice,
  malformed JSON diagnostics, time-window boundaries and canonical branching.
- [x] Versioned stat eligibility and isolated synthetic dialogue fixtures are
  implemented; legacy stat-contract source harness passes 31/31. The family
  integration adds a separate 13/13 focused source suite and Editor-entrypoint
  compile. Unity/EditMode fixture execution remains pending because the correct
  Editor bridge is stale.
- [ ] Add explicit threshold/margin explanation to the window and a same-variant
  restart control if these remain desired after first use.
- [ ] Recheck a live GTE query against the final warm-up code, and verify
  missing-model/index/fallback behavior with a real command run.
- [ ] Compare CLI/window route, variant and traversal for identical inputs.
- [x] Source review confirms the Playground consumes detached synthetic
  context only; it does not read the system clock, write saves/progression,
  update mood/activities, or infer hidden player signals.
- [x] Focused first-party Editor regression suite passes 36/36. The existing GTE
  runtime routing PlayMode test was not run in this continuation; keep fixture,
  inference, compile and visual evidence separate.

The earlier focused Editor fixture suite passed 36/36 before the stat and family
extensions. Current source evidence is 31/31 for stat-context regressions and
13/13 for the family adapter, plus standalone compilation of the modified
runtime and Editor entrypoints. None proves live GTE inference, Unity import,
visual behavior or actual CLI/window parity. Next acceptance step: after the
Editor reconnects, allow normal import/compile, run focused and full EditMode
tests, inspect the window, then compare the same deterministic family scenario
through window and CLI when safe. See [[Topic Runtime Playground Handoff]]. Do
not close, save, or discard the current scene just to satisfy this check.

## Handoff results required

Provide changed paths, exact launch/menu instructions, supported context fields,
scenario format/examples, tests and real inference checks run, evidence of
cross-surface agreement, known gaps, and next catalogue decisions exposed.
Update linked feature/maintenance notes. Do not mark Session Zero complete.
