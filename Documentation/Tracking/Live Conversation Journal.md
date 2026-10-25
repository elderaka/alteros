---
title: Live Conversation Journal
tags: [alteros, design, agent, journal]
updated: 2026-10-22
status: discussion
---
# Live Conversation Journal

Related: [[Architecture]], [[Design Decisions]], [[Feature Register]], [[Agent Tools]], [[SessionZero]].

## 2026-10-22 implementation reconciliation

Earlier discussion below is historical design, not the current implementation
queue. M2/M2.1 and v4 Scenario conversation exist in source; SQL facades now own
runtime content/history behind the same host contracts. Phone/shared-history UI
and optional TTS acceptance are M4, not the current furniture M3 assignment.
See [[Unified Conversation M2 Handoff]], [[Scenario Conversation Handoff]] and
[[../Verification/2026-10-22 Documentation Reconciliation]] for current gaps.
No new runtime/provider acceptance is claimed by this journal update.

## 2026-10-16 - unified Topic-centred conversation locked

Latest approved direction: [[Unified Conversation Design]]. No player-facing
Topic/Agent switch; retrieval-assisted judgment, authored-first playback and
validated generation share one VN presentation contract. Direct/phone remain
channels of one companion; direct interaction adds physical participation rather
than making phone intelligence worse. Completion counters do not consume
repeatable content. Generated occurrences do not automatically enter the catalogue.

This supersedes older explicit-mode and plain-string suggestion proposals below.
Suggested replies use optional `label`/`prompt` pairs, with faithful player-facing
meaning, not executable actions. Initial implementation is bounded by
[[Unified Conversation Implementation Plan]] to headless M1, not working Live UI.
No runtime, scene, provider or audio verification was performed by this journal update.

## 2026-10-16 - approved conversation direction

This records approved direction, not implemented UI, a final harness schema,
or a runtime acceptance result. No Unity assets or code changed in this pass.

- First prove that Alterra can respond through BYOK Live/Agent conversation
  and that optional TTS works. Defining and authorizing Agent tools belongs
  to the next slice. A study-timer tool is not a prerequisite for this slice.
- Topic remains the default local authored path. Live is the player-selected
  Agent conversation experience, not a new automatic routing decision.
- One shared conversation/session supports two presentation channels:
  face-to-face through explicit E interaction and a phone-style messaging UI.
- Both channels continue the same context and history. They are not separate
  characters, agents, or disconnected transcripts.
- Face-to-face replies follow the existing dialogue-node presentation rules;
  phone replies use a conversational girlfriend-texting style. Delivery changes,
  not character identity or established facts.
- The phone provides shared conversation history, including face-to-face
  exchanges inline. Phone messages use ordinary bubbles; face-to-face entries
  use a soft pink tint and a pink left-edge accent rather than a full outline.
  Do not add an "In person" label. Exact geometry/colors remain UI authoring work.
- Use complete request/response turns, not SSE. Show a pending state while
  awaiting the response; validate before presentation.
- Switching presentation must not resend input, create duplicate replies, or
  replay old speech. History is a view, not a second generation pipeline.
- Provide a saved TTS toggle, off by default for the testing slice. Off means
  no automatic synthesis or background voice baking, not merely muted playback.
  Text does not wait for speech; voice errors preserve the text conversation.
  Phone messages are silent by default and opening history does not speak it.
- The minimal harness still owns bounded context/requests, cancellation,
  response validation, and honest key/provider errors. No framework selection,
  tool permissions, autonomous routines, or background generation are approved
  for implementation merely by this conversation.

The user reported Topic selection working before this discussion. This is
player feedback, not a new automated check or proof of the remaining Session
Zero gates. Shared notes contain no private in-game conversation records.

## 2026-10-16 - locked local sidecar and validation direction

### Compacted humanization clarification

User meant a compact natural-conversation guidance layer, not model distillation
or training on human conversations. Goal: humane/natural character behavior,
complementing persona. Earlier discussed examples include specific attention,
independent opinions, honest uncertainty, warmth without constant agreement,
context-appropriate humor and respectful space. These are behavioral targets,
not requirements to insert hesitations, mannerisms or affection into every line.

Keep guidance/examples bounded and clearly separate from actual player memories.
Do not fabricate shared experiences, physical actions, hidden emotional knowledge
or guilt to create an impression of humanity. Personality grounding does not
override the response schema, tool permissions, focus settings or game state.
No training, new prompt bank, provider call or runtime implementation is implied.

Remaining design agenda: final Live response fields and limits; language/channel
policy; supported cues; context/history inclusion and retention; model/provider
capabilities and bounded repair; sidecar transport/credential handoff; cancellation,
delivery acknowledgements and restart policy; acceptance and performance gates.
Session0 BYOK stays complete; first Live slice remains conversation-only.

User accepted a hidden bundled Python sidecar with LangGraph orchestration and
Pydantic data validation. One installed/launchable AlterOS product, not one OS
process; helper is hidden from taskbar but visible in Task Manager. OpenRouter
handles both LLM and TTS generation. Session0 BYOK is complete as described in
[[../Verification/2026-10-16 BYOK Setup]]; do not duplicate it.

Pydantic validates shapes/types/bounds at sidecar boundaries, not the truth of a
memory, personality fidelity, tool permission or current game eligibility. Unity
retains state/effect authority and its own boundary checks. Framework internals
must not become the gameplay/UI protocol. Full detail and performance gates:
[[Architecture#Locked agent-side stack (2026-10-16)]], D-30/31 in [[Design Decisions]].
Transport, credential handoff, model selection, final Live response contract and
late-reply channel-switch behavior remain unresolved. No installation, code,
Unity operation or performance test happened in this documentation pass.

## Next discussion - action, expression, and presentation

Research follow-up: [[Companion Continuity and Memory Research]] records the
user's lifetime-continuity, shared-experience recall and personality concerns.
History should be inspectable like messaging, not an editable memory profile.
Memory layers/retrieval/persona mitigations remain proposals; no framework or
memory implementation was approved by that research discussion.

The user wants both interfaces to convey Alterra's actions, animation, and
emotion. The following are assistant proposals, not approved contracts:

- Separate dialogue content, requested expression/gesture cues, and actual
  game activity state. A smile cue is presentation; "drink water" can be a
  mechanical action and must not bypass the later tool permission boundary.
- Let the model request bounded cues from supported identifiers, never raw
  Animator parameters, arbitrary animation paths, scripts, or stat changes.
  Unsupported cues fall back safely to a neutral presentation.
- Unity resolves cues against what Alterra is actually doing: a facial cue
  may layer over a compatible activity, while conflicting full-body motion
  must defer or be rejected. The harness cannot interrupt an activity just
  because the response text suggests it.
- Face-to-face uses validated dialogue utterances, supported expression and
  gesture cues, and optional speech. It need not become a blocking cutscene.
- Phone uses message bubbles; typing/pending indicators mean a real pending
  request, not proof of a physical action. Future activity indicators/cards
  should reflect game-owned state, not model prose. No automatic reading aloud.
- Stamp each reply/cue/speech operation with the owning turn and delivery
  channel. Switching UI does not move a late response into the wrong physical
  presentation or play stale speech. Exact switch/cancel behavior is still open.
- TTS timing must not hold the whole conversation hostage. Define bounded
  expression duration/reset and cancellation cleanup before implementing it.

Activity/conversation coexistence is clarified below. Still open: which
gestures are cosmetic versus tool-governed actions; how many utterances or
phone bubbles a turn may produce; and what happens to an in-flight reply when
the player changes channel. This is a journal, not a builder-ready spec.

## 2026-10-16 - greetings and activity direction

### Return-to-conversation continuation (2026-10-16 follow-up)

User direction: walking away while a reply is pending or being presented is not
an explicit cancellation. A successfully generated/validated reply remains a
successful result and can be resumed when the player returns through E, with a
contextual greeting such as "About earlier...". Do not duplicate the reply.

Proposed implementation semantics, not implemented or a final schema:

- Track generation outcome separately from presentation progress. Successful
  generation does not prove the player saw/heard the whole reply or any tool
  action completed. Explicit Cancel remains distinct from walking away.
- Store one canonical response ID with ordered utterances. Phone visibility and
  face-to-face presentation reference that response, not newly appended copies.
  Continuation never reruns generation, tools, rewards or Topic consumption.
- Stop face-to-face speech/camera ownership on departure. Remember the last
  completed utterance boundary; a partially interrupted utterance may restart,
  rather than resuming mid-word. Presentation progress is observable UI/audio
  acknowledgement, not a claim to know whether the player understood it.
- Next E interaction may choose a valid pending-continuation greeting through
  the existing proposed priority queue, then present the unfinished response.
  Do not automatically start conversation just because the player approaches.
- User clarification: replaying that stored reply does not select/re-enter Agent
  mode or restart its model/tool loop. A natural hand-back line can return to
  the ordinary E interaction menu, with Topic still the default. Only an explicit
  player choice starts further Agent conversation; continuation is presentation
  of the existing result, not a new request or another copy in history.
- Recommended suppression: if already presented/acknowledged through phone,
  completed in person, explicitly cancelled, or superseded, don't automatically
  repeat it. Explicit history/replay remains possible without new generation.
- Recheck time-sensitive content and current activity before resuming. Expiry,
  exact phone acknowledgement, greeting wording generation and queue priority
  remain to specify. Do not blindly repeat stale advice or perform stale actions.

The existing ConversationTurnRecord/RecordTurn source has no unified response
delivery-progress/idempotency contract; each RecordTurn creates a new ID. This
is a design gap, not a finding that current gameplay already duplicates replies.

### Agent replies are VN dialogue beats (2026-10-16 clarification)

User requirement: Agent output must be an ordered collection of compact VN-style
dialogue nodes, including for long answers, with per-node expression cues. It
must not be a single large paragraph merely because its source is an LLM.
Node boundaries should follow meaningful conversational/explanatory beats, not
arbitrary character slicing. The model should compose the segmentation and cues
together; the host validates it rather than guessing expressions afterward.

Each beat carries spoken text, display text, language and a supported presentation
cue. Cue selection is constrained by the actual allowed vocabulary/state; a
requested expression does not set an emotional stat or authorize an activity.
Phone can render those beats as message bubbles; direct delivery presents them
as VN nodes without merging them into a wall of text. Style differs by channel.

The earlier proposed1–3 brief bubbles is not a universal cap on substantive
answers. Bounded total output and per-node limits still need definition; exceeding
limits must not silently truncate the answer or publish malformed content. Live
replies don't need Topic anchors, scenario selection or generated player branch
nodes. Optional suggested player replies are distinct from graph choices below.
This does not finalize the JSON field names or approve arbitrary tools/attachments.

### Optional suggested player replies (2026-10-16)

User requests optional buttons alongside free text in Agent mode, similar in
appearance to Topic-selection buttons. They can be questions or reactions, such
as "What kind of cat?", "Wow, that's amazing!", or "When did you first keep a
cat?". Zero suggestions is valid; typing remains available. Selecting a button
submits a new player message, not an automatic dialogue branch or tool action.

Current source inspection: canonical nodes in
`Assets/AlterOS/Dialogue/DialogueDefinition.cs` have text/displayText/language,
presentation cues and graph choices, but no Live suggested-reply field. The
updated external generation skill explicitly covers TopicDefinition/ambient,
not Live. The old sibling backend's Live builder still demands tagged three-line
JP/EN bubbles and forbids JSON; it is historical, not the new harness contract.

Existing `DialogueData.TopicSuggestion` has label/prompt plus action/path/generated
dialogue fields. `TopicOptionCatalog` converts those to playback choices. The
`request_topic_prompt` selection handler in DialogueManager currently warns that
background generation is unavailable. Do not reuse that action or expose its
mechanical fields for Agent suggested replies. Reuse an authored button template
only where appropriate; preserve separate behavior/ownership.

Proposed wire addition: top-level `suggestedReplies`, a bounded array of plain
player-facing strings (proposed0..3; exact limit not locked). Empty array means
no buttons. Use the displayed text as the submitted text; no hidden expanded
prompt, route, command, path or tool instruction. Keep this separate from the
ordered VN node list; the live schema itself is still a proposal.

Proposed lifecycle: show suggestions after the final presented beat of the active
Agent reply. Bind buttons to the owning response; discard/disable them after
submission, a newer turn, mode change or cancellation. A click and typed submit
use one dispatch path, with a duplicate-submit guard. Record only the chosen
message as a player utterance; unselected suggestions are not player facts or
memories. History inspection/replay and return-to-E continuation do not start a
new Agent turn or restore stale active buttons automatically.

Generate suggestions with the same model reply, not a second speculative call.
Validate structure, size and duplicates; content must remain grounded and
optional. No provider call or UI/runtime change was performed in this inspection.

### Agreed behavior

- Contextual greetings use priority weights and remain pending while valid.
  On each E interaction, discard invalid candidates and choose the highest
  priority valid greeting. Present one greeting, leaving other valid entries
  for later interactions. With no pending candidate, use the current default.
- A busy acknowledgement may precede a special morning greeting. If afternoon
  arrives before the latter is used, it is invalid and must be removed rather
  than presented. Ordinary defaults must not replenish the special greeting
  indefinitely.
- When Alterra cannot converse, use a short non-blocking busy bark. The game
  remembers the attempted interaction; she does not automatically approach,
  notify, or start a conversation when free. A later player-initiated E can
  select an acknowledgement greeting. Wording belongs in dialogue content.
- Activities distinguish CanTalk (converse alongside the activity) from
  CanPause (pause safely for conversation and resume). Watching TV can permit
  talking; drinking can pause at a safe point, not mid-swallow. If neither is
  possible, use the busy behavior above.
- Phone typing is an activity with both permissions false, not an independent
  IsTypingPhone flag. Any convenience property is derived from activity state.
- Direction accepted: a small activity state machine with data-defined
  activities, separate from the Animator. Definitions describe permissions and
  presentation; runtime owns the current activity/progress and lifecycle.
- Non-pausable does not prevent cleanup on failure, cancellation, or scene
  exit. Waiting on a provider is not physical typing: the suggested typing
  lifecycle starts after a reply is validated and ends with delivery/cleanup.
- Expression/action vocabulary is still to be defined. It needs conditions:
  for example, a strong angry expression must be unavailable below the chosen
  authoritative anger threshold. The harness receives eligible choices and
  game-side validation rechecks them; model prose cannot grant eligibility.

### Details still open

Equal-priority tie-breaking, acknowledgement retention/expiry, save/load policy,
exact greeting consumption point, activity phase transitions and safe-pause
timing, and the expression threshold values/data format are not finalized.
Consume-after-presentation and oldest-first ties are assistant recommendations,
not implemented guarantees. No general scheduler or behavior tree is selected.

### Current implementation evidence and limits

Source inspection during this discussion found a configurable greeting JSON
path in `Assets/Altera/AlteraInteractable.cs`, dialogue/control locks, expression
presets and presentation cue handlers, and a stat-derived availability string.
The focus timer has private UI-owned running state. These are reuse points,
not a shared activity lifecycle, contextual greeting queue, or harness-facing
conditional capability catalogue. DialogueManager.IsTyping is text reveal,
not phone typing. No phone typing clip was identified by the filename search;
imported embedded clips and actual character playback were not audited.
No code/Unity assets were changed or runtime checks run for these findings.

## 2026-10-16 - furniture and room follow-up (unfinished)

The user raised that activities need physical places/objects: for example,
watching TV uses a couch/seat in relation to a TV. Furnishing and future house
expansion therefore affect what Alterra can actually do. The user intends to
delegate creation of a replacement room scene next; this journal does not
create a task, dispatch an agent, replace a scene, or approve a building system.

Assistant proposal for discussion, not yet an approved implementation:

- Furniture offers activity use-points with approach, use pose/orientation,
  and exit space. An activity chooses an available compatible point; it must
  not hardcode room coordinates or use a decorative mesh as proof of usability.
- Keep the activity definition distinct from the furniture instance and its
  use-point. Watching TV needs both a usable seat and a valid TV relationship;
  portable activities such as phone typing need not require fixed furniture.
- Recheck reachability, occupancy/reservation, and object existence before
  starting; release reservations on cancellation/failure. Removal or relocation
  of furniture must fail safely rather than leave Alterra stuck.
- A first room can be hand-authored with a few clearly named use-point markers,
  walkable access and door clearance. Placement UI, purchases, procedural rooms,
  construction, expansion and save migration remain separate later scopes.
- Keep entry points, interaction approaches, character scale, and circulation
  space explicit in the eventual scene handoff. Scene changes must use Unity
  Editor workflows and preserve the existing startup/dialogue references.

Open: first-room dimensions/assets and layout, included activities/furniture,
navigation solution, seat/prop alignment, room expansion rules, and whether
the initial room is fixed or player-rearrangeable. No scene handoff specification
has been approved yet.

## 2026-10-16 - stats research and locked mood display

The user approved layered mood presentation: a compact readable summary,
expanded nuanced current description with unnumbered spectra, supporting
colors, and raw numbers in developer inspection. Expanded axis labels are
placeholder wording to replace with flavor text later. See [[Stats and Mood]]
for the locked boundaries, research references and unfinished catalogue rules.

Activity discussion also established internal steps such as sitting, watching,
changing channel, watching another title, and getting up. Channel/title can be
activity data rather than a different top-level activity for every program.
Phase-specific conditions and effects remain to be authored. Benefits should
accrue during actual participation (continuous rates or action checkpoints),
not automatically arrive as a lump sum on completion. Exact rates, phase
transitions and completion policies remain open.

This entry documents decisions only; no implementation, migrations, semantic
rebake, runtime checks or scene work occurred.

## 2026-10-16 - Topic Playground priority

The user clarified the immediate need: a Twine-like text playground, not only
a static catalogue audit. Submit natural language with synthetic context, see
the actual authored response/branches and inspect selection explanations from
either a headless command or Unity window. Start with existing Topic behavior;
unimplemented stat triggers and activity rules must remain explicit gaps.
[[Topic Playground Handoff]] records this bounded next-session scope. This
tooling slice precedes further Live implementation; it does not cancel the
approved shared-session/optional-TTS direction. No playground code was built
in this journaling/handoff pass.

## Next acceptance checks (not run)

Further Topic design journal (2026-10-16): [[Topic Scenario Selection]] locks
shared anchor ownership, multi-gate eligibility, descending priority override,
same-tier weights and whole-instance replay/consumption. "One-topic" meant
one-time consumption, not a single semantic-label restriction. The current
implementation comparison is [[Topic Scenario Refactor Audit]]; no code changes
or runtime tests were made during this documentation pass.

Further 2026-10-16 discussion: engagement and connection are distinct;
probabilistic Topic opportunities/weights are agreed direction, with balancing
still open. [[Stat Catalogue]] records this. [[Brainstorm Inbox]] records
future external socializing and learned personal Topics as exploratory ideas,
plus the unsettled adequate-self-care versus player-enrichment policy.

1. E -> explicitly select Live -> send text -> see a real provider reply.
2. Continue via phone with shared context and correct history/bubble styling.
3. Switch back without duplicate requests, replies, or speech.
4. With TTS off, confirm no speech-generation request/prefetch is issued.
5. With TTS enabled for face-to-face, hear a real clip; verify cache reuse,
   synthesis failure, cancellation, and text-first behavior separately.
6. Missing key or provider failure leaves an honest, recoverable UI and the
   Topic path usable. This slice must not claim to execute tools or change stats.
