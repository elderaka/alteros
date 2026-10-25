---
title: Unified Conversation Design
tags: [alteros, design, conversation, rag]
updated: 2026-10-20
status: approved-direction
---
# Unified Conversation Design

Related: [[Architecture]], [[Live Conversation Journal]], [[Design Decisions]],
[[Unified Conversation Implementation Plan]], [[Topic Scenario Selection]].

## Approval and supersession

2026-10-19 follow-up: [[Scenario Conversation Design]] owns the approved
Topic/Scenario/Activity distinction, one-running-scenario lifecycle and Appetizer/
main/Dessert target. The generated path is to have parallel head/main stages,
with first visible text or a local opening within five seconds; this does not
add calls to an eligible authored shortcut. Ask Player/answer options and next-E
Suggested Topics are different contracts. Current `suggestedReplies` and one-
request M2 behavior below remain implementation history until versioned migration.
The default bounds were approved2026-10-19; Dessert is closing within the main
response, not a separate stage. [[Scenario Conversation Implementation Plan]]
awaits review; no execution assignment or action/tool authority follows merely
from this design update.

The user approved this direction in conversation on 2026-10-16 and requested an
implementation plan plus a Luna handoff. This is target behavior, not runtime
proof. It replaces D-01's player-facing Topic/Agent switch and D-02's claim that
every conversational routing decision is local. Explicit E, non-modal ambience,
local prepared-content fallback, focus protection and game authority remain.
Older mode-switch paragraphs are historical where they conflict with this note.

## Product behavior

- One companion, one continuing conversation history, two channels: `direct`
  and `phone`. Channel is not a different identity or a Topic/Agent mode.
- Retrieve relevant Topic candidates; online LLM judgment interprets those
  candidates with recent conversation. Embeddings find candidates, not truth.
- Online routing has a separate confidence gate: a ready authored candidate
  at cosine similarity **>= 0.850** plays locally and skips the provider call.
  Below that score, online RAG receives the ranked top-eight candidate cards,
  including weak neighbors, with their semantic scores and host-computed
  availability. This lets the model compare a new request with canonical
  Topics; retrieval context alone does not authorize authored playback. If the
  provider is unavailable, local authored fallback remains confidence-gated to
  the >=0.850 candidates or the host-bound active Topic. Offline mode keeps its
  existing local threshold. The 0.850 value is an initial calibration, not a
  quality claim.
- Recognize continuation, an existing Topic, ambiguity, or a provisional novel
  subject. No-match alone is not novelty. Brief replies such as "yes" are not
  automatically gibberish. Quoted profanity is not automatically abuse.
- Use eligible authored dialogue first. Within the highest eligible priority
  tier, prefer unseen scenarios, then apply the existing relative weights.
  If that tier has no unseen scenario, repeatable authored content remains
  usable; do not generate merely because something was played previously.
- Generate a validated conversational occurrence when prepared content does
  not cover the request. Continuation of an active exchange must not restart
  an unrelated authored introduction merely because the subject matches.
- Generated occurrences are not automatically reusable/discoverable catalogue
  entries. Publication, deduplication and indexing are a separate later pipeline.
- A generated occurrence normally is once-only. Its canonical Topic category
  is not consumed. Authored repeatability and whole-instance once consumption
  retain their existing meaning, including explicit delivered checkpoints.
- Record completed experience separately from consumption: per-instance,
  per-scenario completion count and last-completed UTC. Selection, generation,
  the first node, partial playback and history inspection do not increment it.
- Authored Topic JSON keeps its canonical `nodes` array. A generated Live v2
  judgment uses `reply.beats`; each beat maps to a canonical VN node for the
  shared runner. Both render as meaningful short beats with individual
  supported expressions. Do not wrap a Live reply in invented anchors just to
  satisfy a routable Topic schema.
- Optional suggestions are `{label, prompt}` pairs, not graph choices or tool
  calls. Both fields use the current display language (English), and the prompt
  must faithfully represent the visible label and context. The host omits
  suggestions containing non-Latin letters or supplementary characters rather
  than dropping an otherwise valid reply. Unchosen suggestions are not player
  utterances or memory evidence. This host guard is script-level, not a full
  language detector; additional UI locales need explicit language metadata.

## Phone and presence

Phone supports real conversation, planning and arranging activities. Direct
interaction is valuable through physical presence, voice, expressions and
actually participating together, not a deliberately less intelligent phone.
No blanket relationship/stat discount for phone conversation is approved.

Scenario channel metadata supports direct-only, phone-only or either.
Missing/empty metadata means either for backward compatibility. Phone dialogue
must not claim physical co-presence. Discussing tea by phone must not complete
a different direct-only tea-sharing scenario or simulate its activity benefits.
Equal knowledge does not mean equal physical capabilities. Activity execution
is a future game-owned capability, not supplied by this conversation slice.

## Authority and consent

The LLM can choose only a supplied candidate ID (or the host's active Topic) or report continuation,
clarification or a provisional new subject. The host rechecks current gates,
channel, positive weights, cooldown and consumption before authored playback.
An existing but blocked Topic is not an unknown subject and cannot be relabelled
novel to publish a replacement. Continuation of the host-bound active response
remains conversational continuity, not authored replay; the conversation owner
validates response identity/expiry, and no Topic intro or activity effect is replayed.
Model-declared referenced Topic IDs and the host-bound active Topic
are rechecked for generation too. A blocked unrelated retrieved card does not
block the entire conversation. Schema validation cannot prove that a model has
not semantically disguised a gated subject as novelty; generated output has no
mechanical effects or publication authority, and semantic bypass resistance
requires later evaluation. Deny unknown fields/IDs rather than loading arbitrary paths.
Neither source permits arbitrary stat deltas, currency, animations or tools.

Saving a BYOK key is not consent to start automatic billable requests. Preserve
an explicit online-generation setting/consent when the live milestone is built;
it is not a player-facing conversation-mode switch. Missing key, disabled online
use, offline state or provider failure must leave prepared dialogue usable.

Use a complete bounded reply, no SSE. The harness validates before presentation;
text must not depend on TTS success. A saved TTS switch defaults off for testing
and suppresses synthesis/prefetch, not only playback. Phone/history are silent
by default. Keep the existing credential store and TTS cache boundary.

Walking away suspends presentation, not a successful reply. Store reply identity
and node progress once; a later explicit E can acknowledge and resume eligible
unfinished content. It must not rerun generation/actions or duplicate history.
Explicit cancellation, supersession and obsolete context must be distinguished.
Full history is not the same as the bounded context sent to the model.

## Retrieval and stack

Python sidecar + LangGraph + Pydantic, with OpenRouter for LLM/TTS, remain the
approved stack. Unity retains provider-neutral contracts and game authority.
Do not duplicate GTE in Python. Local GTE is the baseline/offline retriever;
hosted embeddings are an optional measured experiment, not a prerequisite.
Different embedding spaces require separate indexes/bakes, even at the same
dimension. No hosted model or guaranteed latency has been selected.

Start with retrieve-then-judge, not an unbounded agentic search loop. Retrieve
compact candidate cards, not all dialogue graphs or the whole catalogue. A
supported online response may select an authored scene without generating prose;
a generated response may combine judgment and writing in one bounded call.
RAG cannot guarantee factual truth, personality consistency or perfect novelty.

### Locked M2 model and language (2026-10-16)

The user selected **DeepSeek V4.1 Flash through OpenRouter** as the initial M2
conversation model: `deepseek/deepseek-v4.1-flash`. Use this explicit versioned
ID rather than a floating latest alias. Keep model selection in provider
configuration, not embedded in gameplay/dialogue code. No model change, paid
request or automatic generation enablement is implied by this design lock.

The initial interaction/display policy is **English-first**, with **Japanese
speech text required alongside English display text for each generated Alterra
beat**, preserving the authored Topic's bilingual text policy. The user clarified this on
2026-10-16; the earlier English-only-output interpretation was incorrect.

- `text`: Japanese utterance, consumed by Japanese TTS.
- `displayText`: the corresponding English subtitle/message, shown to the player.
- `language`: `japanese`, the speech language, not the display/input language.
- Legacy playback maps `displayText` to `translatedText`; it displays that
  translation and sends `text`, not the translation, to synthesis.
- Optional suggested player replies remain English-first label/prompt pairs;
  they are not spoken Alterra beats.

Generate the aligned Japanese/English pair in the same response, including
when TTS is disabled. Disabling speech suppresses synthesis, not the bilingual
contract. M2 validation must require both nonblank fields, bound both texts and
total response size, and test this exact display/speech mapping. Language tags
and schema validation alone do not prove translation fidelity; assess meaning,
names, amounts and character voice across both versions. Initial testing must
cover Japanese speech quality as well as English conversation quality.

English-first is not an English-only input restriction or a claim of broad
multilingual support. Existing authored dialogue languages/assets are not
rewritten by this decision. TTS voice/model selection remains separate from
the text conversation model. Phone can display English while remaining silent
by default; its generated beats retain the same paired-text contract.

Roleplay quality, provider-specific schema compatibility, complete-response
latency and cost remain to measure. Other apps' adoption motivated the choice;
it does not establish Altera-specific quality or replace harness validation.

## Delivery milestones

### M1: deterministic contract and headless harness

Implement the plain-C# routing/selection foundation and synthetic scenarios.
No actual model, sidecar, provider calls, scenes or phone UI. Inject synthetic
judgments so policy, blocked paths, channels and lifecycle are testable now.
Use existing Topic conditions/history/selection rather than a second engine.
The accompanying plan specifies exact M1 interfaces and tests.
M1 recognizes `existing_uncovered` but returns a distinct unavailable reason;
it must not pretend that this known subject is novel. Actual generation for a
known subject without adequate authored coverage, including the host-owned
coverage distinction from blocked content, is part of M2's reviewed design.

### M2: real direct-channel vertical slice

Detailed handoff: [[Unified Conversation M2 Implementation Plan]]. Its explicit
transport/budget/persistence defaults require user review before execution;
model and bilingual product policy are already locked.

Plan separately after M1: Python protocol parity/Pydantic, LangGraph orchestration,
OpenRouter adapter and model configuration, secure process transport and hidden
helper supervision, bounded recent context, persistent history, explicit online
consent, actual direct UI wiring and offline degradation. Do not declare a
runtime feature from mocked judgment tests. Preserve GTE startup behavior unless
a separately measured change warrants altering it. No tools initially.

The initial model/language are locked above. Transport choice, response budgets,
retry/repair and packaging must be specified and reviewed before M2 code. Raw credentials must not enter
process arguments, logs, history or graph checkpoints. Existing history trims
past 120 turns; do not mistake that for approved lifetime transcript storage.

### M3: phone, delivery and speech acceptance

Plan separately: authored Canvas phone UI, shared persistent history and channel
switching, direct pink accent without an "In person" label, return-to-E playback
progress, saved TTS toggle/cache/failure handling. Prove no duplicate dispatch,
speech or completion across both channels. Test real startup via SplashScene.
Physical actions, memory retrieval, background publication, sentiment and activity
simulation remain separate slices, not prerequisites hidden inside this one.

## Planner defaults, not new product promises

For M1, use top eight candidate cards, ordinal ID tie-breaking, and the existing
local semantic threshold for offline confidence. The online authored bypass
uses the separate 0.850 gate above. A conversation request allows
1..2048 characters; the generated reply allows 1..32 beats, 1..600 characters
per beat and at most 12000 total text characters. Suggestions allow 0..3 pairs,
label 1..80 and prompt 1..512 characters. These are safety/test defaults, not a
claim about tokens, provider limits or ideal conversation length. Reject an
over-limit artifact without silently truncating it. Online budgets need their
own measured M2 design. The executor may change these defaults only with a
documented reason and matching contract fixtures, not an incidental code tweak.

## Evidence and unresolved work

This note is documentation-only. Production still uses legacy routing and has
no Live model path. Staged catalogue and Playground foundations exist but do
not prove a gameplay integration. Advanced memory, catalogue summaries, local
anchor specificity, model/language evaluation and first real tool vocabulary
remain unresolved. Private player content never belongs in these shared notes.
