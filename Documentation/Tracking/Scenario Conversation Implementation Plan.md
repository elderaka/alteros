# Scenario Conversation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a testable Scenario runner and two-stage generated conversation with a five-second local-opening fallback, explicit questions, next-E Topic offers and main-response closing.

**Architecture:** Unity owns scenario progress, presentation, history, permissions and deadlines. One versioned sidecar request fans out to head/main generation and returns independently complete stage messages; Unity admits them without duplicate presentation. Activities are simulated in the initial fixtures; production may execute only an actually supported host capability.

**Tech Stack:** Unity6000.4.4f1/C#, installed NUnit and source harnesses, existing Python3.13/httpx/Pydantic/LangGraph sidecar, current OpenRouter credential/provider policy. No new dependencies, network listener, SSE or external agent framework in Unity.

**Spec:** [[Scenario Conversation Design]]; read it fully, alongside `AGENTS.md`, `TODO.md`, [[Unified Conversation M2 Handoff]] and [[Player Memory Handoff]]. Human design/default approval2026-10-19; implementation is underway. Dessert is only main-response closing, not a third stage/request/service.

## Global Constraints

- Work in `E:\Pleiades\alteros`; preserve the shared checkout and unrelated untracked work. There is currently no Git HEAD. Do not create a baseline by staging everything. Each task checkpoint records focused paths, evidence and hashes; commit only if a safe Git baseline and explicit focused integration policy exist.
- Generic/personal content shares runner semantics; Topic anchors/catalogue identities are not Scenario execution IDs. Do not bulk rewrite authored JSON, rebake anchors or publish generated catalogues.
- At most one running and one suspended scenario; indefinite while app runs. Explicit diversion pause/end/back-out; no automatic resume. Quit ends without false completion. Preserve durable transcript/history; never restore an active scenario from last session.
- Appetizer1–4 beats, normally1–2; complete JP `text` plus EN `displayText`, supported expression. Main supplies outline/next segment and its closing. No separate Dessert stage. No TTS dependency.
- Five-second submit-to-first-visible target with prepared local opening; observe actual frame opportunity. A frozen app or API-ready timestamp is not a passing visual deadline.
- One pending question; at most three session-local next-E Topic offers. Unchosen options are not player evidence. No guilt, forced modal needs interlude or arbitrary LLM effects.
- Authored-first ready shortcut stays local. Two billable requests maximum per generated segment, no automatic retries/repair/escalation. Saved key is not online consent. No new provider/TTS spending is authorized by implementation approval; request a separate bounded live test from the human.
- Keep current v2/v3 strict contracts working. Add a negotiated v4 Scenario path, never silently reinterpret `suggestedReplies`, downgrade required memory, or accept provider/framework internals in Unity.
- Current generated wire key is `reply.beats`; authored/internal graphs keep `nodes`. Reuse current validated beat models and typed local validators. Production provider policy remains DigitalOcean-only, reasoning disabled, existing model ID; do not turn the earlier null-output diagnostics into a provider change.
- No real furniture, navigation, drawing/coding tools, image generation, phone UI or needs simulation in this first slice. Simulated activities must be visibly labelled as synthetic, outside production capability availability.
- No hand-editing Unity YAML/metas. Static controls are Editor-authored Canvas/prefab composition. If Editor is stale, finish source fixtures and report UI/package activation gates as blocked; never force-close/batch-bypass it. Human retains the personal startup/E test.

## Review Focus

1. Two valid outputs can repeat/contradict one another; stage ownership and shared content roles must be evaluated, not assumed from JSON validity (Tasks4,6,9).
2. The single-reply client currently serializes requests; prove real head/main overlap and multi-stage transport while retaining one player-turn owner (Tasks4,5).
3. Consent/forgetting or new player input while head/body are pending must invalidate adoption and metadata writes, including a late callback (Tasks1,4,6).
4. A UI close, app exit or partial head delivery must not falsely complete/consume a scenario or lose the suspended scenario slot (Tasks3,6,8).
5. A double answer, stale next-E button or disappeared use-point must not advance/execute twice or combine different furniture bindings (Tasks2,3,7).

## File and interface map

Keep implementations focused; do not grow `DialogueManager` or `DirectConversationBridge` into the scenario engine.

| Area | New files | Existing seams to modify |
| --- | --- | --- |
| Model/validation | `Assets/AlterOS/Scenarios/ScenarioDefinition.cs`, `ScenarioDefinitionValidator.cs`; Python `scenario_contracts.py` | Existing beat/context/memory types; no bulk content edits |
| Runtime state | `Assets/AlterOS/Scenarios/ScenarioSession.cs`, `ScenarioQuestionState.cs`, `ScenarioTopicOffers.cs`, `ScenarioActivityPort.cs` | Existing Topic eligibility/history through an adapter |
| Host delivery | `Assets/AlterOS/Conversation/ScenarioTurnCoordinator.cs`, `ScenarioTurnProtocol.cs` | `CompanionProcessClient.cs`, transcript event factory, direct coordinator shortcut |
| Sidecar | `scenario_generation.py`, `scenario_protocol.py` under `Tools/CompanionRuntime/src/alteros_companion/` | `protocol.py`, `__main__.py`, provider/prompt/context functions |
| Integration | `Assets/Scripts/Dialogue/ScenarioConversationPresenter.cs`, `Assets/AlterOS/Scenarios/ScenarioPlaygroundSession.cs` | Direct bridge, DialogueManager, existing Playground window/session/command |
| Tests/tools | `Assets/AlterOS/Tests/Editor/Scenarios/`, Python `tests/test_scenario_*.py`, `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`, synthetic fixtures | Existing conversation source harness dependencies |

### Shared contract, fixed by this plan

All new C# runtime types use `AlterOS.Scenarios`; conversation transport stays `AlterOS.Conversation`. Wire version4 advertises `scenario-turn-v1` capability. Bootstrap remains the only secret-bearing message.

- `ScenarioDefinition`: `scenarioId`, `topicId`, `entryStepId`, `steps`. A step is exactly one of `dialogue`, `ask`, `action`, `wait`, `branch`, `end`; closed per-kind fields, stable ID/successors, no arbitrary scripts. Maximum64 steps, next playable dialogue at most16 current-sized beats, at most8 explicit options per question. Automatic transition budget64 per advance; waiting/dialogue/ask yields control.
- `ScenarioRuntimeSnapshot`: host-bound scenario/run/turn IDs, context revision, allowed expression IDs, read-only capability/object facts and current checkpoint. Unknown does not mean zero/false. Capability IDs/typed arguments come from host definitions, never model-created API names.
- For a newly generated definition, Unity allocates the occurrence/definition identity; any provider-local step IDs are validated graph references, not catalogue IDs. `topicId` is a supplied known Topic or empty for a provisional novel subject. Do not fabricate semantic anchors, canonical IDs or ready catalogue entries to fit the Scenario DTO.
- `ScenarioInstanceState`: run ID, definition reference, current step, bound targets, lifecycle/outcome and pending question; session-local. One running + one suspended slot; no recursive stack.
- `ScenarioEffect`: typed output to presenter/activity adapter (`show_dialogue`, `ask_player`, `request_activity`, `show_hint`, `close_exchange`, `end_scenario`). It is not permission to run an action or mutate stats. Every effect carries run/step identity for idempotence.
- `ScenarioEvent`: typed player/game input with run/step/question/activity IDs and bounded data. `ScenarioSession.Advance(ScenarioEvent input, ScenarioRuntimeSnapshot context) -> IReadOnlyList<ScenarioEffect>` is pure C#. `Start(ScenarioDefinition definition, string runId, ScenarioRuntimeSnapshot context)`, `RequestDiversion(ScenarioDefinition nextDefinition, string nextRunId, DiversionDecision? decision, ScenarioRuntimeSnapshot context)` and `Quit()` return that same effect-list type. Null diversion decision requests confirmation; the typed decisions are `pause`, `end`, `back_out`. Read-only `Running`/`Suspended` properties expose the two instance states for consumers/tests.
- `ScenarioQuestionState.Answer(questionId, answerId, string freeText, contextRevision) -> QuestionAnswerResult`; one of typed option ID or free text, not both. Explicit `Skip/Cancel`; invalid input does not invent an answer.
- `ScenarioTopicOffers.Replace(IEnumerable<ScenarioTopicOffer>)`, `ForInteraction(ScenarioRuntimeSnapshot)`, `Select(string offerId, ScenarioRuntimeSnapshot)`: up to3 per session; check eligibility at display/selection; select once. These are proposals/IDs, not published catalogue records.
- New v4 `scenario_turn` carries current `ConversationJobV2`, optional current `MemoryTurnPolicyV3` and explicit source metadata, runtime snapshot and operation (`begin`, `answer`, `continue`). V2/V3 types retain their original semantics.
- `ScenarioTurnJobV4` is the typed payload just described; `ScenarioStageResult` holds parent request/version, stage ID/kind, validated stage payload and usage/status. `ScenarioTurnReceipt` holds parent identity, per-stage outcomes/known usage and terminal reason. `ScenarioTurnInput` is the host command containing that job, host-assigned request/version and prevalidated local opening/bridge. For `answer`, include the Task3 question ID plus typed option/free-text value; do not hide an answer inside an unbound normal turn.
- Responses are complete `scenario_stage` envelopes, same parent request/turnVersion: `stage=head` holds beats only (1–4); `stage=main` holds validated outline, next playable segment (including any closing), Ask Player data, future Topic offers and existing bounded memory proposal metadata. One terminal `scenario_complete` reports both stage outcomes/usages. No `dessert` stage/type. Errors retain known billed usage separately from display failure.
- `ScenarioTurnProtocol` C# and Python `scenario_protocol.py` validate exact field sets/IDs, duplicates, known enums and bounded strings, maximum256KiB outer envelope/128KiB result. Current64KiB turn and existing beat/string limits remain; no credentials in jobs, scenario checkpoints or logs. Request IDs are host-assigned, not canonical Topic IDs.
- `CompanionProcessClient.SendScenarioTurnAsync(string requestId, ScenarioTurnJobV4 job, Action<ScenarioStageResult> onStage, CancellationToken token) -> Task<ScenarioTurnReceipt>`. Keep one serialized player-turn owner, but multiple stage envelopes for it. No second helper process just to bypass locking.
- Python `generate_scenario_turn(job, credential, emit_stage, provider, memory_repository=None) -> ScenarioTurnReceipt`: one consent-filtered context snapshot, up to two async tasks, independent complete validation/admission, shared cancellation. No generation in a callback/automatic repair. Provider stage API remains private to Python.
- Host `ScenarioTurnCoordinator.Begin(ScenarioTurnInput, double nowSeconds)`, `Receive(ScenarioStageResult, ScenarioRuntimeSnapshot)`, `Tick(double nowSeconds)`, `AcknowledgeBeat(stageId, beatId)`, `Dismiss()`, `Cancel()`, `Quit()`: return ordered `ScenarioEffect`s, never mutate Unity objects. Five-second monotonic deadline begins before expensive work. Presenter applies effects on Unity main thread.

## Task 1: Preserve memory metadata before building on the current graph

**Files:** Modify `Tools/CompanionRuntime/src/alteros_companion/graph.py`; test `tests/test_graph.py`, `tests/test_memory_context.py`, `tests/test_stdio_service.py` as appropriate. Do not change schema/consent policy to make a test pass.

**Interfaces:** Consumes current `ProviderResult` and `MemoryTurnDataV3`. Produces the same `generate(...) -> ProviderResult`/`generate_memory_turn(...)` signatures, preserving all validated memory/question/callback fields.

- [ ] Write `test_eligible_confirmed_proposal_survives_generate_and_is_persisted`: use temporary repository, opted-in explicitly confirmed source, allowlisted nonsensitive fact and fake provider; assert result metadata survives and readback contains that fact exactly once.
- [ ] Add `test_generate_preserves_authorized_question_and_callback_metadata` and retain consent-revocation/forgotten-source rejection tests. Assert no proposal is accepted from unclassified/unsupported evidence and unknown question intent is not activated.
- [ ] Run `.venv/Scripts/python.exe -m pytest tests/test_graph.py tests/test_memory_context.py -q` from `Tools/CompanionRuntime`; observe the positive regression fail because reconstruction drops fields, not because fixtures violate policy.
- [ ] Preserve the full validated result when replacing timing (e.g. dataclass replacement); keep current safe dropping of unsupported proposals. Do not add new keys to strict v3 Unity messages; the v4 projection belongs to Task2.
- [ ] Run full `.venv/Scripts/python.exe -m pytest tests -q`, record exact pass/fail/skip counts and focused paths/hash checkpoint. Do not report pre-existing skip as a pass or adaptation as player-accepted.

## Task 2: Versioned Scenario/turn contracts and cross-language fixtures

**Files:** Create model/validator/protocol files in the map; create `Assets/AlterOS/Tests/Editor/Scenarios/ScenarioContractTests.cs`, `ScenarioTestData.cs`, Python `tests/test_scenario_contracts.py`; fixtures `Tools/ScenarioRuntime/Fixtures/v4/`. Modify the existing source harness to include new pure Scenario files/tests without broad source grabs.

**Interfaces:** Produces all shared contract types above; `ScenarioDefinitionValidator.Validate(definition, context) -> IReadOnlyList<string>`, strict `ScenarioTurnProtocol.ParseEnvelope(string)` and Python `parse_scenario_envelope(raw: bytes)`.

- [ ] Write paired fixture tests: valid cat dialogue and drawing-with-simulated-table parse identically; reject duplicate fields/step IDs, missing successor, wrong language, unknown capability, ambiguous object binding, fifth head beat, seventeenth segment beat and `stage=dessert`.
- [ ] Add exact boundary assertions:64 steps accepted/65 rejected;3 offers accepted/4 rejected; second pending question rejected; cyclic automatic64-transition path yields a safe failure, not an infinite loop. Keep legacy v2/v3 fixture parse behavior unchanged.
- [ ] Run existing conversation harness plus Python focused contract test; RED must expose missing new parser/definition support. Create `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1 -Filter Contract` using installed compiler/NUnit and explicit owned tests; fail zero discovery. The existing reflection harness invokes methods synchronously: write async process checks as synchronous `[Test]` wrappers using `GetAwaiter().GetResult()`, or explicitly await returned Tasks in the runner and test that an async assertion failure is counted. Never count unawaited async tests as passed.
- [ ] Implement closed typed models/validators from the Shared contract. Represent closing as the normal segment end/`close_exchange` outcome, never a separate generation stage.
- [ ] Run both new source harness and full Python suite; preserve generated-vs-authored key distinction. Record fixture validation and checkpoint only focused new/modified files.

## Task 3: Pure scenario lifecycle, question and next-E offer state

**Files:** New `ScenarioSession.cs`, `ScenarioQuestionState.cs`, `ScenarioTopicOffers.cs`, `ScenarioActivityPort.cs`; tests `ScenarioLifecycleTests.cs`, `ScenarioQuestionTests.cs`, `ScenarioOfferTests.cs`. Fixture helpers live only in `ScenarioTestData.cs`.

**Interfaces:** Implements shared session/question/offer methods; `IScenarioActivityPort.TryRequest(ActivityRequest request, ScenarioRuntimeSnapshot context) -> ActivityRequestResult` returns unavailable/accepted with authoritative activity ID. Fake implementation belongs only in fixtures/tests.

- [ ] Write lifecycle tests with literal expectations: two-hour no-event advance stays running; closing exchange preserves scenario; wrap-up completes once; Quit marks active and suspended interrupted-by-quit, awards nothing, and next-session constructor has no active run.
- [ ] Write diversion tests: back-out changes nothing; pause retains original checkpoint and releases exclusive work; one new run allowed; second pause cannot silently overwrite saved progress; Continue needs explicit input. Alterra-only completion makes return prompt eligible when CanTalk/focus allows; player-only/together completion does not prompt before next explicit interaction.
- [ ] Write question/offer tests: duplicate/stale answer leaves step unchanged, wrong question ID cannot answer successor, free text is not rerouted to random Topic, cancel/skip follows declared route; offers appear only on a later interaction epoch, not the current exchange, cap3 and selection idempotent, blocked/disappeared target rejected, quit clears offers. Do not use a real profile/history.
- [ ] Run `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1 -Filter Lifecycle` (then Question/Offer); observe RED before implementing pure state transitions. Implement only the declared types/methods and fixed bounds; no Unity lifecycle, LLM, new generic event bus or whole-world simulator.
- [ ] Run complete Scenario and existing Topic/conversation source harnesses. Record checkpoint. Ensure drinking/self-care is an activity event, not a second automatically started Scenario or arbitrary stat delta.

## Task 4: Sidecar head/main fan-out with safe context and stage validation

**Files:** New `scenario_generation.py`, `scenario_protocol.py`; modify existing `provider.py`/`prompts.py` narrowly for stage requests, `protocol.py`, `__main__.py`; tests `test_scenario_generation.py`, `test_scenario_stdio.py`.

**Interfaces:** Implements Python `generate_scenario_turn(...)` with `emit_stage(ScenarioStageResult)` and terminal receipt. Reuse existing provider HTTP transport/credential/error handling and memory preparation/revalidation, not duplicate DPAPI or maintain another model client.

- [ ] Write async barrier test: both fake provider stages must start before either is released; head can return before main, and main can return before head. Exactly2 requests maximum, no retry when invalid/timeout. Head1–4 beats; main carries closing as part of its segment, no third request.
- [ ] Test disabled online/missing key/memory-required-but-unavailable context sends zero calls; stage output cannot propose head memory/actions/questions; main memory metadata uses Task1 preserved result. Revoke consent/forget a source between head and body; no stale body adoption or metadata write.
- [ ] Run `.venv/Scripts/python.exe -m pytest tests/test_scenario_generation.py tests/test_scenario_stdio.py -q`; RED before fan-out. Implement two `asyncio` tasks from one bounded immutable context; head uses short grounded acknowledgement role, main substance/outline. Reasoning off, existing provider pin, no model changes. Keep no-SSE complete NDJSON stage envelopes.
- [ ] Start a local no-key/synthetic helper test: v4 capability handshake, head/main stage IDs and one terminal result, cancel drains/invalidates both children, EOF shutdown does not leave provider tasks alive. Keep memory v3 and conversation v2 tests green. No saved key/API calls.
- [ ] Run full Python suite; record usage/error receipts even for rejected billed outputs, without raw provider errors, secrets, private prompts or reasoning text. Checkpoint.

## Task 5: Owned-process multi-stage transport without breaking legacy requests

**Files:** Modify `Assets/AlterOS/Conversation/CompanionProcessClient.cs`; create/complete `ScenarioTurnProtocol.cs`; tests `ScenarioProcessClientTests.cs` and existing process/protocol tests. Keep transport code independent of MonoBehaviours.

**Interfaces:** Implements `SendScenarioTurnAsync(...) -> Task<ScenarioTurnReceipt>`; consumes Task2 v4 envelope and Task4 sidecar stages. A helper lacking `scenario-turn-v1` cannot trigger an unfiltered fallback after required memory or a partly presented head.

- [ ] Write synthetic stdio helper tests: one outbound turn yields main/head in either order and terminal complete; wrong parent/version/duplicate stage ignored or rejected safely; stage callbacks do not finish/release request ownership early; another player turn cannot interleave by accident.
- [ ] Test explicit cancel, timeout, shutdown, missing capability and malformed/oversize stage; drain stdout safely and preserve existing v2/v3 one-request behavior. A stage callback is not allowed to invoke Unity from the reader thread.
- [ ] Run `Tools/Conversation/Check-UnifiedConversation.ps1 -Filter ScenarioProcess`; RED before transport support. Implement per-turn stage pump inside the existing owned-process request boundary, retaining write/request locks and cancellation identity, rather than calling two serialized SendTurnAsync operations.
- [ ] Run full conversation + Scenario source harnesses and Python stdio tests. Record checkpoint. Do not launch two live helpers or open a local network server.

## Task 6: Deterministic delivery coordinator and five-second fallback races

**Files:** New `Assets/AlterOS/Conversation/ScenarioTurnCoordinator.cs`; tests `ScenarioDeliveryTests.cs`; modify transcript event helpers only for versioned stage/beat identities, not a second writer.

**Interfaces:** Implements shared coordinator methods with injected monotonic seconds and returns `ScenarioEffect`s. The host supplies a prevalidated prepared opening/bridge and current grounded context; no fallback LLM call.

- [ ] Write table-driven clock tests: at4.999s no fallback, at5.000s exactly one opening if no stage committed; body-first skips head; head-first waits for current beat boundary; fallback-first rejects late head; same-tick body/fallback race commits exactly one; body failure after head offers recovery, not a second opening.
- [ ] Test pending waits/one bridge only, player dismiss/supersession/quit, callback after new turn, changed permission/consent and duplicate beat receipts. Exactly-once ordered transcript writes only for actual presentation; receipt/late result does not become presented history. Preserving the current successful-late-reply model cannot resume an ended Scenario or start actions on a new session.
- [ ] Run `Tools/Conversation/Check-UnifiedConversation.ps1 -Filter ScenarioDelivery`; observe RED. Implement deterministic state/tick logic, one presentation owner, stage-specific IDs and bounded terminal timeout using current provider45s and coordinator60s ceilings.
- [ ] Add paired-output fixtures for duplicated opening, contradictory grounded preference, false action-success claim and body requiring an unanswered head question. Apply conservative structural/known-fact admission and evaluate free-text quality separately: neither JSON nor a keyword filter proves universal semantic agreement. Do not add an automatic third repair call. If unverified content cannot be admitted safely, keep opening and expose safe retry/close, not an unvalidated body.
- [ ] Run complete source harnesses; record checkpoint. Actual submit-to-visible timing remains a Unity acceptance check, not inferred from injected clocks/API times.

## Task 7: Authored adapter and Scenario Playground with simulated activities

**Files:** New `Assets/AlterOS/Scenarios/AuthoredScenarioAdapter.cs`, `Assets/AlterOS/Scenarios/ScenarioPlaygroundSession.cs`, and `Assets/AlterOS/Tests/Editor/Scenarios/{AuthoredScenarioAdapterTests,ScenarioPlaygroundTests}.cs`; fixtures `Tools/ScenarioRuntime/Fixtures/Playground/{cats,drawing,coding,diversions}.json`; modify existing Playground window/command and documented CLI entry point additively.

**Interfaces:** `AuthoredScenarioAdapter.Convert(TopicDefinition topic, TopicScenario scenario) -> ScenarioDefinition`; `ScenarioPlaygroundSession.Apply(ScenarioEvent input, double nowSeconds) -> ScenarioPlaygroundReport`. Reuse Tasks3/6 engine, not a second simulation implementation.

- [x] Write adapter tests preserving authored IDs/JP+EN/choices and current Topic checkpoint IDs; selection gates/priority remain owned by Topic. Local authored Scenario preview has zero provider calls. No `.nodes`→`.beats` bulk JSON rewrite or semantic rebake.
- [x] Add deterministic cats, drawing, coding and diversion fixture timelines; all fixture activity outcomes are explicitly synthetic. Pause/replacement/return-participant cases remain covered by the shared lifecycle suite rather than duplicating fixture state machines.
- [x] Add `Apply` as a wrapper around `ScenarioSession` plus a detached synthetic activity port; wire standalone fixture controls into the Editor Playground without scene/prefab changes or generated-content documentation.
- [x] CLI and Editor fixture runners consume the same event/time DTO and `ScenarioPlaygroundSession.Apply`; a deterministic paired-model regression compares reports. Existing Playground source suite 13/13 and Scenario suite 47/47 pass; unified conversation suite 162/162; Unity import plus focused adapter/Playground EditMode tests pass 5/5. Live batch execution, visual review and real GTE remain separate acceptance gates.

## Task 8: Narrow Unity presentation integration, no real activity implementation

**Files:** `Assets/AlterOS/Scenarios/ScenarioConversationPresentationModel.cs` and focused tests; direct coordinator/provider boundary, transcript event validation, `Assets/Scripts/Dialogue/DirectConversationBridge.cs`, `DialogueManager.cs`, and `Assets/Altera/AlteraInteractable.cs`. The existing bridge/dialogue runner/Canvas choice templates are reused; no new MonoBehaviour, static Canvas control, scene, prefab, or serialized wiring was needed in this source slice.

**Interfaces:** Presenter consumes `ScenarioEffect`s and routes current answer options versus next-E offers with their own stable IDs. Bridge captures initial submit clock/context before slow work, feeds stage results via a main-thread queue, Tick from Unity frame loop, and forwards acknowledged beat/close events. Extract existing reply/sequence conversion only if needed; don't duplicate runner or create a giant bridge.

- [x] Finish presentation-model test coverage for answer/free-text permission, main-close versus wrap-up, stale answer IDs, continuation dismiss/reopen without replaying acknowledged beats, and next-interaction offer gating. Current Scenario source harness67/67 and Unified Conversation source harness196/196. No separate Dessert stage/type is present in the v4 contract/tests.
- [x] Connect E interaction precedence, stale-safe answer/offer selections, optional free-text answer, dismissal/reopen and generated offers to existing Canvas choice/text-input templates. Return-prompt and visual E behavior still need the human gameplay check; no player-led/focus change or new static UI was introduced.
- [x] Add the guarded v4 gameplay route through the existing coordinator/provider and sole transcript owner. Ready stages stream through Unity Update while the peer/terminal receipt is pending, after response-receipt durability and host-context checks. Required memory remains gated until revalidation. The deadline starts at submit entry (including synchronous capture); cancellation is rechecked after provider completion; transcript durability gates beat checkpoint advancement; unsupported drawing/movement capabilities remain empty.
- [ ] Current Unity import/compile and focused EditMode verification. A prior 7/7 result predates the final regressions; the latest focused Unity request timed out with the Editor unresponsive. The source harness compiles current C# and passes, but is not Unity EditMode proof. No scene was saved or changed.
- [ ] Prepare the human-owned Splash->ModelTest->E test and separate five-second first-visible measurement. No API/TTS call for self-verification without new explicit authorization. No claim of actual drawing/coding activities.

## Task 9: Package, acceptance matrix, documentation and execution handoff

**Files:** Existing `Tools/CompanionRuntime/build_windows.ps1` (reuse, no dependency upgrades); output new `Build/CompanionRuntimeScenarioAcceptanceReviewFix2` and distinct work folder; update Scenario design/plan/handoff, [[Feature Register]], [[../SessionZero]], `TODO.md` and current implementation snapshot where ownership/wiring actually changed.

**Interfaces:** Package advertises `scenario-turn-v1`, keeps old keyless memory/legacy behavior, v4 mixed-stage stdio test succeeds. Do not switch a saved helper path silently or overwrite valuable existing packages.

- [x] Run current owned source suites: Scenario67/67; Unified Conversation194/194; Topic Playground13/13; Python173 passed/0 skipped with packaged keyless v3 memory and v4 Scenario readiness smokes. `Validate-TopicCatalogue.ps1` confirms57 families/161 scenarios/161 rows. No route JSON, anchors, generated private content, or catalogue migration changed.
- [x] Build the sidecar in a distinct review-fix output/work folder. The packaged keyless v4 bootstrap/offline receipt plus v3 memory command smoke passed with no credential or provider use; Python fixture tests cover mixed head/main output. Current executable SHA-256 and exact smoke scope are recorded in the dated verification note.
- [x] Review M2/M2.1 source regressions, deadline and paired semantic fixtures. Actual visible timing, text quality, TTS, and paid/provider quality remain unmeasured and require a separately approved user pass.
- [x] Record source/package evidence and deferred activities; no route snapshot update was needed because authored JSON did not change. No generated private data or global memory was published.
- [x] Provide the implementation handoff and preserve the remaining Splash->ModelTest->E check as the stop boundary. No auto-start of M3, TTS, phone, furniture, external tools, or provider spend.

## Self-review / spec coverage

Ownership and schema ->Tasks2/7; lifecycle/autonomy/return rules ->Task3; parallel/cancellation/privacy/clock ->Tasks4–6; Ask/next-E separation ->Tasks3/7/8; closing is main content ->Tasks2/4/8; cats/drawing/coding fixtures ->Task7; package/UI/evidence boundaries ->Tasks8/9. Five Review Focus risks each have explicit owning test steps above.

No full needs/activity/furniture implementation is claimed. A physically usable drawing/coding table requires a later integration plan. Proposed pure APIs/types above are new planned work, not existing APIs. Exact constructor syntax/DTO internals may follow adjacent project style, but semantics, limits and cross-task method names must remain consistent.

**Execution checkpoint (2026-10-20):** Tasks 1–9 source integration and keyless package checks pass. Fresh regressions cover cancellation races, streamed stage delivery while terminal receipt is pending, transcript-before-display durability, idempotent transcript retries, playback-advance checkpoints, late failure ownership, submit-time deadline, weak candidate passage to RAG, and confidence-gated local fallback. Current source counts are Scenario67/67, Unified194/194, Python173/173 (packaged keyless v3/v4 smokes), Topic Playground13/13. The latest focused Unity EditMode request timed out; prior 7/7 evidence predates these fixes. Editor remains on ModelTest, Play Mode is off, and no scene/prefab/Room Testbox asset changed. Human Splash->ModelTest->E, visible return-prompt controls, and measured first-visible timing remain open. No provider/TTS call or paid test was made.
