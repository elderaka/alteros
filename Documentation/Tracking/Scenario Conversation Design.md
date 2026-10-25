---
title: Scenario Conversation Design
date: 2026-10-19
updated: 2026-10-22
tags: [alteros, design, conversation, scenario]
status: approved-design-partial-source-implementation
---
# Scenario conversation design

Related: [[Unified Conversation Design]], [[Topic Scenario Selection]],
[[Live Conversation Journal]], [[Agent Tools]], [[Player Memory Handoff]],
[[Unified Conversation M2 Handoff]],
[[../Verification/2026-10-19 Parallel Appetizer Spike]].

This note consolidates the player's approved direction. It is **not implemented
behavior, an execution plan, or permission for provider spending**. The player
approved the marked defaults on2026-10-19, clarifying that Dessert is simply
closing dialogue inside the main response, never a separate stage or request. Preserve
current wire contracts and authored content until an explicit migration is planned.

## 1. Intent and vocabulary

Natural conversation should begin promptly, then become a bounded, game-aware
interaction that can contain speech, questions, allowed activity requests and
waiting for the player. Authored and generated interactions use the same runtime
semantics; an authored interaction never needs an LLM merely to execute.

| Term | Meaning / owner |
| --- | --- |
| Topic | Semantic catalogue meaning, shared anchors and references to relevant scenarios; not an executable dialogue segment |
| Scenario definition | Conditions, steps/branches, allowed outcomes and lifecycle rules of an interaction |
| Scenario instance | One occurrence with runtime progress, bindings, checkpoint and end reason |
| Dialogue sequence | A bounded stretch of VN beats within a scenario |
| Beat | Japanese speech text, corresponding English display text, supported expression/presentation |
| Activity | Game-owned physical work, including phases, use-point reservations, CanTalk/CanPause and authoritative effects |
| Dialogue interaction / exchange | The currently open player-facing conversation surface; closing it need not end a scenario |
| Topic occurrence / consumption | Existing content replay/history concept; do not equate it with every UI close or automatically rename/migrate its IDs |

A Topic indexes scenarios; a Scenario contains steps; a dialogue step contains
beats. A Scenario does not recursively contain semantic Topics as executable
children. Sharing a subject does not mean unrelated route families should be merged.
A brief two-beat conversation is a valid small scenario; objectives/actions are
not mandatory. Topics can have a single scenario.

### Scripted Activity checkpoints and pose safeguard (2026-10-22)

Design amendment from the dancing example; this is not an implemented checkpoint
protocol or permission to start a framework refactor.

- An Action is registered, trusted C# scripted behavior. An editable definition
  references its implementation/configuration; an Activity execution is a running
  occurrence. The same script can serve multiple definitions/branches. Content
  and the LLM can request only registered, host-validated behavior, never supply
  executable code. Script capabilities include movement, furniture use, approved
  animation, non-modal bark requests and game-approved effect checkpoints; full
  dialogue remains Scenario-owned.
- One scripted Activity may reach a named checkpoint while its Scenario presents
  dialogue. The Scenario owns the question and branch; the Activity owns physical
  behavior. Both presentation patterns are supported by the design: stop dancing
  for dialogue, or switch to a compatible light-dance loop during dialogue, then
  continue the selected phase of the same execution.
- Authoring should expose these phases and dialogue boundaries as readable steps;
  it need not require a separate C# class for every branch. Truly independent
  activities may instead end and start another execution using a reusable script.
- **Player clarification: the 30-second pose safeguard is post-interaction /
  post-Scenario, not an inactivity limit inside an active Scenario.** An active
  Scenario blocks this safeguard even while no dialogue UI is open. A continuing
  dance, an unanswered question, or a wait for the player must not arm it merely
  because 30 seconds passed.
- Once the owning interaction and Scenario have finished, a leftover presentation
  pose/loop may remain for at most 30 seconds before returning to a safe default
  pose. This does not extend Scenario execution, award effects, or mark an Activity
  complete. Normal completion/cancellation cleanup still happens promptly; the
  safeguard is recovery for lingering presentation, not a mandatory ending delay.
- Coordinator implementation recommendation: the default must be compatible with
  the actor's current physical state (for example seated idle rather than standing
  through a chair). Recheck ownership at expiry; a newly started interaction or
  Activity must not be reset by an old timer. Resource release, movement cleanup,
  and effect authority remain with the existing Activity host, not an animation
  timeout.
- Checkpoint continuation must correlate to the same live execution and accept a
  branch only if still valid. It must not replay start effects or revive an ended /
  cancelled Activity. The existing quit rule below remains unchanged.

Named intermediate checkpoints, choice-driven continuation, animation bindings,
and this post-end safeguard still require an explicit implementation slice. Their
description here is not source, Unity, or gameplay verification.

Coordinator ownership recommendation, not an assignment: LLM Planning owns the
Scenario/dialogue orchestration and the new interaction contract; extend the
existing ActivityHost instead of adding a parallel executor. Coordinate physical
movement, furniture reservations and Unity animation adapters with Room Testbox,
using explicit file ownership if both sessions work. Workbench consumes the stable
contract afterward. The C# behavior must work through an authored, provider-free
fixture before LLM generation is treated as evidence of it working.

### Existing baseline, not the new target

Current M2 generates strict transient `reply.beats` and maps them into canonical
dialogue `nodes`. A generated reply is not automatically an indexed/replayable
catalogue entry, published Topic, actionable Scenario or completed activity.
Current `suggestedReplies` are response-bound current-exchange continuations;
their field must not silently acquire next-interaction meaning. The implementation
session supplied its read-only factual baseline on2026-10-19; it confirms these
boundaries. Current `suggestedReplies` are rendered on the final current beat,
then submit a normal next player turn after response-ID validation. There is no
pending question/answer state, LangGraph interrupt/checkpointer, production
Appetizer, Dessert or action-capable generated Scenario. Current graph is
validate job -> build prompt -> one provider call -> validate result -> finish.

The session reports latest language-fix evidence Python140 passed/1 skipped,
C#115/115 and a synthetic-key bootstrap/shutdown of the English-suggestions
package. Other129/106 memory counts are separate dated runs, not current combined
Unity acceptance. M2.1 first-E/profile Canvas controls and proposal-confirmation/
question activation remain incomplete; preserve those boundaries in the plan.

New read-only source concern from that report: `graph.generate` reconstructs
`ProviderResult` with only judgment/usage/timing/error; `generate_memory_turn`
later reads its memory proposals, and the stdio response omits question/callback
metadata. Add a positive eligible-proposal persistence/metadata regression and
resolve this before treating adaptation as operational. Do not fix by weakening
consent/provenance rules. This is a source finding, not a reproduced player bug.

## 2. Approved scenario lifecycle

- At most **one running scenario** at a time. Paused progress is not another
  running scenario and cannot execute steps or retain exclusive activity control.
- A scenario may remain open indefinitely while the app is running. Leaving the
  desk, closing dialogue or ordinary inactivity is not an implicit timeout/end.
- A player-requested diversion to another action/scenario asks whether to pause
  or end the current scenario before committing the diversion. A decline/back-out
  leaves the current scenario unchanged.
- Pause preserves a continuation checkpoint. End closes this occurrence.
- Exiting the game ends active and paused scenario execution. The next launch
  must not resume that occurrence automatically. Actual conversation/history may
  persist; it is not evidence that unfinished activities completed.
- Scenario end is not necessarily success. Keep completed, player-ended,
  interrupted-by-quit and failed outcomes distinct. No invented rewards/effects.
- Actual completion/checkpoint events remain idempotent; consuming an occurrence
  does not consume the canonical semantic Topic for all future occurrences.

### Autonomy and interruptions

Needs continue during the scenario. Ordinary compatible self-care is an Activity,
not an automatic second scenario. Alterra can reach a safe pause point, drink
and return without forcing a player cutscene or permission exchange. Only the
game applies recovery/effects as the activity progresses. Existing focus/quiet
rules suppress unsolicited speech/forced interludes, not necessarily silent
self-care. No guilt/relationship punishment for declining or ignoring prompts.

Activities may be compatible with talking, safely pausable, or neither. The
scenario runner asks the activity system; it must not reset pose/reservations
or teleport Alterra because a model proposed an action.

### Return prompts, approved trigger distinction

| Temporary activity participants | When the return prompt becomes eligible | Example |
| --- | --- | --- |
| Alterra only | When her activity finishes and she can talk; respect focus/quiet and presentation ownership | “I'm back. Want to continue?” |
| Player only | Next explicit player interaction; ask whether the player is done | “Are you done?” |
| Together | Next explicit player interaction after the relevant activity completion | “Shall we continue?” |

Never resume automatically. Offer Continue / Not yet / Wrap up. No answer keeps
the scenario paused, without repeated modal prompts. Real-world player activity
completion requires player confirmation; do not infer it from a timer or LLM.
These rules cover resumable diversions; ordinary self-care inside an otherwise
running scenario need not manufacture an unrelated scenario to return from.

### Approved default: bounded suspended progress

Keep **one suspended scenario slot** in addition to the one running slot. No
recursive/unbounded stack of paused scenarios. Before a second scenario would
replace that suspended progress, ask the player to end/replace it or back out.
This was explicitly approved with the final default bundle on2026-10-19.
All suspended slots, if the policy changes, still end at app exit.

## 3. Scenario step vocabulary

Proposed minimal vocabulary for the execution plan:

| Step | Contract |
| --- | --- |
| Dialogue | Present a validated bounded VN sequence; acknowledge actual beat delivery |
| Ask player | Display a question, wait for answer/skip/cancel, then continue from that checkpoint |
| Action request | Request an allowlisted capability with typed arguments; host validates/binds and starts the activity |
| Wait | Observe an explicit game event/condition; may supply an optional objective hint |
| Branch | Choose a declared successor from validated answers/game results, not invented facts |
| End | Close the exchange and/or end the scenario with an explicit outcome |

Steps have stable IDs and declared successors. Invalid references, unbounded
automatic loops, unsupported capabilities and unknown mandatory conditions fail
validation rather than being ignored. The outline can include waiting phases;
it is not a program granting arbitrary code, filesystem access or stat writes.

An objective is the hint for a wait/participation condition, e.g. “Join Alterra
at the drawing table.” It does not itself move an actor, complete an activity or
create another scenario. Bind a specific table/use-point and compatible player
seat; conditions must not combine facts from different furniture instances.
Recheck reachability/reservations and relevant needs/world facts when executing,
not just when the LLM generated the outline. Completion/failure comes from the game.

Proposed default: an unavailable action follows a validated failure branch and
offers a safe next step; never narrate a completed action that did not happen.
No “fallback to an arbitrary similar tool.” Capabilities absent in the host
snapshot must not be offered as executable choices.

## 4. Parallel Appetizer and main generation

### Approved delivery target

- When optional online generation is authorized and needed, start two bounded
  branches for the same player turn: tiny Appetizer and main preparation.
- Appetizer: **1–4** concise bilingual beats, normally 1–2. No action execution,
  game-state effects, memory proposals or premature activity promises.
- Main: Scenario outline plus the **next playable segment**, not every future
  conversation in an indefinitely running activity. Use context/checkpoints to
  prepare later segments only when needed and authorized.
- Maximum **five seconds from player submission to first visible beat**. This
  is a UI target with a prepared local opening if no generated response is ready;
  an external provider response-time promise cannot enforce it.
- TTS must not delay first visible text. Speech controls/cache migration remain
  separate work; the initial slice can remain text-only.
- Use complete bounded stage results, no SSE/partial unvalidated JSON playback.
- Preserve authored-first/local semantic shortcut: a ready eligible authored
  exchange does not need two paid requests. Offline/online-disabled still works.

One sample measured Appetizer readiness2.802s and continuation3.291s, not UI
visibility, full-scenario planning or a reliable percentile/SLA. Repetition was
observed. See the spike evidence; do not ship that diagnostic as orchestration.

### Proposed production orchestration defaults

- Host creates a single turn identity/context version with separate head/body
  stage IDs and a single presentation owner. Both share grounded persona,
  consent/forget filters, bounded recent context, language and supported vocabulary.
- Head receives the minimum grounded context needed; main receives bounded
  candidates, activity facts and relevant scenario checkpoint. Do not bypass
  consent/suppression to make a smaller head prompt.
- Main has no dependency on the exact generated head text. Assign a fixed
  content split: head is acknowledgement/reaction; body provides the answer,
  decisions, questions and proposed activity plan. Head must not ask a question
  that would invalidate body preparation when the player answers.
- If the body is ready before any opening is shown, display the body and discard
  the redundant head. If the head is on screen, finish at a beat boundary before
  admitting the body. Do not overwrite text the player is currently reading.
- At the five-second deadline, commit a prepared local opening only if no stage
  has committed an opening. A late generated head cannot replace/repeat it.
- If the opening ends before the body is ready, permit one non-modal prepared
  bridge/waiting cue, then quiet waiting with cancel/retry controls. Do not create
  an endless stream of extra LLM filler calls or a fake progress percentage.
- Each stage is structurally validated, then checked against current permissions,
  world facts and already-presented content before admission. JSON validation
  alone is not semantic consistency proof. Use role constraints, grounded briefs
  and paired-output evaluations; no automatic third LLM “repair” request.
- Superseding input/answer, explicit cancellation, exit and changed context
  invalidate pending stages. A successful but undelivered result may remain a
  historical receipt under existing late-delivery rules, never seize a new UI,
  become player-presented memory, execute tools or publish content automatically.
- Head/body delivery does not consume/reward a Scenario simply because an HTTP
  request completed. Record actually presented beats once in order; future
  context must distinguish received, presented, cancelled and late results.
- Only the admitted main result may propose memory through existing policy.
  Unchosen options, speculative head/body text and discarded branches are not
  claims by the player. Recheck consent/forget state after each provider wait.
- Same key and explicit online consent, two paid requests maximum per generated
  segment, no automatic retries/repair/escalation. Track usage even on rejected
  billed output. Cancel stops local adoption but cannot promise refunded spend.

Need a measurable UI contract: use monotonic time, commit the fallback on the
next render opportunity, test responsiveness and both stage races. No software
can guarantee a visible frame during a frozen/closed app. This caveat must not
be hidden by reporting API readiness as the five-second visual criterion.

## 5. Ask Player versus Suggested Topic

| Mechanic | Timing / meaning |
| --- | --- |
| Ask Player | A question now, with a pending question ID and scenario continuation |
| Answer options | Labels/typed answer values for that specific pending question; free text may also be permitted |
| Suggested Topic | An optional eligible subject offered on a **later explicit E interaction**; selecting starts/reopens an appropriate interaction |
| Dessert | Closing dialogue inside the main response; not a separate generation/presentation stage or the suggested-topic list |

Question state belongs to a game-owned pending step, not a framework protocol in
Unity. LangGraph interrupt/resume may implement the sidecar seam, but no
LangGraph-internal state or credentials should leak into UI/game contracts.
Avoid checkpointing secrets or replaying pre-interrupt actions on resume.

Proposed defaults: one pending question; stable question/turn/scenario binding;
choice/free-text input validated against that binding; duplicate/expired answers
cannot advance twice. Skipping follows a declared route or asks a clarifying
question. Player may dismiss/decline without being locked into a questionnaire.
An explicit scenario change uses the existing pause/end confirmation. Ordinary
answer text is first handled as an answer, not rerouted to an unrelated Topic.

Answer options require no semantic anchors of their own. Exact label/typed value
is evidence only after the player chooses it. Questions about real player facts
still obey consent/scope/provenance restrictions; asking is not permission to store.

Suggested Topics are not suggestions to speak immediately after the last VN beat.
Preserve faithful label/intent metadata and English display-language policy.
Showing an offer does not consume its Topic. Check current eligibility on the
next E and again on selection; stale or blocked offers cannot bypass gates.
An offer may refer to an existing eligible scenario or a validated intent for
future generation; an unprepared idea must not masquerade as indexed ready content.

Proposed initial scope: up to three session-local offers; selection consumes the
offer once, not the canonical Topic. They survive an exchange closing but expire
on session exit or invalidation. This session-only offer storage is a proposed
default; it does not change durable Topic replay/history or personal catalogues.

Existing `suggestedReplies` semantics must migrate explicitly through a versioned
new contract/adaptation, not silently rename existing serialized fields/callbacks.
Keep current generated `reply.beats` and authored/internal `nodes` distinct.

## 6. Dessert and closing

Default: when the selected topic conversation reaches its declared ending,
present a short closing if appropriate, then close the interaction UI and release
its camera/input ownership. Do not open the next suggested topic automatically.
Dessert is simply the closing portion of the authored/main prepared segment;
it is not a third stage, special standalone result or dedicated LLM request.
It is optional where another closing would be redundant.

A pending question/choice/wait is not an ending. Do not close before the player
can answer merely because the currently generated beats ran out. Finishing a
segment may close the conversation while a scenario waits/continues physically;
its checkpoint controls the next explicit interaction.

Keep these separate: ending an exchange, successfully completing a scenario,
ending it without success, and whole-Topic occurrence consumption. The named
semantic Topic itself never becomes “completed forever” by closing a bubble.

## 7. Synthetic reference scenarios

### Do you like cats?

Topic: cats. Small scenario: head/reaction, main answer, optional Ask Player
(e.g. favourite traits), response segment and closing. No activity/objective is
required. Optional future Topic offers wait for next E; do not append current-
exchange suggestion buttons to keep the dialogue permanently open.

### Wanna draw a car together?

Topic: drawing together. Scenario: reaction; question about style; validated
request for Alterra to use an available drawing table; wait for her ready state
and player's bound adjacent seat; shared activity; contextual E dialogue with
Continue/Not yet/Wrap up; closing/end on explicit wrap-up. An unavailable table
does not become a successful action because dialogue says so. Supported activity
availability is supplied by the game, not invented by the generated outline.

Coding together follows the same lifecycle. Self-care may temporarily change
Alterra's activity without consuming/restarting the scenario. Explicit diversions
use pause/end and the participant-specific return prompt rules above.

## 8. Implementation boundary for the forthcoming plan

Recommended first slice: typed Scenario/step/lifecycle foundation, head/main
orchestration, explicit question/answer versus next-E offers, dessert/exchange
closure and deterministic headless/Playground fixtures with **simulated** activity
results. Then separately integrate only capabilities that actually exist in game.
Preserve M2/M2.1 history, consent, source suppression, late-reply and schema fixes.

Do not quietly include furniture/room construction, full needs simulation,
external coding access, image generation, arbitrary tools, phone UI, production
TTS/Kokoro migration, memory redesign, autonomous paid background calls, or bulk
catalogue rewrites. Actual drawing/coding gameplay must not be claimed from a
simulated activity adapter. Existing authored content needs an explicit adapter/
migration plan and regression gate, not manual catalogue edits in this design pass.

Unity static controls must remain Editor-authored Canvas/prefab composition.
No scene/prefab/meta changes are authorized by this document alone. Source,
package, Editor, visual, latency, activity and user acceptance remain separate.
The human retains their hands-on startup-through-E test.

### Required acceptance categories, not passed assertions

- Head, body, fallback and cancellation race permutations; five-second first-
  visible check; no duplicate opening or forced extra generation.
- Invalid/empty head or main output, offline/missing consent, changed memory
  policy and unavailable capabilities; no action from speculative/stale results.
- Ask/answer with current/expired IDs, free text, skip and duplicate delivery.
- One running scenario, pause/end decision, participant-specific return prompt,
  no automatic resume, no inactivity timeout, quit ends without false completion.
- Topic offers only at next E, eligibility recheck, unchosen offer not memory,
  Dessert closes exchange but not necessarily scenario, once-only history intact.
- Authored/offline compatibility, current v2/v3 adapters and a versioned successor
  contract; rebuild/helper handshake evidence separate from Unity presentation.
- Paired semantic quality set: no duplicate/substantively contradictory head/body,
  no invented actions/memory; timing and quality must both pass.

The baseline report has now arrived. A fresh read-only builder review found no
additional missing product decisions. The player approved the marked defaults;
the design gate is satisfied. Exact wire version, small-file layout,
token budgets, deterministic test scaffolding, allowed first-slice fixture
capability IDs and internal state representations are builder/plan choices;
they must preserve the behaviors and honest unsupported-capability boundary here.

Implementation checkpoint (2026-10-20): the v4 Scenario path is now called
through the existing direct-conversation coordinator and sole transcript
writer. Ready stages are pumped as they arrive, after a durable response
receipt; required-memory turns remain gated on context revalidation. The
five-second fallback clock begins at submit entry, including synchronous
capture. Transcript first-visible recording is separate from the playback
checkpoint, which advances only when the player advances past a beat. Ask/free-
text answers, next-E offers, and E resume-before-greeting are wired in source.
Current checks: Scenario67/67, Unified194/194, Python173/173 including packaged
keyless smoke, and Playground13/13. The latest Unity EditMode request timed out;
the prior 7/7 result predates these fixes, so current Unity verification is
unconfirmed. Splash->ModelTest->E, visual offer/return prompt behavior, and
actual first-visible timing remain unverified. No real activity capability,
provider/TTS call, or paid test was included; see [[Scenario Conversation
Handoff]].

## 9. Builder review and approval bundle

Read-only reviewer `scenario_design_builder_check`,2026-10-19: no important
unaddressed product choices beyond the proposed defaults already named. Review
is planning-readiness evidence, not a code/runtime verification or human approval.

These defaults were presented together and approved on2026-10-19:

1. One suspended scenario slot; replace/end/back-out confirmation if another
   diversion would overwrite it. At most one running scenario remains locked.
2. First slice is orchestration/contracts plus simulated activities in tests/
   Playground; real movement, furniture and drawing/coding gameplay are a later
   explicit integration slice, never claimed from the simulation.
3. Parallel delivery bundle: two requests maximum for an online generated
   segment, body-first skips redundant head, local fallback by five seconds,
   a single presentation owner and no automatic repair/retry calls.
4. Up to three session-local Suggested Topic offers for next E; selecting an
   offer consumes the offer once, not the canonical Topic; recheck gates.
5. Minimal steps and one pending question with explicit answer/skip/cancel and
   failure successors; version existing contracts rather than repurposing fields.

See [[Scenario Conversation Implementation Plan]] for the resulting plan; plan
review still precedes execution in the previously selected implementation session.

This pass changed design/index/status Markdown only. New route snapshots/anchor
rebakes are not needed because no authored/runtime JSON, source or wiring changed.
New/changed links were checked by filesystem resolution, not Obsidian reading-
view inspection. No Unity/tests/package/provider/TTS execution was performed.
