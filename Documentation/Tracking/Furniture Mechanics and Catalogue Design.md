---
title: Furniture Mechanics and Catalogue Design
date: 2026-10-21
updated: 2026-10-22
tags: [alteros, design, furniture, catalogue, authoring]
status: bounded-implementation-authorized
---
# Furniture mechanics and catalogue design

Related: [[M3 Furniture and Activities Design]], [[M3 Furniture and Activities Handoff]],
[[Room Testbox Handoff]], [[Topic Scenario Activity Technical Reference]],
[[Topic Scenario Selection]], [[Runtime Observability]]. Root `TODO.md` owns status.

This consolidates approved furniture principles from 2026-10-19 and the human
decisions on 2026-10-21. Current implementation status (2026-10-22): F1–F4
catalogue, resolver, reservation, layout and actor-bound ActivityHost foundation
is implemented; I1–I2 inventory, handling and SQLite home persistence are
implemented; I3–I5 shared Debug Mode, input, Canvas/prefab and ModelTest wiring
are in place. The current source/Editor evidence and live acceptance boundary are
in [[Furniture Inventory and Placement Implementation Plan]] and
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].
No live profile, Play Mode or player-visible acceptance is claimed. General
functional-point discovery for runtime-created furniture and additional seating,
sleeping or TV executors remain separate work.

## Approved experience

Implementation follow-up2026-10-21: the human authorized bounded implementation
plans and dispatch. [[Furniture Foundation Implementation Plan]] now owns Room
Testbox's executable slice, [[Content Workbench Implementation Plan]] owns Topic
Playground's standalone workbench, and [[Content Workbench Shared Contract]] pins
their interfaces. Earlier "START pending" wording below records the design-only
handoff history; it does not revoke this later bounded authorization. Physical
seating/cameras and full furnishing UI remain outside the foundation plan.

The first authoring example is "Wanna sit together?": invite Alterra, find two
usable seats facing each other, move/use the seats and converse. A table is
**optional for basic sitting together**. A usable shared table can unlock extra
dialogue options or context-specific variants. Do not require a table for every
conversation or treat owning a table somewhere in the house as sufficient.

Player participation approved2026-10-21: Alterra walks to her assigned seat and
waits. The Player manually approaches the other seat and presses T to sit/join,
using the current `Player.Interact` keyboard binding.
Do not automatically move, seat or take control of the Player when the invitation
is accepted. Joining must revalidate the bound arrangement and participant point;
the precise seating transition/camera remains later implementation design.

Chairs and tables are separately placeable furniture, not mandatory combined
prefabs. Placement determines their relationships. Furniture can remain decorative
or retain independent uses when a particular combination does not work.

Furniture enables possibilities; it does not contain dialogue scripts or grant
arbitrary Agent tools. A Topic identifies semantic intent; its selected content
and executable Scenario refer to supported requirements/actions. The host binds
real furniture points and executes mechanics. See the technical reference for
the distinction between `TopicScenario` and executable `ScenarioDefinition`.

## Data ownership

Furniture definition fields below match the supported F1 catalogue DTO/schema.
Resolved arrangement/choice and reservation contracts are supported portable
F2 data. Saved furniture instances and revisioned placement commands are supported
F3 data and are now wired to the existing SQLite game owner and ModelTest room.
Restart/player-profile acceptance remains pending.

| Record | Owns | Must not own |
| --- | --- | --- |
| Furniture definition | Stable type ID, category, scale policy/default, bounds, support kinds, navigation policy, typed points | Room placement, occupancy, progress, executor or dialogue |
| Furniture instance | Stable instance ID, definition reference, transform/support/revision, enabled/condition/resource state | Copies of shared dialogue or definition data |
| Functional point | Local pose and facing, typed purpose, participant/access constraints; appropriate approach/pose/use/exit references | Reliance on mesh pivots or model forward axes |
| Relationship rule | Compatible point types, geometric/access constraints, deterministic ranking | Hardcoded room coordinates or a universal radius for everything |
| Resolved arrangement | Specific instance/point IDs, roles, evidence/revisions and validity | Permanent capability grants based only on proximity |
| Reservation | Claims on actual required points/resources and their execution owner | Blanket occupation of every point on a multi-seat mesh |

Multiple usable points can exist on one item. Shared activities require a
consistent binding for all needed participant positions before movement starts.
Acquire required point/resource claims atomically in stable-ID order and roll
back every acquired claim if any member is unavailable.

## Relationship discovery and validity

Retain the previously approved algorithm boundary:

1. Discover candidates with a **3D sphere** near the relevant functional point.
   Unity Y is height; X/Z form the ground plane. This is broad-phase only.
2. Validate compatible types, relationship-specific distance/facing, usable height
   and reach, obstruction and accessible approach/exit. A point behind a wall or
   on another floor does not become usable merely by entering the sphere.
3. Rank valid candidates by relevant alignment, then distance, then stable IDs.
   Relationship-specific tolerances are catalogue/rule data, not per-room code.
4. Recompute affected relationships on movement/rotation/removal or relevant
   state changes, not an all-pairs search each frame.
5. At activity start, bind exact IDs/revisions and reserve needed points. Do not
   silently switch between nearby chairs/tables during the execution.
6. Revalidate affected bindings and use safe cancellation/cleanup if moved,
   disabled, removed, blocked or occupied. No teleport or fabricated success.

An optional authored association can choose an intended candidate for unusual
layouts. It cannot bypass compatibility, reachability or reservation safety.

Chair→workspace tests use work-facing alignment and seated work height.
Seat→TV uses viewing geometry/visibility, not the chair/table height rule.
Face-to-face seating needs a participant arrangement distinct from two adjacent
chairs facing the same desk. Exact predicates/tolerances are not yet locked.

## Catalogue starting scope

Human-approved initial catalogue2026-10-21:

- **Seat/chair:** one typed seat with seated facing and approach/exit. Support
  independently usable seats; do not assume one point per furniture instance.
- **Table/desk:** a usable surface with functional edges/positions, orientation,
  usable-height information and supported relationship types. Include both a
  general table and a study-table variant.
- **Bed:** define its placement/support and functional points; rest/sleep execution
  is not implemented merely by adding the definition.
- **Water dispenser:** map the existing M3 definition/use-point semantics before
  proposing any migration. Do not invent water inventory the fixture does not have.
- **TV:** define viewing target/facing; viewing relation differs from seat/table use.
- **Window:** a placeable window item, not an application window. Wall/opening
  mounting and host-surface constraints need explicit design, not an assumption
  that every furniture item is a free-standing floor obstacle.
- **Carpet/rug:** floor-supported decoration. Approved overlap rule: a rug may
  layer with recognized furniture in either placement order; walls/room geometry,
  actors and unknown blockers remain unsafe. Broader walkability behavior remains
  a separate rule. The current physics evidence is in the I4-I5 pre-live report.

These are approved catalogue categories, not approval to implement every activity.
Couch/multiple seats remain later extensibility examples. Cosmetic economy,
home expansion and a complete needs scheduler are not required for this contract.
The human additionally requested preparation of furniture arrangement mechanics;
the design/implementation proposal boundary is recorded below.
Room Testbox should propose stable IDs/units and catalogue ownership; neither
these bullets nor the presence of a definition proves an executor exists.

## Requirements versus bonuses

Use explicit supported conditions, not furniture-name matching:

- **Hard gate:** this option/activity needs a usable arrangement; it is not
  executable until that requirement is satisfied.
- **Contextual bonus:** base interaction remains eligible; a condition adds a
  variant's weight or makes an additional branch eligible. Priority/weight policy
  remains owned by the existing Topic selector; do not create a second sampler.
- **Bound context:** a table-dependent option checks the table associated with
  these seats/participants, not any table in the room or global player ownership.

Illustration: two compatible facing seats allow ordinary sitting conversation;
the same seats associated with a usable table allow table-specific conversation.
Other objects unlock only content whose actual requirements and host capabilities
are satisfied. A table does not itself implement games, tea service or coding tools.

## Locked dialogue options and tooltips

Human-approved design: support **available**, **locked** and **hidden** choices.
Current production filtering hides unavailable choices; a locked-choice display
and tooltip contract are new work, not an existing supported field.

| State | Presentation | Behavior |
| --- | --- | --- |
| Available | Normal choice | Revalidate at click; proceed only if still eligible |
| Locked | Visible disabled choice | Hover shows a short player-friendly reason; never advances or requests an action |
| Hidden | No choice rendered | Use for spoilers/private/undisclosed conditions; no tooltip revealing them |

Authored policy chooses whether unmet requirements lock or hide. The host derives
the actual state from game evidence. Provider text cannot assert "unlocked" or
grant capabilities. Multiple failed gates need a deterministic primary hint and
an inspectable complete diagnostic list. Unknown context must not be presented
as a proven missing object, and must never grant execution.

Example player hint: "Needs a usable table between your seats." Tooling may show
more precise causes: missing compatible point, wrong facing/height, blocked path,
occupied/disabled resource or stale revision. Player wording must not leak hidden
conditions, private state or developer IDs.

Assistant accessibility recommendation for refinement: offer the same hint on
keyboard/controller focus; reserve a future touch equivalent. A disabled button
must still have a hover/focus inspection target without becoming executable.
Do not rely on disabled Unity Selectable behavior to deliver this automatically.
Exact input behavior and tooltip timing/layout remain design details.

Static choice/tooltip structure, disabled appearance and styling must be authored
in Canvas/prefabs through the Editor. Runtime code binds state/reason and may
position a genuinely dynamic tooltip; it must not construct the static HUD.

## Detailed refinement: catalogue and relationship resolution

F1 catalogue DTOs/JSON, F2 arrangement/choice/reservation contracts and F3
saved-layout/command DTOs are implemented at the paths recorded below. They began
as portable source contracts; I1-I5 later wired schema-2 inventory/layout through
the SQLite game owner and ModelTest. The current integration and live-test boundary
are in the I4-I5 pre-live report. Geometry tolerances remain catalogue values and
need real-room calibration.

Historical F1/F2/F3 source checkpoint (2026-10-21): schema v1 and catalogue `1.0.0` are implemented in
`Assets/AlterOS/Furniture/` with canonical source at
`Assets/StreamingAssets/Furniture/catalogue.json`. Filtered and full portable
suite passed 43/43, and reflection confirmed no UnityEngine or UnityEditor
reference. The shared contract records the DTO surface at that checkpoint,
including the additive snapshot `legacyAliases` field and F2/F3 call shapes.
Later I1-I5 evidence supersedes its Unity import/wiring gap; live gameplay and
cross-platform acceptance remain open.

### Versioning and identity

Use one versioned, authoring-neutral catalogue document with `schemaVersion: 1`,
`lengthUnit: "m"`, `angleUnit: "deg"`, and coordinate convention
`unity_local_y_up_positive_z_forward`. Validate finite geometry, unique IDs,
broken references, duplicate point definitions, and unsupported point/rule types.
Unknown major versions fail closed. Minor compatible additions require a
documented validator policy.

- `definitionId` identifies a reusable type: `furniture.seat.basic`,
  `furniture.table.general`, or `furniture.water_dispenser`.
- `instanceId` identifies one placed item in its owning room/home. Create it
  once; preserve it through moves, reloads, and catalogue reorder. Never derive
  it from a transform, display name, array index, or mesh name.
- `pointId` is stable within an instance, such as `seat.player`,
  `seat.alterra`, `surface.main`, or `drink`. The exact point reference is the
  tuple `(instanceId, pointId)`. Keep existing M3 full use-point IDs as
  compatibility aliases during migration.
- `relationshipRuleId` identifies a typed rule such as
  `relationship.seat.face_pair` or
  `relationship.seat_pair.table_context`.
- `arrangementId` identifies the selected rule and sorted role-to-point
  assignments. Its separate revision vector changes when proof changes.
- `activityId`/reservation owner remains the existing ActivityHost ticket;
  it does not replace furniture instance or point identity.

The revision vector includes catalogue definition, rule, instance, functional
point, enabled/condition state, and relevant access/navigation evidence. Use
canonical data or explicit revisions; Unity process-local instance IDs are not
durable identity.

### Supported portable records and remaining boundaries

| Record | Fields / status | Ownership and validation |
| --- | --- | --- |
| Catalogue document | F1 `schemaVersion`, `catalogueVersion`, units/axes, `definitions[]`, `relationshipRules[]`, `capabilities[]`, `legacyAliases[]` | Shared authored data; reject duplicate IDs, unsupported major versions, broken refs and non-finite geometry. |
| Furniture definition | `definitionId`, display/localization key, category, fixed scale/default, local bounds, support kinds, navigation policy, point templates | F1 shared type data; does not own placement, occupancy, executor or dialogue graph. |
| Furniture instance | `instanceId`, `definitionId`, room transform, support ref, enabled/condition/resource state, revision | F3 per-home save record; scene is first-load seed only. |
| Functional point | local `pointId`, `pointTypeId`, pose/facing and use IDs; F2 world snapshot adds access evidence/revision | F1 template plus F2 portable room evidence; no Unity transform or Physics types. |
| Relationship rule | stable ID/kind, point types, geometry thresholds | F1 declarations; F2 currently resolves face-pair and table-context only. Other registered kinds remain unknown/unavailable. |
| Resolved arrangement | `arrangementId`, rule, role bindings, revision vector/token, reason/diagnostics, reservation refs | F2 host-independent evidence result; becomes stale on a dependency revision change. |
| Reservation set | owner ticket and exact point refs | F2 atomic point-scoped claims; idempotent release; contextual-only table is not claimed. |
| Choice requirement/result | choice/rule IDs, lock/hide policy, safe reason, optional captured arrangement/revision | F2 pure evaluation; Unity Canvas presentation and Scenario advancement remain F4. |

Keep point geometry roles distinct: `pose` is the actor/prop alignment;
`approach` and `exit` are route anchors; `use` is the validated interaction
target. A seat has a seated pose/facing and approach/exit. A table has usable
surface positions, height/clearance, and access anchors. The current dispenser
keeps its exact four approach/pose/use/exit transforms.

Serialize a functional point relative to its furniture instance. Resolved
arrangements may carry world-space measurements as current runtime evidence;
authored dialogue and provider output never store world coordinates.

### Starter catalogue entries

| Definition / instance example | Functional points | Relation/use | Notes |
| --- | --- | --- | --- |
| `furniture.seat.basic` | `seat` (`seat.sit`) | `relationship.seat.face_pair` | One independently claimable point template per instance. |
| `furniture.table.general` | `surface.main` (`table.surface`) | `relationship.seat_pair.table_context` | Optional relation to an already valid seat pair. |
| `furniture.table.study` | `surface.work` (`table.workspace`) | `relationship.seat.workspace` | Separate from the optional general-table context. |
| `furniture.bed.basic` | `rest.surface`, `rest.entry` | Future rest/sleep use | Catalogue only until an executor is explicitly approved. |
| `furniture.water_dispenser` | `drink` (`dispenser.use`) | `needs.drink_water` | Aliases `testbox.water_dispenser` and `.drink`; no water inventory. |
| `furniture.tv.basic` | `view.target` (`tv.view_target`) | `relationship.seat.viewing` | Visibility/access rules, not table-height rules. |
| `furniture.window.basic` | `window.opening` (`window.opening`) | Placement/support relationship | Requires a wall-opening support identity. |
| `furniture.carpet.basic` | No functional point | Decorative placement | Does not block navigation by default. |

These are catalogue declarations; they do not assert that these items exist in
ModelTest or that a seating executor exists. A later multi-seat definition or
instance override can provide multiple independently claimable seat points; a
pair rule still requires distinct points.

### Relationship rules and resolver

Use broad phase then rule-specific narrow phase. The broad phase is a 3D sphere
around the relevant functional point, with Y as height and X/Z used only for
ground-plane calculations. Sphere membership never proves a usable relation.
Remove disabled or unsupported points before ranking.

Each relationship rule owns its `broadPhaseRadiusMeters`; choose it to cover
that rule's maximum anchor separation plus the largest participating bounds
offset. It is only a candidate-search radius, never an eligibility test. Do not
put a single universal proximity radius in the furniture definition.

The proposed `seat.face_pair` rule takes two distinct `seat.sit` points and
assigns `player` and `alterra` roles. Candidate checks:

1. Both instances and points are enabled, supported, and unclaimed by an
   incompatible owner.
2. Seat anchor separation is within the rule's min/max; each forward vector
   (local +Z transformed to world space) points toward the other anchor within
   the maximum angular error.
3. Seat heights are compatible; approach and exit clearances fit the actor
   capsules; each required approach is reachable or access evidence is marked
   unknown/unavailable.
4. Capsule/line obstruction checks cover participant paths and interaction
   clearance. A wall or another floor cannot pass because the anchors happen to
   fit the broad-phase sphere.
5. Rank valid pairs by maximum facing error, combined facing error, participant
   approach cost, then the ordinal tuple of role-bound point IDs. No random
   weighting is used for physical binding.

Starting values for **detached fixtures only**: seat separation `0.65–2.2 m`,
facing deviation at most `30 deg` per participant, and seat-anchor height
difference at most `0.20 m`. Measure real seat pivots, actor capsules, and
locomotion clearance before using these in a room. Definition-specific access
envelopes can narrow them; they cannot disable collision, reachability, or
reservation checks.

`seat_pair.table_context` consumes a valid `seat.face_pair` result and one
`table.surface`. Require the surface projection to lie between the seat anchors
within a proposed `0.35 m` lateral allowance, within both participants' authored
reachable bands, reachable itself, and clear of collisions/access routes. A
table outside the relation, on another floor, too high/low, blocked, or owned
globally elsewhere is not an association. Exact table dimensions and reach band
remain tunable catalogue data.

An authored override may identify the intended instance/point assignment. It
still runs compatibility, facing, height, access, obstruction, state, and
reservation checks. If it fails, report that result rather than silently
binding different furniture. Without an override, choose the first valid
candidate by stable rule ordering.

Recompute only relations affected by changed instances/points, rule version,
or relevant navigation/access state. Publish a replacement only after it is
fully resolved. An executing activity retains its exact captured binding and
does not migrate to nearby furniture as distance changes.

### Host result, locked choices, and revalidation

The host should return a structured `ArrangementEvaluation`, not a boolean:

```text
state: available | unavailable | unknown
arrangementId: stable identity when bound
revisionVector: definition/rule/instance/point/access revisions
roleBindings: player -> exact seat point; alterra -> exact seat point
primaryReasonCode: one safe reason for player-facing copy
diagnostics[]: failed predicates and involved IDs for developer tooling
```

`available` needs proven current evidence. `unavailable` means a tested
predicate failed; `unknown` means required scene/access evidence was incomplete.
Do not describe unknown as “you do not have a table.” Hidden choices receive no
reason that could expose a private condition. Locked copy uses authored,
localized reason keys, not provider text or raw instance IDs. Developer
diagnostics may include IDs, measurements, and the full failed-rule list.
When authored policy says `lock` but evidence is `unknown`, use a generic
“cannot confirm availability” hint or hide the choice; do not show a specific
missing-object reason unless the host proved that object absent.

At presentation the host evaluates the authored requirement and returns the
choice state plus a safe reason key. On selection, re-evaluate against current
instance/point/reservation revisions. A stale or occupied choice neither
advances the Scenario nor issues an activity request. Refresh the choice from
current evidence through the existing presenter; if a replacement arrangement
is still valid, require a new explicit click before starting against its new
revision. Once the ActivityHost accepts, pin the exact arrangement and do not
rebind it. The author chooses lock vs hide for each requirement; the proposed
schema requires an explicit choice rather than a global default. A hidden
choice reveals nothing. A locked choice stays non-executable and exposes its
safe hint on pointer hover and keyboard/controller focus once that behavior is
designed.

For a shared sit-together action, the host resolves participant roles and
provides only an allowlisted arrangement reference to the action path. Content
does not name transforms or claim completion. Bind both exact seats before
movement starts. Acquire both point claims in stable-ID order as one
transaction; if either is occupied or stale, release all claims acquired by
that attempt. Include a table in the reservation set only if the selected
activity actually uses the surface. A table used only as contextual dialogue
evidence is not automatically an exclusive resource.

### Scenario and Topic bridge shape

Keep `TopicCondition` and `TopicScenarioSelector` responsible for content
eligibility, priority, and weighting. The current evaluator supports
`all/any/not`, stat, time window, fact, object flag, and object number conditions
with tri-state unknown. It has no rule-specific facing, multi-role binding, or
point revision semantics. Host-resolved relationship evidence may be projected
as typed Topic objects/facts for contextual variants, but the host supplies it;
provider output cannot create it. Do not add a second sampler.

Keep executable furniture capability authority in the host. Current
`ScenarioRuntimeSnapshot` has exact capability argument allowlists and
`ScenarioObjectSnapshot(objectId, kind)` only. For a future seating action,
propose a host-owned read-only arrangement binding with role refs and revisions,
plus an allowlisted arrangement token argument. `ActivityHost` re-resolves the
token and revision at request start. No coordinates, relationship JSON, or
reservation authority go in Scenario/provider text.

`CanonicalDialogueChoice` currently has only `label` and `nextId`; a Scenario
question option has `optionId`, `label`, `value`, and `nextStepId`. Neither
supports lock state, requirement IDs, or tooltip copy. Propose an additive
authored choice-availability field with stable `choiceId`, requirement reference,
and `unmetBehavior: lock|hide`; a presenter-only host result carries current
state/reason. Keep the field optional for old JSON and validate it before
routing. This is new contract work, not currently supported data.

### Source reuse and compatibility boundary

| Existing seam | Reuse | Do not assume |
| --- | --- | --- |
| `FurnitureUsePoint` — `Assets/AlterOS/Activities/FurnitureUsePoint.cs` | Compatibility adapter for exact local points and current M3 dispenser IDs/transforms; its `Revision` is a useful invalidation seed. | Not a definition/instance catalogue, relationship graph, multi-point object model, or durable save schema. |
| `FurnitureRegistry` — `Assets/AlterOS/Activities/FurnitureRegistry.cs` | Low-level exact per-point claim and deterministic distance/ID tie-break for the current dispenser executor. | Does not reserve several seats atomically, evaluate relationships, or claim table resources. |
| `ActivityDefinition` — `Assets/AlterOS/Activities/ActivityDefinition.cs` | Reuse for executor/capability tuning and hydration checkpoints. | It configures actions; it is not a furniture type definition. |
| `ActivityHost` / `ActivitySceneHost` — `Assets/AlterOS/Activities/ActivityHost.cs`, `ActivitySceneHost.cs` | Preserve single execution ownership, host-allowlisted capabilities, run/step/payload identity, scene binding, and current capability capture. | Does not own furniture definitions or arbitrary action logic. `ActivityHost` intentionally has one active executor. |
| `ScenarioRuntimeSnapshot` — `Assets/AlterOS/Scenarios/ScenarioDefinition.cs` | Reuse host-owned exact argument allowlists and typed objects. An arrangement token can later be an allowed string value. | Current objects do not carry relationship roles or revision vectors. |
| `TopicConditionEvaluator` / selector — `Assets/AlterOS/Topics/TopicConditionEvaluator.cs`, `TopicScenarioSelector.cs` | Preserve tri-state semantics and current priority/weight policy for content selection. | Current conditions do not implement geometry or locked choices. |
| `CanonicalDialogueChoice` / Scenario question options / Canvas — `Assets/AlterOS/Dialogue/DialogueDefinition.cs`, `Assets/AlterOS/Scenarios/ScenarioDefinition.cs`, `Assets/Scripts/Dialogue/DialogueManager.cs` | Reuse the existing authored choice presenter; extend data/presentation only in a separately approved UI slice. | No current lock state, disabled choice contract, or hover/focus reason exists. |
| `activity_scenarios.json` / `DirectConversationBridge.ActivityScenarios.cs` — `Assets/StreamingAssets/Dialogue/topic/activity_scenarios.json`, `Assets/Scripts/Dialogue/DirectConversationBridge.ActivityScenarios.cs` | Preserve current Follow and Drink menu ownership and return/bark lifecycle. Keep `needs.drink_water` and `available_dispenser`. | Not a furniture catalogue, sit-together action, or general provider/tool schema. |

Migration recommendation: explicitly map each legacy `FurnitureUsePoint` to a
`definitionId`, `instanceId`, and local `pointId`, then verify use IDs and four
transforms. Future mutable placed instances belong to home-save state, not the
Unity scene after seed import. Preserve `testbox.water_dispenser`,
`testbox.water_dispenser.drink`, and `needs.drink_water` through the first
migration. Do not mass-edit Unity YAML or `.meta`; serialized scene wiring stays
Editor-owned. Legacy scenes without a catalogue link should load through the
adapter or fail closed with a clear diagnostic. That was the 2026-10-21 room
fixture checkpoint. The schema-2 home-save adapter and ModelTest wiring were
added later; see the current I2/pre-live verification.

### Walkthroughs and negative cases

| Situation | Resolver result | Choice | Execution |
| --- | --- | --- | --- |
| Two reachable, compatible facing seats; no table | `seat.face_pair` available; exact player/Alterra points bound; table relation unavailable. | Base “Wanna sit together?” available. Table-only choice follows authored lock/hide policy; locked copy can say “Needs a usable table between your seats.” | Reserve both seat points; no table claim. |
| Same pair plus a usable table between them | Both pair and table-context relation available. | Base choice remains; extra option or contextual variant may appear. | Claim the surface only if the chosen activity uses it. |
| One seat faces away | Facing predicate fails; diagnostics include role/angle. A different valid pair may rank next. | Locked safe reason or hidden, per author policy. | No request for the invalid pair. |
| A seat is behind a wall or on another floor | Access/obstruction fails, or is unknown if required evidence is incomplete. | Do not claim a table is missing; only show a safe access reason if locked. | No partial motion or reservation. |
| A seat becomes occupied | Geometry can pass but atomic acquisition returns `point_occupied`. | Selection recheck refreshes current state. | No partial claim if the second seat acquisition fails. |
| Seat/table moves after start | Captured revision no longer matches. | Pending choice refreshes; click cannot advance from stale state. | Safe cancel, release every claim once, truthful failure; never swap furniture mid-activity. |
| An unrelated table exists elsewhere | It fails between/reach/surface checks; global ownership is not association. | Table-only choice stays locked/hidden while the base pair can remain available. | No table claim. |

### Placement, saved layout, and cross-platform reconstruction

The following is the approved design scope for preparing arrangement mechanics.
It does not assume a home-save service currently exists and does not authorize
runtime implementation or a room rebuild.

#### Stable starter definition IDs and functional scope

Use these stable type IDs in the shared catalogue. Display names and asset
references can vary by platform; the IDs, point templates, units, and rule
semantics must not.

| Type ID | Points / support | Intended relations and limits |
| --- | --- | --- |
| `furniture.seat.basic` | One or more `seat.sit` points with seated pose/facing, approach and exit clearance | Face-pair, workspace, and viewing relations. Each point is independently reservable. |
| `furniture.table.general` | `surface.main` and optional edge positions; floor support | Seat-pair table context and future explicitly implemented surface uses. No activity is implied. |
| `furniture.table.study` | `surface.work` with workspace-facing edges, seated-height band and chair clearance | `relationship.seat.workspace`; does not substitute for a table-between-seats relation unless its geometry independently passes that rule. |
| `furniture.bed.basic` | `rest.surface`, `rest.entry`, approach and exit; floor support | Placement and future rest relation only. No sleep action or need effect is implied. |
| `furniture.water_dispenser` | Existing `drink` point and Approach/Pose/Use/Exit anchors | Preserve `testbox.water_dispenser`, `testbox.water_dispenser.drink`, and `needs.drink_water` via explicit migration aliases. No inventory is introduced. |
| `furniture.tv.basic` | `view.target` with facing/visible-area geometry; floor or authored support | `relationship.seat.viewing` uses visibility, angle and distance, not table height. No viewing executor is implied. |
| `furniture.window.basic` | `window.opening` attached to a wall/opening support frame; full local pose | Wall-mounted fixture; requires a valid host surface/opening and cannot be treated as a free-standing floor obstacle. No scenery or interaction behavior is implied. |
| `furniture.carpet.basic` | Floor support and footprint; no blocking functional point by default | Decorative floor placement. Walkability and overlap are separate placement rules; default carpet does not block navigation or count as a wall. |

For a small catalogue, these may be definitions in one document rather than
separate files. Furniture definitions declare geometry and typed functional
points once. Relationship rules refer to point types. Activity/executor
capability descriptors refer to stable capability IDs and required point/use
types; they do not duplicate per-instance placements.

#### Placement and edit operations

Expose a bounded command contract for `place`, `move`, `rotate`, and `remove`.
Each command names the home/room, stable `instanceId`, expected instance
revision, requested room-local transform, and support identity where relevant.
The host validates and commits each operation atomically, then increments that
instance revision and invalidates only dependent relationships.

- `place` allocates a stable instance ID once and checks definition support,
  room bounds, structural collision, required mount/floor support, and minimum
  access clearance. It may commit a structurally safe arrangement even when an
  optional relationship such as table-between-seats is invalid.
- `move` and `rotate` use compare-and-set on the expected revision. Reject
  stale editor commands; do not overwrite a newer move. Rotation is full
  orientation, including wall-mounted/window orientation.
- `remove` uses the same revision check, removes the instance from the active
  layout, marks dependent arrangements unavailable and prevents new reservations.
  Existing references remain diagnosable through their captured IDs/revisions.
- `scale` is stored as dimensionless XYZ scale. Definitions may constrain it
  to uniform or fixed scale; non-finite, non-positive, out-of-range or
  definition-incompatible values reject. Dimensions are never serialized in
  screen pixels.
- Floor items require a room-floor support identity; wall fixtures require a
  wall/opening identity and local attachment pose. Support references are
  validated when loading and after host geometry changes.
- Decorative-invalid means the object is structurally safe to place but fails
  one or more optional use relationships. Structural collision, outside-room,
  unsupported mount, and unsafe clearance failures reject the operation.

For the first catalogue version, all eight starter definitions use
`scalePolicy: fixed` with scale `(1,1,1)`. This keeps authored bounds and
clearances stable while preserving an explicit scale field for cross-platform
round trips. A later definition may opt into `uniformRange` or `axisRange` with
finite positive bounds and a default; no implicit scaling mode is allowed.

The future home-layout service owns command acceptance, save transactions and
durable instance revisions. A Unity room adapter supplies current room bounds,
collision, support, navigation and access evidence; it does not become a second
save owner. Validate the full proposed document and affected structural
constraints first, persist the changed layout atomically, then publish the new
room snapshot/revisions. If persistence fails, retain the previous live snapshot
and report failure. Relationship invalidation follows the committed instance
revision; navigation evidence is refreshed when a moved object changes a
blocking footprint or route. A default nonblocking carpet does not invalidate
navigation solely because its decorative footprint moved.

Normal player/editor placement commands that touch a currently reserved point
or shared resource return `point_in_use` / `resource_in_use` without mutation.
This is the recommended active-use protection. If an external scene or support
change invalidates a running binding despite that guard, the activity safely
cancels, releases every claim idempotently, and reports interruption; it does
not rebind, checkpoint success, or apply an effect. A reservation on one seat
does not lock an unrelated decorative surface or every point on the same item.

#### Home layout document and save ownership

The player home save owns the mutable placed-instance layout and persistent
condition/resource state. A Unity scene is a versioned starter seed. On first
home initialization, import that seed into the home document; after that, load
the saved document as authority. Changes to a seed version do not overwrite an
existing player layout. A deliberate migration may add/remove/reposition
instances only through a reviewed migration with stable ID mapping and backup;
it must not reset unrelated player placement or state. Active activities and
reservations are transient and never restored for automatic resume.

F3 v1 platform-neutral layout baseline:

```text
HomeFurnitureLayoutDocument {
  schemaVersion: 1,
  homeId, roomId,
  catalogueVersion, seedVersion,
  coordinateFrame: "room_local_y_up_positive_z_forward",
  lengthUnit: "m",
  instances: [{
    instanceId, definitionId,
    positionMeters: { x, y, z },
    rotation: { x, y, z, w },
    scale: { x, y, z },
    support: {
      supportId, supportKind, supportFrameRevision, attachmentPointId,
      localPositionMeters, localRotation
    },
    enabled, conditionState, resourceState, instanceRevision
  }]
}
```

Approved implementation extension (2026-10-22): home layout schema 2 adds an
`inventory` array of stable `definitionId` and nonnegative `storedCount` entries.
The portable reader still accepts schema 1 without auto-upgrading it; the
coordinated SQLite adapter owns one-time schema-1-to-2 initialization with 10
additional units for each approved starter definition. Unknown inventory IDs
and placed orphans remain preserved. The DTO/validator/session source is now in
place. This paragraph's statement that the SQLite upgrade and atomic
inventory/layout/event transaction were pending I2 describes the initial I1
checkpoint; I2 is now implemented and synthetically verified. See
[[Furniture Inventory and Placement Implementation Plan]],
[[../Verification/2026-10-22 Furniture Inventory I1 Source]], and the current
[[../Verification/2026-10-22 Furniture Inventory I4-I5 Pre-Live Implementation]].

Support identity refers to a stable support registry, not necessarily a
furniture instance. The room adapter exposes `room.floor` and stable wall/opening
supports such as `room.wall.north.opening.01`; each record includes a
`supportKind` (`roomFloor`, `wallSurface`, `wallOpening`, or an explicitly
supported furniture surface), a support-frame revision and a named attachment
point. Its local attachment pose is stored relative to that support frame.
Moving or replacing a support changes its revision and triggers revalidation.
Missing supports preserve the instance as an inert orphan and block placement
reconstruction until the support returns or a reviewed migration maps it.

Position uses room-local metres: Unity X/Z are the floor plane and Y is
elevation. Rotation is a normalized quaternion to preserve wall mounting and
full orientation; scale is dimensionless. Every platform uses the same room
origin/orientation, axis convention, numeric precision policy and schema. The
saved result is independent of camera, aspect ratio, viewport, DPI, pixels,
input device and rendering quality. Do not round transforms to screen units.
Validate finite values, quaternion normalization, scale constraints, room
bounds and support references on load. A schema-major mismatch fails closed
with a recoverable diagnostic; compatible additive minor fields follow a
documented defaulting policy.

When a definition is unavailable, preserve its complete orphan instance record
including IDs, transform, support and state. Keep it inert and visible to
diagnostics, with no substituted mesh, relationship, action, or capability.
Restore functionality only when the same definition returns or an explicit
reviewed migration maps it. M3 dispenser migration is an explicit alias map,
not a display-name guess.

#### Shared catalogue and tooling discovery

Maintain one canonical versioned furniture catalogue/registry for definitions,
relationship rules and capability descriptors. Registration happens once;
the scene registry indexes currently loaded instances and reservations, and is
not another editable list of furniture types or tools. A standalone workbench
reads the project registry or a generated versioned export of that same
registry. Export includes catalogue version/hash and validation diagnostics.
Adding a valid definition requires data registration, not a second hand-edited
workbench manifest or code change for each chair/table variant. New behavior
still requires a supported executor/validator implementation.

For implementation, the canonical source is the project-owned catalogue
document plus its validator; a generated snapshot is a read-only distribution
form, never a separately maintained manifest. The registry emits definitions,
typed point/use IDs, rule references, capability descriptors, catalogue
version/hash and per-entry validation status. Runtime scene instances and
installed executors remain host-owned dynamic inputs. Existing legacy IDs in
`FurnitureUsePoint.supportedUseIds`, `ActivityDefinition.capabilityId` and
`ActivityHost` mappings should resolve through this catalogue contract or an
explicit compatibility adapter rather than being copied into another tool
list. Live reload versus restart visibility is a runtime policy to decide
during implementation; exported snapshots are immutable by version/hash.

The activity host derives currently exposed capability IDs by intersecting
registered capability descriptors with installed executors, current valid
arrangements/resources and host policy. Scenario JSON references those IDs and
content requirements; it does not restate a tool manifest. Discovery is
read-only: it does not publish content, grant permission, authorize provider
calls, or make a newly registered action executable. Draft validation,
publication, online consent and execution permission remain separate gates.

#### Cross-platform round-trip fixture

Add one synthetic fixture to the design acceptance set and later implementation
tests: a raised chair rotated away from the room axes, a window attached to a
named wall/opening support, and a carpet with a floor support. Serialize on
Windows, load and serialize on Android, then compare every stable ID, all XYZ
metre positions, quaternion orientation (angular difference), scale, support
identity, condition/resource state, and schema/catalogue/seed versions. Compare
the reverse direction too. Suggested tolerances are position <= 0.001 m,
rotation <= 0.1 degrees and scale <= 0.001; fixture values should include a
non-zero Y elevation and non-axis-aligned rotations so lossy 2D/pixel encodings
cannot pass. Also verify missing-definition preservation and that no activity
reservation is restored. This fixture validates serialization parity; it does
not prove Unity scene reconstruction or player-visible placement on either
platform.

### Bounded implementation proposal — historical, superseded by I1-I5

This was the 2026-10-21 ordered proposal, not an implementation plan or START.
Its delivered foundation/inventory items are superseded by the approved
[[Furniture Inventory and Placement Implementation Plan]]; the remaining future
scope below is not implied to be implemented:

1. **Catalogue DTO and validator:** v1 versions/IDs/units, all eight starter
   definitions, capability descriptors, relationship rules and legacy alias
   map; fail closed on unsupported major versions.
   Acceptance: starter entries round-trip; duplicate/broken refs and unsupported
   major versions reject; legacy dispenser IDs still resolve without inventory.
2. **Pure relationship resolver:** synthetic furniture snapshots and deterministic
   face-pair/table-context evaluation. Acceptance: all walkthrough/negative cases,
   exact tie-breaks, override behavior, and unknown evidence are covered without
   Unity objects.
3. **Room layout and save adapter contract:** seed/default import, stable-ID
   migration and orphan preservation, then room-local metre serialization.
   Acceptance: Windows/Android round-trip fixture preserves raised, rotated,
   supported instances and persistent state; transient reservations do not load.
4. **Unity scene adapter and revisions:** capture scene instances, points,
   enabled state and access evidence; preserve `FurnitureUsePoint` via an adapter.
   Acceptance: move/rotate/disable/remove and definition/rule changes invalidate
   dependent relations; no all-pairs scan each frame; decoration remains placeable.
5. **Atomic reservation set and host binding:** multi-point acquisition with
   rollback; bind exact arrangement revisions to an ActivityHost ticket; safe
   cancellation/cleanup. Acceptance: contention on either seat leaves no partial
   claim; stale revisions fail; end/stop/quit release once; dispenser behavior
   remains.
6. **Requirement and locked-choice contract:** optional stable choice IDs,
   lock/hide policy, host-evaluated state/reason and selection-time revalidation.
   Acceptance: old content still parses; locked never advances or requests an
   action; stale selection refreshes; hidden reveals no reason.
7. **Placement commands and active-use guard:** place/move/rotate/remove with
   expected-revision checks, support validation, structural rejection and
   decorative-invalid allowance. Acceptance: occupied point/resource blocks an
   edit; external invalidation cancels safely and never rebinds.
8. **Registry discovery contract:** authoring and standalone tooling read the
   canonical catalogue or its versioned export. Acceptance: registering a
   valid definition exposes its descriptor to tooling without a second list;
   discovery alone cannot publish or execute it.
9. **Editor-authored choice presentation and player acceptance:** static disabled
   style/tooltip/focus target authored in existing Canvas templates. Acceptance:
   pointer hover and keyboard/controller focus show safe copy; disabled choices
   cannot activate; validate from `SplashScene -> ModelTest` in Play Mode.

Any future local-first workbench or AI-agent authoring command consumes the same
validation/resolution contract. Its technology, UI, and publish path remain
unselected and outside these slices. Synthetic preview is labelled simulated
and does not prove Unity navigation, input, or activity completion.

### Approved human decisions and implementation boundary

1. **Player participation — resolved 2026-10-21:** Alterra takes her seat and waits;
   the Player manually approaches the other chair and presses T to sit/join. No
   automatic Player movement/seating. Remaining seat/camera transition details do
   not change this approved player-led entry contract.
2. **Persistence owner — resolved 2026-10-21:** Player home-save state owns placed
   furniture instances, placements and persistent condition/resource state. Unity
   scenes provide starter/default layout, not the mutable player-layout authority.
    This does not persist an active seating activity for automatic resume. The
    schema-2 home layout and stock transaction path are now wired through the
    existing SQLite game owner; see the I2/pre-live reports. Live profile/restart
    acceptance remains pending.
3. **Stale pre-start choice — resolved 2026-10-21:** If its captured arrangement
   revision changes, refresh the choice and require another click, even when a
   replacement arrangement is valid. The stale click must neither start an action
   nor advance dialogue. After activity start, pin the exact arrangement, fail
   safely on invalidation, release all claims, and never auto-rebind.
4. **Edited-save migration — resolved in the shared contract:** A newer Unity
   scene seed never auto-migrates an existing home save. First load imports the
   seed only when no save exists. Player-facing migration UI is deferred.
5. **Catalogue refresh — resolved in the shared contract:** Workbench v1 uses
   explicit Reload and pins the snapshot/hash for each open session. There is no
   runtime watcher or implicit live reload.
6. **Rug layering — resolved 2026-10-24:** A rug may overlap recognized furniture
   whether it is placed before or after that furniture. Room geometry and actors
   remain placement blockers. This records the human-approved placement rule; it
   does not authorize a new layout implementation slice or change the open live
   PoC gate. The rule still needs live placement acceptance in the approved test.
Numeric geometry tolerances remain rule-level catalogue values with detached
fixture defaults; calibrate them in a real room before player acceptance. A
global lock/hide default is unnecessary because each authored requirement must
state its own policy. Placement edits touching a reserved point/resource reject
with an in-use result; external invalidation cancels the captured activity.
Support identity and initial fixed unit scale are specified above. No material
human decision blocks the bounded F4 adapter work; physical seating and
player-facing migration UI remain outside this foundation.

### Cross-platform layout and furniture arrangement extension

Human requirement2026-10-21: future Windows and Android versions must reconstruct
the same saved furniture layout. This is shared layout compatibility, not a new
commitment to implement real-time multi-device synchronization now.

Use platform-independent room/home-local transforms in metres, with stable
home/room/instance/definition IDs and layout/schema revisions. The human's "x and
y" denotes a 2D placement plane, not screen pixels. The supported schema stores
room-local XYZ position in metres, quaternion rotation, dimensionless XYZ scale,
explicit support ID and local attachment pose, plus schema/catalogue/seed versions.
Map the floor plane consistently to Unity X/Z and elevation to Y. This is current
layout serialization, not cross-device transport or synchronization.
Camera position, aspect ratio, touch/keyboard input and render quality must not
change saved placement.

Keep room origin/orientation and mount/support references explicit; windows and
other wall-mounted objects cannot be reconstructed from floor coordinates alone.
Same definitions/functional-point geometry and catalogue revisions must give the
same relationship results on both clients. Missing definitions preserve their
full orphan records and stay inert, with no substitute functionality; never
silently delete or move them.
Actual cross-device transport, account linkage, conflict resolution and concurrent
simulation remain future roadmap work.

The approved foundation applies these arrangement mechanics through F1-F4 and the
I1-I5 inventory/placement continuation: versioned catalogue, resolver/choice and
reservation contracts, revisioned saved-layout commands, SQLite home persistence,
and Editor-wired ModelTest inventory/placement controls. `Player.Interact` now
maps to keyboard T; E is the furniture inventory/held-item control. Structural
placement safety stays separate from optional relationship availability; active
use rejects edits to reserved points and safely cancels on external invalidation.
Cross-platform fixture proof is serialization only, and live Game View/key/restart
acceptance remains pending. See the current pre-live report for exact evidence.

## Authoring and AI-agent tooling boundary

Approved direction: an external, local-first content workbench, not an editor
requiring Unity to write Topics/Scenarios. The shared contract and implementation
plan specify a flow-first browser UI, loopback Python service and C# host; that
workbench is owned by Topic Playground and remains under implementation.

- Authoring knows shared furniture definitions, supported capabilities and rule
  vocabulary; it does not need a live Unity scene merely to save a draft.
- Approved catalogue/tooling requirement: register each furniture definition once
  in the shared catalogue/registry; tooling discovers new valid definitions without
  a second hand-maintained furniture list or a code change for every new chair.
  Standalone tools may read the same project registry or a versioned exported
  snapshot. Placed instances remain separate from type definitions. ModelTest now
  has a persisted room-local layout snapshot, but tooling and ActivityHost do not
  automatically discover runtime visual proxies as functional points; a reviewed
  scene export/live adapter must supply those functional points. New mechanics still need implemented
  executors/validators. Discovery never grants permission or automatically publishes
  unvalidated content. Workbench v1 uses explicit Reload and pins each open
  session's snapshot/hash; it has no watcher or runtime live reload.
- Standalone previews use detached synthetic instances/layouts, explicit stats,
  availability and reservations. Unity may later supply actual scene snapshots.
- Show why an arrangement/choice passed, locked or was hidden in developer mode.
  Clearly label simulated activities versus actual game execution/navigation proof.
- Draft→validate→preview/test→review→explicit publish is the intended workflow.
  Agents use the same contracts; no silent edits to live content or player saves.
- Shared catalogue covers authored definitions only. Do not copy personal generated
  Topics, conversations or memories into the shared vault/catalogue.

## Implementation handoff

The earlier design-only Room Testbox delegation is superseded by the bounded
human-authorized [[Furniture Foundation Implementation Plan]] and exact
[[Content Workbench Shared Contract]]. Room Testbox owns the furniture catalogue,
portable furniture core and narrow Unity scene adapters; the current I1–I5
inventory/placement continuation and evidence are linked above. Topic Playground
consumes the shared catalogue/resolver/choice API and owns its separate workbench.
The 43/43, 52/52 and F4 source-only checkpoints below are historical. Preserve
ModelTest, GUIDs, Follow/Drink Scenarios and the current single ActivityHost.
Physical sitting/cameras, general runtime functional-point discovery for placed
proxies, provider/TTS work, cloud sync and automatic catalogue reload remain
outside the implemented inventory/placement slice.

## Evidence

2026-10-21: consolidated approved discussion against the M3 furniture extension
and current handoff. Documentation-only; no runtime/JSON/schema/UI/scene changes.
Link checks are separate from rendered Obsidian inspection; no gameplay claim.

2026-10-21: completed detailed catalogue/mechanics refinement against
`FurnitureUsePoint`, `FurnitureRegistry`, `ActivityHost`/`ActivitySceneHost`,
`ScenarioRuntimeSnapshot`, `TopicConditionEvaluator`, the canonical choice
model, and the live authored Follow/Drink Scenarios. Main-session decisions
approved player-led seating, home-save ownership, stale-choice refresh,
expanded starter catalogue, cross-platform room-local metre layout, placement
mechanics, and one-source tooling discovery. Builder review confirmed no
remaining blocker to the bounded proposal after support identity and fixed unit
scale were added. The implementation contract later resolved explicit Reload
and no edited-save auto-migration. This historical design checkpoint preceded
source implementation; no runtime or Unity asset changed at that point.

2026-10-21 F1 implementation checkpoint: canonical catalogue schema 1/version
1.0.0, portable DTO/validator/registry and starter catalogue added. Filtered and
full `Check-FurnitureRuntime.ps1` each pass 9/9; reflection reports no Unity
references. This does not establish Unity import/EditMode or runtime acceptance.

2026-10-21 F2/F3 implementation checkpoint: resolver/choice/reservation and
saved-layout/command sources are implemented. Full `Check-FurnitureRuntime.ps1`
passes 43/43; no UnityEngine/UnityEditor references. The round-trip fixture
compares all IDs, positions, orientations, scales, support refs and state in both
serialization directions within <=0.001m/<=0.1deg/<=0.001 tolerances. Unity
scene import, home-save adapter, Android runtime, Editor tests and player
acceptance remained unverified at that checkpoint.

2026-10-21 F4 source checkpoint: the E adapter labels the live dispenser action
“Ask Alterra to get water,” uses exact host capability/use-point/revision checks,
and shares ActivityHost ownership. Standalone E requests have no Scenario event
stream or menu Drink follow-up; they are blocked while a Scenario owns the
conversation. Furniture tests pass 52/52; Scenario and Unified Conversation
source suites exit 0; current runtime/game/editor-test C# sources compile against
cached Unity references. Unity import/EditMode execution, authored Canvas
tooltip/focus template, scene references, and player acceptance remain pending;
see [[../Verification/2026-10-21 Furniture Foundation F4 Source]].

## Current status and Action discovery boundary (2026-10-22)

The current input contract is in [[Furniture Inventory and Debug Design]]:
keyboard T calls `Player.Interact`; E opens inventory when empty-handed and
returns a held item; the remaining placement controls and sole F5 Debug Mode
owner are listed there. Historical E-key references in this note and the F4
checkpoint describe the prior keyboard binding.

Inventory discovers catalogue definitions; the room layout snapshot separately
stores placed instances and room-local XYZ pose/support data. A catalogue entry
or `tv.view_target` does not create an executable furniture use. The
`ActivityHost` registry claims exact authored `FurnitureUsePoint` objects, while
the current placement controller creates a visual proxy without automatically
registering a functional point or executor. See the current detailed boundary in
[[Furniture Inventory and Debug Design#Furniture discovery and action boundary]]
and the approved [[Scripted Actions and Inspect TV Design]].
