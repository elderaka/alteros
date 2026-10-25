---
title: Reusable Content Framework Workflow
date: 2026-10-22
tags: [alteros, planning, workbench, content, actions, scenarios]
status: implementation-tooling-and-runtime-integration-active
---
# Reusable content framework implementation workflow

> **For agentic workers:** use writing-plans for this planning wave. Existing domain
> sessions keep their roles. Source implementation starts only after consolidated
> plan approval; no provider/profile/Unity work is authorized by planning dispatch.

> **Implementation START received2026-10-22:** the human approved implementing all
> windows until the three PoCs are ready for live tests. The historical planning
> restrictions below describe that earlier wave; they do not block this START.
> Follow [[../Verification/2026-10-22 Reusable Content Implementation]] for current
> writer windows, rulings, checks and handoffs. Main keeps supervising without
> further continuation approval. No provider spending or real-profile work is implied.

**Goal:** Make all Workbench content reusable and easy to create: Dialogue, Scenario,
Topic, Furniture, Item and Action. New compatible content must use shared typed
functions/definitions rather than require per-content code wiring. Prove this with
the three user-selected scenarios below, not a TV-specific framework.

**Execution cadence override2026-10-24:** the human requires implementation first,
one consolidated relevant validation pass per completed goal, not per change.
Earlier red-first, per-window review loops and validation-only assignments are
superseded. Existing accepted work stays accepted. Preserve security/publication
validation and honest separation of source readiness from live player acceptance.

**Architecture:** Shared revisioned authoring and typed reference/validation contracts
feed a pinned compiled release. One Scenario owner composes dialogue, shared Action
execution and host-authorized effects; furniture functions and game-state writers own
their effects. Human UI and development-agent commands use the same contracts.

**Implementation finish2026-10-24:** six-kind Workbench/normal host delivered,
three authored proofs integrated into shared SQL r90 and baked pinned release
installed; W4 persistent TV identity import/rebind/save/reopen complete. Ready for
live testing, not a startup/gameplay/audio or remaining browser visual pass.
Historical unchecked planning lists below must not restart accepted implementation
or checks. Continuation heartbeat removed; current evidence is in the execution ledger.

**Stack:** existing Unity6000.4.4f1/C#/SQLite-net, standalone Workbench command host
and planned plain browser/stdlib service. No new framework, provider or database by
default. User-facing texts/languages, conditions, parameters and step order are data.

**Spec:** latest human goal2026-10-22, [[Content Workbench Shared Contract]],
[[Content and Interaction ERD]], [[Multi-Agent Coordination]]. The earlier
[[Scripted Actions and Inspect TV Implementation Plan|Action Framework Foundation]]
is a technical baseline; this note supersedes its one-fixture scope and mandatory
return-to-player/automatic closing for Turn on TV specifically.

## Global constraints and meaning of reusable

- AlterOS only, `E:/Pleiades/alteros`; no archive project or copied knowledge store.
- Common primitives/functions may have C# implementations. A new Action may be a
  thin trusted C# composition or a typed data recipe using those same functions.
  No copied navigation/reservation/cleanup engine, per-TV menu switches, raw SQL
  effects or arbitrary reflection/method invocation supplied by authored/LLM data.
- Shared Dialogue/Subflow/Text/Rule references use stable IDs and pinned revisions;
  scenarios bind reusable definitions, not mutable shared execution state.
- SQLite authoring is shared content authority; published runtime release and private
  player state remain separate. Saving a draft is not publication or runtime success.
- Stats/currency/inventory are host authority. A generated response cannot invent
  effect IDs, grant arbitrary amounts or self-declare tool permissions.
- Static game UI is Canvas/prefab-authored. Workbench web UI is a separate authoring
  tool. Do not implement six duplicated editors/validators/writers or compile loose C#.
- Preserve existing roots, GUIDs, source bytes, private data and concurrent edits.
  Shared files/Editor have one acknowledged writer, never an inferred idle-state lock.
- No keys, billable calls, real profile migration, new packages or scene changes in
  planning. Future runtime checks need an isolated synthetic profile or explicit
  human-approved player use. Source/headless, Editor and actual gameplay proof differ.

## Three acceptance scenarios: current human direction

| Proof | Entry | Scenario flow | Success/end evidence |
| --- | --- | --- | --- |
| Turn on TV | Required anchor `Can you hlep me turn on the tv` plus corrected `Can you help me turn on the tv?`; existing favor submenu also references this Topic | Opening VN dialogue -> reserve/move to eligible TV -> invoke registered explicit set-On function -> end | Exact target TV commits On and changes visible state. No mandatory return or further dialogue after power succeeds |
| Locate TV | Anchor `Where is the tv?` | Find/reserve eligible TV approach -> move toward TV -> authored non-modal bark `Hey, it's here` -> end | Arrival and actually delivered bark are correlated; suppression is not reported as a delivered shout. No power change or forced return |
| Fetch a drink | Anchor `Can you fetch me a drink?` | Move to dispenser -> registered mock take-drink animation/function -> return to player -> automatically open VN dialogue `Here, cheer!` -> give one Water glass -> end | Return and dialogue completion precede one durable inventory grant; retries do not duplicate it |

Turn on TV's new end rule supersedes the earlier TV return/auto-closing fixture;
return + dialogue remains a required generic capability and is proved by Fetch a
drink instead. Each occurrence is a Scenario, not a bare Activity command. Shared
host/Scenario lifecycle must not globally assume every Action ends with return or
every terminal event auto-opens dialogue. Scenario steps/policy choose that behavior.

No TV/dispenser, busy/unreachable/unknown state and stale bindings use authored
truthful fallbacks. One active Scenario remains exclusive; both free text and menu
use the same eligibility/definition path. No fixture-specific text-routing branch or LLM
call is required to run the authored fixtures. New anchors require a real bake/index
from the same selected SQL snapshot and a small labelled phrase/negative test set.

### Minimal Item/effect proof chosen by coordinator

Use a small real shared Item definition `item.water_glass`, display **Water glass**,
and a per-player quantity, rather than claiming an inventory change from a text-only
mock. The user allowed this path. Mock only the animation/collection presentation;
its markers/results must explicitly say mock, not claim a finished animation asset.

Add a generic Scenario host-effect step/function for an allowlisted item grant from
validated authored content: item reference and quantity1, operation identity bound
to run/step invocation, one transaction with event/receipt. It is not an arbitrary
LLM inventory delta or a new physical Activity runner. A second item fixture proves
the grant is not hardcoded to Water glass. No full eating/consumables/clothes/gifting
system is required for this proof, but Item definition/edit/validation must be real.

Grant after the delivery VN sequence is completed, not when merely queued/displayed.
No grant on failed collection, failed return or cancellation before the grant step.
Once committed, cancellation cannot silently undo it; a replay of the same step is
idempotent. A new legitimate Scenario occurrence may award another1. Avoid conflating
Scenario replay policy with global one-time item entitlement. Fetching a drink is
not Alterra drinking it: no hydration/stat recovery from this fixture.

## Current source evidence and gaps

Root inspection2026-10-22: ActivityHost currently supports Movement/Drink only;
generic sequence/builder/continuation/function/power visual files are not present.
AuthoringContentKinds does not yet include general Item/Furniture/Action definitions.
Workbench has SQL read/write/list commands and Topic/Scenario validate/preview,
but `catalogue.read` still returns unavailable and no graphical editor was found.
Current home snapshot resources do not themselves install furniture functions.
GameCommitRequest currently carries progression/layout/events/receipts; the minimal
generic item representation and migration strategy need DBMS's concrete plan.

These are source facts, not new compile/gameplay tests. Existing installed content
release evidence and dated test counts retain their original scope.

## Planning wave and exact writers

Main owns this workflow, shared task summaries and final contract consolidation.
Workers write only their named planning note, preserve each other's files, and report
source-backed signatures/files/schema/version needs/test commands and dependencies.
They must not replace main's whole design or start coding from a planning request.

| Existing session | Thread/host | Sole planning deliverable | Scope |
| --- | --- | --- | --- |
| Alterra Behavior Session | `01a144fe-5ddc-7291-b24e-ff2e4e0f4162` / local | [[Reusable Behavior Framework Plan]] | Shared C#/data Actions, Scenario steps/return/dialogue/effects, three flows, pure/headless core and validation |
| Furniture and Environment Session | `01a15376-d6f0-7c41-b9e5-fcdee723e87b` / local | [[Furniture Functions Framework Plan]] | Definition/function descriptors, exact-target discovery/claims, power, locate, mock collection/animation ports, state visuals and Editor handoff |
| DBMS Session | `01a15e76-daeb-7451-982f-95a510093fd3` / local | [[Reusable Content Storage and Items Plan]] | Six-kind source/runtime mapping, reusable refs, minimal Item/count/grant transaction, power state, receipts/migrations and no data loss |
| Topic Playground | `01a142e1-455c-7952-bfcf-14186d64b463` / local | [[Six Kind Workbench Plan]] | Shared discover/CRUD/validate/preview commands, six real adapters, Flow-first UI/forms/agent examples, no duplicate validators or Unity-only dependencies |

Behavior's human-authorized DBMS/Furniture consultations remain allowed, including
their replies. Main routes Workbench questions/contract handoffs; this note does not
invent wider cross-thread messaging authorization. No HEAD/untracked directory is
not by itself a planning blocker; preserve files and define writer windows explicitly.

## Planned implementation phases (not START)

### Integration defaults from the current plan review

These are coordinator API/compatibility decisions, not claims of implemented APIs:

- New Workbench discovery/validation/preview callers use `kindId` for the six UI
  kinds. Existing Topic/Scenario `kind` requests remain accepted; conflicting aliases
  fail with `kind_alias_conflict` at `/kind`. Existing authoring operations retain
  their storage-kind field. UI kinds and SQL source kinds are explicitly mapped.
- `descriptorVersion=1` describes the tooling envelope; producer schema/validator /
  contract versions and database/release versions remain separate dimensions.
- Unavailable adapters/functions must fail honestly during rollout. Full completion
  still requires all six kinds to support real create/read/edit/validate/preview;
  an empty list of available types or fallback-only UI is not a passing gate.
- A preview may read/pin the requested authoring revision through the shared
  repository. It must not mutate the shared store/player state; any simulated state
  changes stay in isolated fixtures and are labelled synthetic.
- Physical execution lifecycle, durable mutation result, bark delivery and completed
  VN acknowledgement are separate contracts. No generic success callback may imply
  the other three or trigger an inventory grant before its actual Scenario step.
- Pure runtime/validator ports remain usable by the BCL command host; the Python
  loopback service is fixed-argv subprocess transport, not an alternate executor.

All four named domain plans have now been delivered and read. The following
coordinator decisions resolve their overlapping proposals; they are implementation
defaults for review, not additional human-approved product features or existing APIs.
Where an earlier domain paragraph differs, this section takes precedence.

### Consolidated producer contract decisions

1. **Independent Dialogue, not an implicit Topic.** Add `dialogue_definition` for
   new reusable Dialogue documents, alongside `scenario_definition`,
   `topic_definition`, `furniture_definition`, `item_definition`, and
   `action_definition`. A Dialogue contains ordered beats/text/expression and no
   mandatory route or anchor. Existing `generic_dialogue` documents remain
   editable/importable under their existing shape and IDs; do not convert or delete
   them. The new adapter normalizes into the existing Dialogue/Beat/Text tables.
   A Scenario references the same pinned Dialogue in multiple flows without copies.
2. **Text is authorable, not a pre-existing-release prerequisite.** Dialogue beat
   text and definition display names support one owned localized Text object or one
   complete pinned Text ContentRef, never both. Owned Text uses a stable child ID
   and existing `translations[{language,text}]` representation. The compiler emits
   the same Text/Translation rows and dependency closure. This is a shared child
   editor, not a seventh store/editor. English/Japanese are initial defaults, not
   hardcoded columns. A new Item can be created without first publishing its label.
3. **One Action shape and executor.** New `action_definition` schemaVersion1 uses
   stable `id`, `capabilityId`, `implementationKind`, and descriptor-validated data.
   `implementationKind=activity_sequence` requires exactly one of `steps` or trusted
   registered `composerId`; both lower into the same validated sequence. For
   `implementationKind=host_effect`, only registered `functionId` and typed arguments
   are allowed. Do not introduce `data_recipe` as another executor or `scriptId` as
   a second discriminator. Existing compiled Actions migrate with
   `implementationKind=legacy_activity`, preserving their capability/contract and
   behavior; new source cannot claim that compatibility kind.
4. **Recipe storage is lossless.** Canonical sequence steps carry `stepId`,
   `primitiveId`, typed arguments, result bindings, success/failure/cancel edges and
   checkpoint policy. DBMS's normalized step records must retain all those fields,
   not just ordinal/function ID. Canonical primitive IDs are `reserve_nearest`,
   `move_to`, `face`, `bark`, `wait`, `invoke_furniture`, `move_near_player`,
   `checkpoint`, `complete`; a descriptor version defines exact arguments/results.
   No alternate `furniture.invoke_function` spelling in serialized recipes.
5. **Effect ref owns its Item binding.** A Scenario `host_effect` step uses existing
   typed `StepRecord.actionRef`; that pinned host-effect Action binds
   `player.item.grant`, pinned `itemRef` and positive authored `quantity` (1 in
   Fetch). Do not add a redundant mutable `StepRecord.itemRef`. Strict version1
   step data names `requiresCompletedStepId`; the compiler validates that named
   Dialogue step and the Action's Item/text closure. Runtime resolves the exact
   completed execution of that step, not merely its definition ID. Generic
   free-form dataJson is never authorization.
6. **Completion and retry identity.** `invocationId` in game history/effect requests
   means the Flow invocation; each Dialogue/effect step has its own distinct
   `stepExecutionId`. Physical Action invocation/sequence-step IDs remain separate.
   Persist the presenter's completed delivery step before grant, or atomically
   alongside grant in the same game transaction. Require matching save/run/Flow
   invocation and actual completed step. `IScenarioHostEffectPort.Apply` is the
   public pure port; any Item-specific DBMS adapter remains behind it, not a second
   public Scenario runner. Receipt lookup precedes stale-revision rejection; a
   retry retains the original immutable payload. An uncommitted stale attempt may
   be rebased only after absence of a receipt is established.
   Behavior's public request names are authoritative: `scenarioRunId` and
   `completedStepExecutionId` map to DBMS storage's `runId` and
   `completedDeliveryStepExecutionId`. These are explicit adapter mappings, not
   additional execution identities. Item/quantity are resolved from the pinned
   Action, never copied from model or presentation arguments.
7. **Separate persistent functions from presentation.** `power.set` uses
   `IFurnitureFunctionPort` and mutation outcomes
   `applied/no_change/replayed/unavailable/conflict/failed` with committed evidence.
   `dispenser.collect_mock` is a descriptor-classified presentation operation:
   a narrow presentation port starts/cancels it and correlates
   `accepted/running/completed/cancelled/failed` with the active sequence. It grants
   nothing and changes no supply/hydration. `invoke_furniture` dispatches through
   that descriptor; there is still one ActivityHost/executor, not a new lifecycle
   runner. A delivered bark and completed VN also require their own acknowledgements.
   The concrete transient port is Furniture's `IFurniturePresentationPort`
   (`Start`, `Poll`, `Cancel`) with its request/handle/result from
   [[Furniture Functions Framework Plan]]. Behavior's `invoke_furniture` primitive
   consumes that interface directly; do not add a second `IActivityPresentationPort`
   schema. At this boundary, Furniture `runId` maps to Scenario run,
   `invocationId` to Flow invocation, `activityId` to physical Action invocation,
   and `presentationRequestId` to primitive invocation. The handle also pins
   Action ref, sequence step and exact reserved furniture binding. Result mapping
   changes names only, never drops correlation or grants durable authority.
8. **Version and state defaults.** The original migration plan was authoring
   SQLite1->2, compiled content4->5 and game3->4. Implementation retains Authoring2,
   adds Content6 after5 for exact Furniture binding tuples (read-only4/5 compatibility),
   and Game5 after4 for durable exact Flow occurrence pins. Content producer review
   is accepted; Game5 integration remains in the bounded persistence fix round.
   Preserve existing artifacts and test migration/rollback separately. Keep
   progression JSON4 unchanged. Furniture power uses the existing versioned
   numeric resource `device.power`, known0=Off/known1=On; absent remains unknown.
   No new layout version is required merely to register that typed resource.
   Minimal Item stackLimit99 is a coordinator fixture default, not economy design.
9. **Pose safeguard is not teleportation.** After both owning interaction and
   Scenario end, a 30-second stale-pose timer may restore default pose. It never
   restores actor/player position, forces a return, or releases safety-critical
   reservations late. New ownership invalidates the old timer; an active Scenario
   blocks this timer even with its dialogue UI closed.

These contracts must be reflected in the domain notes before implementation. A
consumer must not invent a parallel schema to work around a missing producer API.

### P0: Consolidate shared contracts and ownership

- [x] Collect the four source-backed domain plans and check exact interface names,
  DTO/semantic versions, ID/ref/localization conventions and metadata registry shape.
- [x] Resolve Action primitive vs furniture function vs Scenario host-effect authority;
  ensure physical lifecycle, persistent event/receipt and caller permissions each have
  one owner. Define outcome/error/unknown/pending and argument/result bindings.
- [x] Assign exact source/scene/build/test files and sequential writer windows. Publish
  dependency order: pure contracts -> domain handlers/store -> shared execution ->
  authoring/compiler/adapters -> UI + three fixtures -> integrated acceptance.

### Writer windows after an explicit implementation START

| Window | Sole writer and deliverable | Release condition |
| --- | --- | --- |
| W0 | Behavior: portable Activities/Scenario contracts, sequence DTO/descriptors and pure tests | Pure contract sources build without Unity dependencies; Furniture/DBMS acknowledge signatures |
| W1 | DBMS: Authoring/Content/Persistence models, validators, migrations and typed readers/writers; Furniture separately owns only Furniture function/state adapters | Lossless source/recipe/release round trips, synthetic migration and transaction checks; no shared core edits by Behavior during DBMS window |
| W2 | Behavior: ActivityHost/ScenarioSession integration, launch resolver and existing conversation/menu bridges; uses released DBMS/Furniture APIs | Three pure traces, C#/data parity, cancel/replay/late callback and completed-VN effect checks |
| W3 | Topic Playground: all Tools/ContentWorkbench/**, discovery/adapters/service/UI/tests | Six real create/read/edit/validate/preview paths and UI/CLI parity, read-only synthetic preview |
| W4 | Furniture: acknowledged exclusive Unity Editor/scene/prefab presentation window; Behavior/DBMS consult read-only | Import/compile plus focused Editor evidence; no concurrent Editor owner |
| W5 | Main coordinates selected synthetic fixture bake/publication and startup/gameplay acceptance | Explicit reviewed release selection and requirement ledger; no private-profile/provider access inferred |

Before W0, each owner acknowledges exact proposed file paths from its linked note;
an ownership table is not itself a live lock. DBMS owns all shared Content and
Authoring schema/compiler/validator files even when Behavior proposes a change.
Behavior supplies requirements/tests for those files, then consumes their released
APIs. Each window records handoff and release; non-overlapping pure tests/docs may
proceed, but no simultaneous edits to the same file. Main owns shared status notes.

Implementation scheduling amendment2026-10-23: after W1B acceptance, Main released
an independent W3B editing/discovery window to Topic Playground alongside Behavior
W2. Stable Authoring/Content APIs support six-kind CRUD/validation/forms/graph work
without touching Behavior files. Final shared-runtime previews/parity remain gated
on W2 release; W3B alone cannot satisfy Workbench completion. W4 Editor remains
exclusive and later. See the execution ledger for exact writer acknowledgments.

Confirmed consumer gap2026-10-24: Furniture definitions have compiled SQL records,
but the scene catalogue still reads only JSON and needs its global envelope.
[[Furniture SQL Catalogue Bridge Plan]] is Main's bounded technical amendment under
delegated execution authority: same Furniture UI kind, revisioned settings source,
manifest3 envelope plus exact definition refs, and explicit compiled catalogue
consumer. Authoring3/manifest3 are planned here, not current accepted schema claims.

### P1: Runtime/data foundations reusable across content

- [ ] Shared typed primitive/handler descriptors, one sequence executor, C#/data
  composition parity, bounded waits/mock-animation port, checkpoints/cancel/cleanup.
- [ ] Furniture capability/function metadata and exact-instance state operations;
  use existing claims/CAS/persistence owners, not per-object central switches.
- [ ] Minimal Item definition, authoritative quantities and registered Scenario grant
  port/step with atomic receipt/event, per-occurrence idempotence and synthetic tests.
- [ ] Source/headless/Editor tests per owner prove valid/error/stale/duplicate behavior
  before fixture narratives are treated as evidence of reusable infrastructure.

### P2: Authoring/compile contract for all six kinds

- [ ] Map Dialogue/Scenario/Topic/Furniture/Item/Action edits to typed revisioned
  authoring adapters, shared dependencies and explicit release selection/validation.
- [ ] Implement descriptor discovery and metadata-driven inspector/form contracts;
  shared text/translation/ref reuse, reference-aware copy/retire/delete policy.
- [ ] Typed command/agent fixtures create and edit each kind through the same API;
  preserve unknown fields, CAS conflicts and previous releases without silent loss.

### P3: Human Workbench and detached preview

- [ ] Complete the approved standalone loopback service and Flow-first graph /
  selected-step writing inspector, plus catalogue/forms for all six kinds. Not raw
  database editing or a pretend functional HTML mockup. Agent commands stay first-class.
- [ ] Production-backed selection/flow/sequence validation and deterministic synthetic
  preview show consumed gates, action/function/effect results and missing services.
- [ ] UI/CLI save/reopen/preview parity, reference failures, conflict handling, Unicode,
  loopback security and host build without Unity DLL dependency pass their exact tests.

### P4: Three proof scenarios and real acceptance

- [ ] Author the three fixtures via the real tools/typed commands; bake/index required
  anchors and install only a validated reviewed pinned release through existing gates.
- [ ] Validate normal SplashScene -> ModelTest -> T/free text/favor behavior with a
  safe synthetic profile. Inspect real object movement/state, mock markers, dialogue,
  quantity +1, replay/cancel, no-resource fallbacks and post-end-only pose safeguard.
- [ ] Include two compatible TV definitions/independent instances and a second item /
  non-TV recipe, demonstrating no new executor or per-content switch is required.
- [ ] Produce requirement-by-requirement evidence, not a single green-test total.

## Five review risks

1. A nice TV demo could conceal a framework tailored to it: P1/P4 require unrelated
   recipe/model/item reuse and edits without engine changes.
2. Generic metadata could grant arbitrary effects: P0/P1 require installed trusted
   handlers and pinned authored effect authority, never model-declared permissions.
3. Repeated dialogue/return callbacks could mint items: P1/P4 require delivery-step
   identity, transactional receipts, replay versus new-occurrence tests and failures.
4. Six editors could diverge from the game: P2/P3 require shared validators/core and
   UI/CLI/compiled/runtime parity, not JavaScript copies or fake availability.
5. Concurrent writers could lose source/player data: P0/P2 require acknowledged file
   ownership, snapshot/CAS/migration tests and no real profile in planning/fixtures.

## Planning completion and later goal acceptance

Planning reconciliation finished2026-10-22. All four owners supplied aligned notes
and final reports; their planning turns are terminal. The master plus four plans
and evidence ledger exist with frontmatter and resolvable note targets. The source
seams and concrete proposed contracts were checked; duplicate presentation DTOs and
a broken multiline wikilink were corrected. See the dated evidence ledger for
exact check scope. These static checks do not validate a future executable schema
or prove runtime behavior.

Next action is an explicit source implementation START for W0; no further product
decision is requested for this planning slice. Until then, leave the owners idle
and do not repeatedly dispatch reviews, poll unchanged state, or start any W1-W5
work from these notes. The requested3:10AM follow-up should report ready status
without redoing this finished planning work if nothing has changed.

Planning completion means four compatible domain plans, clear versioned contracts /
writer order/test gates and a consolidated human-reviewable implementation plan.
That does NOT prove the runtime/tooling goal achieved. Keep runtime/content/wiring /
live status separate; do not mark the wider goal complete from plan approval or the
old TV-only test. Implementation needs its own reviewed START, then the P1-P4 evidence.

No source, catalogue, database, scene, profile, package or provider writes are
authorized by this planning wave. Store only shared synthetic examples in the vault.
