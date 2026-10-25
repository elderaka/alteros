# Topic Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task-by-task. User approved inline execution on 2026-10-16, with cooldown starting on successful completion. Do not migrate the authored catalogue or implement Playground UI in this plan.

**Goal:** Establish a versioned, testable Topic/scenario/instance runtime that the later catalogue and Playground agents can consume without inventing selection or consumption rules.

**Architecture:** Add focused plain-C# Topic data/rules under Assets/AlterOS/Topics, reusing current StatSnapshot, stat predicates and canonical dialogue nodes. Keep v1 authored routes on their existing path until explicitly migrated; expose the new runtime contract and synthetic tests first. Whole-instance history and delivered-playback outcomes are separate from semantic routing, dialogue graph traversal and menu presentation.

**Tech Stack:** Unity 6000.4.4f1, existing AlterOS.Runtime / AlterOS.Editor.Tests, NUnit, built-in DataContractJsonSerializer for metadata history; no agent framework or new model/package.

**Spec:** [[Topic Scenario Selection]], [[Stat Catalogue]], [[Player Context Evidence]]; baseline [[Topic Scenario Refactor Audit]]. This plan chooses implementable defaults for previously open details; they are assistant proposals for user review, not retrospective approvals.

## Execution status (2026-10-16)

Tasks1–5 source implementation and both downstream handoffs are delivered.
The task checklists below retain the planned acceptance scope, not an implied
full Unity pass. Actual-source checks31/31 and existing stats/Playground checks
31/31 pass. Fresh review found no blockers; both minor findings were reproduced
RED and fixed GREEN (misplaced expected flag; repeated-node timestamp fence).
Final Unity verification is recorded separately in
[[../Verification/2026-10-16 Topic Runtime]]. No gameplay adapter, catalogue
migration or Playground UI changes belong to this execution.

## Global constraints

- Work only in E:\Pleiades\alteros; pleiades.os is archive-only.
- No Topic-path LLM calls. Generation remains a future producer of validated personal content.
- Shared anchor bank belongs to the Topic definition. Scenarios may add local-specificity requirements; same subject does not mean same one-time occurrence.
- Consumption is whole-instance only, never canonical-topic or scenario-only. Defaults: authored repeatable; generated-experience once; explicit authored milestone may be once.
- Keep authored JSON, existing 57 route IDs, GTE vectors, baked metadata and route snapshots untouched in this implementation. Migration belongs to another agent.
- Keep Playground window/command/context/fixture files untouched. Later Playground work consumes this tested API, not a copied evaluator.
- Preserve existing numeric eligibility API/schema behavior and tests; currently version1 is all-of only and rejects unsupported forms. General Topic condition trees get their own versioned contract.
- No scenes/prefabs/.meta hand edits, UI, furniture runtime, stats simulation, networking or paid provider requests.
- Keep existing gameplay behavior on v1 paths; this is the new runtime foundation, not an unannounced switch of the full shipped corpus.

## Fresh baseline (2026-10-16)

The other session has added CanonicalStatEligibility, CanonicalDialogueEligibilityEvaluator,
numeric validation and synthetic fixtures to the Playground. AuthoredDialogueVariantSelector
still chooses the first eligible path, now accepting StatSnapshot. The earlier
audit's localHour-only observations are historical; do not remove the new work.
The new runtime must reuse numeric predicate semantics while adding tree/object
and lifecycle semantics explicitly. Existing current schemas stay supported.

## Proposed runtime contract

### Definition, instance and scenario

- TopicDefinition: schemaVersion=1 for this new family format (not DialogueDefinition's
  schema), topicId, anchors (list of player-trigger examples), menu label/prompt,
  default replay policy, and scenarios.
- TopicScenario: scenarioId unique within family, integer priority (higher wins),
  finite nonnegative baseWeight, optional condition tree and weight modifiers,
  optional consumptionNodeId, canonical dialogue nodes. No per-scenario replay flag.
- TopicInstance: instanceId unique per occurrence, topicId, source kind and explicit
  resolved replay policy. Do not derive occurrence identity from label/prompt hash.
- TopicHistory: instance-keyed started/presented/completed/consumed timestamps and
  cooldown state. Lifecycle distinguishes display from actual consumption.
- Definition validation checks graph targets/checkpoint membership and known operators,
  but does not require source events to be copied into shared authored assets.

No universal Topic vocabulary promotion is performed by creating a personal
definition. Label+descriptive prompt are later dedup/retrieval input; instance
IDs and source-event identity remain separate. This plan does not implement generation.

### Conditions and object binding

Supported initial kinds: all, any, not, stat, timeWindow, fact, object.
Each node has exactly the fields for its kind; reject unknown kinds/malformed
combinations rather than ignore them. Stat operations reuse current eq/lt/lte/gt/gte
support only after verifying exact existing names; retain those names instead of
inventing incompatible abbreviations. The implementation must document the actual
accepted vocabulary before exposing JSON examples.

Use true/false/unknown evaluation with per-node explanations; invalid or unsupported
schema is a validation error, not an unknown world fact. not(unknown)=unknown;
all is false if a child is false, otherwise unknown if any child is unknown;
any is true if a child is true, otherwise unknown if any is unknown. Require nonempty
all/any and exactly one not child. A null root means no extra gate.

Object predicates evaluate against one bound object. An object node quantifies
over matching detached snapshots, choosing the first successful ID in ordinal order
for reproducibility. Known empty inventory makes exists false; unprovided/incomplete
inventory without a successful match is unknown. Missing object properties remain
unknown, not empty/false. This is a snapshot contract, not furniture component discovery.
Nested references must not mix dispenser A's quality with dispenser B's water level.

### Priority and weights

Remove consumed, cooling-down and false/unknown-gated candidates first. Compute
finite effective weights for remaining candidates. Zero-weight candidates do not
compete or block a lower tier. Find the highest priority with positive-weight
candidates; select only among that tier by normalized relative weight.

Initial modifiers are bounded additive rules: a satisfied condition adds an authored
amount; unknown contributes nothing and is traced. Clamp final negative weight to zero;
reject NaN/infinity and nonfinite accumulated totals. No arbitrary expression evaluator.
Meaningful preference curves may be built from a few explicit threshold bands initially;
automatic biological rates and complex learned weighting are not part of this slice.

Stable-sort candidates by scenarioId before drawing; accept an explicit deterministic
draw in [0,1), not UnityEngine.Random global state. This makes core tests independent of
RNG implementation; a later caller can generate seeded draws and include seed/draw in trace.
No candidate returns a typed no-selection result; existing consumer fallback remains
outside the core. Autonomous say-nothing/opportunity policy is not added here.

### Consumption and persistence

BeginPlayback returns a handle bound to instanceId, scenarioId and a unique attempt ID.
ReportPresentedNode(handle,nodeId,nowUtc) records first presentation and consumes once
if this is its authored checkpoint. Complete(handle,nowUtc) consumes once if needed.
Cancel/Fail close the attempt without consumption unless checkpoint already consumed.
Unknown handles/nodes and stale duplicate completions cannot consume another instance.
Selection or moving graph cursor alone does not report delivery.

Cooldown begins on successful completion, not selection/presentation; cancelling
does not start it. A consumed checkpoint stays consumed after cancellation.
Zero seconds is the default; exact repetition weight penalties are deferred.
Repeatable completion never sets consumed. A new instance with the same topicId has
independent lifecycle. Lifecycle updates are idempotent and must be serialized before
reporting a durable consumed outcome.

Versioned history DTO stores no chat, keys, dialogue payloads or player inference.
Repository accepts an explicit path (tests use temporary files; future runtime caller
chooses Application.persistentDataPath). Write temp, replace/backup safely, preserve
malformed/future-version originals and return an error instead of resetting/saving
defaults over them. No StreamingAssets writes. This adds a new file, not progression
save migration or a generated-archive rewrite. Multi-device authority remains roadmap.

## Review focus

- Unknown and negated unknown cannot select false situations or fabricate absent objects.
- A high-priority ineligible/zero-weight scenario cannot starve valid lower candidates.
- Consuming one instance cannot suppress a different occurrence or only one sibling.
- Traversal, cancellation, stale handles and repeat callbacks cannot falsely consume.
- Malformed/future-version history and failed writes must preserve the original file.

## File ownership and public seams

Create focused files (all new; no broad move of existing controllers):

- Assets/AlterOS/Topics/TopicDefinition.cs: family/scenario/instance DTOs and replay enum.
- Assets/AlterOS/Topics/TopicCondition.cs: tree DTOs, detached context/object snapshots and evaluation trace types.
- Assets/AlterOS/Topics/TopicDefinitionValidator.cs: complete new-family contract validation.
- Assets/AlterOS/Topics/TopicConditionEvaluator.cs: pure conditions, reusing existing numeric leaf evaluation.
- Assets/AlterOS/Topics/TopicScenarioSelector.cs: gates/history/priority/weights and typed result.
- Assets/AlterOS/Topics/TopicHistory.cs: plain per-instance lifecycle and attempt handles.
- Assets/AlterOS/Topics/TopicHistoryRepository.cs: new versioned history persistence with Unity JSON or a narrow existing serializer boundary selected at implementation time.
- Assets/AlterOS/Tests/Editor/TopicDefinitionTests.cs, TopicConditionTests.cs,
  TopicScenarioSelectionTests.cs, TopicHistoryTests.cs, TopicHistoryRepositoryTests.cs.

Do not wire production/Playground adapters in this plan. Deliver exact final
signatures and an example load/select/present/complete flow in a runtime handoff.

## Task 1: Validated family schema

**Produces:** TopicDefinitionValidator.Validate(TopicDefinition definition) returns
List<string> errors; shared models from the contract above. Reuse canonical node
types; preserve existing DialogueValidator behavior. Prefer a minimal graph-only
validation extraction if needed, with all old validator tests retained.

- [ ] Write failing TopicDefinitionTests for a single-scenario family with anchors,
  duplicate scenario/node IDs, broken targets/checkpoint, unknown replay/operator,
  empty anchor bank, invalid priority/weight data and shared bank without repeated
  scenario anchors. Include unknown/future family version rejection.
- [ ] Run named fixture and confirm expected missing feature failure.
- [ ] Implement minimal DTO/validation code, checking resource bounds (max tree
  depth 16, max nodes per condition tree 256) before recursive evaluation.
- [ ] Run new and existing canonical validation tests; keep legacy acceptance unchanged.
- [ ] Record owned files and evidence; no whole-project staging/commit.

## Task 2: Condition tree and object snapshot evaluation

**Consumes:** Task1 schema, StatSnapshot/current numeric evaluator.
**Produces:** TopicConditionEvaluator.Evaluate(TopicCondition rule, TopicContext context)
returns a typed truth value and immutable trace, including object binding IDs.

- [ ] Write failing TopicConditionTests covering all/any/not truth tables, unknown
  stats versus explicit zero, time-window boundaries, absent fact, known empty
  versus missing object inventory, and two dispensers with mutually exclusive
  properties (must not combine them into one eligible dispenser).
- [ ] Test depth/node limits and malformed kinds fail validation, not stack overflow
  or silently eligible conditions. Reject invalid numeric input via existing stat contract.
- [ ] Run RED, implement evaluator using read-only context and current numeric
  leaf semantics, then run GREEN plus existing numeric eligibility tests.
- [ ] Record evidence; do not silently upgrade CanonicalStatEligibility v1's rejected forms.

## Task 3: Scenario competition and explanations

**Consumes:** validated definition/instance, detached context, history view and supplied draw.
**Produces:** TopicScenarioSelector.Select(TopicDefinition definition, TopicInstance instance,
TopicContext context, TopicHistory history, DateTimeOffset nowUtc, double draw) returning
TopicSelectionResult with selected scenario or reason, every rejection, effective weight
and winning tier. No file/model/UI work occurs in Select.

- [ ] Write failing TopicScenarioSelectionTests: priority10/weight1 beats priority0/
  weight9999; ineligible high tier falls back; zero-weight high tier does not block;
  same-tier weights3:1 select A at draw0/.749 and B at .75/.999; reversing input order
  leaves result unchanged; missing/unknown modifier contributes no amount.
- [ ] Include consumed instance, cooldown, all-zero/no candidate, invalid draw (-.1/1/
  NaN/infinity), overflow and no mutation of inputs. Same canonical topic with another
  instance remains selectable.
- [ ] Run RED, implement stable priority/weight selection and bounded trace, then GREEN.
- [ ] Verify all matching uses the same condition evaluator and no global random state.

## Task 4: Whole-instance playback outcomes

**Produces:** TopicHistory.BeginPlayback(instance,scenario,nowUtc),
ReportPresentedNode(handle,nodeId,nowUtc), Complete(handle,nowUtc),
Cancel(handle), Fail(handle) and IsConsumed(instanceId) with explicit result statuses.
The implementation must keep handles opaque enough not to authorize arbitrary IDs;
validate against the originating definition/scenario and lifecycle.

- [ ] Write failing TopicHistoryTests for cancel-before-delivery, cancel-before-
  checkpoint, delivered checkpoint then cancel, normal completion, repeatable
  completion, duplicate/stale handle, wrong node, cooldown completion time
  and independent instances sharing a Topic. Rejected events must not alter history.
- [ ] Run RED, implement in-memory lifecycle with explicit delivery acknowledgement,
  then GREEN. Never treat Current/Continue cursor movement as a delivered node.
- [ ] Re-run selection/history fixtures to prove consumed siblings are all unavailable.

## Task 5: Durable history and downstream handoffs

**Produces:** TopicHistoryRepository.Load(path) and Save(path,history) returning
typed outcomes; serializer details must be recorded, not invented in tests through
Unity-native API stubs. No real player paths in tests.

- [ ] Write failing repository tests in a unique temporary directory: first save/
  reload preserves consumption/checkpoints/cooldown; repeat save is safe; malformed
  and future-version originals remain byte-identical; failed write returns error;
  unknown schema is not reset; no private transcript fields are serialized.
- [ ] Run RED, implement temp/replace/backup and version validation, then GREEN.
  Document platform assumptions; do not claim Android validation from Windows checks.
- [ ] Test consumer transaction behavior: failed persistence is reported as not durable,
  stale retry cannot consume a successor, successful retry is idempotent.
- [ ] Run all new Topic fixtures plus existing first-party Editor regression. Source-only
  harness checks can supplement but never replace Unity import/full-suite evidence.
- [ ] Produce two handoffs: catalogue agent gets exact schema/examples/validation CLI
  requirements without MCP or scene edits; Playground agent gets exact synthetic
  context/history/draw/result APIs and lifecycle/CLI-window parity scenarios.
- [ ] Update TODO, selection note, audit follow-up and evidence. Stop before either
  downstream workstream or bulk content migration. Request fresh code review.

## Verification and stopping conditions

No claim that production gameplay or the current Playground uses this runtime
until its adapter is separately implemented and tested. New core and persistence
tests must pass, existing behavior must remain intact, and compile/import evidence
must be separated from simulation/visual/audio proof. If Unity remains stale,
preserve unsaved Editor and report the exact verification gap; do not close it
for a batch run or fabricate Unity test results from source checks.

Before executing, review the proposed unknown logic, zero-weight handling,
bounded additive modifiers, cooldown timing and safe-persistence boundary. These
settle previously open implementation details; no biological weighting formula,
sentiment model, furniture scanner or generation dedup worker is selected here.
