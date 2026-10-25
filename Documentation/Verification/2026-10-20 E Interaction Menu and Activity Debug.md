---
title: E Interaction Menu and Activity Debug
date: 2026-10-20
tags: [alteros, verification, dialogue, m3, debug]
status: source-and-editor-wiring-verified-player-review-pending
---
# E interaction menu and activity debug

## Human contract

The greeting root has, in order: What are you doing; I want to ask you a favor;
Let's do things together; up to three eligible topic suggestions; Let's chat.
Eligible LLM next-interaction offers have priority, with remaining slots sampled
uniformly without replacement from eligible authored topic options. Never invent
filler when fewer than three are available. Authored topic trigger gates remain.

Favor currently exposes only Follow me, with Back; a running pausable Follow
can instead expose Stop following there. Action availability is rechecked on
display and click. Together exposes Let's get a drink (the existing availability-gated
`needs.drink_water` action), Let's do… (opens the existing input,
does not submit or generate) and Back. Drinking only applies Alterra's existing
activity effects; it does not record or assume that the player drank. Unsupported shared activities are not
presented as executable options. Root Come here/Drink shortcuts from the previous
iteration are superseded, not additional root entries.

## Changes and ownership

- `Assets/StreamingAssets/Dialogue/greetings.json` authors the root and submenu
  dialogue/choices, bilingual speech/display and branch targets. No Generic
  semantic route or anchor changed; route snapshots need no refresh.
- `GreetingTopicSelection` provides a small deterministic-testable selection
  policy; DialogueManager combines suggested and authored choices into the three
  slots before the final chat entry. Existing topic-prompt selections now submit
  through the ordinary consent-gated conversation path, not the stale background
  generation warning. Clicking a topic is an explicit user turn; no call is made
  while building the menu or opening input.
- DirectConversationBridge attaches offers to the actual topic-menu node, not
  the last physical node in the greeting file. Follow/Stop lives in the favor
  branch. Stop closes the exchange; existing host permissions remain authoritative.
- ActivityHost publishes non-consuming start/terminal notifications. SceneHost
  logs `[Alterra Activity] start/finish` with capability, execution/run IDs, phase
  and actual terminal outcome. No chat content or credentials; no per-frame logs.
  Duplicate requests/stops do not duplicate lifecycle notifications. Scenario event
  draining is unaffected. Observer subscription is removed on host disable.
- `AlterraActivityDebugView` only binds text and billboards an authored world-space
  Canvas. State, current action/phase and Scenario/step (or suspended Scenario)
  refresh at5 Hz. No chat/memory text. Disabled outside Editor/development builds.
- Editor menu `AlterOS > Debug > Create Activity Overlay` authored
  `Assets/AlterOS/Testbox/ActivityDebugOverlay.prefab` and its child instance above
  scene object `Altera` in ModelTest. Static layout/font/colors are Editor-authored,
  not built in runtime code. Existing dialogue button templates remain unchanged.

## Evidence2026-10-20

- Red: new topic-selection tests all failed against the empty implementation,
  Unity job `1d5d878a920040fe8ecbdec0ccb131e1`. Lifecycle observer regression failed
  against the unimplemented notifications in the local stubbed M3 harness.
- Green: Unity EditMode **63/63 passed, 0 skipped**, job
  `397c9faf666241c78589810beb3570b0`, reported duration2.523145 s. Includes the
  Activities group, four topic-selection tests and two authored menu-contract
  tests. Stubbed activity/Scenario harness additionally passed49 checks; not
  native movement proof.
- Unity compilation/Console check returned zero errors before authoring. Three
  deprecated FindFirstObjectByType calls in the new Editor authoring helper were
  replaced with FindAnyObjectByType after warnings; runtime scripts unchanged by
  that warning-only cleanup. No full Editor-suite pass claimed.
- Verified correct project `E:/Pleiades/alteros`, Unity6000.4.4f1. Registry briefly
  exposed only an unrelated verification project: no mutations sent there; the
  session was pinned to `alteros@48a268a4` once available.
- ModelTest was active, saved and clean before overlay creation. Editor command
  confirmed authoring/save. Readback verified WorldSpace Canvas at local
  `(0,2.15,0)` above Alterra and all five serialized references populated: label,
  activity host, conversation, dialogue and viewing camera. Canvas layout dirtied
  the scene again; a second explicit ModelTest save succeeded. Final active-state
  query timed out during reload; no force-close/restart/batch bypass.
  Saved SHA-256: ModelTest `701F6DDCB48FF76FC67811988A8B63E73C0A320BE92F08592DC5D35555498876`;
  overlay prefab `5DEBD4C3F28D8D3F710F5102537B36261CC2FE61BAFE9738CA0D3863581D34D6`.
- No Play Mode, physical menu click, live log emission, live movement, visual
  readability, provider/TTS call or key access in this change. Prior broader
  conversation source failures remain recorded in [[2026-10-20 Activity Greeting Choices]];
  this63-test check is not a fresh full-project acceptance result.

## Next player check

Drink-menu follow-up: the content assertion failed before adding the choice and
passed afterward; JSON parsed successfully. The Editor menu-contract test was
updated, but this follow-up has not rerun Unity tests or Play Mode. The earlier
63/63 result predates this addition. Restart Play Mode to reload cached greetings;
check Together→Let's get a drink with a reachable available dispenser.

Enter normally through Splash→ModelTest, press E and inspect seven entries when
three topics are eligible. Open Favor→Follow me, interact again and use
Favor→Stop following. Check start/finish Console records and the debug label as
she moves. Together→Let's do… must only open input. Verify visual layout and text
readability above the actor; no hardcoded runtime styling changes to compensate.
