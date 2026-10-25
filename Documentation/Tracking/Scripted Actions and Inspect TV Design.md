---
title: Scripted Actions and Inspect TV Design
date: 2026-10-22
tags: [alteros, design, activities, scenarios, furniture]
status: user-direction-approved-implementation-plan-awaiting-review
---
# Scripted Actions and Inspect TV

> Purpose clarification2026-10-22: the reusable Action framework and its typed
> Workbench discovery/validation/preview surface are the deliverable. Turn on TV
> is one proof of real execution and state integration, not a per-TV framework.
> The Workbench's full target includes Dialogue, Scenario, Topic, Furniture, Item
> and Action authoring; the initial foundation does not implement every editor.

> Acceptance-case update2026-10-22: the human replaced passive **Inspect TV** with
> **Turn on TV** to prove actual object-function invocation, reusable type/family
> functions and per-instance multi-state furniture. See the amendment below. Earlier
> inspection-only steps remain design history, not the current acceptance target;
> the implementation plan now includes the consulted function/state expansion and
> awaits human review before START. Its filename is retained for existing links.

Related: [[Scenario Conversation Design]], [[Content and Interaction ERD]],
[[SQLite Authoring Cutover Implementation Plan]], [[Content Workbench Shared Contract]].

## Approved direction

Topic is the semantic entry and anchor owner. Scenario is the executable flow of
dialogue, questions, branches, activity requests and waits. Action means registered
trusted C# scripted behavior; an Activity is its running execution. Editable content
references scripts and approved parameters, not executable code supplied by an LLM.
These are approved concepts, not proof that every runtime/authoring path supports them.

**Final human clarification2026-10-22: both authoring forms.** `InspectTV.cs`
should be a thin composition of framework functions, not a bespoke navigation /
reservation/animation implementation. A data-authored recipe is also supported.
Both produce the same validated typed sequence and run through one registered
sequence execution under ActivityHost. New C# composition is optional for new
content; new framework code is necessary only for genuinely missing primitives.
A Scratch-like visual editor is a possible future frontend, not a required editor
build in this slice. This supersedes the brief data-only interpretation above.

The original passive **Inspecting TV** example below is superseded by the Turn on
TV amendment later in this note; its sequence is retained as design history:

1. Free text **Check tv** selects the Topic, then an eligible authored Scenario.
2. If a TV is present and usable, play opening dialogue about the TV.
3. Start the SQL-authored Inspect TV Action definition using thin `InspectTV.cs`
   composition or its equivalent data recipe, through the same reusable sequence
   execution; request movement to the nearest eligible TV.
4. Immediately after movement is successfully initiated, request the authored
   non-modal bark **Let's check the tv**. Do not wait until arrival to emit it.
5. On actual arrival in front of the TV, face its authored viewing point and request
   the authored bark **Okay, so this is tv**.
6. Wait an authored few seconds without blocking Unity, then move back toward the
   player's current reachable position; end the Action only
   after arriving, or report a truthful failure/cancellation.
7. On successful return, the Scenario opens its closing dialogue automatically,
   then ends after that dialogue finishes. No second T press is required. The
   Action never opens a full dialogue itself or mutates a Scenario step index.

If the room is known complete and has no placed TV, the same Topic instead selects
an authored no-action Scenario saying **Ah, but you don't have a tv**. No LLM call is
needed for either path. Do not hide the whole Topic behind the positive TV gate.

## Meaning of no hardcoding

The C# framework necessarily implements primitives and lifecycle safety. Do not hardcode player-facing text,
TV instance names/IDs, world coordinates, anchor comparisons, or per-TV selection
branches in it. Topic anchors, VN beats, localized bark content, scenario flow,
selection conditions, step ordering and script configuration are authored data.
The registered sequence execution uses the existing furniture registry, motion
port and Activity host; it is an IActivityExecution, not a second Scenario runner.

The recipe composes allowlisted typed operations: reserve nearest matching target,
move to its authored approach (with an on-movement-start bark reference), face the
focus point, bark on arrival, wait a finite configured duration, move near player
and complete. Checkpoint/yield/resume supports Scenario-owned dialogue where needed.
Data accepts no arbitrary code, free-form expressions, callable method names or invented tools
are accepted from content. Authoring the same pattern for a different compatible
object must require only recipe/config/content changes, not another executor class.
Trusted C# composers may call a typed builder to declare the same operations; they
do not need to reimplement movement or lifecycle state machines. Code review, not
this builder, is the trust boundary for arbitrary developer-written C#.

A TV definition existing in the catalogue, or a TV unit stored in inventory, does
not prove a TV is placed in the room. Presence, usable/reachable, reservation busy,
and unknown/incomplete room state must remain distinct. Unknown is not absence.
The generic availability query must also work on non-TV furniture in tests.

## Reusable framework requirements retained

- Reuse the exclusive `ActivityHost`, current ScenarioSession/content-flow runner,
  lifecycle events, reservation ownership and presentation system; no second runner.
- Named intermediate checkpoints can yield to Scenario dialogue/Ask Player and
  resume the same execution with a host-validated continuation. Stop-for-dialogue
  and compatible light-loop-during-dialogue are both supported presentation policies.
- Action barks are non-modal authored requests with idempotent phase correlation;
  a bark must not claim dialogue ownership or block safe cleanup.
- Only registered host-approved effects can change stats/flags. Inspect TV awards
  no new effects, currency, relationship progress or fabricated completion.
- The 30-second lingering-pose safeguard starts only once the owning interaction
  and Scenario have ended. An active Scenario suppresses it, even without open UI.
  A new owner invalidates an old timer; default idle must fit the physical state.
  Resource cleanup is immediate, not delayed for this safeguard.
- Exiting ends execution. Saved history/checkpoints do not auto-resume an Activity.

## Coordinator defaults for this plan

These are implementation choices, not additional quoted player decisions:

- Use stable IDs `t.inspect_tv`, `s.inspect_tv.present`, `s.inspect_tv.absent`,
  `s.inspect_tv.unavailable`, `activity.inspect_tv`, shared script `activity_sequence`
  and trusted thin composer `inspect_tv` unless
  an existing canonical ID already covers the exact same meaning; document mappings.
- Nearest means smallest three-dimensional squared distance from Alterra among
  reachable, enabled, unclaimed compatible use-points; tie-break by stable instance
  ID then point ID. Revalidate and reserve before movement. Never teleport.
- Busy/unreachable TV uses an authored unable-to-inspect response, not the absent
  line. Incomplete room state uses an authored cannot-confirm response/no execution.
- Returning uses the current bounded Come Here movement policy. Player clarification
  on2026-10-22 supersedes the earlier coordinator next-T default: this Scenario's
  closing opens automatically after successful return. The Scenario/host owns that
  approved follow-up permission; an arbitrary Action or model cannot grant it.
  If presentation is occupied, defer and revalidate this same run before delivery;
  never hijack a newer exchange, revive an ended run or replay closing twice.
  This does not automatically continue Agent generation or start another Topic.
- Preserve Japanese speech and English display in existing contracts; language-row
  storage remains extensible. The quoted English bark/fallback copy is display text.
  TTS stays off/no billable calls for this implementation acceptance.
- Use2seconds as a synthetic fixture's inspection wait, not a fixed TV constant.
  Changing that duration or operation order must change behavior without C# edits.
- A second non-TV inspection fixture using the same sequence executor proves
  composition/reuse; it is synthetic test content, not a new production feature.
- The C#-composed and data-authored Inspect TV fixtures must have equivalent
  movement/bark/wait/completion/cancellation traces using the same executor.
- A synthetic provider-free checkpoint fixture, not a new full dancing feature,
  proves mid-Activity dialogue and choice-based continuation. The TV example alone
  does not prove this requirement because its intermediate speech is only barks.

## Evidence and implementation boundary

Read-only source inspection on2026-10-22 found: `FurnitureRegistry.TryReserve`
already orders compatible reachable unclaimed points by distance/stable IDs;
`TopicConditionEvaluator` already supports object predicates and Unknown;
`ScenarioActivityAdapter` currently projects only terminal events; ActivityHost's
executor/capability checks and scene capture are restricted to Movement/Drink.
The TV catalogue declares `tv.view_target` with no use ID, not a working inspect
executor. Runtime-placed furniture currently needs a functional-point integration
check rather than assuming its visible proxy automatically becomes usable.

SQLite authoring is current shared content authority. New content must use typed
revision/CAS authoring and explicit build selection, validation, compilation and
installation gates. Existing per-step ActionRecords are derived request metadata,
not proof of a reusable editable Action/script registry. No loose JSON live fallback.

See [[Scripted Actions and Inspect TV Implementation Plan]]. This note records design
and source observations only: no implementation, provider, Unity or gameplay pass.

## Session coordination

LLM Planning is now **Alterra Behavior Session**; Room Testbox is now **Furniture
and Environment Session**. Thread IDs are unchanged. The human authorizes Alterra
Behavior to ask DBMS/Furniture directly for this slice's technical information, and
those owners to answer it. See [[Multi-Agent Coordination]] for identities, file /
Editor ownership and escalation boundaries; this does not itself issue START.

## Current acceptance case: Turn on TV (2026-10-22)

Human requirements:

1. Alterra must interact with an object and execute its actual function, not just
   walk up and narrate success.
2. Furniture exposes type/family-specific functions reusable across compatible
   variants; new TV models should not require copied behavior implementations.
3. Placed furniture supports multiple states. A Scenario ending must not be confused
   with an object function succeeding or a state being reset.

First-slice state scope approved by the human2026-10-22: **Off/On only**, visibly
demonstrated on the TV. Channels, sources and playback states are deferred. This
approves the test scope, not a detailed state/schema contract or implementation START.

The mandatory natural-language anchor is **Can you help me turn on the tv?**;
**Turn on TV** may remain an additional anchor/short label, not an exact-string
router shortcut. This is a full dialogue-led Scenario, not a bare furniture command.
The existing **Can you do me a favor** menu branch also contains a **Turn on the TV**
option. Free text and that authored menu choice select the same canonical Topic /
eligible Scenario definitions and run the same host checks; they must not maintain
separate dialogue copies or launch the Activity directly.

The proposed flow is opening dialogue ->
reserve nearest eligible placed TV -> move/departure bark -> arrive/face -> request
registered furniture function -> observe truthful result -> optional authored wait
-> return to player -> automatically open Scenario closing -> end. Failure/absence
branches remain authored, not C# text. Both thin C# composition and typed data recipe
remain required authoring paths into one Activity execution.

Both entries retain the authored no-TV dialogue when the room is known empty, and
truthful busy/unreachable/unknown responses. A missing power capability must not
hide the entire semantic entry if its no-action fallback Scenario is eligible.
Menu selection supplies explicit intent, not permission to bypass reservations,
availability, existing Scenario lifecycle or function validation. Opening and closing
are VN beats, distinct from the Action's non-modal barks.

### Proposed ownership and first-slice defaults

These are coordinator recommendations pending detailed contract/state review:

- Action/framework primitive `invoke_furniture` dispatches only a registered typed
  function supported by the exact reserved instance. No reflection, arbitrary C#
  method names, direct SQL patches or unvalidated state assignment from content/LLM.
- Furniture definition advertises supported capabilities/registered handlers. An
  installed function handler owns preconditions, the transition and truthful result;
  the Action owns ordering, not the internal TV implementation. Furniture and
  Environment owns this contract, with DBMS for persistent-state integration.
- Reusable power behavior can serve several TV definitions (and other compatible
  powered furniture); TV-specific media behavior is a separate capability, not a
  giant switch over model names. Different instances never share one mutable state.
- The human approved per-instance power Off/On and visible state on the testbox TV
  for the first slice. Channel/source/playback remain deferred independent state
  fields, not one enum combining every possible state. Persistence/handler details
  below still need owner contract review.
- Prefer explicit set-power-to-On over Toggle. Already-On is a truthful no-change
  result, not a second transition. Repeated function requests must not duplicate
  effects, and return/cancellation must not undo an already committed power change.
- Persist authoritative object state through the existing home/game writer, keeping
  shared definitions separate from per-instance runtime data. Exact schema and
  migration needs require owner review; no new database is authorized by this note.
- Acceptance must show a real state/visible change, two compatible TV definitions
  sharing a function, and two placed TVs retaining independent state. Existing
  absence/busy/unreachable/unknown/stale-binding/duplicate/cancel tests still apply.

Current inspected layout DTO has enabled, conditionState and numeric resourceState;
these fields do not establish a typed power-function API or arbitrary multi-state
handler support. Existing interaction policy dispatches installed capabilities, not
an already verified TV power implementation. This is design-only, not a runtime pass.

### Owner consultation incorporated into implementation plan

Alterra Behavior consulted Furniture and Environment and DBMS directly. Both
confirmed the existing exact-target reservation chain and schema2 home instance
resources within game schema3. There is no current TV function handler, generic
sequence executor or committed power-to-renderer projection. The state-capable DTO
is not itself an implementation of those contracts.

The revised plan proposes `power.set` with Off/On over allowlisted `device.power`
values0/1/known, exact instance/version/lease validation and the existing CAS game
writer. Store the power state, typed cause event and operation receipt atomically;
do not derive retry intent from a newly loaded mutable candidate. Existing adapter
event inference needs a narrow typed descriptor path. Missing resource is unknown,
not Off; enabled/conditionState retain their current meaning. Prefer no new table
or schema bump because the existing resource list can represent the value.

State/handler/visual changes are assigned to A2F/A2P/A2V in
[[Scripted Actions and Inspect TV Implementation Plan]]. Current proposed IDs are
`t.turn_on_tv`, `s.turn_on_tv.present`, `s.turn_on_tv.absent`,
`s.turn_on_tv.unavailable`, Action `activity.turn_on_tv`, shared execution script
`activity_sequence` and thin trusted composer `turn_on_tv` / `TurnOnTV.cs`.
These replace the unpublished inspect example's proposed IDs, not existing saved
content IDs. The shared Workbench can later author the same typed sequence; it must
not execute raw method names, write arbitrary instance state or grant permissions.
