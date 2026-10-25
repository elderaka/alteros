---
title: Scenario Conversation Implementation Handoff
tags: [alteros, conversation, scenarios, handoff]
reviewed: 2026-10-22
status: source-and-package-checks-pass-unity-and-human-acceptance-pending
---
# Scenario conversation implementation handoff

Related: [[Scenario Conversation Design]], [[Scenario Conversation Implementation Plan]],
[[Topic Runtime Playground Handoff]], [[Room Testbox Handoff]],
[[../Verification/2026-10-20 Scenario Conversation M2.1 Implementation]]. The approved
implementation plan remains the task-by-task source of truth.

## 2026-10-22 reconciliation

The source-only database cutover now places transcript/session data behind their
existing SQL facades and migration gates. Legacy JSONL/JSON are import evidence,
not live writers. Candidates and coverage use the pinned shared SQL release.
The installed `content.sqlite3` passed read-only trusted-release, SQLite integrity
and foreign-key inspection; its manifest matches authoring revision 63 and the
439-document selected snapshot. The earlier expression-validation blocker has
been corrected. This does not prove normal startup, runtime consumption, player
dialogue playback or profile migration; those remain open. Preserve event/source
IDs, cursors, revision pins and sole coordinator ownership. See
[[Unified Conversation M2 Handoff]], [[../Verification/2026-10-22 Full SQLite Cutover]],
and [[../Verification/2026-10-22 Documentation Reconciliation]].

Later M3 source captures real ActivityHost capabilities/objects for bounded
movement and Drink; drawing remains unsupported. Furniture inventory/placement
is separately assigned to Room Testbox. Existing E is the source binding until
its approved T/E migration is verified. Earlier evidence below retains its date;
current AGENTS.md supersedes the old blanket no-restart instruction. This review
ran no Unity tests, provider calls or player acceptance.

## Current checkpoint

Tasks 1–9 have current source/package checks; latest Unity EditMode acceptance
is inconclusive. The v4 Scenario path is called from the existing
`DirectConversationCoordinator`; it does not create a second transcript owner
or silently downgrade a required-memory turn. `DirectConversationBridge` feeds
stage callbacks through a main-thread queue, starts the five-second fallback
clock at submission entry (including synchronous capture), and uses existing
DialogueManager/Canvas sequence and choice/text-input templates. A well-bound
ready stage can display while the peer stage/terminal receipt is pending, after
the response receipt is durable; required-memory turns wait for memory/context
revalidation. First-visible transcript recording is separate from the resume
checkpoint, which advances only after the player advances past a beat. No new
static UI, scene, prefab, or serialized reference was added.

This is not yet a player-accepted vertical slice. The latest focused Unity test
request timed out; the previous 7/7 result predates these source fixes. Preserve
the Editor on `Assets/Scenes/ModelTest.unity`, Play Mode off, and Room Testbox
scene/assets unchanged. Splash->ModelTest->E, visible question and offer
interaction, actual five-second first-visible timing, TTS, and provider quality
still require a human gameplay pass. No provider/TTS request or paid test was
made.

Task 8 integration notes:

- `ScenarioConversationPresentationModel` binds answers to run/step/question/
  option identity; free-text permission is exposed only while the current ask
  allows it. It separates current answers from offers gated to a later E
  interaction, and dismissal does not end the Scenario.
- `DirectConversationCoordinator` selects negotiated v4 for online RAG when
  both provider and host support it. Confident authored matches remain local;
  legacy provider paths retain their v2/v3 behavior. Stage messages are queued
  and consumed as they arrive; the first well-bound ready stage persists the
  response receipt before any generated effect is presented. The deadline ticks
  before queued stages at each frame boundary so late arrivals cannot steal the
  fallback. Cancellation after a provider race remains cancelled, and a late
  failure is status-only after dismissal rather than taking over a newer E
  interaction.
- V4 memory turns reuse the already-started v4 helper for consent/context
  commands. If required policy cannot be prepared or revalidated, the turn
  fails closed. Transcript response/turn status and visible-beat events remain
  written by `DirectConversationCoordinator` only.
- `DialogueManager` presents VN beats and bounded choices with authored Canvas
  templates. First-visible transcript events are idempotent across retry times;
  the resumable checkpoint advances only when the player advances past a beat.
  `AlteraInteractable` resumes an eligible active Scenario before its ordinary
  greeting and injects eligible next-interaction offers into the loaded greeting
  sequence. Return prompts currently surface as status only; their final visual
  treatment remains for the human check.
- Drawing remains unsupported; the initial empty activity port was superseded
  by the bounded M3 ActivityInteractionBridge. Only actual host capabilities are
  exposed; Playground synthetic activities never become gameplay permissions.

Task 7 remains additive and detached:

- `AuthoredScenarioAdapter.Convert` maps only the selected authored Topic graph
  into the Scenario runner; Topic selection retains gates, priority, weight,
  and history ownership. It preserves IDs, Japanese speech, English display
  text, expressions, and choices; unsupported authored language fails closed.
- `ScenarioPlaygroundSession.Apply` uses the shared `ScenarioSession` with a
  detached synthetic activity port. Fixtures are under
  `Tools/ScenarioRuntime/Fixtures/Playground/`; no provider request can be made.
- The Topic Playground Editor window and batch entry point use the same event/
  time model. Fixture JSON stays out of `StreamingAssets` and production
  capabilities.

No authored catalogue, anchor, route snapshot, generated private content,
Room Testbox asset, or production dialogue JSON changed.

## Evidence

- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`: 67 passed, 0 failed.
- `Tools/TopicPlayground/Check-TopicPlayground.ps1`: 13 passed, 0 failed.
- `Tools/Conversation/Check-UnifiedConversation.ps1`: 194 passed, 0 failed.
- `Tools/CompanionRuntime/.venv/Scripts/python.exe -m pytest tests -q`:
  173 passed, 0 skipped, 0 failed (packaged v3 memory and v4 Scenario
  readiness smokes explicitly enabled).
- `Tools/TopicRuntime/Validate-TopicCatalogue.ps1`: 57 families, 161
  scenarios, 161 migration rows validated; content remained unchanged.
- Current source harnesses compile the changed C# including the bridge and
  DialogueManager. The latest focused Unity EditMode request timed out; the
  previous 7/7 result predates the streaming/checkpoint/cancellation fixes.
  Preserve scene state and use current AGENTS.md for verified-hang recovery;
  do not run batch mode against an open/dirty project. Older diagnostics include
  UnityActivityMotion/ActivitySceneHost NavMeshPath initialization and recursive
  Topic-condition serialization depth. NavMesh construction was later corrected
  in the M3 follow-up. No current corrected EditMode, Play
  Mode, visual review, GTE query, or TTS/provider request is claimed.
- Current Editor development sidecar at
  `Build/CompanionRuntimeV4/AlterOSCompanion/AlterOSCompanion.exe` passed the
  keyless v4 readiness and v3 memory package smokes. Its v4 response advertises
  `scenario-turn-v1`; generation is false with an empty key. SHA-256 is recorded
  in the dated verification note. No provider call was made.

## Resume / acceptance

1. Preserve the current Editor/Room Testbox state. No more scene or prefab work
   is part of this handoff.
2. Human test: launch from SplashScene, enter ModelTest, use E to interact, then
   test online RAG with a configured key only if desired. Verify an authored
   high-confidence route stays local; otherwise v4 delivers the answer through
   VN beats. Try dismissal/reopen, Ask Player choices/free text, and next-E
   offers. Inspect Console for safe route/status diagnostics.
3. Measure the actual frame when the first local/generated beat becomes visible
   relative to submit; do not infer the five-second target from API or helper
   timestamps. Observe return-prompt UI separately.
4. TTS quality/latency and any provider spend require separate explicit
   approval. The opt-in Editor resolver points to the V4 folder; no saved user
   setting or credential was changed.

The implementation stops here pending that human gameplay acceptance. No
automatic M3 expansion, provider call, TTS call, phone UI, furniture build-out,
or external-tool capability is authorized by this handoff.
