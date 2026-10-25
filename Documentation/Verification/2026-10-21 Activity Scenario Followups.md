---
title: Activity Scenario Followups
date: 2026-10-21
tags: [alteros, verification, m3, scenarios]
status: source-verified-player-acceptance-pending
---
# Activity Scenario follow-ups

Related: [[../Tracking/Topic Scenario Activity Technical Reference]],
[[../Tracking/M3 Furniture and Activities Handoff]],
[[2026-10-20 E Interaction Menu and Activity Debug]].

Current-input note (2026-10-22): this dated report's physical E references describe
the prior keyboard binding. `Player.Interact` is now mapped to T in the Input
Actions asset; E is furniture inventory/held-item return. That remap is wired but
not live-key verified. Current Scenario/Unified source suites pass 67/67 and
197/197, as recorded in the furniture pre-live report; this does not prove Play
Mode activity behavior. The M3 Drink next-interaction follow-up remains separate
from the approved Inspect TV automatic closing, which is still only a design/plan.

## Cause and approval

Human testing reported activities starting but E still showing the greeting,
with no drink return/follow-up. Source confirmed `StartGreetingActivity` issued
an orphan standalone request, E explicitly fell through to the greeting, and
completion pumping required a Scenario owner that did not exist for that request.
The isolated drink fixture also had no return-walk action.

Human approved return→bark→wait for E, retaining a future host-controlled
independent-conversation seam. Static UI, room and navigation are unchanged.

## Changes

- `Assets/StreamingAssets/Dialogue/topic/activity_scenarios.json`: live authored
  wrappers for Follow/Drink. Follow waits until stopped; Drink waits for real
  completion, requests Come here, waits for that terminal, then queues closing.
  Separate unavailable, interrupted and return-failure content. No anchors changed.
- `Assets/AlterOS/Activities/AuthoredActivityScenario.cs`: host-authored DTO and
  engagement policy. Content cannot authorize independent speech.
- `DirectConversationBridge.ActivityScenarios.cs`: focused partial for local
  start, contextual E controls, pending completion and readiness bark; reuses
  ScenarioConversationPresentationModel and the existing host activity port.
- Existing bridge: Scenario ownership for these menu actions, contextual E before
  greeting, deferred closing and local acknowledgements without generated-response
  receipts. Pending generated stages block new menu activities. No provider needed.
- DialogueManager: `activity_continue` ends the bound exchange before closing,
  allowing following to resume; ambient synthesis honors TTS off before fetching.
- Local authored exchanges do not currently append to the generated transcript.
  Live wrappers are distinct from unchanged Testbox fixtures/semantic catalogues.

Future engagement may resolve `independent` only with host permission and defers
while busy. The bridge passes permission=false. No unsolicited conversation/camera
scheduler was implemented. No new UI hierarchy or serialized references.

## Evidence and limitations

- Before content implementation, the extended M3 harness failed with missing
  `activity_scenarios.json`. Afterward **56/56 source checks passed**. Unity APIs
  are stubbed. New tests run the real Scenario session/presentation: drink→return,
  correlated outcomes, failed action/return, dismissal/resume/completion and
  initiative permission. This is not movement or Canvas proof.
- `Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1`: **67 passed, 0 failed**;
  existing JSON DTO-field warnings. No provider calls.
- Correct Editor confirmed: alteros@48a268a4, Unity6000.4.4f1, ModelTest, Play off.
  Initial Console baseline contained an earlier "no valid NavMesh" diagnostic;
  a later Console read returned zero errors after import. No room/NavMesh edit.
- Unity initially refused tests while compiling, then disconnected temporarily.
  Later request accepted job `aed448d2622f47639bfaa6a42845a725` for Activities,
  greeting and Scenarios; status polling timed out with **no result**. Editor.log
  records bridge timeouts. No Editor test pass claimed.
- Final Assembly-CSharp source compilation succeeded (exit0) with Unity's bundled
  Roslyn and current Bee response inputs/references. Output was overridden into
  `.superpowers/sdd/Activity Scenario Followups/d45fe45607cd49eba935d8d4b46c574d/`,
  not Unity's live assemblies. Existing obsolete API warnings remain. This checks
  the final bridge/DialogueManager source, not Editor import or gameplay acceptance.
- No Play Mode, physical E/Continue/Stop, live bark/return, camera release, audio,
  startup, full Editor suite, provider or API-key test. No scene/prefab/save/provider
  configuration changes; no forced Editor recovery or unsaved-scene discard.

## Next acceptance

1. Let the correct Editor import final source, check Console and run focused
   ActivityScenarioContentTests/Activities/greeting tests. Preserve unsaved scenes.
2. Splash→ModelTest→E→Favor→Follow: move, E, Keep following/Stop. No normal
   greeting; Continue resumes, Stop releases, Escape dismisses without ending.
3. Together→Drink: dispenser phases, return walk, one non-modal readiness bark,
   next-E closing. No automatic VN/camera. Dismissed closing resumes only remaining
   beats; completed closing cannot replay on the next E.
4. Controlled unavailable/blocked drink/return: truthful failure, no invented
   effects. TTS off remains silent/no fetch. Provider/TTS quality testing separate.
