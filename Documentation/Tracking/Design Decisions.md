---
title: Design Decisions
tags: [alteros, decisions]
reviewed: 2026-10-16
---
# Design Decisions

Related: [[Architecture]], [[SessionZero]], [[Feature Register]], [[Semantic Index]], [[Agent Tools]].

This index preserves earlier brainstorming without replacing the detailed architecture. “Approved” describes direction, not completed implementation.

| ID | Decision | Standing |
| --- | --- | --- |
| D-01 | Explicit E and non-modal ambience remain; earlier default Topic/player-selected Agent switch | Mode selection superseded by D-32 on 2026-10-16; production remains legacy |
| D-02 | Local ready-dialogue routing remains available without an LLM | Universal local-only selection superseded by D-32; offline fallback retained |
| D-03 | Embedding, sentiment evidence, and LLM are separate model responsibilities; TTS is separate speech infrastructure | Approved; sentiment implementation undecided |
| D-04 | One shared dialogue schema/runner for generic and validated personalized content | Approved; personalized publication/playback incomplete |
| D-05 | Reusable Intent/Tone/Topic catalogue; mandatory canonical topic identity and branch-local anchors; local specificity matters | Approved target; current flat routes are not migrated |
| D-06 | Catalogue growth follows reusable concepts/contracts, not dialogue count; LLM cannot silently promote vocabulary | Approved |
| D-07 | Background adaptation batches useful evidence; retrieve a relevant catalogue slice; validate trigger examples rather than treating response prose as player intent | Approved; worker absent |
| D-08 | Background embedding/indexing yields to active use; no full generic rebake for every personal variant | Approved target |
| D-09 | Agent starts with game-owned study/plans/timers/shared activities; provider-neutral bounded BYOK boundary | Approved; conversation-only first slice precedes tools; stack selected in D-30 |
| D-10 | Only validated deterministic game events apply stats/relationship/currency; LLM cannot mint or submit arbitrary deltas | Approved |
| D-11 | Initiative ceiling: 0 reactive, 1 suggestive, 2 proactive, 3 explicitly enabled routines; no permission escalation from relationship or sentiment | Approved target |
| D-12 | Focus/quiet and active work suppress optional interludes/need TTS; defer/coalesce offers; no guilt/absence/decline penalties | Approved target; gate absent |
| D-13 | Sentiment is optional, disclosed, short-lived, uncertain evidence; explicit settings win; classifier cannot infer consent | Approved target |
| D-14 | TTS uses finalized text; cache/preparation separate from routing; no-key/failure must preserve usable text/Topic paths | Approved; provider seam exists, runtime proof pending |
| D-15 | Session Zero proves startup → E → unified authored-first conversation (online judgment and offline fallback) → stats/speech/focus before study → shared break → personalized Topic milestone | Approved sequence; selection wording revised by D-32 |
| D-16 | Track features, searchable dialogue catalogue, focused flow graphs, and runtime explanation separately; graph is a view, not source of truth | Approved tracking direction in current discussion |
| D-17 | Static HUD/UI structure, layout and styling are authored in Canvas scenes/prefabs, not constructed in runtime code; runtime binds state/content and handles genuinely dynamic presentation | Approved 2026-10-15; [[../Verification/2026-10-15 Topic Recovery and Canvas]] |
| D-18 | One Live conversation/session has face-to-face and phone channels with shared context/history and channel-appropriate delivery; complete request/response, no SSE | Approved 2026-10-16; [[Live Conversation Journal]]; not implemented |
| D-19 | Shared phone history uses ordinary phone bubbles and soft pink face-to-face bubbles with a left-edge accent; no "In person" label | Approved 2026-10-16; exact Canvas styling pending |
| D-20 | Saved optional TTS switch defaults off for the testing slice; off suppresses automatic synthesis/baking, text never waits for voice, phone/history silent by default | Approved 2026-10-16; [[Live Conversation Journal]]; implementation/audio proof pending |
| D-21 | Next slice proves real Live replies and optional TTS before defining or authorizing Agent tools | Approved 2026-10-16; candidate capabilities in [[Agent Tools]] remain proposals |
| D-22 | Contextual greetings are prioritized pending entries, revalidated on E; expired entries are discarded and valid lower-priority greetings remain for later interactions | Approved direction 2026-10-16; [[Live Conversation Journal]]; queue/retention details unfinished |
| D-23 | Activities distinguish CanTalk alongside from CanPause safely; busy interactions bark and leave any later conversation entirely player-initiated | Approved direction 2026-10-16; no activity runtime implemented in this pass |
| D-24 | Small data-defined activity state machine owns lifecycle independently of Animator; phone typing is a non-talkable/non-pausable activity, with failure/cancellation cleanup | Approved direction 2026-10-16; vocabulary, phases and furniture integration still under discussion |
| D-25 | Mood display uses compact summary, expanded nuanced current description/unnumbered spectra and supporting palette; raw emotion numbers are developer-facing, final detail text is flavor copy | Locked direction 2026-10-16; [[Stats and Mood]]; thresholds, exact copy/colors and implementation pending |
| D-26 | Activity benefits accrue through actual performing-phase rates or action checkpoints; completion is an outcome, not an automatic duplicate payout | Agreed direction 2026-10-16; [[Stats and Mood]]; concrete stat/effect catalogue unfinished |
| D-27 | Prioritize an authored Topic Playground: natural-language input, synthetic context, branch traversal and explanations through CLI and Unity Editor surfaces sharing production rules | Approved tooling direction 2026-10-16; [[Topic Playground Handoff]]; detailed implementation plan pending, full Live tools/stats out of scope |
| D-28 | Shared Topic anchor bank selects a family; eligible scenarios compete only in the highest eligible numerical priority tier, then by relative weight; gates support compound context/object requirements | Approved direction 2026-10-16; [[Topic Scenario Selection]]; executable condition/schema/weight details pending |
| D-29 | Replay/consumption scope is the whole Topic instance, never scenario-only or the canonical category; repeatable authored defaults, one-time generated experiences/milestones | Locked 2026-10-16; completion or authored delivered checkpoint consumes; cancel beforehand does not; persist and exclude all consumed siblings |
| D-30 | Hidden bundled Python sidecar; LangGraph orchestration and Pydantic boundary validation; initial LLM and TTS via OpenRouter; one launched product, separate helper process | Locked 2026-10-16; [[Architecture#Locked agent-side stack (2026-10-16)]]; no runtime/framework implementation yet; Unity retains permissions and game authority |
| D-31 | Include readiness, CPU/RAM, frame-time, reply-latency, memory-growth and recovery measurements in the first sidecar slice; bounded foreground/background work | Agreed direction 2026-10-16; performance targets are not measurements or a guarantee; remote inference does not eliminate local processing |
| D-32 | Unified Topic-centred conversation: retrieve candidates, optional online LLM judgment, eligible authored-first responses, generated occurrences when needed; no player-facing Topic/Agent switch | Approved 2026-10-16; [[Unified Conversation Design]]; M1 headless plan only, not implemented |
| D-33 | Record scenario completion count/time independently of whole-instance once consumption and replay policy; prefer unseen within the highest eligible priority tier | Approved authored-first/counter direction; exact tie-break and migration defaults specified in [[Unified Conversation Implementation Plan]] for review |
| D-34 | Phone and direct share intelligence/context/history; scenario channel eligibility and physical participation differentiate them, not blanket phone stat penalties | Approved 2026-10-16; phone UI/activity implementation pending |
| D-35 | Optional suggested player replies use faithful label/prompt pairs, separate from graph choices, tool calls and unchosen player evidence | Approved clarification 2026-10-16; replaces earlier plain-string proposal |
| D-36 | Initial M2 conversation model is DeepSeek V4.1 Flash through OpenRouter, explicit ID `deepseek/deepseek-v4.1-flash`; English-first input/display with paired Japanese speech text per generated beat (`text` Japanese, `displayText` English, `language` japanese) | Locked/clarified by user 2026-10-16; [[Unified Conversation Design#Locked M2 model and language (2026-10-16)]]; earlier English-only interpretation corrected, no runtime change |

## Still open

2026-10-19 conversation follow-up: [[Scenario Conversation Design]] consolidates
the following approved target decisions (implementation pending):

- **D-37:** Topic is semantic catalogue meaning; Scenario owns interaction flow;
  dialogue sequences contain VN beats; game-owned Activities execute approved
  action requests. Generated replies do not automatically become catalogue entries.
- **D-38:** One running scenario; indefinite while app runs; requested diversions
  ask pause/end; compatible self-care is an activity; return prompts require
  explicit resume; quit ends occurrences without falsely completing them.
- **D-39:** Parallel Appetizer/main preparation when online generation is needed;
  Appetizer1–4 beats (normally1–2); five-second first-visible target with local
  fallback; main prepares outline plus next segment; no SSE/TTS dependency.
- **D-40:** Ask Player and bound answer options continue the current interaction;
  Suggested Topics are for a later explicit E; Dessert closes the current exchange
  by default, not necessarily the scenario. This amends D-35's target placement;
  old `suggestedReplies` field behavior must migrate explicitly, not be reinterpreted.

The new note records a fresh builder check and explicit approval of the default
bounds on2026-10-19. Dessert is simply main-response closing, not another stage.
[[Scenario Conversation Implementation Plan]] is written for review before
execution; none of these approvals are runtime evidence.

Semantic thresholds and local-anchor scoring; sentiment model/languages/calibration; Agent turn budgets and sidecar protocol/packaging/security; needs balancing and time-away rules; TTS fallback policy and release cache storage; cross-platform key storage; interaction retention/deletion; cosmetic economy details. Initial orchestration stack and OpenRouter generation provider are locked in D-30.

## Follow-up, not yet done

Game-design and Obsidian-memory skills were researched after the initial
documentation pass. The user declined additional packages as unnecessary for
now; no skills were installed. Strengthen the existing repository-root
`AGENTS.md` and linked documentation instead. Revisit extra tooling only for a
demonstrated gap, not to replace these sources of truth.

## Decision changes

Keep the decision ID stable. Record date, reason, replacement decision, impacted features/content/tools, and evidence. Mark superseded decisions instead of leaving contradictory “current” designs.
