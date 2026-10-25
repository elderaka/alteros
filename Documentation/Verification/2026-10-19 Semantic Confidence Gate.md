---
title: Semantic Confidence Gate Verification
tags: [alteros, conversation, routing, verification]
updated: 2026-10-19
status: source-verified-editor-and-player-pending
---
# Semantic Confidence Gate Verification

Related: [[Unified Conversation Design]], [[Unified Conversation M2 Handoff]],
[[Feature Register]].

## Implemented

- Online turns filter authored candidates at a separate **0.850** cosine-similarity
  threshold. A ready candidate at or above the threshold uses authored playback
  without a provider call; otherwise the provider receives only candidates at
  or above the threshold. Offline local routing is unchanged.
- In Editor/development builds, each direct query writes a
  `[AlterOS Topic Similarity]` report to the Unity Console: online/offline mode,
  routing decision, authored gate, and up to eight Topic IDs with similarity,
  authored availability and whether each can enter provider context. The player
  utterance is not logged. If GTE is unavailable, a warning records runtime
  state, startup stage, anchor count and failure reason. There is no HUD or
  key-5 toggle.

## Source evidence (2026-10-19)

| Check | Result | Evidence boundary |
| --- | --- | --- |
| `git diff --check` | Passed | Whitespace/conflict-marker check only |
| `Tools/Conversation/Check-UnifiedConversation.ps1` | **109 passed, 0 failed** | Includes the new report-format regression test; source harness, not Unity Editor compilation |
| Unity 6000.4.4f1 Editor C# compilation | **Preprocessor error cleared** | `DirectConversationBridge.cs` was reimported and `Assembly-CSharp.dll` rebuilt after the corrected source timestamp. Current Console has no `CS1028`; it retains two unrelated Topic Playground serialization-depth errors at `TopicPlaygroundWindow.m_TopicFamilies`. |

Covered behavior includes weak candidate removal, the inclusive 0.850 boundary,
high-confidence authored bypass, no provider candidate below the gate, and no
weak-authored fallback after provider failure. Existing C# `CS0649` warnings for
JSON DTOs remain; the harness reported no errors.

## Pending acceptance

- Submit a query in `ModelTest` and inspect the Unity Console for its candidate
  scores and route decision; verify the player's utterance is absent from the
  log. No scene or prefab was changed.
- With online mode enabled, test one query below 0.850 (provider path with weak
  candidates omitted) and one ready authored match at or above 0.850 (authored
  playback with no provider request). Record the displayed scores and outcome.
- No live GTE query, provider request or TTS request was made by this change.
  The 0.850 cutoff remains an initial tuning value until reviewed
  against a labeled phrase set.
