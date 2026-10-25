---
title: Topic Scenario Activity Technical Reference
date: 2026-10-21
updated: 2026-10-22
tags: [alteros, technical-reference, dialogue, scenarios, activities]
status: source-contract-runtime-acceptance-pending
---
# Topic, Scenario and Activity — technical reference

Related: [[Scenario Conversation Design]], [[M3 Furniture and Activities Design]],
[[Topic Scenario Selection]], [[Unified Conversation Design]],
[[../Verification/2026-10-21 Activity Scenario Followups]]. Paths below are relative
to `E:/Pleiades/alteros`. Source is authoritative; this is not gameplay acceptance.

Current keyboard mapping (Editor-wired, live-unverified): `Player.Interact` is
keyboard T; E opens inventory when empty-handed and returns held furniture.
References below to an “interaction” mean the `Player.Interact` action unless
they explicitly identify an earlier dated physical-E report.

## Ownership and the two meanings of scenario

| Type | Owns | Does not own |
| --- | --- | --- |
| `TopicDefinition` | Semantic family, shared anchors, menu label/prompt, eligible content variants | Navigation, camera, provider permission |
| `TopicScenario` | A selectable content variant: gates, priority, weight, dialogue nodes | A running activity or an orchestration instance |
| `TopicInstance` / `TopicHistory` | Authored/generated identity, replay/once policy, consumption, completion counts and cooldown | Applying game effects |
| `ScenarioDefinition` | Executable graph of dialogue, questions, actions, waits, branches and an end | Truth of an action's completion |
| `ScenarioSession` | One running occurrence plus a suspended slot, current step, correlated events | Unity objects, navigation, audio or UI |
| `ScenarioConversationPresentationModel` | Answer identities, visible-beat checkpoints, exchange open/closed, `Player.Interact` resume and next-interaction offers | Granting capabilities or driving an actor |
| `ActivityHost` | Exclusive action execution, request identity, interruption, trusted terminal outcome | Topic selection, conversation wording |
| `DialogueManager` / authored Canvas | VN display and choices, player advance, camera/control restoration, TTS boundary | Selecting game effects or inventing action outcomes |

`TopicScenario` and `ScenarioDefinition` are different existing C# contracts, not
interchangeable JSON. `AuthoredScenarioAdapter.Convert(topic, variant)` converts a
selected canonical Topic node graph into executable dialogue/ask steps. It does
**not** turn arbitrary Topic choice actions into executable tools. Activities use
explicit validated Scenario action steps. A Scenario can span several
`Player.Interact` exchanges;
closing the dialogue box does not necessarily end the Scenario.

```text
free text / explicit player choice
  -> authored-first Topic selection or opt-in online generation
  -> executable Scenario occurrence
       dialogue -> ask -> action request -> wait -> dialogue -> end
                              |              ^
                         ActivityHost -> trusted outcome
```

## Topic data and selection

Source: `Assets/AlterOS/Topics/TopicDefinition.cs`, `TopicScenarioSelector.cs`,
`TopicCondition.cs`, `TopicHistory.cs`, `TopicHistoryRepository.cs`.

- Family: `schemaVersion`, `topicId`, `menuLabel`, `prompt`, `anchors`,
  `defaultReplayPolicy`, `cooldownSeconds`, `scenarios`.
- Variant: `scenarioId`, `priority`, `baseWeight`, `condition`, optional `channels`,
  `modifiers`, `consumptionNodeId`, canonical `nodes`.
- The host filters eligibility, chooses the highest eligible priority tier, then
  uses weights within that tier. A weight is not a priority override or permission.
  Unknown context is distinct from zero; unsupported conditions fail closed.
- Replay is instance-wide: `repeatable` or `once`. Generated instances default to
  once. A completion counter does not prohibit replay by itself. Presentation,
  consumption and completion/cooldown are separate history events.
- Authored Generic JSON remains under
  `Assets/StreamingAssets/Dialogue/Generic/<routeId>/` as retained import/provenance
  source. Current runtime readers use the pinned SQL release at
  `Assets/StreamingAssets/Content/content.sqlite3`; the installed artifact passed
  read-only inspection, but startup/player acceptance remains open. Staged canonical
  catalogue files under `Tools/TopicRuntime/StagedCatalogue/` and dated documentation
  route snapshots are not runtime sources.
- Anchors retrieve families; scenario eligibility and weighting decide the context.
  Changing a local action menu does not require rebaking semantic anchors.

## Executable graph schema

The host-authored activity scenario library is read as the logical SQL release
document `topic/activity_scenarios.json` through `RuntimeContentStore`. The physical
`Assets/StreamingAssets/Dialogue/topic/activity_scenarios.json` file remains source
and Editor-fixture evidence; it is not a separate runtime authority.

Source: `Assets/AlterOS/Scenarios/ScenarioDefinition.cs`,
`ScenarioDefinitionValidator.cs`, `ScenarioSession.cs`.

Root: `scenarioId`, `topicId`, `entryStepId`, `steps`. Empty `topicId` is permitted
for a host-authored action Scenario that is not a semantic catalogue family.
Every successor must name a step in the same graph; step IDs must be unique.

| `kind` | Relevant fields | How it advances |
| --- | --- | --- |
| `dialogue` | `beats`, `nextStepId`, `closeExchange` | Acknowledged beats, then `dialogue_complete` |
| `ask` | `questionId`, `question`, `options`, free-text/skip/cancel successors | Bound answer, permitted text, skip or cancel |
| `action` | `capabilityId`, `arguments`, success/failure successors | Host acceptance result, **not** physical completion |
| `wait` | `activityId`, `conditionId`, `objective`, success/failure successors | Host-correlated condition met/failed |
| `branch` | `sourceId`, `cases`, `defaultNextStepId` | Supplied branch value or the session's carried answer value |
| `end` | `outcome`, `closeExchange` | Ends the occurrence and emits release/end effects |

`branch.sourceId` is metadata, not an arbitrary query into game state. The host
must provide supported evidence; this is not a general-purpose scripting language.

Beat fields: `id`, `text` (Japanese speech), `displayText` (English display),
`language: japanese`, `expression` (host-supported vocabulary). Answers have
`optionId`, `label`, `value`, `nextStepId`. A question is part of the **current**
Scenario; a `ScenarioTopicOffer` is an optional topic suggestion for a later
explicit interaction.
Closing ("Dessert") is ordinary main dialogue, not a third provider stage.

Validator bounds: 64 steps; 1–16 beats per dialogue; up to 8 ask options;
up to 8 action arguments; up to 16 branch cases. Head/Appetizer has 1–4 beats;
main has at most 3 topic offers. Text and identifiers have their own bounded
lengths in `ScenarioDefinitionValidator`; do not treat these summary counts as
the whole validation contract.

## Runtime identity and helper API

`ScenarioRuntimeSnapshot` is immutable host context: scenario/run/turn IDs,
nonnegative `contextRevision`, checkpoint step, known Topic/intent IDs,
expressions, capabilities with exact allowed arguments, typed objects and wait
conditions. Provider output cannot expand these allowlists.

| Helper | Main use |
| --- | --- |
| `ScenarioDefinitionValidator.Validate(definition, context)` | Validate complete graph and host-bound actions before start |
| `ScenarioSession.Start(definition, runId, context)` | Begin a fresh occurrence; do not reuse a previous action's run ID |
| `ScenarioSession.Advance(event, context)` | Process a matching answer/result/condition/wrap-up |
| `ScenarioSession.RequestDiversion(...)` | Explicit pause/end/back-out; suspended-slot policy |
| `ScenarioSession.Quit()` | End running/suspended occurrences; runtime activity host also shuts down |
| `ScenarioConversationPresentationModel.AcknowledgeDialogueBeat(...)` | Advance the first-unacknowledged-beat checkpoint |
| `CompleteDialogue(...)` | Complete only after the required beat acknowledgements |
| `DismissExchange()` / `ResumeForInteraction(context)` | Close/reopen presentation without resetting occurrence progress |
| `ActivityInteractionBridge` | Scene-facing capability capture, request, `Player.Interact` exchange, stop, outcome projection |
| `ScenarioActivityAdapter.DrainScenarioEvents(...)` | Project host terminals onto the correct run/activity wait, once |

Important events include `dialogue_complete`, `answer`, `activity_result`,
`condition_met`, `condition_failed`, `wrap_up` and explicit diversion/resume
events. Effects include `show_dialogue`, `ask_player`, `request_activity`,
`wait_for_condition`, `close_exchange`, `release_activity`, `end_scenario`,
`return_prompt` and rejection statuses. A side effect needs an actual consumer:
the DTO merely naming an effect does not prove live execution. In particular,
general suspended-return UI remains limited; do not infer full diversion UX.

## Action contracts, furniture and stats

Source: `Assets/AlterOS/Activities/ActivityHost.cs`, `ScenarioActivityAdapter.cs`,
`ActivitySceneHost.cs`, `MovementActivity.cs`, `DrinkWaterActivity.cs`.

- `movement.come_here(target=player)`: captures a reachable player-vicinity
  destination once. It is not indefinite pursuit if the player keeps walking.
- `movement.follow_player(target=player)`: follows until stopped/failure/quit;
  catching up is waiting, not completion. `Player.Interact` pauses/faces safely; closing that
  same exchange resumes only the same still-owned execution.
- `needs.drink_water(target=available_dispenser)`: binds/reserves a typed
  supported furniture use-point, approaches, performs bounded drinking phases,
  applies host-owned hydration checkpoints, then releases the reservation.

Requests bind `(runId, stepId)` plus a stable payload; a matching duplicate reuses
its ticket, a changed payload is rejected. Acceptance returns an activity ID.
Waits must match that actual ID/run, not merely the generic capability name.
Only a trusted completed terminal selects success. Stop/failure/quit are not
successful drinking. Effects already earned at real sip checkpoints are retained.
No LLM-authored stat deltas, no player-drank inference, no currency minting.

Furniture owns instance/use-point identity, supported use, approach/pose/use/exit
transforms and availability. Invalid/moved/disabled targets fail and clean up.
Scene references and static presentation stay Editor/Canvas-authored. Animated
drinking quality, general furnishing and desk/webcam behavior are separate work.

### Current furniture discovery boundary (2026-10-22)

`FurnitureSceneAdapter` loads the canonical catalogue and can seed layout from
authored room `FurnitureUsePoint` components. `HomeFurnitureLayoutDocument`
stores placed instances using stable IDs, room-local position in metres (x/y/z),
rotation, scale and support identity/local pose. `ActivitySceneHost` builds the
current `FurnitureRegistry` from its serialized `furnitureUsePoints` array; the
registry reserves and edit-claims exact registered use points. Scenario capability
capture currently exposes registered Movement and Drink capabilities, not every
catalogue definition or placed object.

`FurnitureHandlingController` creates visual proxies for placed layout instances,
but those proxies do not automatically receive functional-point registration or
an executor. The TV catalogue's `tv.view_target` is viewing geometry, not a use ID,
ActivityHost capability or Inspect TV executor. Inventory/catalogue presence,
placed-instance presence and executable availability must remain distinct. The
approved [[Scripted Actions and Inspect TV Design]] and plan own any later dynamic
point-discovery/registration design; this note does not imply it exists. Detailed
furniture input/placement evidence: [[Furniture Inventory and Debug Design]] and
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].

## Player-interaction activity Scenarios and presentation policy

Runtime content: `Assets/StreamingAssets/Dialogue/topic/activity_scenarios.json`.
Wrapper DTOs: `Assets/AlterOS/Activities/AuthoredActivityScenario.cs`.
Bridge: `Assets/Scripts/Dialogue/DirectConversationBridge.ActivityScenarios.cs`.

The host-authored wrapper has `capabilityId`, an ordinary executable `scenario`,
`waiting` / optional `returning` beats, `continueLabel`, `stopLabel`, `readyBark`
and `engagement`. This is **not** a new provider response schema or permission.
It is separate from isolated `Assets/AlterOS/Testbox/Scenarios/` fixtures.

- Favor→Follow starts the authored follow occurrence. `Player.Interact` while following replaces
  the greeting with Keep following / Stop following. Continue ends its exchange
  and resumes movement; Stop targets the exact activity ID.
- Together→Drink starts drink → terminal wait → Come here → terminal wait →
  completion dialogue. Returning `Player.Interact` has contextual wording. A failed drink never
  requests a return under the pretense that it completed; failed return has its
  own "had water, couldn't return" line.
- In the existing M3 Follow/Drink wrapper, completion dialogue is pending, not automatically opened. A non-modal generic
  readiness bark is attempted once when no exchange/provider turn is active;
  the next explicit interaction presents the pending dialogue. Dismissal preserves its unacknowledged
  beats; completing it ends the occurrence. Another start is blocked until then.
- Inspect TV has a separately approved exception: after successful return, its
  owning Scenario opens its authored closing automatically. The host must acquire
  presentation safely, deduplicate/correlate the same live run and discard stale
  callbacks. This is design/plan only, not current runtime behavior; see
  [[Scripted Actions and Inspect TV Design]].
- During a cannot-talk phase, `Player.Interact` gives an authored non-modal acknowledgement,
  not the normal menu and not a camera takeover. Stop is only offered where the
  activity reports safe pause support. Escape remains the existing dismiss path.
- Current policy is `bark_then_e`. `ScenarioEngagementPolicy.Resolve(requested,
  independentlyAuthorized, interactionBusy)` separates content intent from host
  authority. Busy means defer. `independent` requires explicit host authorization;
  otherwise it falls back to bark/`Player.Interact`. **The current bridge always supplies false.**
  A future initiative scheduler can use that seam for unsolicited conversations
  after focus/quiet, proximity, cooldown, safety and permission checks. Such a
  scheduler/camera dispatcher is not implemented or enabled by this slice.

Local authored activity dialogue does not need a provider or a generated-response
receipt. It uses the shared VN renderer with local beat acknowledgements. It does
not currently append these local exchanges to the generated transcript/history
pipeline; adding that must preserve the sole coordinator writer. Existing TTS
settings still apply to ambient barks; the shared synthesis boundary honors TTS
off. No new UI hierarchy, serialized references or external calls are required.

## Generated path and capabilities not yet implemented

`DirectConversationCoordinator` / `ScenarioTurnCoordinator` own the generated
turn; Python under `Tools/CompanionRuntime/src/alteros_companion/` owns provider
request/validation. `scenario_protocol.py`, `scenario_contracts.py`,
`scenario_generation.py`, `prompts.py` and `provider.py` describe that boundary.
Negotiated v4 sends head and main stages with run/turn/stage identity; the
five-second Appetizer fallback target is a UX deadline, not a speed guarantee.
Head is dialogue only; main supplies the validated graph and optional later offers.
Unity still revalidates context, capabilities, receipts and stale/cancel fences.

Generated graphs can request the same host-allowed actions. They cannot run
arbitrary Python/C#, use arbitrary furniture, invent tools or apply stat values.
Current activity-menu completion deferral is the explicit local wrapper path;
do not assume all generated/background Scenarios already inherit that policy.
Broader initiative, tool libraries, image generation, external coding actions,
autonomous routines, resumable saves and full phone UI remain separately scoped.

## Authoring and checking changes

1. Reuse a capability if mechanics already exist. New mechanics require a host
   executor/permission contract and tests before any prompt lists them.
2. Author graph plus truthful unavailable/cancel/failure branches. Use the live
   wrapper only for these local menu entries; fixtures are not published content.
3. Validate against current known expressions/objects/capabilities. No guessed
   target IDs; no content-derived capability grants.
4. Exercise start/accept/wait/complete, reject/fail/stop, repeated interactions, stale IDs,
   dismissal, return failure, duplicate events and quit. Check replay separately.
5. Run focused Editor tests and the source checks below; report them separately.
6. Test Splash→ModelTest→T interaction, actual movement, safe interruption, camera release,
   bark timing and follow-up consumption. Compile/import is not gameplay proof.

Useful commands from the root:

```powershell
& '.superpowers/sdd/M3 Furniture and Activities Implementation Plan/Check-M3.ps1'
& 'Tools/ScenarioRuntime/Check-ScenarioRuntime.ps1'
& 'Tools/Conversation/Check-UnifiedConversation.ps1'
```

The first harness stubs Unity APIs: it proves source behavior, not NavMesh or UI.
The Topic Playground and Scenario Playground have separate scopes; see their
handoffs before treating a synthetic report as production routing acceptance.
No real personal memories/generated user catalogue are copied into documentation.

Documentation links/source paths were checked from disk; Obsidian's rendered
view was not inspected in this slice.
