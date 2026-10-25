# Player Memory M2.1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. The user explicitly requested implementation to start in the existing M2 session, not another implementation-planning turn. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local short-/long-term player memory, opt-in first-meeting onboarding, bounded source-backed recall and game-controlled personal-question scheduling to the existing M2 conversation path.

**Architecture:** Keep Unity's append-only transcript as source truth; a policy-controlled Python SQLite repository stores derived memory. Add an explicitly versioned memory-capable local protocol and bounded context stage to LangGraph, preserving v2 operation when memory is unavailable/off. LLM writes proposals, never SQL or authoritative player state.

**Implementation status (2026-10-19):** Tasks 1–3 have source-level
implementation and regression coverage, including the production v3 turn path.
Task 4 has the structured same-response wrapper and pure question policy, but
question-history/settings integration and pending-proposal review are incomplete.
Task 5 has authored onboarding prompts, a tested session model, and durable
progress reads; first-E and Canvas controls remain unwired because Unity MCP
timed out on a read-only Console request. Task 6 source/package checks pass; its
Editor/player acceptance gates remain pending. See [[Player Memory Handoff]] and
[[2026-10-19 Player Memory M2.1 Implementation]]. Python128/128 and C#106/106
passed with the package smoke enabled. Source tests do not establish Unity
Editor, UI, Play Mode, provider, TTS, or player-visible acceptance.

**Tech Stack:** Existing Unity/C#, Python3.13 sqlite3, installed Pydantic/LangGraph/httpx; no additional database/agent/vector dependencies. DeepSeek model and paired JP speech/EN display remain unchanged.

**Spec:** [[Player Memory Design]]; source baseline [[Unified Conversation M2 Handoff]]; research [[Companion Continuity and Memory Research]].

## Execution authorization and boundary

The human approved trying this memory architecture and explicitly authorized this
plan's handoff to START implementation. Do not respond with another generic plan
or ask again whether to begin. These named defaults are bounded trial choices;
change only if a concrete compatibility/security problem requires it and document
why. Ask only for missing authority, destructive recovery or material scope changes.
Existing M1/M2 source remains the foundation; M2 runtime acceptance stays pending.
No paid calls, TTS requests or force-closing the stalled Editor are authorized.

## Global Constraints

- Root `E:\Pleiades\alteros`, not archived pleiades.os. Read actual current AGENTS/TODO/source; preserve other sessions' edits and no whole-tree staging in this untracked repo.
- All personal data/fixtures obey the spec's consent/provenance/scope/expiry rules; shared tests are synthetic. Never read private USER.md or Codex memory files for player data.
- Existing v1/v2 fixtures and local prepared dialogue continue working. No silent extra keys in strict v2 JSON. Version3 is negotiated/explicit; old helper means no memory feature, not an unsafe downgrade or broken offline conversation.
- SQLite memory operations are keyless and never call providers. Generation remains separately opt-in; memory proposals use the existing single generation response, never another extraction request.
- Unity owns consent, source evidence, profile confirmation and game state; Python validates/appends derived records. No model effects, raw SQL, file paths or persona rewriting.
- New static controls are authored through the Unity Editor on Canvas/prefabs; no runtime-built HUD, YAML/meta edits or unrelated scene saves. Implement source/tests while blocked; report actual UI wiring separately.
- The numerical limits, timers, fields, question allowlist and forgetting semantics in the spec apply to every task. Inject time/draw in tests. Expiry and deletion are different operations.

## Review Focus

1. Forgotten or superseded data reappearing through recent transcript, replay, caches or a late proposal (Tasks1,3,5).
2. Model narration/roleplay/third-party quotations becoming authoritative player facts (Tasks1,4).
3. Offline onboarding or Settings accidentally requiring an API key, online consent or valid persona (Tasks2,5).
4. A stale24h activity resurfacing as current, or every callback refreshing its expiry (Tasks1,3).
5. Duplicate/racing first-meeting callbacks and LLM questions bypassing focus/consent/cooldown (Tasks4,5).

## Existing files and proposed decomposition

Existing Python: `Tools/CompanionRuntime/src/alteros_companion/{contracts,protocol,__main__,graph,prompts,provider}.py`. BootstrapV2 currently requires a nonempty key; the stdio process therefore cannot simply be reused for keyless SQLite without an explicit new path. `ConversationJobV2` currently has no playerProfile/memory fields. `graph.generate` validates/builds/calls/validates once. Preserve that one-call path.

Existing Unity: `Assets/AlterOS/Conversation/{ConversationProtocol,CompanionProcessClient,ConversationTranscript,DirectConversationCoordinator}.cs`, `Assets/Scripts/Dialogue/DirectConversationBridge.cs`. Transcript has eventId/sequence/source/channel/UTC and bounded ReadPage/ReadRecent; it remains the only source-history writer. Do not introduce another competing transcript.

Create focused Python `memory_contracts.py`, `memory_repository.py`, `memory_context.py`; C# `Assets/AlterOS/Memory/{PlayerMemoryContracts,PlayerMemoryClient,PlayerQuestionPolicy,PlayerOnboardingSession}.cs`; scene binders `Assets/Scripts/UI/PlayerMemorySettingsPanel.cs` and `Assets/Scripts/Dialogue/PlayerOnboardingController.cs`. Keep protocol3 serialization focused in `Assets/AlterOS/Memory/PlayerMemoryProtocol.cs` rather than growing the existing large parser unchecked. No speculative provider/registry abstraction.

## Task 1: transactional SQLite memory with clocked expiry and forgetting

**Files:** Python memory_contracts/repository; tests `Tools/CompanionRuntime/tests/test_memory_repository.py`, `test_memory_policy.py`.

**Interfaces:** `MemoryRepository(db_path, player_id)`, `set_consent(settings,expected_revision,now_utc)`, `upsert(proposal, evidence, consent, now_utc)`, `confirm(proposal,evidence,consent,now_utc)`, `read_context(query,consent,now_utc)`, `forget(memory_id,now_utc)`, `list_page(cursor,limit)`. Return structured accepted/pending/rejected/duplicate/error outcomes; no exceptions with personal payloads. Context returns core/current/recalled records and suppressedSourceIds.

- [x] Write failing tests: roundtrip/restart, source+proposal duplicate, scoped actors, contradictory coffee correction, multiple interests survive,24h/2h boundary, historical recall not current, retrieval doesn't renew expiry, unknown/null vs zero, memory off, unconfirmed routine, invalid source quote, wrong player, forgotten source reinsertion, read-only/corrupt DB rollback and bounded paging.
- [x] Run `python -m pytest tests/test_memory_repository.py tests/test_memory_policy.py -q` from Tools/CompanionRuntime and capture the initial failures.
- [x] Implement schemaVersion1 database with transactional migrations; existing tables are never dropped/rebuilt automatically. Tables: memories (spec fields), evidence (host source metadata/excerpts), suppression (source/category/key; no forgotten prose), onboarding progress, question history and consent revision. Use parameterized SQL, a single writer, bounded reads/transactions and foreign-key/source consistency. Source dedup+revision checks fence racing writes after forget/consent changes. File corruption fails read-only with safe error, never overwrites the DB.
- [x] Use FTS5 only if available; test a parameterized escaped-LIKE fallback with the same consent/time/scope filters. Don't implement embedding infrastructure. No keyword hit => zero recalled entries. Exact preference corrections use matching category/key, never delete the player's entire profile.
- [x] Run tests; verify no dependency added and only temporary test DBs touched. Read queries must not change freshness or resurrect expired observations. List Settings data independently from LLM eligibility.

## Task 2: protocol3 memory-only service and C#/Python parity

**Files:** C# PlayerMemoryContracts/Protocol/Client; existing CompanionProcessClient and Python protocol/contracts/__main__; tests `Assets/AlterOS/Tests/Editor/Memory/PlayerMemoryProtocolTests.cs`, Python `test_memory_protocol.py`, shared `Tools/Conversation/Fixtures/memory-v3/`.

**Interfaces:** Version3 stdio envelope retains `{protocolVersion,type,requestId,payload}`. Bootstrap3 `{apiKey,playerId,memoryRoot}` accepts empty apiKey, validates host absolute persistent-data path and never exposes it in model jobs. Ready3 reports capabilities memory=true, generation=credential available; no provider contact. Memory command3 payload `{operation,playerId,consentRevision,nowUtc,data}` supports consent, source_sync, profile_set, confirm, context, list, forget, onboarding/onboarding_read, question_ack. Explicit operation-specific payload models, not arbitrary executable dictionaries. C# client methods use request IDs and a frozen consent snapshot.

- [x] Write parity/process tests for keyless local startup, v2 compatibility, old helper rejection, strict envelopes, wrong player, line/path limits, cancellation, persona absence, safe errors, and source values as plain text.
- [x] Implement keyless local startup separately from generation readiness. V2 remains supported; v3 operates without persona/key for local memory while generation keeps its original gates. The owned child/stream pump is shared and serialized; requests retain the 256KiB cap.
- [x] Sync only post-checkpoint player request sources from Unity ReadPage in pages <=32 events/16KiB, with source/session/scope/timestamps and bounded excerpts. The first empty source-sync request checkpoints at the current transcript sequence, so pre-opt-in history is not backfilled. Gapped event sequences advance with `throughSequence`; generated assistant text is not profile evidence.
- [x] Run C#/Python fixture and fake-process tests. The memory-only path does not read a saved API key. A missing helper reports unavailable; local authored conversation remains available.

## Task 3: bounded short-term/profile/recall context in the existing graph

**Files:** Python memory_context and graph/prompts; C# DirectConversationCoordinator/Bridge plus focused PlayerMemoryClient adapter; tests `test_memory_context.py`, `Assets/AlterOS/Tests/Editor/Memory/PlayerMemoryContextTests.cs`.

**Interfaces:** `ConversationJobV3` wraps `{conversation:ConversationJobV2,memory:MemoryTurnPolicy}`; policy contains playerId,consentRevision,nowUtc,currentPlayerSourceId,focusAllowed,quiet,explicitRecall,authorizedQuestionIntent. Repository results are assembled privately by the sidecar, not trusted from model output. `build_memory_context(repository,job,policy)` returns <=4core/3current/3recalled/3000chars plus allowedCallbackIds. Keep ordinary v2 response/turn path unchanged when memory is off or v3 unavailable.

- [x] Add focused tests for corrections, unrelated/expired context, fictional scope, disabled memory, bounded context, forgotten/untraceable history, consent revoke in flight, and no extraction before opt-in.
- [x] Add the deterministic memory-context stage before prompt construction and keep memory as dated untrusted user data, separate from character rules. V3 carries source IDs beside, not inside, strict v2 history.
- [x] Recheck consent revision and suppression after the provider wait; discard replies after revocation/forgetting and never auto-regenerate.
- [x] Exercise graph/coordinator integration with fake providers. A callback ID the host did not authorize is rejected; one memory turn makes one mock provider call. This is source evidence, not provider-quality proof.
- [x] Persist the nonpersonal memory-policy-required marker. Missing v3 capability or local policy fails closed online rather than downgrading to unfiltered v2; pre-memory saves retain v2 compatibility and offline authored play remains available.

## Task 4: bounded proposals and system-controlled question policy

**Files:** Python memory_contracts/provider/prompts, C# PlayerQuestionPolicy and client validation; tests `test_memory_proposals.py`, `Assets/AlterOS/Tests/Editor/Memory/PlayerQuestionPolicyTests.cs`.

**Interfaces:** Provider response for memory-enabled v3 turns is a new wrapper `{judgment:ConversationJudgmentV2,memoryProposals:[],questionProposal:null,usedCallbackIds:[]}`; usage/timing remain outside provider JSON. Max3proposals; max1allowlisted question proposal/callback ID; preserve v2 schema for memory-off path. Proposals `{category,key,value,scope,sourceId,evidenceQuote}` carry NO recorded timestamp/SQL/status/consent. Parse/validate in Pydantic and host C#, then repository rechecks actual evidence and consent. `PlayerQuestionPolicy.Evaluate(context,knownFields,questionHistory,nowUtc,double draw)` returns intent or none without mutating history; `AcknowledgeQuestion` mutates only after actual presentation.

- [x] Add structured v3 proposals and regressions for source-quote mismatch, unclassified/fictional scope promotion, unsupported callback IDs, confirmation gates, sensitive categories, and no model-authored timestamps. Invalid memory metadata is dropped without blocking an otherwise valid reply.
- [x] Bundle the proposal wrapper with the single generation response; no extraction request or retry is added. The host/repository still own source membership, scope, category, consent and timestamps.
- [x] Implement/test the pure question gate with fixed draw/clock: 0.15 eligibility, one per session, seven-day cooldown, unknown focus/quiet blocks, known/disabled answers and idempotent acknowledgement.
- [ ] Wire question history/settings into the live coordinator and present pending memory proposals for confirmation. The current coordinator leaves the authorized question intent empty, so proactive questions remain disabled; no pending-proposal UI is present.
- [ ] Run/accept end-to-end v2/v3 provider behavior and verify a generated authorized question is presented and acknowledged exactly once. No new provider calls are authorized in this work.

## Task 5: authored first meeting and editable memory controls

**Files:** PlayerOnboardingSession; scene binders; authored `Assets/StreamingAssets/Dialogue/Onboarding/first_meeting.json`; Canvas objects/prefab through Editor only; tests `PlayerOnboardingTests.cs`, `PlayerMemorySettingsTests.cs` under the Memory Editor tests directory.

**Interfaces:** `PlayerOnboardingSession.NextStep(snapshot)`, `SubmitAnswer(stepId,value)`, `SkipStep(stepId)`, `Finish()`; steps consent -> preferred_name -> interest -> shared_activity_preference -> complete. Every prompt has canonical JP/EN beat fields. Controller intercepts first explicit E when ready, not startup or ambient triggers. Reuse authored VN/input/choice templates; no parallel dialogue renderer or arbitrary generated anchors.

- [x] Add synthetic tests for opt-out, skippable steps, Unicode preferred-name bounds, interruption/resume, no replay after completion/skip, and advance-only-after-save acknowledgement. Add bilingual authored prompts and a durable onboarding-cursor read that works after helper restart.
- [ ] Wire deterministic onboarding prompts/profile writes into the first explicit E interaction. The pure session model and authored JSON are not connected to DialogueManager yet; TMP rich-text disabling is also pending.
- [ ] Author/wire the Canvas profile/settings panel (memory opt-in, edit, list, forget/confirm, personal-question settings) and its local-storage/remote-use disclosure through the Unity Editor.
- [x] Repository forgetting removes active memory/index and keeps minimal suppression; production turn preparation filters forgotten source IDs, and coordinator policy changes cancel/discard in-flight results. Original JSONL remains; no forensic-erasure claim is made.
- [ ] If the Editor remains unavailable, preserve it and keep Canvas/scene bindings pending. Current read-only Console request timed out; no serialized assets were changed.

## Task 6: regression, fake-provider acceptance, packaging and handoff

**Files:** `Tools/Conversation/Check-PlayerMemory.ps1`, shared fixtures/README; current Python tests; docs `Documentation/Verification/2026-10-18 Player Memory.md`, `Documentation/Tracking/Player Memory Handoff.md`, TODO/Feature Register/Architecture/SessionZero where changed.

- [x] Add synthetic lifecycle tests for onboarding profile data, recent-to-historical expiry after 25 hours, preference correction, fictional wizard scope, rejected unconfirmed activity, duplicate question acknowledgement, forgetting, restart and source-replay suppression. Separate graph tests cover consent/forget during an in-flight turn and unsupported callback abstention.
- [x] Run full Python and C# source suites plus Topic Runtime, Topic Playground, and catalogue validation. `Check-PlayerMemory.ps1` runs the actual production sources and packaged helper smoke; 0 tests is failure. All databases are temporary and tests make no network requests.
- [x] Build a new Windows onedir package with SQLite and test keyless startup, keyless onboarding write/read across process restart, path-with-spaces launch, and offline runtime without developer Python. Package SHA-256 and exact path are recorded in the 2026-10-19 verification note. FTS5 and escaped-LIKE fallback tests run in Python.
- [ ] Unity Editor import/Console, EditMode, Canvas wiring, and no-key Play Mode were not run: the active Editor's read-only Console request timed out. Preserve the open state; do not batch-restart or force-close.
- [x] Verify the 3000-character and per-section memory context caps and one mock request per memory turn. Real provider formatting/naturalness/latency remain unverified; no API/TTS call was made.
- [ ] Complete the Unity/UI/player acceptance and the remaining question-history/settings/pending-proposal integration. Update evidence and handoff before marking M2.1 complete; stop before phone UI, TTS, sentiment, cloud sync or autonomous activity.

## Plan self-review

Short/long memory, source ownership, actor/scope, expiry vs deletion, consent,
question gating, paired-text output and old-protocol regression each map to an
explicit task/test above. M2 source APIs were inspected; memory bootstrap cannot
require its existing API-key gate. SQLite adds no dependency; v3 wrapper avoids
silently weakening strict v2 contracts. No wait-for-plan-approval loop is required:
the user explicitly authorized this bounded trial and implementation dispatch.
Remaining risks are semantic extraction/question quality, plaintext privacy,
source-transcript retention and stale Unity access; report them honestly.

A fresh read-only builder review found no blocking gaps in the six-task plan;
explicit consent command and no-unsafe-downgrade guard were additionally checked
during self-review. This is planning review, not implementation/test evidence.
