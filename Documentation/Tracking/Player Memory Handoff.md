---
title: Player Memory Handoff
tags: [alteros, memory, handoff]
updated: 2026-10-22
status: source-and-package-verified-runtime-pending
---
# Player Memory Handoff

Related: [[Player Memory Design]], [[Player Memory Implementation Plan]],
[[2026-10-19 Player Memory M2.1 Implementation]],
[[Unified Conversation M2 Handoff]], [[2026-10-18 Conversation Smoke]].

## Current state

Storage reconciliation 2026-10-22: `ConversationTranscript` reads/writes private
SQL through the same facade; legacy JSONL is retained import evidence. Memory
sync consumes bounded transcript pages, preserving sequence/source event IDs and
unclassified player evidence. Python memory remains a separate owner; do not
merge it into the Unity game/conversation DB or bypass consent/forget filtering.
See [[Unified Conversation M2 Handoff]] and
[[../Verification/2026-10-22 Documentation Reconciliation]]. Historical test counts
below are not fresh Unity/provider acceptance. E is still the source interaction
binding; approved T remapping belongs to the separate furniture assignment.

The memory-aware v3 source path is integrated and covered by headless
regressions. The helper can bootstrap locally without a key, the Unity
coordinator checkpoints/synchronizes the transcript after the current sequence,
passes source IDs separately from the strict v2 job shape, filters forgotten
sources, and rechecks consent/suppression after the provider wait. Raw transcript
chat is unclassified; unsupported memory proposals are dropped without blocking
an otherwise valid VN reply.

The authored bilingual onboarding JSON, a pure acknowledgement-driven session
model, durable cursor reads and the keyless sidecar operation exist. They are
**not wired** to first-E presentation or Canvas UI. The pure question policy is
tested but not scheduled by the coordinator. Pending-proposal confirmation and
question-history/settings UI remain unfinished. M2.1 is therefore **not
player-ready or complete**.

The earlier read-only Console request timed out. Follow current AGENTS.md for
verified-hang recovery; do not batch-bypass or hand-edit scene/prefab/meta assets. The previous
M2 UI/editor and provider acceptance gaps remain.

## Verified source/package evidence (2026-10-19)

- `Tools/Conversation/Check-PlayerMemory.ps1`: Python **129 passed** (includes
  fresh packaged-binary smoke), C# conversation/memory source checks **106
  passed, 0 failed**.
- `Tools/TopicRuntime/Check-TopicRuntime.ps1`: **33 passed, 0 failed**.
- `Tools/TopicPlayground/Check-TopicPlayground.ps1`: **13 passed, 0 failed**.
- `Tools/TopicRuntime/Validate-TopicCatalogue.ps1`: **57 families, 161
  scenarios, 161 migration rows validated**.
- Fresh onedir helper:
  `Build/CompanionRuntimeM2Task6FinalAcceptance/AlterOSCompanion/AlterOSCompanion.exe`.
  Synthetic keyless bootstrap/write/shutdown/restart/read worked from a path
  containing spaces. SHA-256 is recorded in the linked verification note.
- Recovery/persistence source coverage includes offline and missing-key
  fallback, helper stop/restart, duplicate turn rejection, explicit cancel vs
  timeout, late replies not stealing a successor surface, transcript restart/
  idempotence, consent/forget races and source replay suppression.
- No OpenRouter/TTS request, key read, Unity scene save, or player-facing test
  occurred in this continuation.

The C# source runner produced existing `CS0649` warnings from JSON DTO fields,
no errors. Headless C# compilation is not Unity Editor compilation. The current
DigitalOcean structured-output issue (`message.content` null) from the earlier
smoke remains unresolved; this handoff did not diagnose or retest it.

## Remaining work

1. Wire the authored first-meeting flow into the first explicit E interaction,
   using existing dialogue/input presentation and save-ack semantics.
2. Build and wire Canvas memory settings/profile review/edit/forget controls
   with plaintext-storage and retained-chat-history disclosure. Memory storage
   consent stays separate from online-generation consent.
3. Complete question-history/settings retrieval and authorized intent scheduling;
   focus/quiet unknown must continue to fail closed. Add pending proposal expiry,
   confirmation, dismissal and consent-revoke behavior before surfacing model
   proposals.
4. On Editor recovery, allow normal import/compile, inspect Console, run focused
   bounded focused EditMode tests, and inspect actual Canvas bindings. No scene or
   prefab change has been made in this continuation.
5. The user performs the final player-facing `Splash -> ModelTest -> E ->
   authored/generated dialogue/follow-up` test with their own OpenRouter key,
   after provider behavior is separately accepted. Do not claim or duplicate
   that test.

## Safety and scope

- No provider spend or TTS request is authorized here. Do not use a saved key or
  make a live call without separate explicit authorization.
- Preserve the unresponsive Unity Editor and all untracked work. The repository
  is still an unborn initial snapshot; do not stage/reset the whole tree.
- The transcript and memory DB are local plaintext. Forgetting a memory does
  not erase retained conversation history/import source bytes; do not claim forensic erasure.
- Phone UI, TTS, tools, sentiment, cloud sync and autonomous activity remain
  out of M2.1 scope.
