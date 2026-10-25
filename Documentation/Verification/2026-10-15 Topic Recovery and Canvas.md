---
title: Topic Recovery and Canvas Cleanup
tags: [alteros, verification, dialogue, ui]
date: 2026-10-15
---
# Topic Recovery and Canvas Cleanup

Related: [[../SessionZero|Session Zero]], [[../Tracking/Feature Register|Feature Register]], [[../Tracking/Design Decisions|Design Decisions]].

## Scope and ownership

Prepare the existing authored Topic slice for the player's first hands-on test,
not implement Agent mode or claim Sprint Zero complete. Static HUD/UI layout
belongs to scene Canvas objects and authored UI prefabs. Runtime code binds
content/state and may instantiate those templates for dynamic lists or move
world-space UI. This rule is also recorded in root `AGENTS.md`.

No runtime HUD builder, package, provider call, or authored dialogue change was
added. All scene/prefab edits were made through Unity Editor APIs; no Unity YAML
or metadata was edlet'sited by hand.

## Fixes

- `DialogueRouter` reports actual playback success and tries sorted variants
  until one passes existing validation and time conditions. If none starts,
  the semantic caller can run its existing fallback rather than remain busy.
- Topic Cancel dismisses the greeting behind input, stops pending semantic
  routing, and follows normal camera/HUD/control restoration without invoking
  the sequence-completion callback.
- Escape uses the same dismissal path during dialogue, open input, and the
  submitted routing wait; pending routing also counts as a control lock.
- `DialogueManager` stores the semantic coroutine handle before a ready-model
  route can finish synchronously. Sequence exit avoids duplicate input-camera exits.
- `WorldDialogueChoicePresenter` no longer overrides authored layout spacing.

## UI location index

All scene objects below are in `Assets/Scenes/ModelTest.unity`.

| Surface | Authoring location |
| --- | --- |
| HUD | `AlterOSHUDCanvas/SafeAreaRoot` |
| Task scrolling/layout | `RightTaskPanel/TaskList/TaskContent` under SafeAreaRoot; ScrollRect, mask, VerticalLayoutGroup and ContentSizeFitter |
| Task row template | `Assets/Prefabs/HUDTaskToggle.prefab` |
| Status, clock/date, credits, focus | `TopLeftStatusCard`, `TopCenterBar`, `TopRightPill`, `RightFocusPanel` under SafeAreaRoot |
| Topic input and Talk/Cancel controls | `DialogueCanvas/InputPanel/InputContainer` |
| Dialogue text, TTS toggle, continue cue | `DialogueCanvas/DialoguePanel/BodyGlass` |
| World-choice viewport/content | `WorldDialogueChoices/WorldChoicesCanvas/ChoiceScrollView/Viewport/ChoiceRoot` |
| Wrapping world-choice template | `Assets/Prefabs/WorldChoiceButton.prefab` |

Task rows now have authored heights and a scrolling layout rather than identical
positions. Choice templates derive preferred height from wrapped text, and the
viewport scrolls instead of requiring every option to fit simultaneously.
Blank portrait decoration and the unwired placeholder room navigation are
hidden; this does not implement or remove the corresponding future features.
The focus progress image is an authored horizontal fill rather than a blank square.

An early Editor JSON snapshot represented scene references as zero; direct
SerializedObject reads and the wiring test established that world-choice root
references were already assigned. The fix targets layout, not nonexistent
missing-reference damage.

## Regression baseline

- `6c369075ccb746309de8a710cc6b64db`: rejected authored route incorrectly returned true.
- `5514870594bc4404a6807e5015fcb93c`: both rejection and Cancel regressions failed as expected before the fix.
- `c79673508ae14ef18854be6a9adb6307`: both control-flow cases passed after the fix; task layout still failed before Canvas edits.
- `185467a671a440b781e34b9435716a85`: 18/19 Editor tests passed. Existing `StartupResponsivenessTests.FailedWarmupReleasesWorker` exceeded its 60-second worker-creation budget (78.877 seconds).
- `19a3790aa16448ab8a43c1e53e7d830d`: another 18/19 result, worker creation at 62.716 seconds. This run used the last compiled assembly while an Escape-edit compile error was present; it is not final Escape validation. The duplicate keyboard local was removed.
- The cleanup test now uses the same 180-second cold-start budget as the existing Play Mode startup test. It still creates/faults the real worker and asserts Failed state and worker disposal; no failure assertions were removed. This is test setup tolerance, not a model-loading speed improvement.
- Review identified Escape being skipped after submission. Early queued-key probes left pending routing active, but later inspection showed Escape was not recognized in that Editor update context; those probes are not valid keyboard proof. The final fixture delivered the key inside a Dynamic input update, explicitly observed `wasPressedThisFrame == true`, and the real manager Update cleared pending routing, input visibility, and its control lock. This is assisted Input System evidence, not a physical keypress.
- Final Editor suite `31989dca85574e42825f1bac8873fb35`: **19/19 passed**, 43.931 seconds reported test duration, including real failed-worker cleanup and scene/prefab wiring checks. Compilation was idle and the Console had no errors before this run.
- Final first-party Play Mode suite `e10d7803f0064c2aaa8617af436a3207`: **12/12 passed**, 100.798 seconds reported test duration, including GTE warm-up, retry recovery, and subsequent slang routing. This is not a cold-start performance benchmark.
- The later live acceptance attempt was interrupted by an Editor error/restart reported by the player. A Crash Handler window was also observed during window inventory, but its cause was not established. After the player reloaded Unity, MCP reconnected to the verified AlterOS root, edit mode, no compile activity, and zero Console errors. Pre-restart test results are retained as such, not evidence that this interruption is fixed.

## Post-restart live checks

- Normal Splash → ModelTest reached GTE Ready after the player reloaded the Editor.
- Six greeting choices fit in the previewed 420-unit viewport, including `Let's Chat!`; the same viewport size was then saved in ModelTest through the Editor.
- The actual Let's Chat choice callback opened the input panel. Its wired Cancel button closed input and ended the greeting (`IsActive`, `IsInputOpen`, and `BlocksPlayerControl` false).
- The Talk button submitted `ngl im kinda tired` and selected `player_tired`, with the busy panel hidden. The Rest first choice callback was also exercised, but a full naturally timed branch completion was not completed in this pass.
- With a temporary 0.99 similarity threshold, unrelated synthetic text went to `unclear_short`; dismissal cleared control flags. The threshold was restored to 0.45 in runtime and never saved to the scene.
- An in-memory synthetic route with blocked first / eligible second variants started the second node. A blocked-only catalogue returned false. A deliberately missing fallback closed the busy panel instead of trapping input. Original catalogue/cache state was restored and no dialogue files were changed.
- After cancellation/fallback checks, the manager was inactive, input closed, control flags clear, and `playerController.IsInDialogueMode == false`.
- TTS was skipped for these checks. `Application.runInBackground` was temporarily enabled for automation, then restored before stopping Play Mode; no Player Settings were saved.

Captures: `Captures/SessionZero/0a-canvas-six-greeting-choices.png`,
`0a-canvas-topic-input.png`, `0a-canvas-tired-topic.png`, and `0a-canvas-fallback.png`.
The earlier `0a-canvas-input-before-escape-fix.png` also shows corrected HUD rows.

The final Console read exposed hide-animation attempts on already-inactive HUD
and dialogue panels. These are lifecycle errors, not a successful quiet-Console
acceptance result. `InactivePanelVisibilityTests` reproduced both failures
(`3c821389697743ffae93c6bf6183ee51`, 2/2 failed): hidden alpha stayed at 1 instead of
0 because no coroutine could start. `AlterOSHudPanel.SetVisible` and
`AlterOSDialoguePanelView.StartVisibilityRoutine` now apply visibility immediately
when inactive/disabled rather than scheduling an animation on an inactive owner.

After these guards and the saved viewport change, Editor suite
`88bb5ed589bd40cdb4b7e9718828147a` passed **21/21** in 35.543 seconds, including both
inactive-panel regression cases with no unexpected messages. The final review
found no additional concrete guard defect. Final Play Mode rerun
`b7e3fda421334ee28320656f1baaa538` passed **12/12**, 52.876 seconds reported duration.
Combined final first-party suites: 21 Editor + 12 Play Mode tests, all passed.
The live captures above precede the last inactive-panel guard; that guard was
verified by its reproduced Editor regression and the full rerun, not a claimed
new zero-error walkthrough of the entire scene.

## Handoff checks

For the player's first test, start from `Assets/Scenes/SplashScene.unity`:

1. Wait for the loading stages to finish; check the room HUD is readable.
2. Approach Alterra and press E. Finish the greeting and open `Let's Chat!`.
3. Try `ngl im kinda tired`, finish the authored response, and interact again.
4. Try unrelated input, then Cancel or Escape from input/dialogue. Confirm the
   panel closes, the camera returns, and movement/E interaction work again.

Use the mouse wheel when a choice list exceeds its viewport. Speech/provider
testing is separate; text interaction should not require a paid key.

Final automated tests and bounded assisted checks are recorded above. Do not
infer player-visible success from Canvas references or compilation alone. Physical mouse/keyboard feel,
speech quality, Agent mode, full stats, focus/initiative, and generated content
remain separate acceptance work.
