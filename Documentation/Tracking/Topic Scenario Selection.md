---
title: Topic Scenario Selection
tags: [alteros, design, dialogue, topics]
updated: 2026-10-16
status: approved-direction-schema-pending
---
# Topic Scenario Selection

2026-10-19 target clarification: [[Scenario Conversation Design]] extends the
meaning of Scenario from a dialogue graph to an interaction flow with dialogue,
questions, allowed activity requests, waits and explicit endings. Topic remains
semantic meaning/anchors; closing one exchange does not inherently end/consume
its scenario or canonical Topic. Existing selector/history and old schema
descriptions below remain historical/source contracts until an explicit adapter/
migration plan. No catalogue or anchor rebake occurred in this design pass.

Related: [[Live Conversation Journal]], [[Design Decisions]], [[Stat Catalogue]], [[Topic Scenario Refactor Audit]], [[Dialogue Catalogue]], [[Topic Playground Handoff]].

## Journal - locked direction (2026-10-16)

Topic playback uses local semantic evidence and deterministic game-owned
selection rules plus seeded/testable weighted randomness. It never calls an
LLM. LLM use belongs to background candidate generation and explicit Live mode.
Generated candidates join the same ready-only Topic pipeline after validation.

### Separate identities and ownership

- Topic definition: canonical meaning and shared semantic anchor bank. "One
  anchor" means one shared bank, not necessarily one example phrase/vector.
- Topic instance: an available authored or personal occurrence, with its own
  lifecycle/history. Several instances can share a canonical Topic definition.
- Scenario: an alternative dialogue graph within the instance's Topic family.
  Owns gates, priority, weights, local specificity and dialogue presentation.
- Parent/scenario names organize content, not automatic semantic taxonomy or
  condition inheritance. Illustrative IDs such as beverage.drink.thirsty are
  scenario/content IDs beneath a shared beverage.drink Topic, not new universal
  Topic entries. Actual IDs/folder/schema compatibility need review.
- Shared routing belongs once at the Topic level rather than copied into every
  scenario. Local anchors remain possible for more specific meanings (coffee
  is not interchangeable with generic water just because both involve drinking).

### Gates, priority and weights

- Scenarios may have multiple combined gates (all/any/not), not a fixed count.
  Object conditions in one binding must inspect the same selected object.
- Hard gates establish what is true/possible; needs can influence selection
  weights rather than mechanically firing a Topic whenever a threshold crosses.
- An unavailable action may enable an acknowledgement scenario. Empty dispenser
  blocks using it, not discussing it; do not falsely claim an action succeeded.
- Higher numerical priority overrides lower among eligible scenarios. Only
  scenarios in the highest eligible tier compete by weight. An ineligible
  high-priority scenario never blocks lower eligible candidates.
- Equal-priority candidates use relative weights; a huge weight is not a
  substitute for an override guarantee. More conditions do not automatically
  confer priority. Unselected scenarios are not permanently consumed.
- Explicit player submissions and autonomous offers are distinct. Autonomous
  offers also need opportunity/focus/initiative checks and a say-nothing outcome.

### Whole-Topic replay and consumption

- Replay policy belongs to the whole Topic instance only. There is no
  scenario-only consumption mode in this design.
- Most default/authored Topics are repeatable, with optional cooldowns.
  Authored milestones may be one-time. Generated experience Topics default
  to one-time unless deliberately authored otherwise.
- Default one-time consumption happens on completion of the selected scenario's
  meaningful delivery. An optional authored consumption checkpoint can consume
  it earlier, once that checkpoint is actually presented.
- Cancelling before completion/checkpoint does not consume the Topic. Cancelling
  after a reached checkpoint does not undo consumption.
- Consuming an instance makes ALL its scenarios unavailable, including higher
  priority alternatives. A one-scenario instance naturally loses that scenario.
- Consumption persists across app restarts. Consuming dinner occurrence A does
  not consume canonical dinner or a later occurrence B with the same Topic ID.
- Repeatable playback records history/cooldown, not permanent consumption.
  Seen/started and consumed are distinct concepts.

The user's earlier phrase "one-topic" meant one-time consumption. It was not
approval of a new rule limiting semantic annotations to exactly one Topic ID;
existing multi-Topic/local-specificity design is not superseded.

## Runtime foundation follow-up (2026-10-16)

User approved the runtime defaults, with cooldown beginning at successful
completion rather than presentation. The additive source implementation now
exists: see [[Topic Runtime Catalogue Handoff]] for schema and limits and
[[Topic Runtime Playground Handoff]] for exact APIs. Source fixtures pass31/31;
[[../Verification/2026-10-16 Topic Runtime]] distinguishes Unity acceptance.
Unknown propagates through negation; zero-weight tiers do not block lower
tiers; modifiers are bounded additive. Catalogue/gameplay/Playground migration
is separate, so these are not claims about the existing live v1 path.

## Historical pre-implementation questions

[[Topic Runtime Implementation Plan]] proposes explicit defaults for this
review gate and scopes the work to shared runtime, tests and history. Bulk
catalogue migration and Playground presentation belong to separate later agents.
Its unreviewed defaults must not be represented as existing behavior.

Exact versioned JSON fields, definition/instance IDs and persistence layout;
condition vocabulary/object binding selection; unknown propagation (especially
negation); weight formulas/zero-weight tiers; cooldown start/repetition rules;
rechecking after state changes; checkpoint presentation versus graph traversal;
completion, cancel and failure result contract; crash/retry persistence behavior.
Seeded fixtures and explanations are the intended testing surface. The earlier
assistant defaults are proposals unless explicitly accepted above.

## Synthetic acceptance scenarios to author

1. Eligible priority 10 overrides eligible priority 0 irrespective of relative weight.
2. Ineligible priority 10 permits eligible priority 0; same-tier seeded selection
   is reproducible and explains weight contributions.
3. Multiple dispenser facts cannot be combined across different object instances.
4. Once-only instance completes -> every sibling scenario becomes unavailable;
   a second instance sharing the canonical Topic remains eligible.
5. Cancel before checkpoint leaves it available; reached checkpoint plus later
   cancel remains consumed; repeat callback/reload does not apply twice.
6. Repeatable instance remains eligible subject to its configured cooldown.
7. Generated ready status alone does not prove eligible, validated, voiced or
   consumed. Synthetic fixtures never write player saves.

These are planned tests, not implemented/passed assertions. The current
implementation gaps and bounded migration order are in [[Topic Scenario Refactor Audit]].
