# AlterOS Architecture

> This document describes the target product boundaries. For the facts that
> are actually wired in the Unity checkout, read
> [UnityProjectContext.md](UnityProjectContext.md) first. Last implementation
> reconciliation: 2026-10-22 (source/handoffs, not new runtime acceptance).

## Product definition

Authored-content ownership approval 2026-10-22: SQLite is the authority for shared
dialogue/routing drafts and published revisions; JSON is explicit import/export only.
Workbench/agent CLI share typed edits, validation and snapshot-based publication.
Workbench commands and the Editor build entry use the SQL authoring store; the
439-source migration, deterministic export and headless checks pass. On 2026-10-22,
the installed release passed read-only SQLite integrity, foreign-key and trusted-release
inspection, and its manifest snapshot matched authoring revision 63. This does not
prove startup or player acceptance. The approved human/agent authoring direction is
a Flow-first graph with a selected-step writing inspector and shared typed commands.
The W1 command backend is present; graphical W2-W3, preview/parity W4 and acceptance
W5 remain open in the existing Workbench plan. See
[[Tracking/Content Workbench Shared Contract]],
[[Tracking/Content Workbench Implementation Plan]],
[[Tracking/SQLite Authoring Cutover Implementation Plan]],
[[Verification/2026-10-22 SQLite Authoring Core]] and
[[Verification/2026-10-22 Full SQLite Cutover]].
Private memories/conversations, game state and secure keys retain their owners;
this is not authority to merge stores, delete originals or auto-publish drafts.

Data ownership follow-up2026-10-21: [[Tracking/Data Management Design]] owns
approved continuous persistence, one persistent Alterra/account, optional local-first
account linking, privacy/history separation and desktop-authoritative mobile
companion rules. The desktop SQLite foundation and bounded runtime cutover are
implemented in source; their journal and separate storage owners are described
below. The authored release is present and passed read-only artifact inspection;
corrected Editor tests, startup/player acceptance and real-profile migration remain
open. See
[[Tracking/Data Management Handoff]].
Accounts, mobile synchronization and hosted billing are future service work, not
implied implementations or permissions granted by local game state.

Conversation target follow-up2026-10-19: [[Tracking/Scenario Conversation Design]]
consolidates Topic as semantic index, Scenario as interaction flow, game-owned
Activities, parallel Appetizer/main preparation with a five-second first-visible
fallback target, Ask Player versus next-interaction Topic offers, and Dessert/exchange
closure. Approved conceptual rules supersede older conflicting target language;
the default bounds were approved2026-10-19. Dessert is closing within the main
response, not a separate stage. The v4 Scenario coordinator and bounded M3
activity bridge exist in source; live timing/action acceptance remains unverified.

For linked feature/content/state/tool tracking and the decision index, open
[the Obsidian-ready design home](Home.md). Tracking notes distinguish approved
design from implementation, wiring, and runtime proof; they do not configure
game behavior.

AlterOS is an interactive companion game where the player lives, talks, studies, and plays with Alterra through natural-language interaction.

Its defining feature is adaptation: the immediate experience stays fast and authored, while a background process learns useful things about the player and prepares personalized dialogue for future interactions.

The current Unity client is the self-contained AlterOS companion surface. It
owns the player, character, dialogue, presentation, input, and TTS surfaces
behind provider-neutral boundaries.

## Current implementation snapshot

The normal startup route is `SplashScene -> ModelTest`. GTE routing and authored
free-text playback have source/scene wiring, and the current authored release is
installed at `Assets/StreamingAssets/Content/content.sqlite3`. Its read-only integrity
and trusted-release inspection passed and its manifest matches authoring revision 63.
Older playback evidence does not establish a currently runnable whole path; startup
and player acceptance remain open. TTS uses the OpenRouter adapter
and local cache path. Session persistence and generated-dialogue archive metadata
exist, but the background LLM
adaptation worker and personalized semantic overlay are not wired yet.

Full SQLite cutover implementation follow-up 2026-10-22: game progression, home
layouts and Topic history share `<persistentDataPath>/Data/alteros-game.sqlite3`;
sessions, repetition, ambient state and transcript use a separate private database;
generated archive metadata/payloads use another private database. A trusted startup
journal reconciles per-domain receipts while Python memory and DPAPI credentials keep
their existing owners. Only bounded host-approved effects from validated routes in a
pinned SQL content release can change progression; public/provider and legacy sequence
starts remain untrusted, and dialogue cannot mint credits. The installed 439-source
release matches the current authoring snapshot; read-only artifact inspection is not
Unity startup/player or real-profile acceptance. Details and evidence:
[[Tracking/Full SQLite Cutover Implementation Plan]],
[[Tracking/Content Data Integration Handoff]].

Authored topic suggestions are owned by `TopicOptionCatalog`; the duplicate
injection path in `AlteraInteractable` has been removed. Generated records are
not surfaced as choices until their playback path exists.

The codebase is transitional: provider-neutral code lives under
`Assets/AlterOS/`, while scene-wired dialogue and presentation bridges remain
under `Assets/Scripts/` and `Assets/Altera/`. The target architecture below is
the direction for new work, not a claim that every boundary is already split.

## Core rule

The current production authored path is local and does not wait for an LLM.
The approved unified-conversation target may use a bounded online LLM judgment
after retrieving Topic candidates; its offline path stays useful without a key.
Background adaptation never blocks foreground conversation. See
[[Tracking/Unified Conversation Design]] for the current target. M2/M2.1 and v4
conversation are source-integrated behind consent; loaders consume pinned SQL
content. Artifact installation and runtime acceptance are separate gates.

Foreground request/response/presentation events use the sole coordinator and SQL
transcript facade when storage is ready. Optional research contribution recording
has separate consent; ordinary chat persistence does not enable it. Background
adaptation may batch or ignore trivial events: “always learning” does not mean
a paid call or model training after every line.

## Approved model roles

Responsibilities approved on 2026-10-14 and retained for the unified target.
They do not imply three models running continuously on the player's GPU.

| Role | Responsibility | Execution |
| --- | --- | --- |
| Embedding | Semantic evidence for Topic routing and indexing validated personalized trigger examples | Local pinned GTE; foreground routing takes priority over background indexing |
| Sentiment analysis | Uncertain, short-lived text sentiment evidence for response style and interruption suppression | Intended local classifier on submitted text; implementation not selected |
| LLM | Optional online Topic judgment and VN replies; later permitted in-game tools, memory proposals, and background generation | BYOK on demand with explicit online consent; background work yields to foreground conversation |

TTS remains a separate speech-generation service. It may use another hosted or
local model, but does not own routing, sentiment, permissions, or game state.

The sentiment implementation may use an evaluated existing classifier, a
custom-trained classifier, or the BYOK LLM as a judge. If the LLM performs that
job, sentiment and generation share a physical model/provider while remaining
separate responsibilities. No sentiment model, training pipeline, or new
provider has been selected or installed by approving this architecture.

## Conversation entry and mode selection (historical baseline, superseded 2026-10-16)

The switch described in this section is no longer the product target. D-32 and
[[Tracking/Unified Conversation Design]] replace it with one Topic-centred
conversation, authored-first retrieval and optional online judgment. The
implementation snapshot below records the earlier audit, not current source.
Do not reintroduce the switch or implement a second planner from this section.

There are three different kinds of Alterra speech, and they should not share
one ambiguous router:

| Entry | Player intent | Mode / owner |
| --- | --- | --- |
| `E` while looking at Alterra | Start an intentional conversation | Opens the authored greeting flow |
| Free text inside the conversation | Choose what to talk about | Explicit `Topic` mode; GTE routes to ready authored dialogue |
| Idle/proximity ambience | Alterra speaks without requesting a reply | Ambient bubble only; it never opens Agent mode |

The conversation panel should expose one explicit Topic/Agent switch. Topic is
the default. The submitted text is dispatched according to the selected mode;
the system must not infer “this sounds like an agent request” from casual text.

The current code keeps that decision in `ConversationModePolicy` and
`DialogueManager.TrySetConversationMode`. Topic is selectable now; Agent is a
deliberate unavailable state until the provider-neutral harness is implemented.
The scene UI is not wired to this seam yet.

- `Topic` uses the current local semantic route and authored fallback.
- `Agent` is the future BYOK workflow seam and may be unavailable until the
  harness exists.
- If Agent mode is unavailable, the UI reports that state and preserves the
  Topic path; it must not silently change the meaning of the input.

The current `ChatButton`/`AssistantButton` objects are not a functioning mode
switch: `AlterOSHudShell.navButtons` is empty in `ModelTest`, and the two scene
buttons are serialized with the same Assistant tab value. They should be
replaced or rewired through the Unity Editor as part of the flow cleanup, not
treated as an existing routing mechanism.

```text
                         SYNCHRONOUS

 Player free text + current user context
                    |
                    v
     Local semantic router (embedding or zero-shot)
                    |
          +---------+---------+
          |                   |
          v                   v
 Generic dialogue     Ready personalized dialogue
          |                   |
          +---------+---------+
                    |
                    v
       Dialogue runner + game features
                    |
                    v
          Cached/available speech


                         ASYNCHRONOUS

              Recorded interactions
                    |
                    v
          Background adaptation job
                    |
        +-----------+-----------+
        |                       |
        v                       v
 Update user profile     Generate candidate branches
                                |
                                v
                    Validate dialogue and safety
                                |
                       +--------+--------+
                       |                 |
                       v                 v
                 Generate TTS      Validate manifest
                       |                 |
                       |       Queue GTE indexing
                       |        during low activity
                       +--------+--------+
                                |
                                v
                 Publish as ready personal content
                                |
                                v
                Eligible for a future sync route
```

## Stats and initiative

Design decisions agreed on 2026-10-14; harness integration is not implemented.

Alterra's physical and social needs influence dialogue eligibility, variant
selection, and Agent expression. Game-wide state retains explicit ownership:
companion needs, relationship progression, player activities/cosmetic currency,
and environment state. Semantic evidence still determines what the player
means; a low need must not replace their requested subject with an unrelated
topic.

All stat, relationship, and economy changes belong to validated game events.
The Agent may request a permitted activity, but cannot supply arbitrary stat
deltas or mint currency. Generated dialogue must obey the same boundary.
Activity completion effects apply once per activity instance. A player's
reported water check-in is separate from Alterra completing her own drink.

Alterra can maintain physical needs through ordinary game logic. Solo activities
can supply some social recovery, while shared activities supply more. Repeated
low needs must not automatically start paid Agent turns.

The player selects an initiative ceiling. Stats affect which permitted activity
Alterra prefers; relationship progression changes the familiarity of her
invitations without increasing her permissions.

| Level | Permitted initiative |
| --- | --- |
| 0 - Reactive | Quiet self-care; shared activities begin at the player's request |
| 1 - Suggestive | Occasionally offers a shared activity and awaits acceptance |
| 2 - Proactive | Begins permitted self-care and offers shared activities at suitable moments |
| 3 - Routine-driven | Follows routines explicitly enabled by the player |

Higher initiative means more purposeful activity, not a requirement for more
chatter. An authored interlude temporarily owns presentation while preserving
the selected Agent mode and conversation context, then returns control to that
conversation.

### Interruption handling

Player attention should be checked independently of companion needs and the
initiative ceiling. Explicit focus/quiet settings and active task/tool execution
block unsolicited conversation takeover, camera transitions, modal choices, and
need-related TTS. Waiting for a provider response or pausing between messages
does not establish that the player is available for an interruption.

During protected work, Alterra performs permitted self-care quietly. A shared
activity opportunity may be deferred, coalesced to one pending offer, or
discarded. Need severity and elapsed waiting time never override the attention
gate. At an eligible boundary, offer the activity and let the player accept or
defer it; routine-specific preauthorization must remain subordinate to quiet
settings. Recheck current needs, cooldowns, and context before delivery, and do
not drain a backlog of prompts when focus ends.

Declining an offer, suppressing reminders, or being away should not produce
relationship penalties. Shared attention/stimulation recovery can be deferred
without escalating guilt-oriented invitations. Pending interludes and speech
must not block completion of the player's Agent task.

The current focus-session UI has its own private timer state; it does not yet
publish a shared activity/interruptibility signal. The above policy needs a
runtime owner and explicit lifecycle events when implemented.

### Session sentiment analysis

Sentiment analysis is an approved supplementary capability for adaptive
interruption timing and response style, with a disclosed setting to disable
the inference. Its scores remain
short-lived session signals with source, confidence, and freshness, rather than
durable emotion labels or player/relationship stats. Explicit player commands
and focus settings take precedence; low-confidence or missing estimates abstain.

A negative-text estimate may reduce optional chatter, but a positive estimate
must not unlock interruptions, alter permissions, or bypass activity acceptance.
Basic sentiment polarity is not an emotion or flow-state detector: for example,
the Cardiff multilingual sentiment classifier labels text negative, neutral, or
positive ([model configuration](https://huggingface.co/cardiffnlp/twitter-xlm-roberta-base-sentiment-multilingual/blob/main/config.json)).
Sarcastic text can express different underlying emotions
([research example](https://aclanthology.org/2022.lrec-1.756/)).

Before choosing a model, evaluate task-directed complaints, explicit quiet
requests, joking/profanity, sarcasm, quotations, and supported languages/slang.
Measure unwanted interruptions and unnecessary suppression of helpful offers,
not just sentiment-label accuracy. The deterministic attention gate should work
even when this optional classifier is disabled or unavailable.

## One dialogue format, two content sources

Generic and personalized dialogue should use the same runtime schema and dialogue runner.

### Generic dialogue

- authored and reviewed for every player
- bundled with the game or delivered as versioned content
- canonical topic anchors and embeddings prepared ahead of time
- speech preferably pregenerated or cached
- never requires an LLM during interaction

### Personalized dialogue

- generated from approved user context by a background LLM workflow
- stored only for that user
- validated before becoming routable
- indexed and supplied with TTS before being marked ready when required
- never inserted directly into the conversation while still generating

The router should not care how a ready dialogue was created. It receives eligible dialogue records and selects among them.

## Dialogue and semantic ownership

A dialogue branch is a response artifact. It owns its text, choices, presentation cues, eligibility rules, and speech metadata; it does not own the universal semantic vocabulary.

The target content split is:

| Contract | Owns | Typical mutability |
| --- | --- | --- |
| `IntentCatalog` | Predetermined, reusable interaction intents such as greeting, ask, request, self-report, or farewell | Authored and versioned |
| `ToneCatalog` | Predetermined delivery signals such as warm, playful, serious, distressed, or urgent | Authored and versioned |
| `TopicCatalog` | Reusable concepts such as `state.tired`, `beverage.coffee`, `activity.study`, or `time.night`, plus their semantic anchor banks | Expanded through reviewed catalog changes |
| Dialogue definition | One generic or personalized response branch and its topic/intent/tone references | Authored or written by a validated background job |
| Local anchors | Branch-specific facts or constraints such as a clock window, named item, user preference, or current activity | Owned by the branch or encounter |

The approved starter entries are maintained as individual review documents in
[Documentation/Semantics/](Semantics/README.md). The folder is the design
surface for the catalogue; runtime vector assets remain derived build output.

Every branch must bind to at least one canonical topic anchor, even when the topic has its own semantic anchors. A topic may have no fuzzy semantic bank when a deterministic local anchor is sufficient; it must still have a stable `topicId` so the branch remains inspectable and reusable.

For example, `state.tired` is reusable across many branches. `beverage.coffee` should remain a separate topic so it can combine with tiredness, studying, gifting, or preference dialogue rather than being nested under one of them. A personalized tired response references those same IDs instead of creating a second tired topic.

The semantic model does not select a final line. It produces evidence for the existing catalog. Context gates, relationship state, cooldowns, repetition rules, and authored priorities decide which eligible dialogue variant consumes that evidence.

## User-aware semantic routing

The semantic model answers one narrow question:

> Which available dialogue best matches what the player just said?

An embedding model and a zero-shot classifier are alternative implementations of this responsibility. The first implementation should be chosen by benchmarking both against the same labeled player-phrase set rather than committing to both.

Routing uses two kinds of information:

1. **Semantic match** — similarity between player text and reviewed Intent, Tone, and Topic anchors.
2. **Structured context** — eligibility or reranking based on relationship stage, known preferences, recent interactions, game state, cooldowns, and repetition.

Do not embed an entire save file or conversation history into every query. Semantic matching finds candidates; explicit context rules decide whether those candidates are appropriate for this player now.

### Semantic capabilities

AlterOS initially has two universal semantic matrices:

- **Intent** answers what kind of interaction the player is attempting. The approved starter catalogue has `greet`, `farewell`, `presence`, `ask`, `request`, `share`, `relationship`, `repair`, `boundary`, and `fallback`; these groups may later split when their response or state contracts differ.
- **Tone** describes how the player is presenting the line. Tone is evidence, not a diagnosis of sincerity or a replacement for user-state facts. A generated branch separately declares its intended response tone for Alterra; the tone of Alterra's prose must not be used to infer the player's trigger intent.

Topic resolution is a catalogue/index capability rather than a third matrix. It resolves reusable topic IDs and branch-local anchors after the universal matrices have produced their evidence. Dialogue selection remains a separate authored capability that consumes intent, tone, topics, local anchors, and explicit context.

The runtime boundaries are therefore:

```text
AnalyzeInput(text)
    -> ranked Intent evidence + ranked Tone evidence
ResolveTopics(evidence, context)
    -> universal Topic candidates + matching local anchors
SelectDialogue(candidates, context)
    -> one ready generic or personalized branch
IndexGeneratedDialogue(manifest)
    -> asynchronous embedding/index update
```

The first two matrices may return more than one useful result. A primary result and a bounded set of secondary evidence preserves mixed inputs such as "I'm exhausted but I need coffee" without forcing the entire line into one label.

### Two-pass routing and anchor precedence

The synchronous route uses one input embedding and two semantic passes:

1. **Universal pass:** score Intent and Tone against their reviewed anchor banks and retain confidence, support, and margin evidence.
2. **Topic pass:** shortlist topics compatible with the intent/context, require each candidate branch's topic anchor, then evaluate its local anchors and authored priority.

Local anchors are more specific than broad semantic resemblance. Hard matches for named entities, time windows, known user facts, or current activities act as gates or priority evidence; they should not be reduced to an arbitrary cosine bonus.

For example, an input such as "It is late, I am exhausted, and I need coffee" can produce:

```text
Intent: request + self-report
Tone: tired / urgent
Topics: state.tired + beverage.coffee
Local anchor: time.night
```

A generic tired-at-night sleep branch and a tired-at-night coffee branch may both be eligible. The coffee branch wins when its required `beverage.coffee` local/topic anchor matches, because it is the more specific authored contract. If no topic anchor matches, the router falls back to a broader intent response rather than guessing.

Every routing result must be inspectable during development:

- selected dialogue ID and source
- best and runner-up scores
- confidence or margin
- context filters and boosts applied
- fallback reason when nothing was selected

## Adaptation and “learning”

Learning has three concrete outputs:

1. A compact user profile containing useful preferences, facts, boundaries, and relationship state.
2. Memory summaries or interaction signals that can influence future routing.
3. New personalized dialogue branches prepared for future use.

It does **not** initially mean fine-tuning the embedding model, training a personal model, or allowing generated text to rewrite core game logic.

Every interaction appends a small event. A background worker periodically examines new events, updates the profile, and decides whether a new branch is useful. Repeated or trivial events can be collapsed into one adaptation job.

Generated content moves through a minimal lifecycle:

```text
candidate -> validated -> pending_index -> ready
          \-> rejected
```

Only `ready` content enters the routing index. A branch that reuses existing canonical topics and adds no new trigger aliases may skip `pending_index`; it still needs the same validation and publication gate.

### Catalog growth policy

Catalog growth is driven by new reusable decisions, not by the number of dialogue files. The promotion rules are:

| Evidence | Change |
| --- | --- |
| Same meaning expressed by new slang, language, or paraphrase | Add a reviewed anchor to the existing topic or matrix row |
| New personalized response with existing semantic references | Add a dialogue variant only |
| A local concept is reused across independent features or branches | Promote it to a universal `TopicCatalog` entry |
| The branch has a different response contract, state change, safety policy, or lifecycle | Add or split an `IntentCatalog` row |
| One-off encounter detail | Keep it as a local anchor |

LLM output may suggest an existing ID, a new alias, or an `unknown_topic`, but it cannot silently create or promote universal taxonomy. A candidate promotion is reviewed, added to the same-model evaluation set, checked for regressions and ambiguity, and only then included in a versioned universal bake. Personalized aliases stay in a user overlay unless they prove broadly reusable.

## Unified conversation direction (2026-10-16)

Player-memory follow-up (2026-10-18): [[Tracking/Player Memory Design]] and
[[Tracking/Player Memory Implementation Plan]] define the authorized additive
M2.1 trial: SQLite-derived memory beside the source transcript, short-term
freshness and long-term recall, source/consent-governed writes and host-selected
questions. This supersedes memory being wholly deferred from M2, not its pending
runtime acceptance.

Implementation follow-up (2026-10-19): the source now contains the local SQLite
repository, keyless v3 memory protocol and memory-aware conversation turn path,
plus source/suppression revalidation. These do not change the ownership rules
above: raw player chat remains unclassified until scope is established, and the
model cannot write authoritative state. Authored onboarding data/session logic
exists but is not wired to first-E or Canvas settings. See
[[Tracking/Player Memory Handoff]] and
[[Verification/2026-10-19 Player Memory M2.1 Implementation]] for exact proof
and the missing runtime/UI gates.

The approved [[Tracking/Unified Conversation Design]] supersedes earlier
player-selected Topic/Agent switching and local-only conversational judgment.
Retrieve candidate Topics, use optional online judgment, select eligible authored
dialogue first, and generate validated transient occurrences when appropriate.
Direct and phone share identity/history but differ in physical participation.
Completion evidence is separate from replay permission/whole-instance consumption.
Local prepared dialogue remains available without a sidecar or online consent.
The production loader is not switched by this approval. See
[[Tracking/Unified Conversation Implementation Plan]] for the bounded M1
headless foundation; real sidecar/phone/TTS integration follows separately.
Where older paragraphs below describe an explicit mode switch or automatic
return to Topic mode, treat that selection policy as superseded, not current.

## Planned BYOK harness and workflows

The approved harness has two execution paths: a bounded live Agent loop and a
fixed background adaptation workflow. Neither is implemented in the Unity
client. The archive records are preparation surfaces only.

### Locked agent-side stack (2026-10-16)

- Python sidecar, LangGraph for orchestration, Pydantic for boundary data
  validation. PydanticAI is not selected as an additional agent framework.
- Ship as one installed product: Unity launches/manages a hidden local helper,
  with no separate terminal/taskbar window or player-installed Python required.
  It remains a separate process visible in Task Manager. Packaging, secure local
  transport and process supervision are not implemented by this decision.
- Initial LLM and TTS generation use OpenRouter. Local GTE remains a separate
  bounded inference responsibility; do not duplicate its loaded model in Python
  merely because a sidecar exists. Sentiment model/runtime remains undecided.
- M2's initial text conversation model is `deepseek/deepseek-v4.1-flash`,
  English-first input/display with Japanese speech text per beat (`text`
  Japanese, `displayText` English, `language` japanese), as clarified by the
  user on 2026-10-16. Keep model selection in provider
  configuration; it is not a TTS-model choice or a runtime integration claim.
  See [[Tracking/Unified Conversation Design#Locked M2 model and language (2026-10-16)]].
- Unity uses an AlterOS-defined versioned protocol, not LangGraph's internal
  state/messages. It owns presentation, authoritative game state, permission
  checks and effect application. Local Topic playback must survive sidecar loss.
- Pydantic validates sidecar request/reply, model output, tool arguments/results
  and generated-content structure. Use explicit strictness, extra-field rejection,
  bounds and semantic validators where needed; validation does not prove factual
  truth, character consistency, player consent or permission. Unity independently
  validates the boundary and any requested game effect against current state.
- Keep internal graph state lightweight where appropriate rather than repeatedly
  validating full lifetime histories. Public contract fixtures must agree across
  Python and C#; neither runtime may silently reinterpret unsupported fields.
- BYOK Session0 is complete as a scoped implementation; consume its shared
  credential boundary, do not redo it. Do not persist provider credentials into
  graph checkpoints, message history, diagnostic output or serialized job state.
  Credential transfer/use across the local process boundary is still to design.

Performance gates belong to the first sidecar slice: cold/warm readiness, idle
CPU/RAM, frame-time spikes, median/tail reply latency, memory/checkpoint growth,
background-load contention and recovery without duplicate side effects. Earlier
GTX1650 baseline and proposed60FPS/~16.7ms frame target guide measurements, not
a measured performance guarantee. Remote generation avoids local LLM/TTS model
compute, not network latency, serialization, storage or resource contention.
Text remains independent of TTS completion. Keep foreground work prioritized,
background concurrency and per-turn/tool budgets bounded. No stack benchmark,
package install or helper implementation was performed by this design lock.

References: [LangGraph state schemas](https://docs.langchain.com/oss/python/langgraph/graph-api),
[Pydantic models](https://docs.pydantic.dev/latest/concepts/models/),
[Pydantic strict mode](https://docs.pydantic.dev/latest/concepts/strict_mode/).

### Unified conversation delivery and later tool harness

Approved presentation direction (2026-10-16): one continuing conversation uses
the same authored-first/RAG-assisted Topic system through face-to-face and phone
channels. Channel affects delivery style, not identity or continuity. Phone history includes
face-to-face entries with pink tint/left-edge accent, without an "In person"
label. Generated replies use complete request/response, not SSE. The immediate
implementation sequence is M1 headless rules, M2 real direct replies and M3
phone/history plus TTS acceptance; none is inferred from design approval.
The saved testing-slice TTS toggle defaults off and suppresses automatic
synthesis/baking; text never waits for speech and phone/history stay silent by
default. These are targets, not claims of wired behavior. See
[[Tracking/Live Conversation Journal]] for the journal and still-open
action/animation/expression discussion.

The first tool scope is in-game actions, such as study sessions, plans, timers,
and permitted shared activities. The loop builds an authoritative context
snapshot, calls the player's provider, validates and executes any permitted
tool requests, then presents a finalized reply through the shared speech and
presentation surface. Casual conversation may need no tool calls.

Context combines authored character rules/examples, relationship and activity
state, relevant supported player facts, and recent lines actually presented.
Generated claims do not become evidence about the player. Generated replies reuse
utterance presentation without requiring reusable Topic routing metadata.

Generated conversation answers are structured VN-style dialogue beats even when the response is
lengthy: meaningful compact nodes with individual supported expression cues,
not a monolithic paragraph or post-hoc character slicing. Phone renders the
beats as messages; direct conversation uses node playback. Per-node/total limits
and the final online wire fields remain to specify. Resuming an existing
successful reply after a player-initiated `Player.Interact` action (currently T) is presentation only; it
does not restart model/tool execution. Returning to Altera does not require
switching a Topic/Agent mode.

Each turn needs a request ID, cancellation, a deadline, and bounded model/tool
work. Activity effects must be idempotent. Closing a conversation, changing
delivery channel, or accepting an interlude must prevent stale replies or queued speech
from taking over its successor. Background work cannot bypass these controls.

### Background adaptation

Using the player’s chosen model provider, it can:

- summarize durable user information from recent interactions
- propose profile updates without erasing prior facts blindly
- identify useful personalized dialogue opportunities
- generate dialogue branches in the shared dialogue schema
- submit those branches for deterministic validation

Background jobs select useful opportunities from recorded interactions instead
of generating a branch for every turn. They persist progress for a later app
session, recheck their supporting facts before publication, and cannot initiate
live conversation or apply stat/currency rewards. They reuse the same BYOK
boundary while remaining separate from the live conversation loop.

The game remains usable without a configured API key: prepared dialogue and
offline local retrieval continue to work. BYOK currently enables TTS and is the
planned boundary for explicitly enabled online judgment/generation and adaptation.

Provider credentials must never be committed, stored in `PlayerPrefs`, or written into dialogue assets. The current Windows implementation uses the encrypted DPAPI store described in the TTS section below; other platforms still require their own secure store.

### Planned generated-dialogue semantic contract

The following is the contract to implement when the background worker exists.
It is not currently produced or consumed by a live LLM workflow.

The background LLM receives only the relevant retrieved catalogue slice, not every catalogue entry. It returns a structured manifest beside the dialogue text:

```json
{
  "intentId": "request",
  "topicIds": ["state.tired", "beverage.coffee"],
  "responseToneIds": ["tone.warm"],
  "localAnchors": ["time.night"],
  "triggerExamples": [
    "I am exhausted but need coffee",
    "It is late and I need caffeine"
  ]
}
```

The manifest is authoritative for the branch's declared contract only after validation:

1. Parse the shared dialogue schema and require known Intent, Topic, Tone, and local-anchor IDs.
2. Semantically verify the `triggerExamples` against the canonical catalogue. The generated response prose is not used as the player's trigger intent because it describes Alterra's answer, not the player's meaning.
3. Audit the generated prose against its declared response tone and safety rules.
4. Generate or queue TTS independently.
5. If new aliases or topics require vectors, queue them for the local GTE worker during low-activity time. A branch that reuses existing topics and aliases needs no new embedding.
6. Publish the branch into a per-user routing overlay only after all required work completes.

Until publication, the candidate remains unavailable to synchronous routing and generic content continues to work. If a hosted embedding service is used later, its model, revision, pooling, normalization, and dimension must match the canonical vector space; otherwise it can only provide a secondary ID suggestion, not vectors inserted into the local GTE index.

## TTS

TTS is a content-preparation and playback service, not part of dialogue routing.

- Generic dialogue can ship with pregenerated audio.
- Personalized dialogue calls TTS in the background after validation.
- A dialogue may become text-ready before audio is ready, but the player-facing behavior must be explicit: wait, use a local fallback voice, or show text without speech.
- Provider-specific speech markup must not leak into dialogue text.

The current provider adapter uses OpenRouter's speech endpoint with
`qwen/qwen-audio-3.0-tts-plus` and requests MP3 output. `TTSSystem` owns the
provider configuration while `DialogueManager` owns cache lookup, cache writes,
and text-only fallback. Ambient speech delegates through the same path so it
does not maintain a second provider client.

`TTSCacheUtility` searches `StreamingAssets/TTSPrebaked` first and
`StreamingAssets/TTSCache` second. `DialogueManager` writes newly generated
MP3 clips to the runtime cache, prunes it at its 512 MB soft cap, prefetches the
next four nodes, and waits on a missing foreground clip with loading dots and a
1.25-second retry interval. This is current dialogue playback behavior, not a
background personalized-dialogue baking worker.

Credential resolution is deliberately small:

1. `OPENROUTER_API_KEY` is an environment-variable override for development.
2. A user-supplied key can be saved through `TTSSystem.SaveUserApiKey`.
3. On Windows, the saved key is protected with the current user's DPAPI and
   stored under `Application.persistentDataPath`; it is never written to a
   scene, asset, `PlayerPrefs`, or log.

The current client exposes the BYOK storage seam but does not yet add a settings
screen. A future settings surface can call the existing save/clear methods
without changing the dialogue or provider boundary. Non-Windows secure storage
still needs a platform-specific implementation before release. Runtime cache
writes also need to move from `StreamingAssets` to
`Application.persistentDataPath` before packaged builds are treated as
production-ready.

TTS candidates should be compared by Alterra’s actual voice requirements:

- voice quality and consistency
- latency and streaming
- emotional control
- required languages
- local hardware and package size
- hosted cost and commercial rights

### M2 conversation transcript

The M2 direct-conversation path uses `ConversationTranscript` as its sole new
writer. It appends versioned events to the private
`<persistentDataPath>/Data/conversations.sqlite3` store. Legacy JSONL is import-only;
the importer validates the complete bounded stream, stores exact source bytes in
chunks with the event rows/receipt, and rejects incomplete or corrupt snapshots.
Reads retain bounded paging/recent-context semantics; there is no 120-turn lifetime
cap. Recent model context includes player submissions and Altera nodes acknowledged
as presented, not generated-but-unpresented replies. Request receipt, validated
response and presentation are separate events so stored text is not proof it was heard.

This database is private to the local installation but is not encrypted at rest. It
contains conversation text. This is an interaction log, not a long-term
memory/retrieval service. Existing player-memory consent/source suppression continues
to govern which transcript IDs may be synchronized; the migration does not restore
memory or imply capture/online consent.

## Data ownership

| Data | Owner | Mutability |
| --- | --- | --- |
| Intent and Tone catalogues | Authored semantic catalogue | Versioned; changes require evaluation and rebake |
| Universal Topic catalog | Authored semantic catalogue | Expanded only through reviewed promotion |
| Generic dialogue definitions | Authored content | Read-only at runtime |
| Editable authored content and Workbench drafts | Authoring repository | Local SQLite schema 1; W1 typed CRUD and Editor build use the SQL repository; approved Flow-first graph/selected-step editor W2-W3, preview W4 and acceptance W5 remain open; runtime acceptance separate |
| Personalized dialogue definitions | Per-user content store | Written only by validated background jobs |
| Canonical routing vectors/index | Semantic routing system | Rebuilt when a model or universal catalogue version changes |
| Personalized routing overlay | Semantic routing system | Appended or atomically replaced when ready content is indexed |
| Generated dialogue semantic manifest | Background adaptation workflow | Candidate until validation and publication |
| Branch-local anchors | Dialogue/content owner | Scoped to a branch or encounter |
| User profile and relationship state | User-state store | Updated through explicit operations |
| Durable game progression, home layout, host events and operation receipts | Game persistence repository | Local SQLite; validated host operations only |
| Topic occurrence/replay state | Game persistence repository | Game schema 3; one-time JSON and prior root-SQL migration |
| Conversation sessions, repetition, ambient state and transcript | Private conversation repository | Local schema-2 SQLite; legacy JSON/JSONL are import evidence |
| Generated dialogue archive records and payloads | Private content repository | Local schema-1 SQLite; unvalidated payloads remain unavailable |
| Migration readiness and per-domain receipts | Runtime migration journal | Local SQLite; no cross-store transaction claim |
| Interaction events | Interaction log | Append-only until summarized/retained by policy |
| Generated speech | Audio cache | Replaceable and reproducible |
| Dialogue presentation and game effects | Unity dialogue/game systems | Driven by the selected dialogue definition |

## Implemented dialogue and runtime core

The provider-neutral assembly contains these plain-C# responsibilities under
`Assets/AlterOS/Dialogue/` and `Assets/AlterOS/Semantics/`:

- `DialogueDefinition`: the shared generic/personalized content shape, routing examples, context tags, nodes, choices, presentation cues, and speech cache key.
- `DialogueValidator`: rejects unsupported versions and sources, malformed nodes, ambiguous choice/continuation nodes, duplicate IDs, and missing branch targets.
- `DialoguePlayback`: owns only deterministic traversal through a validated dialogue graph.
- `AuthoredDialogueRouteCatalog`: projects canonical sequence paths from the pinned SQL release; directory loading remains authoring/test tooling.
- `GteSemanticRuntime` and `DialogueRouteIndex`: load the semantic index from the SQL release, load the baked GTE model/tokenizer assets, warm the encoder at startup, and rank authored routes.
- `OpenRouterTtsClient` and `OpenRouterCredentialStore`: provider request construction and local Windows key custody.

The current checkout still contains a large scene-wired `DialogueManager` as
the presentation bridge. `TTSSystem`, `DialogueRouter`,
`ConversationSessionStore`, and `GeneratedDialogueArchive` are the existing
Unity-side seams around it. New provider work should keep moving behind focused
services rather than adding more responsibilities to the bridge.

### Migrated generic corpus

`Assets/StreamingAssets/Dialogue/Generic/` retains the imported source JSON for 57 semantic routes and 161 dialogue variants; it is not the current editable authority. The SQLite authoring store is editable through typed Workbench CRUD commands, and runtime readers use the pinned SQL release. The current release covers 439 source files and retains exact source hashes. Variants are grouped by `routing.routeId`.

The per-variant `routing.examples` field is a bootstrap representation of the earlier flat index. The target catalogue design moves reusable Intent, Tone, and Topic anchors out of individual dialogue files; variants retain only their canonical IDs, local anchors, and branch-specific eligibility. Migration must preserve route IDs and content while removing duplicated universal anchors.

The migration retained text, translated display text, branching choices, expressions, simple animation IDs, language, and time-of-day routing windows. It normalized the companion name to Alterra and removed ambiguous choice fallbacks.

Generated content, waiting/filler pools, tests, system diagnostics, runtime counters, untyped effects, and files without semantic routing metadata were not imported.

The following responsibilities are still missing or incomplete:

- `UserState`: a compact profile and explicit user facts separate from session history.
- `AdaptationWorker`: performs the BYOK background workflow and publishes validated personal content.
- `PersonalizedRoutingOverlay`: indexes ready user-specific branches without rebuilding the generic corpus during play.

### Local semantic runtime

The current local semantic implementation uses the pinned
`gte-multilingual-base` encoder through Unity Sentis
(`com.unity.ai.inference` 2.6.1). A fixed-length FP16 ONNX export and Hugging Face
tokenizer live under `Assets/Resources/AlterOS/Semantics/Models/`. The authoritative
current semantic vectors are in the pinned SQL release; retained legacy vector assets
are not the runtime index authority.

`GteSemanticRuntime` is created automatically before the first scene loads, persists for the application lifetime, and performs one warm inference before reporting `Ready`. It exposes embedding and route-ranking calls but does not own UI, TTS, or dialogue presentation. Input arriving before readiness should wait briefly or use the documented fallback path; it must not silently pretend that routing succeeded.

The model accepts `[1,128]` integer token IDs and attention masks and returns a normalized 768-dimensional embedding. The Python exporter and anchor baker remain development tools; the shipped game needs the imported model/tokenizer and the SQL-backed vector index.

The current runtime loads the semantic route index from the pinned SQL release and keeps the GTE model/tokenizer in `Resources`. The installed release has 673 embeddings, 57 routes and 768 dimensions; its read-only integrity/trusted-release check passed and its manifest matches authoring revision 63. Startup and player acceptance remain open. The target topology keeps the same pinned GTE model and one-embedding-per-input behavior, separates the canonical Intent/Tone/Topic catalog from dialogue variants, and adds a separately persisted personalized overlay. New generated content must not trigger a full generic rebake during play.

The Unity Editor main toolbar exposes a `GTE` dropdown under `AlterOS/Semantics/GTE`. It starts the existing Semantic Lab anchor baker asynchronously with an automatic, CPU, or CUDA device choice, refreshes the generated vector and metadata assets on success, and also provides a reimport-only action. This is editor tooling only; it does not add a scene object or change runtime startup.

### Free-text authored routing

The greeting menu's `Let's Chat!` choice reuses the existing topic input panel. Submitted text waits for the pre-warmed `GteSemanticRuntime`, selects the highest-confidence route with at least one supported anchor, and asks `DialogueRouter` to read the first stable variant from the SQL release. The router validates the canonical dialogue shape and converts it into the existing local sequence runner so presentation and TTS remain owned by the dialogue surface.

This first slice is intentionally local and synchronous after model warm-up: it never calls an LLM. Unknown, low-confidence, unavailable, or malformed routes use the authored `unclear_short` fallback. The route catalog is projected from the installed SQL release; authoring changes require a new validated release. Startup/player acceptance remains pending.

## Vertical-slice status

Session Zero is the prerequisite for the first adaptation milestone: normal
startup, T interaction with E inventory control, real Topic/Agent selection and conversation, stat
inspection, speech/fallback, and protected focus behavior. The current
[Session Zero inventory](SessionZero.md) records which paths are implemented,
which are scene-wired, and which lack runtime proof. It also preserves the
approved study -> shared water break -> personalized Topic milestone for later.

Implemented now:

- `SplashScene -> ModelTest` startup route
- 57 authored semantic routes and 161 generic dialogue variants
- GTE startup warm-up, route ranking, toolbar rebake, and authored free-text playback
- session/repetition persistence
- OpenRouter TTS, Windows BYOK storage, prebaked/runtime cache lookup, prefetch, and foreground wait/fallback behavior

Scaffolded but not end-to-end:

- generated-dialogue archive lifecycle; generated playback/menu entries remain unavailable
- human-reviewed Intent/Tone/Topic catalogue separated from the migrated route corpus

Still required to prove the adaptation gimmick:

1. A background BYOK worker that produces validated dialogue manifests.
2. A TTS baking queue that marks generated branches ready only after audio is available or an explicit text-only policy is selected.
3. A per-user semantic overlay and a later input that routes to a generated branch.

Study modes, minigames, multiple agents, and broad workflow orchestration should
wait until that loop is proven.

## Decisions still open

- sentiment implementation: evaluated existing classifier, custom training, or LLM judge; language coverage, calibration, and local inference cost
- harness implementation, per-turn limits, and background scheduling (Python/LangGraph/Pydantic stack locked; not implemented)
- anchor phrases, thresholds, and migration from the flat route examples into the approved catalogue
- local-anchor representation and specificity/priority scoring policy
- post-training policy for the GTE encoder
- model delivery/quantization policy for release builds
- additional LLM providers supported by BYOK beyond initial OpenRouter
- sidecar transport, packaging, supervision and background scheduling
- TTS provider comparison and local fallback policy beyond the current OpenRouter adapter
- Cross-platform credential storage for BYOK
- persistent runtime TTS cache location and in-flight request deduplication
- retention and deletion policy for interaction history and generated content
