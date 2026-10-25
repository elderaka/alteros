---
title: Reusable Behavior W2 Integration
tags: [alteros, verification, activities, scenarios]
updated: 2026-10-24
status: source-implemented-review-pending
---
# Reusable Behavior W2 — Activity / Scenario integration

## Scope and status

This records the Behavior W2 source integration across explicit Topic-menu selection,
Scenario host effects, ActivitySequence lifecycle, and the Unity-facing primitive port.
The existing `ActivityHost` remains the sole exclusive physical-activity owner;
`ActivitySequenceScenePrimitivePort` dispatches primitives through that host's one
reservation registry and NavMesh adapter. The approved W2 boundary does not include
Scene/Prefab authoring or wiring, Unity Editor work, provider calls, profile access,
or changes to the authored SQL catalogue.

The scoped W2 review fixes are implemented in source. Portable source compilation and
the Scenario runtime suite pass; this is not a Unity compilation, scene-wiring, Play
Mode, or player-acceptance pass. The serialized port slot remains for Furniture's
later W4 wiring window.

## Behavior and composition

### Explicit Topic menu

`TopicMenuSelection` checks the selected Topic against the current prepared candidate
set. The menu path adds that exact selected Topic candidate and enters the same authored
resolver/Scenario flow used elsewhere; it does not ask semantic routing or the online
provider to reinterpret the button choice. Unknown, unavailable, or missing selections
fail closed.

### Completed-Dialogue host effect

`ScenarioStep.requiresCompletedStepId` identifies the prior Dialogue requirement.
`ScenarioSession` records the exact completed dialogue execution identity and issues a
correlated host-effect request only after that completion. `ContentFlowExecutionState`
projects the pinned host-effect Action from the selected release. Success advances only
after a matching durable `applied`/`replayed` result; a missing port returns correlated
`unavailable`. The generic content validator still rejects untrusted/model-authored
host-effect steps.

### ActivitySequence lifecycle

Three proof traces use the same ActivityHost-owned sequence runner:

| Trace | Primitive order | Completion boundary |
| --- | --- | --- |
| `TurnOnTv` | reserve → move → furniture function | Function result is complete before the sequence ends |
| `LocateTv` | reserve → move → bark | Sequence stays active until the non-modal presenter acknowledges visibility |
| `FetchDrink` | reserve → move → furniture presentation → move near player | Presentation handle completes before return movement and end |

These portable trace tests use fake primitive/presentation ports. They prove the
Scenario/Activity lifecycle and correlation rules, not live furniture, item, hydration,
or player-visible effects.

### Unity-facing primitive port

`ActivitySequenceScenePrimitivePort` implements `IActivitySequencePollingPort` in the
Scripts-side scene bridge assembly. Its `Awake` composition registers the production
operations with `ActivitySequencePrimitiveDispatcher`; `ActivitySceneHost` calls
`Start`, `Cancel`, `Tick`, and `ReleaseSequence` through the existing
`sequencePrimitivePortComponent` slot.

| Primitive | Production source behavior |
| --- | --- |
| `reserve_nearest` | Uses `ActivitySceneHost`'s existing `FurnitureRegistry`; selects the nearest reachable point satisfying the requested use and optional catalogue function/presentation binding. Emits an opaque token mapped privately to the exact `FurnitureBinding`. |
| `move_to`, `move_near_player` | Commands the existing `UnityActivityMotion` NavMesh agent; polls arrival/block/timeout; clears motion on cancellation or release. |
| `face` | Revalidates the reserved binding, then faces its authored use transform. |
| `bark` | Resolves exactly one pinned Dialogue and its pinned Text from `RuntimeContentStore.Current`; sends the authored beat through `AlteraAmbientBubble` non-modally and completes only after the visible-bubble acknowledgement. Multi-beat Dialogues fail closed in this primitive. |
| `wait` | Uses the host's monotonic real-time tick, with the catalog's bounded 0–300 second duration. |
| `invoke_furniture: power.set` | Revalidates the exact claim through Furniture's target validator; obtains durable game/layout/instance revisions from the shared progression furniture store; invokes `FurniturePowerFunction`; derives the GUID operation ID from `furniture-power-set\n{scenarioRunId}\n{flowInvocationId}\n{stepExecutionId}\n{actionInvocationId}\n{sequenceStepId}`. |
| `invoke_furniture: dispenser.collect_mock` | Uses Furniture's existing `FurniturePresentationRegistry` request/handle/result lifecycle and `DispenserCollectMockPresentation`. This is deliberately mock-only and grants no item or hydration. |
| `complete` | Completes synchronously through the sequence contract. |

`scenario.checkpoint` remains unadvertised by `ActivitySceneHost`; the sequence
executor therefore rejects it before reaching the primitive port. No stat or currency
effect is inferred from it. The root `ActivitySceneHost` discovers the Furniture
catalogue through `IFurnitureCatalogueSnapshotSource` rather than referring to a
Scripts-side concrete component type.

Resource cleanup is sequence-scoped: the same registry releases claims by physical
`actionInvocationId`; movement, pending bark and mock presentation operations cancel
their own transient work. Late/stale visibility callbacks cannot complete a released
sequence. Sequence primitive startup is deferred until the first `ActivityHost.Tick`,
after the accepted physical Activity has been registered as `ActivityHost.Current`.
Each newly entered sequence step binds its fresh physical action ID before dispatch;
stale events from a prior sequence are ignored, and unavailable requests retain the
new request's correlation ID so the declared failure path can run. Furniture reserve
and binding validation require that exact active physical sequence owner. A Furniture
catalogue with error diagnostics contributes no reserve-use capability; sequence,
completion, movement, and bark capability discovery remains independent of that gate.
Stopping or disabling the ambient presenter before bark visibility acknowledges
failure once; stopping after visibility does not replace the successful acknowledgement.

## Files

New source and test files in the W2 integration diff:

- `Assets/AlterOS/Activities/ActivitySequenceBarkOperation.cs`
- `Assets/AlterOS/Activities/ActivitySequenceExecution.cs`
- `Assets/AlterOS/Activities/ActivitySequencePrimitiveDispatcher.cs`
- `Assets/AlterOS/Activities/ActivitySequenceReleaseAdapter.cs`
- `Assets/AlterOS/Conversation/TopicMenuSelection.cs`
- `Assets/AlterOS/Scenarios/ScenarioHostEffectExecutor.cs`
- `Assets/AlterOS/Topics/TopicScenarioContentResolver.cs`
- `Assets/Scripts/Activities/ActivitySequenceScenePrimitivePort.cs`
- `Tools/ScenarioRuntime/ScenarioHostEffectFlowTests.cs`
- `Tools/ScenarioRuntime/TopicEntryFlowTests.cs`
- `Tools/ScenarioRuntime/TopicMenuFlowTests.cs`
- `Tools/TopicRuntime/TopicContentRefShim.cs`

Modified source, tests, and harnesses in that diff:

- `Assets/Altera/AlteraAmbientBubble.cs`
- `Assets/AlterOS/Activities/ActivityExecutionContracts.cs`
- `Assets/AlterOS/Activities/ActivityHost.cs`
- `Assets/AlterOS/Activities/ActivitySceneHost.cs`
- `Assets/AlterOS/Activities/ActivitySequenceRunner.cs`
- `Assets/AlterOS/Activities/FurnitureRegistry.cs`
- `Assets/AlterOS/Activities/ScenarioActivityAdapter.cs`
- `Assets/AlterOS/Conversation/ConversationContracts.cs`
- `Assets/AlterOS/Conversation/ConversationContractValidator.cs`
- `Assets/AlterOS/Conversation/DirectConversationCoordinator.cs`
- `Assets/AlterOS/Conversation/TopicCandidateRetriever.cs`
- `Assets/AlterOS/Conversation/UnifiedConversationPlanner.cs`
- `Assets/AlterOS/Furniture/FurnitureFunctions.cs`
- `Assets/AlterOS/Scenarios/ContentFlowExecutionState.cs`
- `Assets/AlterOS/Scenarios/ScenarioDefinition.cs`
- `Assets/AlterOS/Scenarios/ScenarioSession.cs`
- `Assets/AlterOS/Tests/Editor/Activities/FurnitureRegistryTests.cs`
- `Assets/AlterOS/Tests/Editor/Activities/ActivitySceneHostCapabilityTests.cs`
- `Assets/AlterOS/Tests/Editor/Activities/ActivitySceneHostSequenceOwnershipTests.cs`
- `Assets/AlterOS/Tests/Editor/Activities/AmbientBarkInterruptionTests.cs`
- `Assets/AlterOS/Tests/Editor/Conversation/DirectConversationCoordinatorTests.cs`
- `Assets/AlterOS/Topics/TopicDefinition.cs`
- `Assets/AlterOS/Topics/TopicDefinitionValidator.cs`
- `Assets/AlterOS/Topics/TopicHistory.cs`
- `Assets/Scripts/Dialogue/DialogueManager.cs`
- `Assets/Scripts/Dialogue/DirectConversationBridge.cs`
- `Assets/Scripts/Furniture/FurnitureSceneAdapter.cs`
- `Tools/ScenarioRuntime/ActivitySequenceFlowTests.cs`
- `Tools/ScenarioRuntime/ScenarioSequenceRepeatFlowTests.cs`
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`
- `Tools/TopicRuntime/Check-TopicRuntime.ps1`

Producer-owned dependency present in the captured after-tree, excluded from the
Behavior W2 review scope because its progression-factory changes were producer-reviewed:

- `Assets/Scripts/Progression/AlteraProgressionStore.cs`

No Scene, Prefab, `.unity`, `.prefab`, YAML, or `.meta` file was changed. The
W2-specific additions in this note do not replace the canonical contracts in
[[Reusable Behavior Framework Plan]], [[Reusable Content Framework Workflow]],
[[2026-10-23 Activity Sequence Catalog]], or [[Furniture Functions Framework Plan]].

## Verification evidence

- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1` — **163 passed, 0 failed**.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1 -Filter ActivitySequenceFlow` —
  **44 passed, 0 failed**. Includes first-Tick owner registration, plain sequential
  sequence rebinding, the three proof traces, explicit Topic menu, completed-Dialogue
  host-effect correlation, and bark suppression/visibility/cancel/late-callback/release
  cases.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1 -Filter ScenarioSequenceRepeatFlow` —
  **1 passed, 0 failed**. A compiled Flow runs sequence A then B, ignores stale A
  acceptance/result events while B is current, and fails the scenario when B is
  unavailable.
- `Tools/TopicRuntime/Check-TopicRuntime.ps1` — **33 passed, 0 failed**.
- `Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` was attempted but did not reach
  tests: its portable compilation is missing the `AlterOS.Activities` source dependency
  used by `ContentContracts.cs` (`ActivitySequenceBuilder` unresolved). This is a
  harness compile failure, not a Furniture test result.
- The focused bark suite first failed to compile because the operation/presenter types
  were absent; after adding the lifecycle implementation and harness inclusion, the
  focused suite passed.

These PowerShell harnesses compile their listed portable source before running the
tests; they do not compile the Unity-facing port, `ActivitySceneHost`, or scene scripts.
Main reported that, after fix round 3, Unity 6000.4.4f1 compiled with zero errors and
the Start-method script warning was absent. Focused Editor reruns passed ambient bark
4/4 and sequence ownership 2/2. The earlier pre-fix ambient run was 2/4 because it
expected runtime coroutine/lifecycle behavior in EditMode. The passing EditMode checks
cover presenter bodies and ownership mapping, not real PlayMode fade/deactivation
lifecycle. No PlayMode test or provider request was run. Serialized-reference
inspection, ModelTest/SplashScene startup, NavMesh movement, durable power, furniture
presentation, and player acceptance remain unverified. Once wired, bark uses the
existing ambient bubble/TTS path; this slice changed no provider configuration or TTS
preference.

## W4 wiring and remaining producer review

Furniture's later W4 scene window must add/assign `ActivitySequenceScenePrimitivePort`
to the authored scene object, bind `ActivitySceneHost.sequencePrimitivePortComponent`
to that enabled component, and verify its references to the current
`FurnitureSceneAdapter`, `AlteraProgressionStore`, `AlteraAmbientBubble`, room ID/root,
and Navigation. Then run Unity import/compilation and focused Editor/Play Mode checks,
including confirmation that ambient bark respects the existing TTS preference. No new
UI, modal dialogue, provider configuration, or TTS preference was added by this source
slice.

Furniture confirmed the two catalogue-snapshot interface edits were not explicitly
approved before they were made: `IFurnitureCatalogueSnapshotSource` in
`Assets/AlterOS/Furniture/FurnitureFunctions.cs` and its implementation in
`Assets/Scripts/Furniture/FurnitureSceneAdapter.cs`. Main has now ratified retaining
these two narrow source seams for W2 after Furniture's read-only review. That does not
transfer future writer ownership. The eligibility-filter overload in
`Assets/AlterOS/Activities/FurnitureRegistry.cs` stays within the existing registry;
there is no second reservation registry.

The scoped review/fix round also corrected compile blockers in the Unity-facing port
and DialogueManager, plain and compiled sequential sequence ownership, exact physical
owner checks, Furniture catalogue fail-closed advertisement, and pending bark
interruption acknowledgement. Portable evidence does not prove these Unity-facing
sources compile. Next acceptance gate: Main's normal Unity import/compilation and
focused Editor checks, then Furniture-owned W4 Unity wiring/runtime verification.
Keep source, Unity Editor, and player acceptance status separate.

### W2 fix round 2 — compile-only corrections (2026-10-24)

The scoped re-review found two new CS0246 blockers. `AlteraAmbientBubble` now catches
`System.Exception` explicitly, and `ActivitySceneHostCapabilityTests` imports
`AlterOS.Scenarios` for `ScenarioCapabilitySnapshot`. These are namespace-resolution
fixes only; no portable suite was rerun for this round. Main's clean Unity compilation
and focused capability, ownership, captured-revision, and bark Editor tests remain the
next verification gate. This source change does not itself establish Unity compile or
Editor-test success.

### W2 fix round 3 — EditMode proof seams and Unity message collision (2026-10-24)

Main's actual Editor run reported two ambient EditMode failures and four Unity script
errors for `ActivitySequenceScenePrimitivePort.Start()`. The EditMode tests were waiting
for the production coroutine's fade/visibility callback and relying on Unity to invoke
`OnDisable`; neither is a reliable runtime lifecycle proof for this non-`ExecuteAlways`
component in EditMode. `AmbientRoutine` now delegates its existing visibility decision
to a small private acknowledgement method, which the EditMode test calls directly.
The disable test directly invokes the actual `OnDisable` handler body twice and checks
one failure acknowledgement. This proves those production bodies, not Unity's real
activation/coroutine lifecycle. No `ExecuteAlways` was added and no test was disabled.

`ActivitySequenceScenePrimitivePort` now implements `IActivityPrimitivePort.Start`
explicitly. Existing ActivitySequenceExecutor callers still invoke the same interface
contract; the component no longer exposes a method named exactly `Start` for Unity's
MonoBehaviour message lookup. An Editor reflection regression checks both the absence
of a declared `Start` message and retention of the interface mapping. Main's later
compile and focused Editor rerun passed these source corrections.

### W2 fix round 4 — PlayMode isolation assessment (2026-10-24)

No PlayMode assembly or test source was added because the existing test entry point is
not isolated from normal startup/profile hooks. `GteSemanticRuntime` has an unconditional
`BeforeSceneLoad` bootstrap; its `Awake` starts initialization, which opens the shipped
SQL release, loads the GTE model/tokenizer Resources, creates a Worker and warms it.
`AlterOSGameSettings` has an unconditional `AfterSceneLoad` bootstrap whose `Awake`
reads `PlayerPrefs`. A PlayMode test run would therefore risk the exact model startup
and real-profile access excluded by this task before the test body can create its
synthetic objects. `AlteraAmbientBubble.FetchTTS` itself is disconnected when its
`dialogueManager` is null, but that does not neutralize these unrelated global hooks.

Smallest safe follow-up: Main coordinates an approved test-only suppression seam for
both global bootstrap owners, with normal builds/runtime unaffected; then add a
dedicated PlayMode test assembly under `Assets/AlterOS/Tests/PlayMode/Activities` with
only a synthetic AudioSource, Canvas and TMP text. Keep `dialogueManager` null and cover
`VisibleAcknowledgementArrivesAfterFade` plus
`DeactivationDuringPendingBarkAcknowledgesFailureOnce`. No production component should
be marked `ExecuteAlways`. Until that isolation seam and test-root/dispatch are prepared,
real fade and `SetActive(false)` lifecycle remain an explicit acceptance gap.

### W2 acceptance isolation audit — persistence and conversation boundary (2026-10-24)

Read-only source and ModelTest serialization review found the current progression
test-root latch is not a whole-game test root. `ProgressionTestPersistenceRoot` validates
and schedules a temporary directory, but only `AlteraProgressionStore.GetPersistentRoot`
consumes that override. ModelTest serializes active `DirectConversationBridge`
(`enableUnifiedConversation: 1`), `AlteraProgressionStore`, `ConversationSessionStore`,
and `GeneratedDialogueArchive` components.

The normal ModelTest path can touch the real profile before player input: the session
and archive `Awake/Load` paths call `RuntimeMigrationStartup.Ensure`; that coordinator
derives its journal, game/conversation/personal-content DB targets and several legacy
import sources directly from `Application.persistentDataPath` (shipped content and
archive-manifest inputs are under read-only `StreamingAssets`). `DirectConversationBridge.Awake`
also reads the online/session PlayerPrefs and creates/saves a session ID if absent
before its unified-conversation early return; with the serialized setting enabled it
continues to real-root topic history, transcript and memory paths. Its `Start` imports
identity aliases from PlayerPrefs. `ConversationTranscript.CreateInPersistentDataPath`,
`ConversationSessionStore`, `GeneratedDialogueArchive`, and the scenario host's
`ScenarioPersistentDataPath` do not consult the progression override. The current code
has no in-memory preference/offline-startup seam shared by those owners.

The constructor only stores `OpenRouterCredentialStore.GetApiKey` as a delegate; this
audit did not invoke it or inspect credentials. Provider/helper startup is deferred to
conversation submission, and TTS key lookup is deferred to synthesis/key-check paths.
Nevertheless, an actual test must avoid reading the real online preference to decide
whether those paths are enabled. `PlayerMemoryClient` accepts a root, but the current
Bridge supplies the real persistent path; `MemoryPolicyRequired` later checks the
`Memory/memory-policy-required.flag` there, and helper requests are separate from the
progression DB.

Smallest safe follow-up is to extend the existing Editor test-root latch, not add a
second storage authority: expose its already-validated root to `RuntimeMigrationStartup`
and pass that same root through the conversation/session/archive/transcript and memory
facades. Keep each existing owner and DB as the sole writer. Pair it with a test-only,
in-memory set of the specific conversation identity/online and TTS preferences, plus
an explicit offline/no-helper test mode; ordinary builds retain current PlayerPrefs,
memory, and credential behavior. The acceptance harness should prove all private paths
stay beneath the marked temporary root and assert zero calls to provider, credential,
helper-process, and TTS boundaries. DBMS must own migration/storage path propagation;
Behavior/Conversation owns Bridge identity, offline and transcript injection. No runtime
profile values, preference values, credentials or databases were read; no source or
scene was modified by this audit.
