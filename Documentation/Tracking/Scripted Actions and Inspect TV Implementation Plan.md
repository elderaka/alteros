# Action Framework Foundation Implementation Plan

> **Latest workflow2026-10-22:** [[Reusable Content Framework Workflow]] is the
> current cross-domain planning target. It adds three proofs and minimal Item/grant
> authoring; Turn on TV now ends after power succeeds, while Fetch a drink proves
> return + Scenario dialogue. Earlier TV-return fixture steps in this baseline are
> superseded. Do not execute this one-fixture baseline before the new domain plans
> are consolidated and implementation is explicitly approved.

> Purpose clarified by the human2026-10-22: prepare reusable runtime and authoring
> contracts for a Workbench covering Dialogue, Scenario, Topic, Furniture, Item and
> Action definitions. **Turn on TV is one proof fixture, not the framework's API or
> main deliverable.** The historical filename is retained for links. The human
> approved this design direction and requested implementation planning; this document
> plans that work, not a claim that it is already implemented or an execution START.

> Current acceptance is **Turn on TV**, superseding passive inspection. The
> Furniture/DBMS read-only consultation is incorporated in A2F/A2P/A2V below.
> Execute in order A0/A1/A2/A2F/A2P/A2V/A3/A5/A4/A6 after human approval.
> A5 builds/tests the shared executor before A4 connects its SQL/tooling preview.
> Both thin C# composition and data recipes use one execution. Off/On with visible
> feedback is approved; channels/sources/playback are deferred. Filename is retained
> to preserve existing links. This plan is awaiting review, not implementation START.

> **For agentic workers:** Use `executing-plans` task-by-task in the existing
> **Alterra Behavior Session** (formerly LLM Planning). Read the spec and DBMS refresher before coding. This
> plan is ready for human review, not implementation START until explicitly approved.

**Goal:** Implement a reusable Action composition/execution foundation and its
Workbench-facing discovery, validation and headless-preview contract. Prove reuse
with C# and data-authored fixtures, with Turn on TV as one integrated acceptance case.

**Architecture:** Extend ActivityHost and the existing Scenario/compiled-flow
execution path, never a second runner. A registered script executes mechanics;
SQL-authored definitions supply ordered typed steps, configuration and text references.
Turn on TV is tested as both a thin `TurnOnTV.cs` framework composition and an
equivalent data recipe. Both resolve to the same validated typed sequence and
`activity_sequence` execution; data-only content must not require a new C# class.
The live room
registry supplies authoritative availability/bindings; the host validates them
again at execution and continuation.

**Tech Stack:** Unity6000.4.4f1, current C#/.NET and SQLite-net package, existing
authoring/compiled-content stores and command host. No new framework or provider SDK.

**Spec:** [[Scripted Actions and Inspect TV Design]],
[[Scenario Conversation Design#Scripted Activity checkpoints and pose safeguard (2026-10-22)]],
[[Content Workbench Shared Contract#Workbench-wide target and readiness (2026-10-22)]].

## Deliverable and proof boundaries

The deliverable is installed primitive/function descriptors, typed versioned Action
definitions with stable references, one pure sequence execution path, lifecycle /
failure/checkpoint semantics, revision/CAS storage and commands used by both tools
and game adapters. TV-specific labels, selection rules and step ordering stay in
fixture content or its thin trusted composer, not the framework engine.

Proof requires more than the TV looking correct: execute a different synthetic
non-TV recipe without adding an executor or changing a factory/capability switch;
show a recipe edit changes order/duration/text without C# changes; compare composer /
data/headless/runtime traces. TV then proves the real furniture-function, persistence,
VN entry/exit and visible-state integration. A full Workbench UI, general Item
gameplay and every future furniture mechanic are separate slices, not blockers here.

## Global constraints

- Work only in `E:/Pleiades/alteros`; archive `pleiades.os` is out of scope.
- Read AGENTS.md, TODO.md and Home.md. Preserve concurrent/shared edits. No broad
  staging/commits, worktree copy, scene reset, YAML/meta edits or package installation.
- Lead: Alterra Behavior Session `01a144fe-5ddc-7291-b24e-ff2e4e0f4162`.
  Furniture and Environment Session `01a15376-d6f0-7c41-b9e5-fcdee723e87b`
  (formerly Room Testbox) owns furniture/scene integration.
  It reported idle and Editor released at planning time. Confirm ownership before
  touching shared Activity/furniture files or Editor assets; use one writer, not
  simultaneous edits. No automatic separate Room implementation task is implied.
- The human explicitly authorizes Alterra Behavior to ask DBMS Session and Furniture
  and Environment Session for technical clarification and those owners to reply.
  Use [[Multi-Agent Coordination]]; ask owners before routing implementation questions
  to the human. This is not authority for new scope or overlapping edits.
- Topic Playground `01a142e1-455c-7952-bfcf-14186d64b463` / local remains the sole
  writer for Tools/ContentWorkbench/**, including the A4 command host, build script
  and command tests, not just the frontend. Alterra Behavior supplies stable pure
  contracts/source lists/fixtures; it must not edit those tooling files itself.
  Main coordinates the A4 handoff/dispatch after human START approval; existing
  Behavior-to-DBMS/Furniture consultation permission is not silently expanded to
  unrelated sessions. No W2-W5 UI build or new Workbench task is started here.
- Reuse sole coordinator/transcript and Activity/Scenario owners, current SQL APIs,
  game-wide F5 flag, T interaction/E inventory/Q placement contract and authored UI.
- No credentials, real profile migration, provider/TTS requests, cloud work, automatic
  playback publication or full-room redesign. Any Play Mode test requires a safe
  isolated profile or explicit human-approved profile use; no silent real-save run.
- Source tests, Editor compilation/tests, content installation and player-visible
  acceptance are separate gates. Never describe source-only success as gameplay.

## DBMS refresher and verified starting seams

Read [[SQLite Authoring Cutover Implementation Plan]],
[[../Verification/2026-10-22 SQLite Authoring Core]], [[Content and Interaction ERD]],
[[Content Data Integration Handoff]], [[Full SQLite Cutover Implementation Plan]],
and current source before trusting older session assumptions.

- Editable shared content/drafts: `Tools/ContentWorkbench/Workspace/authoring.sqlite3`,
  `Assets/AlterOS/Authoring/AuthoringRepository.cs`, authoring schema1. JSON is
  explicit import/export, not a second live writer. Use revision/CAS commands and
  explicit selection; never direct SQL patches or overwrite concurrent revisions.
- Current documented checkpoint is store revision63 /439 sources, export
  `Content/AuthoringExports/02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27/`.
  These are dated evidence, not invariants; capture current identity/revision first.
- `AuthoringContentSourceProjection`, `ContentReleaseCompiler`, `LegacyContentAdapter`
  and `ContentBuildProcessor` compile a pinned selected snapshot. Compiled schema4,
  manifest2 at baseline; do not bump schema just to add a compiled behavior ID.
  If storage/wire representation changes, add explicit compatibility/version tests.
- Runtime uses a validated pinned content release through `RuntimeContentStore` /
  `ContentRepository`. Inspect actual installed artifact/readiness; user reported a
  corrected Editor build, while older docs still say expression/install blocked.
  Do not restore obsolete 104-expression blockers without fresh evidence.
- Separate private gameplay, conversations, generated-content, Python-memory and
  credential stores remain separate. This task is not permission to merge them.
- `ActionRecord.contractJson` currently derives capability/arguments from Scenario
  action steps. Do not mistake these per-step request records for registered scripts.
- TopicRecord.entries already has flowRef/conditionJson. Use that semantic-to-flow
  boundary. Existing TopicScenario's legacy embedded-dialogue form must keep working.
- `FurnitureRegistry.TryReserve(useId, requestedObjectId, activityId, out binding)`
  and `IsValid/Release` provide shared claims. Current ActivitySceneHost capture only
  exposes Movement/Drink; placed proxies need live functional-point registration.
- `IActivityExecution` has Start/Tick/Stop/BeginExchange/EndExchange/Shutdown/DrainEvents.
  ActivityEvent already has checkpointId. ScenarioActivityAdapter drops intermediate
  events today. Extend both DTO and compiled-flow routes, not only test scaffolding.

## File and interface map

New focused runtime files under `Assets/AlterOS/Activities/`:

- `ActivityScriptRegistry.cs`: immutable trusted script descriptors/factories;
  definitions can reference only installed scripts. No runtime C# evaluation.
- `ActivityContinuation.cs`: request/result DTO and optional continuable execution
  port; existing Movement/Drink need not implement a fake continuation.
- `ActivityPresentationRequests.cs`: non-modal bark requests and pose ownership.
- `ActivityPoseSafeguard.cs`: clock-driven post-end guard, no MonoBehaviour/UI logic.
- `ActivitySequenceDefinition.cs`: typed serializable ordered operation/config DTOs.
- `ActivitySequenceValidator.cs`: allowlisted operations, typed bindings/references,
  bounds and declared checkpoint/continuation validation before execution.
- `ActivitySequenceExecution.cs`: one reusable IActivityExecution with bounded
  per-Tick step progression; no TV-specific executor or duplicate Scenario owner.
- `ActivitySequenceBuilder.cs`: typed builder plus `IActivitySequenceComposer`
  contract `Compose(ActivitySequenceBuilder builder, ActivitySettings settings)`;
  trusted registered C# composition produces the same sequence definition as data.
- `TurnOnTV.cs`: thin IActivitySequenceComposer using those framework functions.
  No direct scene lookup, navigation/Animator manipulation, text literals or runner.
- `ActivitySequencePorts.cs`: portable typed target/movement/bark/function/pose ports,
  pure bindings/results and clock injection. Physical Unity ports are adapters;
  synthetic preview supplies detached ports to the exact same sequence executor.

Extend existing ActivityHost/contracts/settings/definition/scene host/registry,
ScenarioActivityPort/Definition/Validator/Session/ContentFlowExecutionState/Adapter,
and scene-side `DirectConversationBridge` partials. Split a focused partial rather
than adding a new manager or more unrelated code to the large main bridge.

Extend existing `Authoring/`, `Content/`, ContentBuildProcessor and Workbench command
sources only for the new validated definitions/references. Preserve pinned snapshots,
compiler/reader parity, metadata, bilingual rows and unchanged existing fixtures.
Room-owned functional-point integration is in `Assets/Scripts/Furniture/` (current
handling controller is `FurnitureHandlingController.cs`, not a nonexistent placement
controller). Furniture changes use the existing shared catalogue and saved-layout owner.

## Review focus

1. Unknown room data must not become no TV; catalogue/inventory ownership is not placement (A2).
2. TV moved/removed/claimed between selection and execution must fail truthfully and release only this owner's claims (A2/A5).
3. Duplicate/out-of-order checkpoints, answers and events must not replay effects/barks or revive ended executions (A3).
4. SQL drafts/release mismatch or unsupported nested fields must not silently drop data or enable a JSON fallback (A4/A6).
5. An old30-second timer must never reset an active Scenario/new Activity; cancellation/quit still cleans promptly (A5).

## A0 — Reorient and pin the baseline

**Files:** listed refresher/source seams; new dated evidence note
`Documentation/Verification/2026-10-22 Scripted Actions and Inspect TV.md`.

- [ ] Read refresher; record actual authoring store/build-set snapshot, installed
  release readiness, script capability registry and current test commands. Use
  `authoring.info/list/read` through the command host; do not read private data.
- [ ] Inspect `Tools/ContentWorkbench/CoreHost/WorkbenchCommands.cs` and current
  command envelopes before invoking; preserve UTF-8 stdin/stdout and conflict hashes.
- [ ] Run Scenario, Unified Conversation, Furniture and Authoring source baselines
  using commands in A6; separate pre-existing errors. Confirm shared file/Editor
  ownership. Record no provider call and no real profile access.
- [ ] Write the evidence note's baseline and source/Editor/live acceptance axes.

## A1 — Registered reusable script contract

**Files:** new `ActivityScriptRegistry.cs`; existing ActivityContracts.cs,
ActivityDefinition.cs, ActivityHost.cs, ActivitySceneHost.cs; new
`Assets/AlterOS/Tests/Editor/Activities/ActivityScriptRegistryTests.cs`.
Define the pure ActivitySequenceDefinition/Builder/Validator/Ports/primitive
descriptor contracts here; A5 implements the shared execution over those contracts,
then A4 exposes it through SQL authoring/tool commands.

**Interfaces:** `ActivityScriptRegistry.TryResolve(string scriptId, out
ActivityScriptDescriptor descriptor)`; descriptors carry stable script/capability
IDs, argument requirements, declared checkpoint/continuation vocabulary and factory.
ActivitySettings receives optional scriptId/configuration via a backward-compatible
constructor/copy path. Keep Movement/Drink enum values and serialized assets valid.
An Action definition is keyed by its revision-pinned ContentRef, not capabilityId.
`ActivityRequest` gains optional `actionDefinitionRef` with a backward-compatible
constructor; new authored requests must supply it. Legacy capability-only requests
resolve to exactly one registered default definition. Multiple definitions may share
one capability/script; capability snapshots deduplicate them without losing selection.
The shared `activity_sequence` descriptor supports typed operations `reserve_nearest`,
`move_to`, `face`, `bark`, `wait`, `invoke_furniture`, `move_near_player`, `checkpoint`, `complete`.
Binding/target properties use registered typed references, not reflection/evaluated
expressions. `move_to` can name an authored `onStartedBarkRef`, emitted once only
after movement is accepted, before arrival. The wait clock yields across Ticks.
Coordinator bounds for this slice: at most64 steps, finite waits from0 through300
seconds; no arbitrary loops/recursion/code. Checkpoint waits for a player are governed
by the active Scenario, not these finite timed-wait durations or the post-end guard.
Definitions select exactly one composition source: inline typed `steps`, or a
registered trusted `composerId`; reject ambiguous both/neither for sequence Actions.
The composer (for example `turn_on_tv`) builds ActivitySequenceDefinition via
ActivitySequenceBuilder before execution. Validate its output with the exact same
validator as data, then use the same ActivitySequenceExecution factory. The builder
declares operations; it does not move actors during composition. Never load loose
C# from content or pretend arbitrary trusted C# is sandboxed by this interface.
Sequence DTOs, descriptors, builder/validator/executor and their port signatures must
be BCL-only, so the command host can reuse them without Unity DLLs. Existing
ActivityContracts.cs mixes plain contracts with Vector3-based IActivityMotion:
split only the pure required DTO/lifecycle interfaces into a focused
`ActivityExecutionContracts.cs` if necessary, preserving namespace/signatures and
serialized enum values. Adapt the existing Unity motion/registry ports in
ActivitySceneHost; do not replace those systems or port their logic to JavaScript.

- [ ] Red tests: duplicate IDs, missing implementation, malformed/unknown parameter
  and invented capability rejected; two definitions reuse one factory; legacy
  Movement/Drink still request and execute unchanged.
  Also assert two definitions sharing a capability select different pinned config/
  bark references, missing/stale definition revisions fail closed, and a definition
  cannot claim a script/capability combination its trusted descriptor does not allow.
- [ ] Run focused EditMode tests to observe the intended failures, not compile noise.
- [ ] Implement registry-backed dispatch and descriptor-based argument validation.
  Replace M3-only Supports/name switches in touched capture paths with the registry;
  adding another registered script must not require another central TV-name switch.
- [ ] Green focused tests and ActivityHost regressions; unsupported definitions
  stay unavailable rather than bypassing host capabilities.

## A2 — Live furniture presence and executable availability

**Files:** FurnitureRegistry.cs, FurnitureUsePoint.cs, ActivitySceneHost.cs;
focused new `ActivityFurnitureAvailability.cs`; Room-owned FurnitureHandlingController /
FurnitureSceneAdapter glue; furniture catalogue via its current approved owner;
new `ActivityFurnitureAvailabilityTests.cs`, existing FurnitureRegistry tests.

**Interfaces:** `ActivityFurnitureAvailability.Evaluate(string capabilityId)` returns
presence known/unknown, present instance IDs, usable bound point IDs, rejection
reasons and snapshot revision. Presence comes from placed-instance/catalogue state;
executable usability also requires registered script, authored use-point, NavMesh
reachability and available shared claim. Project to TopicContext objects and
host-owned Scenario facts without an LLM asserting either.

- [ ] Red tests for zero/two TVs, closest unreachable vs farther usable, equal
  distances, stored-but-unplaced TV, incomplete room, busy TV and a non-TV descriptor.
  Verify adding/removing/moving confirmed furniture updates the next snapshot.
- [ ] Run those tests to confirm gaps. Never infer absent from a filtered usable list.
- [ ] Implement generic registered-point discovery/update and current-room snapshot
  capture, including dynamically placed units. Use stable IDs/catalogue-local point
  positions/facing, not names, fixed scene arrays or hardcoded world coordinates.
- [ ] Configure the TV's reachable approach/pose/view/exit through the existing
  functional-point framework. This supplies physical target/reservation binding,
  not a separate passive-inspection capability or executor. A2F declares supported
  `power.set` function/use metadata; the Action is `activity.turn_on_tv`. Existing
  `activity.watch_tv` viewing metadata grants neither that function nor execution.
- [ ] Green tests; reserve/revalidate at start, deny edit while reserved, and ensure
  no second registry/claim owner bypasses current furniture-edit protections.

## A2F — Typed furniture-function contract and shared power handler

**Writer:** Furniture and Environment, with an explicit Activity-port handoff to
Alterra Behavior. **Files:** new `Assets/AlterOS/Furniture/FurnitureFunctions.cs`,
`FurniturePowerFunction.cs`; existing catalogue DTO/validator/registry, shared
catalogue and FurnitureSceneAdapter; new FurnitureFunctionTests.cs and fixtures.

**Interfaces:** `IFurnitureFunctionPort.Invoke(FurnitureFunctionRequest request)`
returns FurnitureFunctionResult with applied/no_change/replayed/unavailable/conflict /
failed status, committed state evidence and safe diagnostic code. Request carries
functionId, exact bound instance/use-point identity, reservation owner, expected
instance/layout/game revisions, run/activity/sequence-step IDs, stable operationId
and typed desired power. Separate physical-point revision from persisted instance
revision; never compare them as if they were the same counter.

- [ ] Red tests for unregistered function, unsupported definition/use, wrong actor /
  reservation, unknown target, stale binding and forbidden argument/state assignment.
  A declaration alone never installs a handler or grants authority.
- [ ] Register function `power.set` with desiredState Off/On only. Compatible furniture
  definitions opt in with typed metadata/handler contract; no switch over TV model
  names. Choose resource key `device.power` for this new contract: known values0/1
  mean Off/On. Do not repurpose `enabled` or `conditionState`. Invalid known values
  fail closed; missing/known=false remains unknown, not implicitly Off.
- [ ] Implement shared handler over the existing exact-target claim/capability ports
  and injected typed persistence port implemented in A2P. Define IFurniturePowerStore /
  FurniturePowerCommand with the pure function contract here so this task compiles
  against a fake store before the DBMS adapter is added. Content/LLM cannot choose arbitrary
  resource keys, mutate resources directly or invoke reflected C# methods.
- [ ] Define two compatible synthetic TV definitions using the same registered
  function. Different actor instances keep independent state; already-On is no-change.
- [ ] Green tests and catalogue backward-compatibility checks. Add optional typed
  function metadata through the existing catalogue owner; if versioning is needed,
  retain explicit reading/validation for old definitions instead of silent defaults.

## A2P — Atomic per-instance power state through the existing game writer

**Writer:** DBMS, with a typed port contract agreed with Furniture; no new store.
**Files:** FurniturePersistenceAdapter.cs, GamePersistenceContracts.cs and the
existing SqliteGameRepository receipt path only where needed; new focused power
persistence tests in the current persistence harness.

**Interfaces:** `IFurniturePowerStore.SetPower(FurniturePowerCommand command)` returns
the typed durable result used by A2F. Command contains the stable operation identity,
fixed target/definition/revisions and validated desired power. Reuse the same adapter /
IGameRepository/CAS/operation-receipt transaction chain as furniture home writes.

- [ ] Red synthetic SQLite tests: exact instance Off->On persists after reopening;
  resource unknown can be explicitly set known-On without fabricating an old value;
  unrelated state, inventory, other furniture and progression remain byte/semantic
  equivalent. Test removed/orphan/unsupported target and stale game/layout/instance CAS.
- [ ] Commit resource update, incremented target instance/layout revisions, typed
  `furniture_power_changed` event and operation receipt atomically. Event includes
  target instance, prior known/value, new value and originating actor/run/activity /
  step/function identity. Use allowlisted typed event input, not an arbitrary caller
  eventKind string. Fix the current generic home-event descriptor seam narrowly.
- [ ] Retry the same operation ID/payload returns its recorded result without a
  second state-change event; same ID/different payload conflicts. Already-On returns
  no-change and no power-change event. Persist its receipt using the existing game
  command/receipt mechanism if a new command is accepted; do not fabricate a power
  transition solely to obtain a receipt. The adapter currently derives payload from
  candidate layout bytes: ensure this function's stable command payload includes its
  identity/intent rather than recomputing it from later layout state on retries.
- [ ] Fault-injection tests show rollback leaves old state/event/receipt/visual truth
  unchanged. Cancellation after committed power does not roll it back; only a later
  validated power command may change it. Do not mark return/Scenario success early.
- [ ] Prefer existing home schema2 resourceState within game schema3, no new table
  or schema bump merely for a registered resource. New structured fields/tables, if
  genuinely required, need the existing explicit home/game migration path and tests.
  No real save migration. Old saves lacking the key remain unknown, not reset.
- [ ] Green persistence/receipt regressions and reopen tests; provide the exact port
  and event contract to Furniture/Behavior, not raw SQL usage instructions.

## A2V — Visible state comes from committed per-instance truth

**Writer:** Furniture and Environment, exclusive Editor ownership for serialized
presentation. **Files:** new FurniturePowerStateView.cs under Assets/Scripts/Furniture,
the existing placed-instance/scene snapshot projection, focused Editor tests and
Editor-authored renderer/material references on the test fixture/template.

- [ ] Add pure projection tests for known-Off, known-On and unknown, and Unity tests
  for instance isolation with shared materials. No preview ghost or another object's
  appearance may be changed by this instance's power notification.
- [ ] Bind the test TV's screen/indicator appearance to its committed resource state,
  including first load, dynamic placement and reopened synthetic home. Off/On must
  be visibly different; unknown stays honestly unknown. No media/video subsystem.
- [ ] Update only after durable success or after rereading committed state; simulated
  persistence failure never flashes On. Initial synthetic fixture state is explicit
  known-Off, seeded/placed through the existing writer, not runtime renderer defaults.
- [ ] Green projection/Editor tests; record actual Game View proof separately. No
  scene/prefab/YAML editing by shell, no runtime-constructed static HUD.

## A3 — Checkpoint dialogue and correlated continuation

**Files:** ActivityContinuation.cs, ActivityContracts.cs, ActivityHost.cs,
ScenarioActivityPort.cs, ScenarioActivityAdapter.cs, ScenarioDefinition.cs,
ScenarioDefinitionValidator.cs, ScenarioSession.cs, ContentFlowExecutionState.cs;
new `ActivityContinuationTests.cs`, existing ScenarioActivityAdapterTests.cs;
synthetic `Tools/ScenarioRuntime/Fixtures/Playground/activity-checkpoint.json`.

**Interfaces:** `ActivityContinuationRequest` carries runId, stepId, activityId,
checkpointId, continuationId. Host `Continue(request, context)` returns a typed
accepted/unavailable/conflict result. Optional `IContinuableActivityExecution`
accepts only declared continuation at its currently pending checkpoint. Add
`continue_activity` Scenario step with checkpointId/continuationId and existing
success/failure successors; activity identity is host-bound, never model invented.
Project declared intermediate checkpoints onto registered wait conditions using
run/activity/checkpoint correlation, not direct Scenario step-index mutation.

- [ ] Red tests: checkpoint -> ask -> same execution continues selected branch;
  stop-for-dialogue and light-loop policy both work; normal terminal wait unchanged.
  Duplicate event/request is idempotent, conflicting reply rejected, wrong/late
  activity/run/checkpoint cannot advance; cancelled/quit execution never resumes.
- [ ] Run focused tests red, then implement one-shot named checkpoints per execution
  for this slice; do not promise repeating checkpoint occurrences without identities.
- [ ] Preserve checkpoint events arriving before the wait is entered, correlate and
  consume once; bound/discard queues on end. Pending dialogue must not duplicate start
  or rewards. Implement both ScenarioDefinition and compiled-flow parity.
- [ ] Use an authored synthetic fake execution to prove the dance-style branching;
  no need a new production dance animation/activity. Run tests green.

## A4 — SQL-authorable definitions, references and release parity

**Writer split:** DBMS owns the Authoring/Content storage/compiler/Editor-build
changes and their ContentData tests; Alterra Behavior owns the pure Action contracts
and sidecar/Scenario integration; Topic Playground owns all changes under
Tools/ContentWorkbench/**, including authoring-command glue and command tests.
Agree exact file ownership before edits. A consumer finding a needed producer change
requests the owner rather than modifying the producer's files concurrently.

**Files:** AuthoringContracts.cs, AuthoringContentSourceProjection.cs,
ContentContracts.cs, ContentReleaseCompiler.cs, LegacyContentAdapter.cs,
ContentRepository.cs, ContentBuildProcessor.cs; WorkbenchAuthoringStore/commands
only if needed; AuthoringRepositoryTests.cs, HeadlessContentDataTests.cs,
Tools/ContentWorkbench/tests/test_commands.py and synthetic authoring fixtures.

**Interfaces:** Add validated `action_definition` authoring kind for
`{schemaVersion:1, actionId, scriptId, capabilityId, parameters, steps, composerId, barkRefs}`.
For scriptId `activity_sequence`, require exactly one of nonempty typed `steps`
or registered `composerId`; the latter selects trusted compiled C# composition.
Other registered specialized scripts retain their own typed parameter contract;
do not force legacy Drink/Movement through a new sequence migration. `barkRefs`
maps declared phase IDs to finalized localized dialogue/beat references; C# never
contains their text. Reuse ContentRef and text rows. Reuse ActionRecord contractJson
for the compiled versioned definition; do not conflate it with derived per-step
request records or erase the latter's arguments. Topic entries retain flowRef and
conditionJson; extend typed Topic authoring references only where current projection
cannot express executable Scenarios, with explicit schema/compatibility validation.
Per-step request ActionRecord.contractJson must carry `definitionRef` separately
from `arguments`; its referenced reusable definition stores script/config/bark refs.
Preserve this identity when projecting to ScenarioStep/ActivityRequest; host resolves
only from the pinned release, never latest mutable authoring content. Add explicit
dependency edges so compiler selection closure cannot omit the definition/text.
Definition parameters are fixed except fields the trusted descriptor explicitly
declares as per-request arguments; no undeclared override, script replacement, bark
rewrite, expanded checkpoint vocabulary or arbitrary effects in a request.

- [ ] Red tests: SQL CAS round-trip retains script/config/bark/flow references;
  authoring selection includes all dependencies; unknown script/checkpoint/continuation
  or dangling text/flow rejected at validation/build, not silently stripped.
- [ ] Run focused tests red. Implement the narrow typed projection/compiler/readers
  using registered host vocabulary, not the draft's self-declared permissions.
- [ ] Add continuation/checkpoint representation to normalized Flow compilation,
  validation and runtime; update release schema/manifest only if needed, with old
  release compatibility/rejection tests. Stable ID/revision pins must survive reopen.
- [ ] Round-trip Topic -> entry rule -> Scenario Flow -> Action definition ->
  localized bark refs through isolated authoring DB, staged release and runtime read.
  Test default/unknown room contexts and a present Scenario with an unavailable
  executor: validation must not block the unrelated no-action absent Scenario.
  Unknown/unregistered scripts or invalid declarations reject authored release
  validation; a valid registered implementation temporarily unavailable in the
  current scene only disables execution/positive entry, not a valid absent sibling.
- [ ] Green Authoring/ContentData/Workbench tests and existing source-format parity.
  Existing runtime scripts remain C#; no .cs ingestion/compilation via authored data.

### A4 Workbench-facing acceptance: discover, validate, preview

**Sole tooling writer:** Topic Playground. Main sends the approved bounded A4 API
task to that existing session after it acknowledges the writer window. Behavior
hands over descriptor/contract versions, pure source list, composer/data fixtures
and expected traces; DBMS supplies repository/selection APIs. No silent writer
transfer to Behavior. Main routes any required Topic Playground consultation unless
the human explicitly authorizes another communication path.

**Files:** new pure `ActivityPrimitiveDescriptor.cs` beside the sequence contracts;
`Tools/ContentWorkbench/CoreHost/WorkbenchCommands.cs`, new
`WorkbenchActionCommands.cs`, existing WorkbenchPreview.cs and build-core.ps1;
`Tools/ContentWorkbench/tests/test_commands.py` and synthetic fixtures.

**Interfaces:** `schema.describe` with payload `{kind:"action"}` returns installed
script/composer/primitive/function descriptors with stable id/contractVersion,
parameter fields/types/bounds/allowed references, result schema, declared checkpoints,
continuations and diagnostic codes. Descriptor content is generated from the same
trusted registries used by validators/execution, not a hand-maintained UI manifest.
Extend existing `content.validate` and `preview.run` with `kind:"action"`; retain
their normal versioned envelope/diagnostic shape and existing Topic/Scenario behavior.
Kind values remain explicit mappings to `action_definition` authoring documents.

- [ ] Red tests: descriptors agree with actually accepted operations; unknown or
  unavailable kind/script reports an honest diagnostic, never a fake editable type.
  Adding a registered synthetic primitive descriptor changes discovery without a
  per-TV UI branch. Draft content cannot add its own permitted functions/effects.
- [ ] Implement discovery and action validation by calling the production registry /
  ActivitySequenceValidator. Reuse authoring.list/read/write and typed CAS rather
  than a new Action database/file writer. Preserve IDs, optional fields and refs.
- [ ] Implement action preview with the actual portable ActivitySequenceExecution,
  synthetic target/function/state ports and fake monotonic time. Produce bounded
  step/result/bark/checkpoint/terminal traces, consumed conditions and rejection paths.
  No private profile, live scene, real function effect, GTE/provider call or SQL commit
  outside isolated fixtures. Simulation must label itself, not claim NavMesh/animation proof.
- [ ] Composer/data/preview parity tests cover changing target, ordering, duration,
  missing dependencies, failed primitive, cancellation and checkpoint continuation.
  Two different fixtures run without per-recipe compiler changes. An unavailable
  physical service is unavailable in production, not replaced by preview evidence.
- [ ] Run Workbench command tests and a built-host dependency check: executable
  launches with no Unity Editor/Unity DLL requirement. Include only pure source
  dependencies in its build list; do not link ActivitySceneHost/UnityActivityMotion.
- [ ] Publish a versioned synthetic Action definition + valid/invalid request/trace
  example for the later UI/development agents. This command/API handoff completes
  Action preparation; it is not implementation of W2-W5 or Item authoring.
- [ ] Topic Playground releases its bounded writer window only after reporting exact
  files, API/descriptor versions, built-host command/dependency evidence, fixture
  parity and remaining gaps. Behavior then consumes the API without reopening those
  files; a later producer change needs another agreed window. If owner access or
  dispatch is unavailable, report A4 tooling pending rather than implement a second
  command host or claim Workbench preparation complete.

- [ ] Refresh the sidecar's registered Scenario schema/validation and generation
  instructions in `Tools/CompanionRuntime/src/alteros_companion/scenario_contracts.py`,
  `scenario_protocol.py`, `scenario_generation.py` and `prompts.py` for the precise
  checkpoint/continuation contract. Preserve v4 Appetizer/main framing, EN display /
  JP speech and memory rules. Definition references, like target IDs, must come from
  the host-supplied allowed set; the model cannot invent or substitute a revision.
  Add synthetic Python tests for supported continuation output and invented script,
  checkpoint, effect or target rejection. Python may request host-bound behavior;
  it never reads Unity's authoring DB directly or gains raw SQL/game-effect authority.
  Run `uv run --locked --group dev pytest tests -q` from `Tools/CompanionRuntime`
  with the already configured runtime (no dependency installation); do not call a
  provider or rebuild a sidecar package until its tests justify it.
- [ ] If the supported generated wire contract changed, build a new uniquely named
  onedir helper with the existing build script, preserve old packages, pin Unity's
  resolver to that verified version and run synthetic keyless readiness/protocol
  checks. Do not leave a new source contract paired with a stale selected helper.
  This is not permission for a model/provider-policy change or a live request.

## A5 — Shared sequence execution, Turn on TV recipe and post-end pose safety

**Files:** ActivitySequenceDefinition.cs, ActivitySequenceValidator.cs,
ActivitySequenceExecution.cs, ActivitySequenceBuilder.cs, TurnOnTV.cs,
ActivityPresentationRequests.cs, ActivityPoseSafeguard.cs,
ActivitySceneHost.cs, focused DirectConversationBridge partial and existing ambient
bubble adapter; new ActivitySequenceExecutionTests.cs, TurnOnTVRecipeTests.cs,
ActivityPoseSafeguardTests.cs. TurnOnTV.cs is only a thin composer; the data form
is an equivalent authored recipe, not a second executor.

**Interfaces:** ActivitySequenceExecution implements IActivityExecution using the
typed ordered in-memory recipe/contracts and registry from A1. A4 subsequently
supplies pinned SQL definitions and tool preview. It tracks a recipe step ID and step
state under the existing ActivityHost; no TV-specific phases/branch switch. It
requests authored bark IDs via an injected bounded presentation port. The port returns
accepted/suppressed/unavailable without blocking cleanup. The pose guard uses an
injected monotonic clock, presentation owner token and `interactionOpen/scenarioActive`.
Only after both end does lingering presentation get a30-second recovery deadline.

- [ ] Red tests: successful movement initiation emits first bark exactly once;
  failed move emits none; real arrival/face emits second once; cannot claim return
  completed before actual arrival. Player moves/disappears, nav blocks, TV binding
  changes and cancellation all produce truthful bounded outcomes and release claims.
- [ ] Add tests changing target query, bark text, wait duration and operation order
  in recipe fixtures with no C# edits. A second non-TV fixture runs through the same
  factory/executor without a new class, capability switch or compiled registry edit
  for that recipe. Reject unknown operations, duplicate step IDs, invalid references,
  excessive step counts, nonfinite/negative/over-limit durations and undeclared effects.
- [ ] Implement thin TurnOnTV.Compose using only builder operations/configuration.
  Red/green parity tests feed C# composition and equivalent data fixture into
  the same execution: compare normalized move/bark/wait/return/terminal traces and
  cancellation/cleanup, allowing different run IDs. Changing bark/config in either
  form must not require navigation code edits. A4 adds SQL-backed parity; no full
  Scratch UI in this task.
- [ ] Test suppressed ambience under focus/quiet, no ambient bubble adapter and no
  modal interruption. Use a fake monotonic clock: after arrival the configured
  two-second wait does not block frames, and return starts only when it elapses.
- [ ] Add fake-clock tests: active Scenario at30/300seconds never reset; end then
 29seconds no fallback,30seconds compatible default; new owner cancels old timer;
  cancel/quit immediately clears resources, never waits30seconds or awards success.
- [ ] Implement typed sequence primitives using shared motion/registry: reserve ->
  move with onStarted bark -> face -> arrival bark -> invoke power.set(On) -> wait ->
  move near player -> complete. Function and desired state come from validated
  authored configuration, not strings embedded in the composer. The configured wait is intentional; never
  use it as a substitute for arrival. No blocking sleep or world-origin teleport.
  Barks use existing non-modal templates. Power changes only through A2F/A2P, with
  no arbitrary stat effect. Abort on
  primitive failure/cancellation; later recipe steps cannot run, and shared cleanup
  releases this execution's resources. Unknown operations never become successful no-ops.
- [ ] Test `invoke_furniture` against the exact reserved target. Give every step
  invocation a stable command identity; retries must not create a fresh command ID.
  Wait for applied/replayed/no-change before continuing; conflicts/failure never
  produce a turned-on success line. Record committed function result even if later
  return/cancellation fails. A self-owned power commit must not accidentally treat
  its resulting state revision as an external stale-target change; carry forward
  trusted returned state evidence without switching to a different object.
- [ ] Wire only registered approved animation/pose bindings; safe compatible idle
  if a clip is absent. The human approved automatic closing for this Scenario:
  after successful return, the host/Scenario presents the authored closing without
  another T press. Reuse the existing independently-authorized engagement seam;
  the validated Scenario follow-up/run context grants bounded presentation authority,
  never arbitrary model content, script calls or a global initiative bypass.
- [ ] Red/green regressions for automatic closing exactly once after actual return,
  defer while another exchange owns presentation, revalidate on safe delivery,
  discard stale/cancelled/suspended-run callbacks, and never auto-start a new Topic
  or generation request. Activity completion does not end the Scenario before closing;
  the30-second pose timer remains suppressed until closing/Scenario actually end.
- [ ] Green tests plus existing Come/Follow/Drink/reservation/lifecycle regressions.

## A6 — Author Turn on TV and prove the full data path

**Files:** use shared SQL authoring commands, revisioned deterministic export under
`Content/AuthoringExports/`; isolated fixtures under `Tools/ActivityRuntime/Fixtures/`;
new `Tools/ActivityRuntime/Check-ActivityRuntime.ps1` if no existing repeatable source
harness covers these tests. New harness reuses current compiler/test conventions,
not a package installation. Update source lists for actual new dependencies.

- [ ] Author one Topic with mandatory anchor **Can you help me turn on the tv?**
  (retain **Turn on TV** as an optional additional anchor/short label) and present/absent/
  unavailable-or-unknown eligible Scenarios. Positive graph: opening -> request
  Turn on TV recipe (movement/barks/power function/wait/return) -> completion -> automatic
  closing -> end. Negative graph: authored
  no-TV line -> close/end, zero Action requests. Preserve bilingual speech/display.
- [ ] Add authored **Turn on the TV** under the existing **Can you do me a favor**
  branch. Update the canonical SQL-backed greeting/menu content through typed CAS
  edits, using existing Canvas choice templates. Menu and free text enter the same
  Topic/eligible Scenario definitions, not a direct function/Activity command.
  Source seams: ActivityGreetingMenu.cs currently hardcodes Movement/Drink targets;
  DirectConversationBridge.StartGreetingActivity also has a limited capability switch.
  Reuse/extend the shared validated Topic/Scenario menu-dispatch contract rather than
  adding a TV-name conditional or a second menu/Scenario runner.
- [ ] Red/green entry-parity tests: typed full anchor and favor-menu selection show
  opening VN dialogue before Action execution, then the same function/return/automatic
  closing behavior. In a complete room without a TV, both choose authored no-TV
  dialogue and issue zero Action requests. Revalidate busy/unknown/stale state at
  menu click, and guard duplicate starts/clicks. A missing executable capability
  must not suppress a valid no-action fallback Scenario. Menu selection bypasses
  neither host permissions nor an existing Scenario's pause/end transition rules.
- [ ] Use typed SQL writes, explicit build-set updates and deterministic export,
  preserving original source bytes and revision/CAS. No loose Generic JSON live
  path, manually patched SQL rows or string-equality shortcut in router.
- [ ] Test lookup by known Topic ID separately from free-text semantic matching.
  Since this adds anchors, bake the selected snapshot's semantic index through the
  existing local GTE tool and revalidate source/model/text hashes. No provider calls;
  no reuse of unchanged old vectors for the new anchor. Record bake time and IDs.
  Baseline ContentBuildProcessor gathers `generic_dialogue` routes/hash only and
  Semantic Lab documents a Generic-JSON baker. Verify actual current bake input;
  extend it narrowly to include the selected SQL Topic anchors/entry-flow mappings
  from the same pinned snapshot. Use a versioned semantic-input manifest if needed,
  preserving existing Generic route semantics and stale-hash rejection. Do not
  create a dummy duplicate Generic dialogue or route exact text specially to conceal
  this integration gap. Regression: compiled t.turn_on_tv appears in ranked local
  candidates from its baked Can you help me turn on the tv? anchor, alongside
  unchanged existing routes. Test a near negative such as turn OFF the TV to ensure
  a conflicting request cannot execute set-On solely due to shared TV vocabulary.
- [ ] Validate/compile a staged selected snapshot and inspect its dependencies;
  first use synthetic data. Explicitly install only the reviewed authored test
  release through the existing build/import gate; never install an invalid draft.
- [ ] Run: `./Tools/ContentData/Check-Authoring.ps1 -Group All`,
  `./Tools/ContentData/Check-ContentData.ps1 -Group Flows`, `-Group Semantics`,
  `-Group Bake`, `-Group Publish` (each separate invocation),
  `./Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`,
  `./Tools/Conversation/Check-UnifiedConversation.ps1`,
  `./Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1`,
  new Activity source harness, and Workbench `test_commands.py` via configured Python.
  Use correct64-bit PowerShell; fix stale harness source lists within this scope,
  never remove tests or fake registration to get green. Retain full failure evidence.
- [ ] With exclusive Editor ownership, allow import/compile and run focused Activity /
  Scenario / furniture EditMode tests. Use Editor for ModelTest test-TV markers,
  registered definitions/references and any serialized settings. Preserve scene roots.
- [ ] Stop before unsafe real-profile Play Mode. If isolated profile is available,
  test normal `SplashScene -> ModelTest -> T -> free-text request or favor submenu`:
  placed, absent, busy,
  unreachable, moved/removed during execution, multiple TVs and dynamically placed
  TV. Check both barks during movement/arrival, return, automatic closing with no T press, no duplicate
  starts/ends,30-second guard and F5 state overlay. Otherwise record this matrix as
  pending human acceptance, not a completed runtime pass.
- [ ] Add first-slice acceptance for actual Off->On screen/indicator change, already-On
  no-change, two compatible TV definitions sharing the handler, two independent
  instances, persistence failure with no visual lie, and synthetic restart preserving
  committed power. Do not count a bark or renderer-only toggle as functional success.
- [ ] Carry a bounded trusted function-result code into the Scenario's authored
  closing selection: applied versus already-On/no-change must not share a line
  falsely claiming a new transition. A failure or cancelled return can acknowledge
  a committed power result without pretending the whole Action/Scenario succeeded.
- [ ] Update this checklist/evidence, Topic Scenario Activity Technical Reference,
  Scenario Conversation Handoff, Feature Register, TODO and Maintenance. Workbench
  receives the stable script/config/checkpoint vocabulary, not a new UI assignment.

## Completion and stopping conditions

Implementation may proceed through source, headless content and focused Editor
checks after approval. Stop for conflicting ownership, unsupported migration that
would lose data, new permissions, credentials/spend, or unsafe profile testing;
otherwise continue task-by-task without asking the human to re-plan minor details.
Any schema evolution must remain narrowly scoped and covered by compatibility tests.

Report exact files/contracts changed, authoring/export/release identities, test
counts/commands and source-versus-Editor-versus-live evidence, remaining blockers
and the first acceptance step. Do not call the slice complete until the player-facing
matrix is actually verified; a source-ready handoff with those gaps is acceptable.

## Planning self-review

Spec coverage: TV branches and generic presence (A2/A6); reusable script registration
(A1); true intermediate dialogue (A3); SQL definitions/localization/flow parity(A4);
ordered barks/return/cleanup(A5); local anchors/release and startup acceptance(A6).
The Turn on TV expansion additionally covers registered function authority(A2F),
durable per-instance command/event/receipt semantics(A2P), committed-state visual
binding(A2V), invocation/result propagation(A5) and the complete Off/On matrix(A6).
New DTO/method/step names above are planned interfaces, not claims that they exist.
Execution method is the existing Alterra Behavior Session (formerly LLM Planning),
not a newly spawned implementer.

Fresh builder check2026-10-22 identified the missing definition-selection identity
between compiled requests and ActivityHost. A1/A4 now carry a revision-pinned
actionDefinitionRef/definitionRef, dependency closure and fixed-versus-request
parameter authority. The reviewer reported no other material ambiguity. Root's
follow-up source read additionally exposed the Generic-only bake/build input;
A6 explicitly covers SQL Topic anchor inclusion rather than assuming it exists.
Static links/headings/fences are checked separately; no implementation tests were
run while writing this plan.

The fresh reviewer read the identity and semantic-bake amendments and reported no
remaining material inconsistency. Human review/implementation approval is still
separate from this builder check.
The later human clarification adds dual C#-composition/data-recipe authoring through
the same sequence executor; that amendment was self-checked for task/interface
coverage, not part of the earlier independent builder result. It does not issue START.
The later function/state expansion is based on the direct Furniture/DBMS read-only
consultation and main's narrow adapter/DTO source check. It is self-reviewed for
spec coverage, exact-target identity, retry payload stability, persisted-versus-point
revision distinction and source/Editor/live evidence separation; no new independent
review or implementation tests are claimed for this expansion.
The subsequent Workbench-purpose clarification adds portable execution ports and
A4 discovery/validate/preview acceptance. This remains a self-reviewed planning
amendment, not a new test result or source implementation claim.

## Delegation prompt

Dispatch record2026-10-22: this plan and mandatory DBMS reorientation were sent to
the existing LLM Planning thread as a read-only briefing, explicitly not START.
Human review/implementation approval remains the next gate.

For Alterra Behavior Session: reorient from the new DBMS before implementing; your earlier
JSON/transcript assumptions may be stale. Read root AGENTS.md/TODO.md/Home.md, this
plan, its design, the SQLite Authoring verification and the current source it names.
The human selected your existing session as lead; do not create a replacement agent,
re-plan the feature from scratch or implement the Workbench UI. Before START, report
only concrete ownership/prerequisite conflicts and do not edit runtime/content.

Once the human approves implementation START, execute
A0/A1/A2/A2F/A2P/A2V/A3/A5/A4/A6, preserving sole host /
Scenario/coordinator ownership and coordinating Room-owned files/Editor access.
For A4 Workbench backend/build/test edits, hand the bounded contract to main for
Topic Playground dispatch; do not edit Tools/ContentWorkbench/** yourself.
Deliver authored Turn on TV through SQL -> pinned release -> semantic route -> present
or absent Scenario -> shared sequence executor / Turn on TV composition or recipe ->
localized barks -> registered power function/durable state/visual change -> configured
timed wait -> return -> automatic closing, plus
the synthetic checkpoint-continuation and post-end pose tests. No secrets, provider
spend, fake furniture checks, per-topic C# route branches, private-profile migration
or unapproved Play Mode. Report files, IDs/hashes, actual checks and remaining player
acceptance gaps back to the human/main session; never claim source checks prove play.
