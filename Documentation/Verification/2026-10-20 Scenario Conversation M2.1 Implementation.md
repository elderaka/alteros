---
title: Scenario Conversation M2.1 Implementation Verification
tags: [alteros, scenario, conversation, verification]
date: 2026-10-20
status: current-source-and-package-checks-pass-unity-and-gameplay-acceptance-pending
---
# Scenario Conversation M2.1 implementation verification

Scope: complete the approved Task 8 v4 conversation/presentation integration
and Task 9 source/package matrix. No Room Testbox scene/assets, authored Topic
JSON, anchors, or route snapshots were changed. No provider key was read, no
provider/TTS request was sent, and no paid test was made.

## Implementation

- `DirectConversationCoordinator` now routes online RAG to the negotiated v4
  provider path when both provider and host expose the Scenario contracts.
  Confident authored matches remain on the local authored path. Existing v2/v3
  provider behavior remains for providers without v4 support.
- `ScenarioTurnCoordinator` starts its fallback deadline at submit entry,
  before synchronous capture and later helper/memory preparation.
  `PumpScenarioTurn` ticks the deadline before queued stage results. Ready,
  well-bound stage results can be processed before the peer stage/terminal
  receipt; the response receipt is appended first. Required-memory turns stay
  queued until memory/context revalidation completes.
- `DirectConversationCoordinator` remains the only writer of response,
  presented-beat, and delivered-status events. Response receipt durability
  precedes any generated-stage presentation. Beat transcript events use the
  coordinator's stage/turn-derived stable identity and are appended only from
  the actual `DialogueManager` first-visible callback. Transcript retry IDs are
  idempotent when the retry timestamp differs. The separate resume checkpoint
  advances only after the player advances past the beat; failed transcript
  writes cannot complete a Scenario step.
- Required memory turns stay on v4; memory consent/context calls reuse the
  already-started v4 helper's v3 memory command channel. Missing or changed
  consent/context fails closed. No automatic legacy downgrade was added.
- `DirectConversationBridge`, `DialogueManager`, and `AlteraInteractable` reuse
  the existing Canvas dialogue/choice/text-entry templates for generated beats,
  question options/free text, eligible next-E offers, and E resume-before-
  greeting precedence. No static UI, prefab, serialized reference, or scene
  edit was required. A return-prompt currently changes status only; its visual
  prompt remains pending.
- `ScenarioConversationPresentationModel` exposes the current ask's free-text
  permission and returns bound effects when reopening an interaction. A
  partially typed or not-yet-advanced beat resumes from that beat rather than
  skipping to its successor.
- Cancellation is checked after provider completion and before stage/result
  admission. A late failure after dismissal is status-only and does not reopen
  the retry modal or dispatch an abort that closes the next interaction.
- Online authored bypass remains at >=0.850. Below it, the v4 RAG request now
  receives the ranked top-eight candidate cards and their host availability;
  weak similarity alone cannot play authored dialogue. The current v4 Scenario
  error path returns unavailable on helper/provider failure; it does not yet
  attempt confidence-gated authored fallback or active-Topic continuation.
  This remains a follow-up gap, separate from sending weak candidates to RAG.
- Editor helper resolution no longer probes historical acceptance builds in
  precedence order. It selects only `Build/CompanionRuntimeV4/AlterOSCompanion/`
  and returns unavailable when that v4 development package is absent.

## Validation

- Scenario source harness: 67 passed, 0 failed.
- Unified Conversation source harness: 194 passed, 0 failed.
- Topic Playground source harness: 13 passed, 0 failed.
- Companion Runtime Python suite: 173 passed, 0 skipped, 0 failed. Packaged
  helper smokes were explicitly enabled against the rebuilt v4 package;
  coverage includes keyless v4 readiness, v3 memory, mixed head/main fixture
  transport, and cancellation.
- `Tools/TopicRuntime/Validate-TopicCatalogue.ps1`: 57 Topic families, 161
  scenarios, and all 161 migration rows validated; authored content was not
  modified.
- Current PowerShell source harnesses compile the changed C#, including
  `DialogueManager` and the bridge. The latest focused Unity EditMode request
  timed out; the previous 7/7 EditMode result predates the final streaming,
  resume, cancellation and durability fixes. Current Unity compile/EditMode is
  therefore unverified. Preserve the open ModelTest Editor; do not force restart
  or batch-run against its unsaved state.
- Earlier Console diagnostics include `UnityActivityMotion` and
  `ActivitySceneHost` NavMeshPath initialization and recursive
  `TopicCondition.children` serialization-depth warnings. No current Play Mode,
  visual review, GTE query, or TTS/provider request occurred. No Room Testbox
  scene or asset was changed.
- Full source/package checks do not prove first-visible timing, visual Canvas
  layout, live E priority, actual provider quality, or audio.

## Windows package

Built from current source with `Tools/CompanionRuntime/build_windows.ps1` into a
new side-by-side output/work folder; prior builds were preserved. The packaged
helper passed keyless v4 readiness and v3 memory smokes. The v4 probe confirms
`scenario-turn-v1` and memory capabilities, with generation unavailable when
no key is supplied. No test invoked OpenRouter.

Executable:
`Build/CompanionRuntimeV4/AlterOSCompanion/AlterOSCompanion.exe`

SHA-256:
`BCD5F20C112C9D6DC5F6778E5AD646A507305DEA26A91717507E5B4AC4BC6E86`

The editor resolver now selects this path for its opt-in development helper;
no saved user setting or credential was changed.

## Remaining acceptance

Human-owned check: launch from `SplashScene`, enter `ModelTest`, and use E.
Verify local authored shortcut and a v4 RAG response; inspect VN beat pacing,
question choices/free text, next-E topic offer, dismiss/reopen, and greeting
precedence. Measure actual first-visible frame time from input submission. Check
return-prompt UI separately. TTS/provider quality and any API spend need their
own explicit authorization; do not infer them from the synthetic package test.
