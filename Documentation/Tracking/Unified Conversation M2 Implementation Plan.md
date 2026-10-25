# Unified Conversation M2 Implementation Plan

> Reconciliation 2026-10-22: this is the original bounded M2 execution plan, not
> a request to redo implemented work. [[Unified Conversation M2 Handoff]],
> [[Scenario Conversation Handoff]] and root TODO own current source/acceptance.
> Phone/history/optional TTS were moved to M4; M3 now covers furniture/activities.
> The later SQLite cutover supersedes file-backed runtime storage details below,
> while preserving facade, event identity, consent and presentation semantics.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove startup -> E -> free text -> eligible authored dialogue or a real structured generated continuation, without a Topic/Agent switch, while preserving offline play.

**Architecture:** Unity retrieves candidates, owns context/history/eligibility and presents validated dialogue. A hidden managed Python child receives a bounded snapshot over inherited standard streams, uses a small LangGraph workflow and OpenRouter, and returns strict Pydantic-validated judgment/dialogue. Unity independently validates and resolves the plan; generated prose cannot apply game effects or publish a catalogue entry.

**Tech Stack:** Unity 6000.4.4f1/C#, Python, LangGraph, Pydantic, one HTTP client, OpenRouter `deepseek/deepseek-v4.1-flash`. No LangChain agent/tool runtime, PydanticAI, remote backend or local general LLM.

**Spec:** [[Unified Conversation Design]], especially the corrected bilingual contract; [[Unified Conversation M1 Handoff]] is the actual source baseline.

## Approval gate and scope

The product direction/model/language and proposed transport, budgets, pilot
coverage, persistence layout and packaging were approved on 2026-10-16 with the
clarifications below. Approval does not authorize paid API tests or dismissing
an unsaved scene. No live provider call is in the normal test path.

This plan implements direct conversation only. Phone/history UI, optional speech
controls/audio acceptance and return-to-E presentation polish are M3. Durable
response identity/progress is included now so M3 cannot duplicate work. No tools,
stat changes, autonomous activities, background generation/publication, advanced
memory retrieval, hosted embedding service or wholesale UI/content redesign.

## Global Constraints

- Work in `E:\Pleiades\alteros`; `pleiades.os` is archive-only. Read AGENTS/TODO first.
- Preserve open ModelTest/Topic Playground and other sessions' edits. Unity Editor owns scene/prefab/meta composition. Do not close or discard unsaved scenes to run batch mode.
- Keep M1 schema-v1 fixtures/behavior supported; new online contract is explicitly v2. No silent reinterpretation of v1 replies.
- Every v2 generated beat: Japanese `text`, English `displayText`, `language="japanese"`; English-first player input/display. Both texts remain required with TTS off. No mandatory second translation request.
- Authored-first, priority then unseen then weight; consumption is whole-instance and completion evidence is separate. Discussion of an experience is not replaying it or awarding its effects.
- Existing BYOK P panel, DPAPI store and environment override are reused. Saving a key does not enable online generation.
- No credentials in arguments, saved config, prompts, stdout diagnostics, exceptions, transcripts or graph/checkpoint state. No telemetry/LangSmith tracing.
- Gameplay contracts never expose provider/LangGraph internals or arbitrary effects/paths. Never feed raw model JSON into legacy StartSequence/effect parsing.
- No SSE. UI waits asynchronously for a complete validated reply, never blocks its thread waiting for process/network I/O.
- No Git HEAD exists in the reported baseline. Never stage the entire untracked tree or commit unrelated work; checkpoint focused files only after establishing a safe user-approved baseline.

## Approved clarifications (2026-10-16)

- Task 1 tests coverage against the validated staged Topic catalogue and
  migration map. Task 6 must verify the same IDs/payloads after promotion to the
  production catalogue and keep the new loader disabled until that check passes.
- A generated noncanonical occurrence uses a host-assigned `responseId` as its
  durable occurrence identity; it does not receive a fabricated canonical Topic
  or scenario ID and is never published by M2.
- `ConversationTranscript` is the single authoritative writer for the new M2
  path. Existing `ConversationSessionStore` records remain read-only; no dual
  writes are added.
- The proposed eight-request/$0.25 live smoke remains a separate spend approval.
  No key or provider call is used during implementation. Do not describe that
  proposed manual cap as a hard provider-enforced spend ceiling.
- For OpenRouter output, request JSON mode and validate the parsed response with
  strict Pydantic models plus the application judgment validator. Keep
  `provider.require_parameters=true`; do not rely on endpoint schema enforcement
  for authorization or gameplay rules. Error diagnostics are allowlisted stage
  names only (`response_json`, `response_envelope`, `provider_termination`,
  `judgment_schema`, `usage_metadata`, `output_format`,
  `judgment_validation`); never surface response text or validation details.

## Review Focus

1. A false uncovered/novel judgment must not replay a blocked experience or apply its effects (Tasks 1, 6).
2. Credential changes, provider errors and process crashes must not leak secrets, retry billable work automatically or break offline dialogue (Tasks 3, 4).
3. Closing E, duplicate submits, shutdown and a reply arriving late must not duplicate history/completions or seize the successor UI (Tasks 5, 6).
4. Japanese speech/English display fields must survive both validation layers and rendering unchanged, with no English string sent as Japanese TTS input (Tasks 2, 6).
5. Long/partial/corrupt history, malformed protocol and output truncation must fail safely without deleting older records or publishing partial dialogue (Tasks 2, 3, 5).

## Proposed operating limits

One in-flight conversation turn, no pending-turn queue. One provider generation
request per submission, zero automatic repair/retry/fallback-model calls. Manual
retry creates a new request and makes its cost visible. Sidecar readiness timeout
15s, provider deadline45s, total turn deadline60s; these are proposed usability
defaults, not measurements. Timeout after sending may still incur provider cost.
Helper shutdown gets2s then terminates only the owned child process.

Keep M1 input2048 chars, top8 cards, 0..3 suggestions. V2 generation: 1..16
`reply.beats`, each text/displayText at most600 characters; total Japanese+English
text at most12000 characters; provider max_tokens4096. This may be insufficient
for some long answers: reject truncation honestly, never silently cut beats or
auto-call again. Prompt for concise complete beats. Recent context: at most12 presented
utterances/6000 combined text characters, with speaker/source/response IDs; never
send unseen/undelivered Altera prose as already heard. Character rules <=8000
characters; candidate/context/coverage text <=10000; provider request JSON
<=64KiB UTF-8. These are byte/character guards, not exact tokenizer budgeting.
Suggestion `label` and `prompt` use the current English display language. The
host omits entries with non-Latin letters or supplementary characters without
rejecting the rest of a valid reply. Locale-tagged suggestions remain future
work if the UI gains additional display languages.

Protocol lines <=256KiB UTF-8, version2 envelope; inner dialogue limit remains
128KiB. Disabled online/key-missing/helper-failed paths use current local prepared
fallback. HTTP failures do not masquerade as generated successes. No permanent
busy state. Generation is off by default; an authored Canvas toggle enables it,
separate from TTS and not described as a Topic/Agent switch.

## Source map and intended ownership

Existing M1: `Assets/AlterOS/Conversation/{ConversationContracts,ConversationContractValidator,TopicCandidateRetriever,UnifiedConversationPlanner}.cs`, Topic selector/history v2, `Tools/Conversation/` checks. Production `DialogueManager.AgentModeAvailable` remains false and its text submission still uses the legacy semantic coroutine.

New focused C# files under `Assets/AlterOS/Conversation/`:
`ConversationCoverage.cs`, `ConversationProtocol.cs`, `CompanionProcessClient.cs`,
`ConversationTranscript.cs`, `DirectConversationCoordinator.cs` (pure async owner).
Scene-wired binding under `Assets/Scripts/Dialogue/DirectConversationBridge.cs`;
`DialogueManager.cs` receives only narrow dispatch/presentation hooks, not a
second monolithic harness. Reuse the canonical-to-legacy mapping after exposing
a safe generated-reply adapter with no mechanical fields.

Python lives in `Tools/CompanionRuntime/`: `pyproject.toml`, lockfile,
`src/alteros_companion/{contracts,protocol,provider,prompts,graph,__main__}.py`,
`tests/`, `README.md`, and a PyInstaller onedir build recipe. Pin actual tested
compatible dependency versions during setup; don't invent versions from memory.
Use a repo-local environment, never global installs. Build output/venv stay ignored.
Release helper lives beside the player under `CompanionRuntime/`, not source assets.

## Task 1: coverage-aware known-subject generation without replay bypass

**Files:** New `ConversationCoverage.cs`, `Assets/StreamingAssets/Dialogue/ConversationCoverage.json`, and `ConversationCoverageTests.cs`. Keep M1's schema-v1 planner behavior unchanged; Task 2 owns the typed v2 uncovered-judgment path and consumes this policy/snapshot API.

**Interfaces:** `ConversationCoveragePolicy` keyed by topicId, with `allowUncoveredDiscussion` and scenario entries `{scenarioId, coverageSummary, experienceKey}`. Missing permission defaults false. Summary <=400 chars, experienceKey stable <=128 chars; during Task 1 resolve IDs against the validated staged Topic catalogue and migration map. Task 6 revalidates the same IDs against the promoted production catalogue before enabling its loader. This is host-authored metadata, never model-authored permission. `CoverageSnapshot BuildSnapshot(candidateTopicIds, policy, catalogue, authoredInstances, currentContext, history, nowUtc, channel)` exposes summaries and ready/blocked status, not entire graphs. Pilot permission is only `player_tired`, `ask_preference`, `ask_task_help_short`; write reviewed subject/experience descriptions for those families. Other existing Topics remain authored-only/clarification in M2.

V2 `existing_uncovered` requires known topicId, referencedTopicIds and a paired-text reply. Host allows it only for the explicit pilot policy, subject-level discussion, no action/effect requests. A consumed scene is unavailable to authored playback but its subject may still be discussed. Scene availability is not permission to discuss its whole category. Generated occurrences never satisfy an authored experienceKey, increment its completion count, or become routable. Model coverage judgment remains fallible; semantic disguises need scenario evaluation, not a claim that schema validation proves truth.

- [x] Write failing tests for the policy-file pilot IDs/experience map, fail-closed defaults and invalid references, bounded metadata, and consumed authored availability remaining blocked while subject discussion permission remains separate.
- [x] Implement the host-authored staged-catalogue policy validator and candidate-scoped summary snapshot. It exposes no graph text, never mutates TopicHistory, and checks current authored availability using the shared selector.
- [x] Preserve M1 schema-v1 `KnownTopicCoverageDeferred`; the v2 `existing_uncovered` outcome is implemented with its distinct v2 judgment in Task 2, per the recorded interface ruling.
- [x] Run the full conversation suite35/35, Topic Playground13/13, Topic runtime33/33, catalogue57 families/161 scenarios and Unity coverage EditMode5/5. Unity compilation/import completed with no new errors; existing warnings remain.

## Task 2: bilingual v2 contract parity and bounded context

**Files:** `ConversationProtocol.cs`, distinct v2 judgment/job/result DTOs and planner validation, existing contract validator, `ConversationCoverage.BuildSnapshot` consumption from Task 1, Python `contracts.py`; tests `ConversationProtocolTests.cs`, `tests/test_contracts.py`; shared fixtures `Tools/Conversation/Fixtures/v2/`; repo-local `pyproject.toml`/`uv.lock` for reproducible contract tests.

**Interfaces:** Envelope `{protocolVersion:2, type, requestId, payload}`; types bootstrap/ready/turn/result/error/cancel/shutdown. Bootstrap is transport-only and secret-bearing; turn payload `ConversationJobV2` contains request, prepared candidates, coverage snapshot, bounded presented history, character rules and allowlisted expressions. No key in this job. Result carries v2 judgment and sanitized usage/timing. Error carries stable code and retryable flag, never upstream raw text; optional `diagnosticCode` is an allowlisted stage name only and contains no provider response detail. `ParseEnvelope(string)`/Python `parse_envelope(bytes)` enforce shape/size/version; `BuildJob(...)` returns a frozen snapshot. C# and Pydantic reject extra/duplicate properties and coercion; Python JSON decoding must explicitly reject duplicates/nonfinite numbers, not rely on Pydantic alone.

- [x] Write matching positive/negative fixtures: paired beat, missing English, wrong speech language, unknown field, oversized translation, unknown cue, wrong request/session binding, provider truncation, duplicate property, mismatched version, prompt injection in retrieved text. Synthetic data only.
- [x] Run both parsers against identical fixtures; capture initial failures.
- [x] Implement strict v2 `reply.beats` plus validation before mapping to canonical runner nodes. Both texts are required/nonblank; language tag isn't proof of translation quality. Keep user text/history/candidate content as labelled data, not system instructions. Prompt construction/grounding checks remain in the provider prompt stage (Task 4); do not import old mode-switch/JP-EN three-line instructions or private USER/MEMORY files.
- [x] Run C#/Python parity and context tests: no undelivered content, stable recent ordering, omission never truncates a node mid-text, request/byte limits, no credentials in serialized job/schema.

## Task 3: private managed sidecar transport and packaging

**Files:** `CompanionProcessClient.cs`, Python `protocol.py`/`__main__.py`, runtime packaging recipe/README, `CompanionProcessClientTests.cs`, `tests/test_protocol.py`.

**Interfaces:** `StartAsync(CancellationToken)`, `SendTurnAsync(string requestId, ConversationJobV2, CancellationToken)`, `CancelAsync(string requestId)`, `RefreshCredentialAsync(CancellationToken)`, `StopAsync()`. The host supplies request identity so results stay bound to its turn. Child uses inherited redirected stdin/stdout; UTF-8 newline-delimited compact JSON (literal line breaks escaped inside strings), serialized writes and bounded reads. No TCP listener or discoverable endpoint. ProcessStartInfo uses an absolute executable, `UseShellExecute=false`, `CreateNoWindow=true`; no shell launch/string interpolation. The Task 6 bridge must select only the packaged helper or an explicitly enabled/validated Editor development path. Missing packaged helper disables online path cleanly.

- [x] Write failing process tests using a fake helper: correct ready, wrong version, overlong/incomplete line, stderr secret marker, exit mid-turn, duplicate result, stdin closed, cancellation, parent EOF, restart after failure.
- [x] Implement async pumps that drain both streams without deadlock. Stdout only protocol; stderr is drained/discarded. Credentials are injected through a provider callback and sent once in bootstrap only; task 6 wires the existing `OpenRouterCredentialStore`. Refresh/restart helper after Save/Remove key when idle; no automatic resend. Credential removal cancels active online use locally. Clear the inherited `OPENROUTER_API_KEY` override before launch.
- [x] Graceful EOF/shutdown stops child; terminate only the stored owned Process, never by process-name matching. No automatic replay after crash. Ready does not wait for a provider call; offline game startup does not wait for Python. A future new explicit submission may restart a failed helper.
- [x] Run process tests without real keys/network. Built a Windows Python 3.13 / PyInstaller 6.22.3 onedir package and launched it from a path with spaces with no developer Python on PATH. It returned ready, then the explicit `provider_unavailable` stub response; no real provider request or credential was used. Unity production launch wiring remains Task 6.

## Task 4: one bounded OpenRouter request in LangGraph

**Files:** Python `provider.py`, `prompts.py`, `graph.py`; `tests/test_provider.py`, `tests/test_graph.py`; test helper configuration.

**Interfaces:** `async generate(job: ConversationJobV2, credential: str) -> ProviderResult`, where ProviderResult contains parsed v2 judgment, usage and safe status. Graph stages are validate_job -> build_prompt -> call_provider -> validate_result -> finish. Inject provider in tests. Credential is closed over, not a graph field. No checkpointing of credentials/provider headers; no durable graph replay required for this one-call slice. Unity owns durable conversation.

- [x] Write failing mocked-HTTP tests: model ID exact; JSON response format; supported-provider parameter enforcement; one call only; 401/402/429/5xx; timeout after send; invalid JSON; refusal; output token limit; unrelated tool_calls; prompt injection; errors containing key markers. Assert no retries, repair calls, trace export or raw error logging.
- [x] Implement a single JSON-mode request, `stream=false`, proposed45s provider timeout, no SDK auto-retries, and `provider.require_parameters=true`. Strict Pydantic output models and `validate_judgment` remain authoritative; never trust model output for eligibility. The API's current documented output limit field is `max_completion_tokens=4096` (not deprecated `max_tokens`); no unverified reasoning parameter is sent. Exclude reasoning from UI, history and further prompts. No tools parameter in M2.
- [x] Include paired Japanese/English examples, distilled SOUL/IDENTITY personality guidance, authored-first and coverage rules in the bounded prompt. Generated dialogue never reports a game action as completed without authoritative evidence. Don't claim this prompt guarantees semantic compliance.
- [x] Run mocked tests and graph validation. Python 42/42 and C# conversation 54/54; a fresh Windows onedir package was built and launched from a path containing spaces with PATH limited to System32, no Python path or API key, bootstrap returned ready, stderr empty and clean exit. No live provider call. Live smoke testing stays a separate explicit user approval with a stated spend/request cap.

## Task 5: durable transcript and delivery state without fake long-term memory

**Files:** `ConversationTranscript.cs`, tests `ConversationTranscriptTests.cs`; narrow adapter in existing `Assets/Scripts/Interaction/ConversationSessionStore.cs` only if needed to prevent a second authoritative writer for the new path.

**Interfaces:** `Append(ConversationEvent): AppendResult`, `ReadRecent(int maxUtterances,int maxCharacters)`, `ReadPage(string cursor,int count)`. One stable companion/session identity across launches and channels; request/response IDs, source/channel/topic/scenario, validated nodes, presented-node progress and status events. For an unpublished generated occurrence, the host-assigned responseId is its stable occurrence identity; do not invent a canonical Topic/scenario ID. `ConversationTranscript` is the only M2 writer; existing `ConversationSessionStore` remains read-only. Append-only versioned JSONL under Application.persistentDataPath, single writer with flushed events; bounded paged reading, never lifetime load into RAM. Tail recovery ignores/quarantines only an incomplete final line and preserves original bytes; corruption in the middle fails read-only rather than rewriting. Persist request receipt and result/presentation separately so history doesn't imply the player heard pending lines.

- [x] Write failing tests: >120 events remain stored; duplicate IDs/acknowledgments don't append twice; partial last line; corrupt middle; write failure; restart with request/result-unpresented; no raw credentials; recent history excludes unpresented dialogue. Legacy records remain untouched/read-only; no deletion/trimming to fit context.
- [x] Implement bounded transcript with explicit privacy statement (private local files, not guaranteed encrypted at rest). No credential/effect fields. A partial final line is ignored by reads, but append fails without changing it; middle corruption is read-only. Disk failures return an explicit unsaved result. New continuation binding and resend behavior remain coordinator-owned in Task6.
- [x] Test duplicate completion/history acknowledgments and crash boundaries. Source-to-transcript provenance is explicit; summaries/model claims are not new user facts. ReadPage is a seam, not phone UI or advanced memory retrieval.

## Task 6: production direct-channel coordinator and safe node presentation

**Files:** `DirectConversationCoordinator.cs`, `Assets/Scripts/Dialogue/DirectConversationBridge.cs`, focused `DialogueManager.cs`/`DialogueRouter.cs` hooks, `DialoguePlayback.cs` extraction if required, tests `DirectConversationCoordinatorTests.cs`, `DirectConversationPresentationTests.cs`; reviewed production Topic content under `Assets/StreamingAssets/Dialogue/Topics/`.

**Interfaces:** `SubmitAsync(string text,CancellationToken)`, `DismissPresentation()`, `CancelTurn()`, `AcknowledgePresented(string responseId,string nodeId)`. Main-thread presenter receives only validated canonical nodes/authored selection. Reuse existing VN runner semantics, extracting a node-only validated playback entry point if necessary; don't invent anchors or create a second dialogue engine. Generated conversion leaves all legacy stats/effects/actions empty. Online flag persisted as nonsecret setting, default false.

- [x] Write failing fake-provider tests for online, authored/offline fallback, clarify, missing key, duplicate submit, changed active response during a request, dismissal/late success, unavailable GTE and bilingual display. Existing planner/selection regressions cover consumed instances and invalid references. Confirm generated history is an unpublished response occurrence, not a new catalogue entry.
- [x] Promote the validated staged families through `Tools/Conversation/Promote-StagedTopics.ps1`; the 57 Topic payloads and migration map were copied byte-for-byte and revalidated. Existing legacy files remain; no vectors/bake. The production-loader feature gate remains explicit in `ModelTest`.
- [x] Reuse warmed GTE route matches via a small adapter from `DialogueRouteMatch[]` to M1 cards (actual best-anchor examples, prepared availability and SHA-256 model/index identity). No second worker/index is loaded. Missing/mismatched Topic families fail closed.
- [x] On online submit, freeze host context/candidates/presented history, call the helper once, validate binding, re-evaluate current state, plan and present. Offline selects locally. Dismissal/context change stores a successful late response once without auto-presenting it. Continuation uses host-owned response/topic IDs. No concurrent submission queue.
- [x] Bind authored playback to real `TopicHistory.BeginPlayback`/`ReportPresentedNode`/`Complete` and save at checkpoints/completion. Each node is acknowledged to the sole transcript only when the typewriter displays its first character. Generated VN beats preserve Japanese/English fields and have no stats/effects/action fields. The direct validated path is text-only; it never prefetches or starts billable TTS.
- [x] Through Unity Editor, added the online-enable toggle and safe status text under the existing `DialogueCanvas/InputPanel`, attached and configured the bridge, and saved only `Assets/Scenes/ModelTest.unity`. Existing E greeting/ambience and typed input remain. Suggested replies use existing choice UI and are bound to their response ID; no phone UI or activity simulation.
- [ ] Run deterministic integration tests and no-key Play Mode tests after the Unity Editor is safely recovered. The focused M2 EditMode selection passed16/16, but a full190-test request left the current Editor unresponsive before returning. Do not call a source check a Game View pass or force-close the Editor to get a result. Existing unrelated test failures must remain visible, never hidden/skipped.

## Task 7: bounded acceptance, evidence and stopping gate

**Files:** `Tools/CompanionRuntime/README.md`, `Documentation/Verification/2026-10-16 Unified Conversation M2.md` (actual run date), `Documentation/Tracking/Unified Conversation M2 Handoff.md`, TODO/Feature Register/SessionZero/UnityProjectContext as affected.

- [x] Run the Python suite and shared C#/Python fixtures, `Tools/Conversation/Check-UnifiedConversation.ps1`, Topic/Playground source regressions and staged/production catalogue checks. Final source results after review fixes: C#72/72, Python42/42, Topic Playground13/13, Topic Runtime33/33, promoted catalogue57 families/161 scenarios. PowerShell promotion syntax passes.
- [ ] Safely recover/confirm the AlterOS Editor; normal compile/import, Console, full EditMode and relevant PlayMode tests. Preserve unsaved content; no batch-mode workaround that closes it. Verify no-key startup through SplashScene -> ModelTest, E and local authored routing.
- [ ] After separate human approval of spend cap, use explicit online consent and real saved/env key without printing it. Proposed live smoke cap: <=8 text requests, <=US$0.25 total authorized budget, abort on repeated malformed/provider errors; report actual usage/cost if supplied and unknown cost honestly. No TTS calls. Test authored selection, uncovered known subject, continuation, ambiguity, optional suggestions, late result, provider loss and manual retry where feasible. Use mocked failure tests to avoid burning requests on artificial errors.
- [ ] Measure helper cold/warm readiness, idle working set/CPU, frame-time around startup/submit, full validated reply latency and recovery behavior. Report sample count and provider route; eight live requests are not statistically reliable tail-latency proof. No fixed FPS/latency success claim without measurement. Package helper acceptance separately from Editor launch.
- [ ] Update evidence distinguishing source tests, package, Unity compilation, UI-assisted/physical E, generated text, quality and audio (not tested). Preserve private logs outside shared docs; publish only synthetic examples/aggregate numbers. Record hashes/changed files and unresolved semantic/translation issues. Do not mark real Live acceptance passed without an actual provider result visibly presented.
- [ ] STOP at direct conversation. No M3 phone UI/TTS expansion, tools, memory service or background catalogue generation. If paid approval/MCP/packaging is unavailable, hand off precise remaining gates rather than bypass them.

## Self-review and prerequisites

This plan explicitly addresses M1's known-uncovered gap and consumed-continuation
clarification; corrects the English-only mistake; separates transport credentials
from job/state; preserves source provenance, offline behavior and gameplay-owned
effects. M1 source/check evidence is inherited from its handoff, not rerun here.
The short pilot scope limits editorial work while exercising the actual paths.
Coverage reliability and translation fidelity are evaluated, not guaranteed.

User approved the proposed transport/limits/persistence/pilot on 2026-10-16
with the clarifications above. Dependency versions/SDK parameter compatibility
must be verified during setup. Live calls need a separate explicit spend
approval; saving BYOK is insufficient.
Stop on conflicting concurrent edits, an unexpected destructive migration, a
required private prompt read not authorized, or a packaging/security assumption
that cannot be met. Return questions rather than silently changing architecture.

References checked while planning: [LangGraph overview](https://docs.langchain.com/oss/python/langgraph/overview),
[Pydantic strict mode](https://docs.pydantic.dev/latest/concepts/strict_mode/),
[OpenRouter structured outputs](https://openrouter.ai/docs/guides/features/structured-outputs).
