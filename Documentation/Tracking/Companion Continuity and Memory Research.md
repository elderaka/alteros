---
title: Companion Continuity and Memory Research
tags: [alteros, research, memory, persona]
reviewed: 2026-10-18
status: research-not-approved-design
---
# Companion continuity and memory research

Related: [[Live Conversation Journal]], [[Stats and Mood]], [[Player Context Evidence]],
[[Agent Tools]], [[Brainstorm Inbox]]. Research pass2026-10-16; no implementation,
provider selection, model benchmark or framework installation performed.

## 2026-10-18 - roleplay memory follow-up

Status: research and architectural recommendation, not an implementation plan
or a claim that M2 already supports durable profile/recall. The earlier research
remains historical; current conversation supersedes its explicit mode-switch
assumption. DeepSeek V4.1 Flash and bilingual output are locked separately in
[[Unified Conversation Design]].

### User direction clarified

- First meeting is a short skippable conversation: preferred name, interests,
  desired shared activities. Age/location stay optional in Settings.
- After opt-in, harmless explicitly stated preferences may be remembered;
  routines/more personal information need confirmation. Tentative statements
  stay tentative. Correction/deletion controls remain necessary.
- The game schedules an eligible personal-question intent and the model writes
  its wording. Model curiosity may propose a candidate, not grant permission.
- Natural callbacks to older memories are acceptable when relevant; not only
  explicit "remember when" requests.
- Some information stops appearing as current context after its freshness
  window (e.g.24h for a recent activity), without being deleted. Historical
  recall must retain dates and must not present an expired state as current.

### Primary-source findings

- [Generative Agents](https://arxiv.org/html/2304.03442v2) retrieves experiences
  using relevance, recency and importance, and separates observations from
  derived reflections. Borrow the ranking idea, not unrestricted autonomous
  reflection or an unvalidated emotion/importance score as truth. Its simulation
  results are not months-long companion reliability evidence.
- [LongMemEval](https://arxiv.org/html/2410.10813v2) evaluates temporal reasoning,
  updates and abstention. Its tested designs found that fact-only compression
  loses information compared with conversational units. Preserve source turns
  beside derived preferences; evaluate reading accuracy separately from retrieval.
- [LOCOMO-CONV](https://arxiv.org/abs/2609.03467) is a2026 preprint on implicit,
  conversational retrieval. Its reported gaps motivate testing natural callbacks
  separately from explicit factual QA; this is not a universal model ranking.
- [Letta core memory](https://docs.letta.com/v1-sdk/memory/memory-blocks)
  distinguishes always-visible human/persona blocks and supports read-only
  blocks. Borrow a compact profile/identity split, without adding a second agent
  runtime or giving the model uncontrolled write access.
- [LangMem background extraction](https://github.com/langchain-ai/langmem/blob/main/docs/docs/background_quickstart.md)
  supports delayed consolidation. Useful pattern for bounded low-activity work;
  an extra extraction pass still costs money and needs explicit budgets.
- [Graphiti](https://help.getzep.com/graphiti/getting-started/quick-start)
  supports temporal facts and hybrid graph retrieval; the documented setup uses
  a supported graph backend. Temporal validity is useful to borrow, but a graph
  server is not justified yet for one local companion/player.

### Recommended first architecture

Keep the current Unity transcript as source history. A single policy-controlled
repository in the Python sidecar can own SQLite-derived memory records and
bounded queries; Unity owns consent, authoritative game events and permissions.
This ownership is proposed, not an approved storage migration. LangGraph calls
the repository rather than exposing SQL or arbitrary writes to the model.

Maintain compact declared profile, source-linked episodic records and expiring
current context separately. Persona rules are authored and not ordinary user
memory. Every record needs actor, scope (real-world claim/in-game experience/
fictional hypothetical), provenance, event/recorded time, validity, freshness
and correction/supersession status. Altera's generated words must not become
facts about the player or proof that an activity occurred.

Begin with structured filters plus SQLite full-text retrieval
([FTS5 documentation](https://www.sqlite.org/fts5.html)); test semantic retrieval
with the existing GTE only if paraphrase recall needs it. Topic-routing quality
does not establish episode-retrieval quality. No new vector database required
by this recommendation; test FTS availability in the packaged runtime.

For every turn, provide a small profile and eligible recent context, plus at
most a bounded selection of relevant source-backed memories. Filter by consent,
actor, validity and deletion before relevance ranking. A relevance threshold
must permit zero results. A separate callback gate suppresses repetition,
sensitive subjects, declined questions and focus interruptions. At most one
unsolicited callback per reply is a proposed starting limit, not a locked value.
No fabricated "retrieval delay" is needed to act human; uncertainty should be
honest when evidence is missing or ambiguous.

Explicit profile input can be recorded deterministically. LLM extraction of
incidental statements produces schema-validated proposals, batched under a
separate background budget; don't make a model call for every trivial turn.
Store provenance and preserve prior versions on corrections. Retrieval can
affect salience, never automatically renew current-validity or freshness dates.
Expired, superseded and player-deleted are distinct states. Deletion must also
invalidate summaries/indexes/caches and prevent old-source re-extraction.

### Candidate library decision and test gate

Recommendation: SQLite repository + existing LangGraph/Pydantic, with optional
later LangMem extraction helpers if evaluation justifies them. Do not install
Mem0, Letta or Graphiti merely to label the system "memory". Their broad feature
sets don't establish AlterOS-specific consent, once-only experience or offline
correctness. No universal best roleplay memory library was established.

Compare recent-history baseline, summary-only baseline and layered retrieval
using synthetic Altera/player timelines. Include natural callbacks, failed vs
completed shared activities, fictional statements, corrected coffee preference,
expired stress, deleted facts, identical events on different dates, explicit
boundaries and unavailable evidence. Measure unsupported recall, current-fact
accuracy, abstention, callback appropriateness/repetition, latency and total
extraction/retrieval/generation tokens separately. Human review is required for
naturalness; vendor QA scores cannot substitute for those checks.

No experiments reproduced, dependencies installed, provider calls made, Unity
operations performed or private user data read in this follow-up. Source links
were inspected through paper HTML/abstracts and official docs. No performance,
privacy, translation or memory-quality pass is claimed; Obsidian reading view
remains unverified.

## User requirements, distinct from proposed solutions

- One Alterra across phone and direct interaction, without player-facing new-chat resets.
- Recall shared experiences when asked "remember that time when X" without
  requiring photographic memory or accepting invented recollections.
- Preserve established facts and personality over long use.
- History inspection should feel like a messaging app, not editing a character's
  memory/profile variables. This does not yet decide deletion, export, retention
  or whether edits to messages affect derived memories.
- Cost and bounded harness behavior matter. Existing authoritative stats/activity
  boundaries and player-selected Agent mode remain intact.

## Evidence from primary sources

### Long context is not sufficient

Lost in the Middle (TACL2024) found position-sensitive retrieval in the models
tested. This is a reason to test context use, not proof every present model has
the same failure rate. [Paper](https://aclanthology.org/2024.tacl-1.9/).

### Memory quality includes updates, time and abstention

LongMemEval (ICLR2025) separates extraction, cross-session reasoning, temporal
reasoning, updates and abstention. Its experiments found benefits from smaller
conversation units and richer retrieval keys; replacing original exchanges with
only facts/summaries lost information on many tasks. Those results are specific
to its models/setup, not a measured forecast for GTE or Alterra.
[Paper](https://arxiv.org/html/2410.10813v2),
[authors' code/data](https://github.com/xiaowu0162/LongMemEval).

LoCoMo (ACL2024) provides long dialogues grounded in personas and event graphs,
with human verification/editing. It offers useful long-range recall tasks but
is not a real longitudinal companion-player study.
[Paper](https://arxiv.org/abs/2402.17753),
[authors' code/data](https://github.com/snap-research/locomo).

### Separate archive from working context

MemGPT (2023) demonstrates tiered context management and searchable external
history for multi-session conversation. Borrow the separation, not an entire
agent runtime or unbounded model-controlled memory tools.
[Paper](https://arxiv.org/html/2310.08560v2).

Generative Agents (UIST2023) combines recorded experiences, retrieval, reflection
and planning in a Sims-like simulation. Useful inspiration for embodied
experience, not proof of reliable months-long companion continuity or bounded
BYOK costs. [Paper](https://arxiv.org/abs/2304.03442).

MemoryBank (AAAI2024) explores time/significance-based forgetting and reinforcement.
For Alterra, an experiment could lower retrieval prominence without deleting
the source history; that choice is our proposal, not the paper's validation of
this game design. [Paper](https://arxiv.org/abs/2305.10250).

### Persona needs its own evaluation

Measuring and Controlling Instruction (In)Stability (2024, revised2025) evaluates
drift and mitigation trade-offs. Its split-softmax method modifies inference
attention; ordinary remote BYOK APIs cannot be assumed to expose that access.
The earlier project page uses the persona-drift title; the revised paper has
the instruction-stability title. [Paper](https://arxiv.org/html/2402.10962v3).

Examining Identity Drift (2025 revision, under-review preprint) reports model-
and size-dependent identity drift and that a supplied persona is not a general
guarantee. Do not generalize its size findings into "larger is always worse."
[Paper](https://arxiv.org/abs/2412.00804).

RoleLLM (ACL Findings2024) separates role knowledge and speaking style and
explores role-conditioned tuning. Character-LLM (EMNLP2023) trains on character
experiences. Useful for authored identity/evaluation inspiration, not reasons
to train on every private player interaction or assume fine-tuning solves recall.
[RoleLLM](https://aclanthology.org/2024.findings-acl.878/),
[Character-LLM](https://arxiv.org/abs/2310.10158).

## Proposed AlterOS interpretation, not approved implementation

One companion identity and ordered lifetime timeline; multiple internal episodes
and bounded per-request contexts. Episodes organize retrieval without exposing
"new chat" or wiping continuity. Projects may attach study documents/tasks but
must not create another Alterra or partition shared relationship history.

Proposed layers:

1. Authored character foundation: core values, background, voice examples,
   boundaries and explicitly allowed development. Never rewritten by ordinary
   chat summarization or a player command disguised as a memory.
2. Authoritative present state: validated needs, mood, activity and actual events.
   Model prose does not prove an action happened or grant tools/stat changes.
3. Source timeline: timestamped delivered messages and game events with channel,
   speaker and stable IDs. Distinguish planned, attempted, failed and completed.
4. Derived indexes: episodic cues, facts/preferences and commitments linking to
   source IDs. Record assertion vs inference, recorded time vs event time,
   correction/supersession, and provenance. Model confidence is not calibrated truth.
5. Bounded working context: foundation, present state, unresolved commitments,
   recent turns and query-relevant source excerpts. Memory content is untrusted
   evidence, not instructions overriding the foundation or permission policy.

Recall example: vague "that rainy night before the exam" searches lexical and
semantic cues plus time/events, then reads candidate source exchanges. Search
index summaries guide discovery; source excerpts support claims. If ambiguous,
ask which occasion; if absent, don't assert shared recollection. Broader recall
may take an extra bounded retrieval step; it is not a photographic guarantee.

GTE reuse is only an initial experiment. Topic anchor quality does not establish
long-conversation retrieval quality. Test slang, mixed language, names, temporal
references and near-duplicate episodes independently. No new embedding model
or vector database is selected by this note.

Personality proposal: put a compact stable foundation in every request; supply
only relevant authored dialogue examples, state-driven expression and memories.
Keep immutable identity separate from evolving state and supported development.
Do not reinforce an accidental out-of-character answer into permanent canon.
Prompt anchoring is a mitigation to measure, not a guarantee. Model changes
require regression evaluation; provider-neutral does not mean behavior-identical.

Cost proposal: bounded tokens and recall calls per turn, background batch work
with its own budget, no LLM memory pass after every trivial exchange. Record
input/output tokens, memory extraction calls and TTS spend separately. Test
latency and accuracy at equal budgets; no dollar estimate is possible without
chosen model/provider/workload. The first recall prototype need not have a graph
database, autonomous reflection or a new framework.

## Proposed evaluation before choosing a memory library

Compare recent-history-only, rolling-summary-only and source-backed retrieval
on the same synthetic lifetime transcript. Sample selected public benchmark
tasks separately; do not claim a full benchmark score from adapted samples.

Include paraphrased episodic recall, rare details, multiple similar events,
relative dates, corrected preferences, prior assistant promises, uncompleted
activities, imaginary/role-play events, false-premise questions and long absences.
Test phone/direct continuity and recall after context reconstruction/compaction.

Measure retrieval evidence recall, unsupported-claim rate, latest-fact accuracy,
appropriate abstention, personality adherence, latency and token cost separately.
Retrieving the right source is not equivalent to generating a faithful answer.

Persona tests should cover disagreement, expertise beyond role knowledge,
frustration, low energy, affection, boundaries, jokes and player attempts to
rewrite her. Assess compatible variation, not identical wording. Use authored
rubrics and human review alongside automated scoring; an LLM judge is not truth.

## Unresolved choices and evidence limits

- What belongs in permanent character canon versus supported development?
- What retention/export/deletion controls coexist with the history-only UX?
  Any deletion must explicitly address source excerpts, indexes and summaries.
- Which game/Topic events should become recallable experiences, avoiding repeated
  generic authored lines being misrepresented as unique personal milestones?
- How much recall latency/cost is acceptable and what is the honest fallback
  if embedding/model service is unavailable?
- Framework/library selection follows the prototype evidence, not vice versa.

Sources were inspected through abstracts, paper HTML and author repositories;
no published experiments were reproduced. Older-model results are not present-
provider rankings. No code, Unity operation, paid API call or player data used.
Markdown/source links checked locally; Obsidian rendered reading view unverified.
