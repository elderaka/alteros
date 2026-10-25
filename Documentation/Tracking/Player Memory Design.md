---
title: Player Memory Design
tags: [alteros, memory, profile, design]
updated: 2026-10-18
status: approved-exploratory-implementation
---
# Player Memory Design

Related: [[Unified Conversation Design]], [[Unified Conversation M2 Handoff]],
[[Companion Continuity and Memory Research]], [[Player Context Evidence]],
[[Player Memory Implementation Plan]].

## Authority and scope

The user approved trying short-/long-term layered memory and explicitly asked
this session to plan it and send the existing M2 session instructions to START
implementation, not another planning round. The defaults here make that trial
bounded and replaceable. Implement as additive M2.1; do not redo M1/M2, replace
the transcript, or treat their outstanding Unity/runtime checks as completed.

## Short-term versus long-term

- Short-term: existing recent presented conversation plus eligible current
  circumstances. A recent activity is automatically current for24h from its
  event timestamp; afterwards it remains historical, not deleted. Temporary
  self-reported state is current for2h. These are first-slice policy constants,
  not biological/emotional truth. Missing evidence remains unknown.
- Long-term: explicit preferences/profile, active goals and source-backed
  experiences. Preferred name, declared boundaries and interaction preferences
  are compact core profile; likes/dislikes/experiences are retrieved selectively.
- Durable is not always-injected. Historical records may inform natural callbacks
  when relevant, with dates and appropriate uncertainty. Recall never refreshes
  validity/freshness or turns an expired state into a current state.
- Authored persona is not ordinary player memory. Neither extraction nor a player
  instruction can rewrite companion identity, consent or game permissions.

## Storage and ownership

Use Python standard-library SQLite through one focused repository in the existing
sidecar; no new memory package/server/vector runtime. Unity owns consent/settings,
source transcript, authoritative game events, identity and presentation. The
repository owns derived player memory transactions. LangGraph calls bounded
repository functions; the model never receives SQL, arbitrary paths or write tools.

Database: `<Application.persistentDataPath>/Memory/player-memory.sqlite3`.
Unity supplies this trusted bootstrap location; model output cannot select it.
DB and M2 transcript are private local plaintext, not encrypted-at-rest promises.
Keyless helper startup and memory-only commands work without provider credentials,
persona loading or online-generation consent. A missing helper leaves ordinary
local dialogue usable; show memory-unavailable/unsaved and do not claim a save.
After memory/forgetting has ever been enabled for a save, online conversation
requires a successful memory-policy/suppression read even when capture is off.
An old helper or unavailable DB cannot silently fall back to unfiltered v2
history. Preserve a nonpersonal host flag for this requirement; offline authored
dialogue remains available. Saves never using memory may keep the v2 path.

Do not copy/read the external private USER.md/MEMORY.md or Codex memory files.
The in-game profile serves the same purpose but belongs to this player/save.
Existing JSONL transcript remains the source record; don't migrate/rewrite it.

## Records and safe writing

Record fields: memoryId, playerId, category, key, value, actor, scope, sourceKind,
sourceId, sourceText, eventAtUtc, recordedAtUtc, validUntilUtc, freshUntilUtc,
status, supersedesId. Times use UTC; recordedAt is host-assigned. Optional unknown
times are null. Status is active/superseded; expiry is computed, not a delete.
Only player actor is valid for profile facts. Scope is real_world_claim,
in_game_experience or fictional; do not present a claim as independently verified.

Initial categories: preferred_name, interest, dislike, interaction_preference,
shared_activity_preference, boundary, active_goal, recent_activity,
temporary_state, routine. Host-confirmed scope/source distinguish hypothetical
roleplay, user declarations and completed game events. Completion cannot come
from model narration. Age/location are Settings-only optional, NOT auto-extracted
or proactively asked in this slice; leave their full UI for later.

Preferred name <=80 chars; other values/source excerpts <=512 chars; category/key
IDs <=80 chars. Source IDs <=128; no arbitrary JSON blobs or SQL. Transactions
deduplicate by sourceId/category/key/value; explicit corrections supersede only
the matching category/key, preserving different interests and historical evidence.
No wholesale profile replacement. Conflicting/ambiguous statements stay proposals.

Profile memory defaults off. Onboarding asks explicit initial opt-in. After opt-in,
harmless explicitly stated interests/dislikes and typed onboarding answers may
be saved automatically. Persistent interaction preferences/boundaries and routines
require direct Settings input, a clear remember instruction or an explicit
confirmation; temporary frustration cannot become a permanent personality trait.
More personal proposal content is held in RAM for confirmation, not silently
persisted. No health/financial/precise-location/credential categories or inferred
diagnoses/personality scores. Schema rules cannot perfectly detect sensitive
information disguised in free text: expose review/correction and test failures.

For ordinary online turns, memory candidates are optional structured proposals
alongside the same ONE model response; no extra extraction call/automatic retries.
At most3 proposals, each citing a supplied current player-source ID and short
evidence quote. Host/repository validate source membership, quote existence,
category, scope and consent before writing. Unchosen suggested replies, generated
dialogue, quoted third-party facts and fictional statements aren't player facts.

## Reading and natural recall

First implementation: category/time filters and parameterized text search;
prefer SQLite FTS5 when available, with a tested safe LIKE fallback for packaged
runtimes lacking it. No new embeddings/bake. Preserve source excerpts so a later
semantic retriever can improve recall without rewriting the store.

Context limits: <=4 compact core facts, <=3 current records, <=3 recalled records,
<=3000 combined memory characters. Filter consent, actor/scope, deletion,
supersession and validity before ranking; exact category/name matches then lexical
relevance and time, stable memoryId ties. Zero results is valid. Never return
unrelated memories just to fill the limit. Historical recall is labelled as such.

Relevance may support at most ONE unsolicited callback in an ordinary reply;
host passes allowed callback IDs, and same memory is suppressed for24h after an
actually presented callback. Explicit player recall questions can bypass callback
cooldown but not consent/deletion. With absent/ambiguous evidence Altera asks or
admits uncertainty; no fake remembering performance or invented delays.

## Onboarding and controlled curiosity

First boot schedules one authored first-meeting conversation, presented on the
first explicit E interaction after Splash -> ModelTest readiness. Do not force
a blocking dialogue over startup. First question offers memory opt-in/skip,
then preferred name, interests and what the player enjoys doing together. All
steps are skippable/editable and have Japanese speech + English display text;
no LLM/key is necessary. Save progress only on acknowledged presentation/answers,
not selection. Resume unfinished onboarding; completed/skipped onboarding does
not replay every launch. No roleplay affection/stat rewards for answering.

The game chooses questionIntent from an allowlist: preferred_name, interest,
shared_activity_preference. One personal question at a time, at most1 proactive
profile question per play session,7-day per-intent cooldown after presentation;
probability0.15 evaluated once at an eligible completed exchange using injectable
clock/draw. No frame polling, focus/quiet override or interruption of active work.
Known answers, recent questions and declined subjects are ineligible. Declining
an intent suppresses it until Settings re-enables it; Skip only defers onboarding.
Model curiosity can propose ONE allowlisted intent, but the same host gate must
approve it. Ordinary task clarification isn't a profile-question lottery.

Offline scheduled questions use authored lines; online wording may be generated
within an authorized turn. Probability does not trigger an extra billable request
or start a conversation unprompted. This is a first-slice scheduling rule, not
the full autonomous initiative/activity system. Focus/quiet unknown => do not
schedule proactive questions/callbacks until explicit host evidence allows it.

## Controls and deletion

Provide authored Canvas controls for remember-preferences opt-in, profile review/
edit, forget selected memory and stop/re-enable personal questions. Online
transmission requires BOTH online-generation consent and permitted memory use;
ordinary memory storage consent is not remote-provider consent.

Forget removes usable memory/value/excerpt/index entries and records minimal
source suppression IDs so source replay cannot recreate it automatically.
Filter those sources from future LLM recent-context/recall building too. This is
logical forgetting, not forensic secure erasure. Original private chat history
remains in M2 JSONL; disclose this and offer the separate history-deletion task
later. Don't claim total data deletion while its source transcript still exists.
Turning memory off stops capture/injection but doesn't silently erase stored
records. A display/Settings read is not permission to inject memories.

## Not in this slice

Graph databases, LangMem/Mem0/Letta dependencies, training, sentiment models,
advanced semantic/multi-hop recall, cloud sync, age/location questioning, actual
activity simulation, phone UI, TTS generation, paid extraction jobs and transcript
migration. Model-quality/translation/naturalness validation remains separate
from deterministic source/protocol tests.
