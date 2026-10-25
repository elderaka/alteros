---
title: M3 Furniture and Activities Design
date: 2026-10-19
updated: 2026-10-22
tags: [alteros, design, activities, furniture, m3]
status: implementation-in-progress
---
# M3 — reusable actions and furniture activities

Related: [[Scenario Conversation Design]], [[Scenario Conversation Implementation Plan]],
[[Room Testbox Handoff]], [[Live Conversation Journal]], [[Stat Catalogue]],
[[Agent Tools]], [[Feature Register]], [[../Verification/2026-10-19 M3 Activity Source Check]]. Root `TODO.md` owns cross-session status.

Current keyboard mapping note (2026-10-22): `Player.Interact` is T; E opens the
furniture inventory when empty-handed and returns a held item. The interaction
semantics below remain, but physical E-key wording has been updated to the
`Player.Interact` action where it described the former binding. Live input remains
unverified.

## Intent and approval boundary

Make three small, real activities usable through authored Scenarios: **Come here,
Follow me, and Drink water**. Prove movement, continuous activity, and a physical
furniture binding without needing an LLM or building a universal scripting engine.
Phone/history/optional TTS are M4. Human full-document and implementation-plan
approval and START received2026-10-19. Source implementation is underway; this
is not evidence of gameplay or provider-spending permission. See
[[M3 Furniture and Activities Implementation Plan]] and the implementation
handoff for current evidence.

### Human decisions already established

- Actions must be reusable/extendable rather than embedded in individual dialogue.
- Come here approaches once and stops; it does not keep following a moving player.
- Follow me continues until a stop request. During a `Player.Interact` exchange she stops moving,
  faces the player, and resumes following when that exchange closes.
- Initial test scope is those movement activities plus Alterra drinking at a
  dispenser; shared player hydration, seated desk companionship and its camera
  design are not required to prove these first actions.
- Existing Scenario lifecycle remains: one running plus one suspended Scenario,
  explicit diversion handling, no automatic suspended-Scenario resume, and quit
  ends execution. Merely closing a dialogue exchange need not end its Scenario.
- Host owns effects, permissions and truthful completion. Prepared content works
  offline; repeated explicit interactions must not require repeated model generation.

### Assistant defaults proposed for this slice

Defaults below are recommendations, not additional human approvals. They are
Inspector-tunable, conservative and subject to scene scale/rig verification.
The builder chooses concrete class names and compatible navigation/animation
implementation after inspecting existing project facilities; no package is
authorized just because a design mentions navigation.

## 1. Ownership and data

| Concept | Owns | Must not own |
| --- | --- | --- |
| Topic | Semantic meaning/retrieval and relevant Scenario references | Navigation or object coordinates |
| Scenario | Dialogue, questions, activity requests, waits and outcome branches | Moving transforms, applying stats or inventing successful activities |
| Action definition | Stable capability ID, executor kind, target requirements, phase/permission policy and authored configuration | Mutable per-attempt state or arbitrary code supplied by content |
| Action request | Actor, typed supported arguments, source/run/step identity and requested target role | Provider internals, arbitrary paths/coordinates, stat deltas |
| Activity execution | Unique host ticket, concrete bindings, progress, movement/animation ownership and outcome | A second conversational Scenario or shared mutable definition state |
| Furniture instance/use-point | Stable instance identity, supported uses, approach/pose/use/exit transforms and occupancy | Deciding which conversation is true or crediting a model's claim as success |

Use reusable authored definitions (prefer Inspector-authored assets for shared
configuration) and focused executors for each genuinely different behavior.
Keep lifecycle rules testable in plain C#; Unity components bind actual actors,
transforms and presentation. Do not introduce a manager/framework per action,
a generic user-script interpreter, or a new dialogue runner.

Multiple definitions can reuse an executor; Come here and Follow me share
movement facilities without pretending snapshot and moving targets are identical.
Unknown capabilities/arguments fail closed. The same host boundary may receive
authored, validated generated or later autonomous requests; M3 only needs the
authored test path. Register a capability only when its executor is usable.

### Furniture contract extension approved2026-10-19

Consolidated follow-up2026-10-21: [[Furniture Mechanics and Catalogue Design]]
owns the approved separate-furniture/proximity rules, optional table bonuses,
locked dialogue choices with hover tooltips, proposed catalogue records and the
Room Testbox design-refinement handoff. It is not implementation START for seating,
locked UI or a standalone workbench. The M3 dispenser implementation bound below
remains unchanged.

Following research and further human discussion, furniture relationships are
automatically discovered from placement, with an optional authored override for
unusual arrangements. The human also approved leaving unusable arrangements
placeable as decoration: functional rejection is not a blanket placement ban.
This extends the approved design and implementation scope; START was received
2026-10-19.

- **Definition:** shared supported capabilities and functional-point configuration;
  not per-instance occupancy, resources or an activity's progress.
- **Instance:** stable identity, placement/revision and actual enabled/resource state.
- **Functional points:** typed seats, workspaces, outlets, viewing targets or other
  supported locations with local positions/orientations. Interaction use-points
  retain their approach/pose/use/exit roles. Multiple points can exist on one object.
- **Relationship:** a validated connection between specific points on specific
  instances, not a global nearest-object association. Activities state which
  capabilities/point relationships they need; furniture does not contain dialogue.
- **Reservation:** applies to the actual usable point/resource, not automatically
  the entire mesh. A two-seat object can support independently claimed seats;
  shared activities later need all required points bound consistently before start.

Use a **3D sphere broad-phase** around the relevant functional point to discover
candidates (Unity Y is height, X/Z the ground plane). Sphere inclusion is only
candidate discovery: relationship-specific distance, facing, height/reach,
obstruction and accessible approach checks determine usability. A nearby point
on another storey or behind a wall is not usable merely because it is in radius.
No single universal sphere radius or angle fits every relationship.

For chair→workspace, prefer compatible work-facing alignment and usable seated
work height; for seat→TV, use viewing geometry/visibility rather than chair-table
height tests. Mesh pivots/model forward axes are not reliable functional points.
Rank valid candidates by alignment where relevant, then distance and stable IDs
for deterministic ties. Author radius/angle/height tolerances per relationship
type, not as hardcoded per-room coordinates.

Refresh affected relationships on placement/movement/rotation/removal or relevant
state changes, not an all-pairs scan every frame. Bind exact instance/point IDs
when an activity starts; do not switch targets continuously as distances change.
Moving/disabling/removing a bound object invalidates its revision and uses the
activity's safe cleanup/recheck path. An authored override chooses the intended
candidate but cannot bypass reachability, compatibility or reservation safety.

Furniture with no valid relationship can remain decoration, or retain independent
uses: a chair facing away from a desk can still support sitting. Unavailable
activities must expose an inspectable reason (wrong facing/height, blocked access,
missing compatible point, occupied/disabled), not a false executable option.

**M3 implementation bound:** establish these extensible data/validation boundaries
and prove the dispenser's standalone use-point. General chair/desk/TV relationship
discovery, multi-resource transactions and furnishing placement UI remain later
integration, not newly implied requirements for the three-action slice. Tests
for those later relations may be detached design fixtures, not production claims.

### Research basis (patterns, not a framework dependency)

- [Forbus/Wright: programming objects in The Sims](https://www.qrg.northwestern.edu/papers/Files/Programming_Objects_in_The_Sims.pdf): object-provided behavior, availability tests and need advertisements.
- [Software Inc. furniture documentation](https://www.softwareinc.coredumping.com/wiki/index.php/Furniture_Modding): typed snap/interaction points and linked furniture points.
- [Epic Smart Objects overview](https://dev.epicgames.com/documentation/en-us/unreal-engine/smart-objects-in-unreal-engine---overview): shared definitions, slots, claims and invalidation; execution separate from descriptive object data.
- [Maxis: concurrent interactions in The Sims 4](https://gdcvault.com/play/1021210/Concurrent-Interactions-in-The-Sims): constraints for compatible concurrent behaviors.

Automatic 3D relationship discovery and decorative-invalid placement are AlterOS
design decisions, not claims that all these sources implement that exact method.

## 2. Shared execution contract

Proposed lifecycle: requested, accepted, running (with phases), terminal.
Terminal reasons distinguish completed, player-stopped, cancelled, failed,
and interrupted-by-quit. Accepted means started/owned, not completed.
Temporary movement suspension for a `Player.Interact` exchange is a phase, not a second Scenario
and not the suspended-Scenario slot.

- One primary locomotion/physical activity owner for Alterra. Requests do not
  silently replace an existing owner. Scenario-owned diversions use the existing
  pause/end/back-out decision before replacement; standalone conflicts return a
  busy result rather than introducing a second confirmation mechanism.
- Give each attempt a host activity ID and correlate its events with its source
  run/step. A duplicate request must not start a duplicate activity. A late event
  from a cancelled/old execution cannot advance another Scenario or apply effects.
- Recheck binding, reachability, actor availability and ownership at execution.
  A request accepted earlier is not permanent permission to use a missing object.
- Cancellation/quit/failure releases paths, motion/pose ownership, props and any
  reservations exactly once. Never restore an activity automatically on launch.
- Keep movement timeouts, route repath interval, stopping radius and retry bounds
  in authored configuration. No silent teleport or infinite recovery loop.
- State/progress remains inspectable headlessly; navigation/pose/audio truth
  still requires Unity checks. Do not make synthetic progress a production event.

## 3. Come here

Conceptual capability ID: `movement.come_here` (proposed, not a registered API).

At acceptance, snapshot a reachable destination near the player's position,
outside their collision volume. Walk toward that fixed destination, stop inside
the configured arrival tolerance, face the player if still present, and complete.
If the player moves, do not chase or continually resample their destination.
Already within the comfortable distance can complete without unnecessary motion.

Recommended first tuning: comfortable separation 1.5 m; sample a safe nearby
point rather than assuming the player transform lies on navigable ground.
Target loss, unreachable destination and bounded stalled movement produce honest
failure; do not acknowledge arrival before the movement controller confirms it.
During a `Player.Interact` exchange, stop safely and talk; after closing, continue toward the original bound
destination unless explicitly cancelled. No automatic new destination.

## 4. Follow me

Conceptual capability ID: `movement.follow_player`.

Resolve the player's current reachable vicinity repeatedly at a bounded cadence.
Use different stop/resume distances to avoid foot shuffling: proposed defaults
stop at about 1.5 m, move again beyond 2.2 m, repath no faster than every 0.3 s
unless the chosen controller needs stricter safety handling. Tune against real
colliders; these are not promises about the current rig or room.

Catching up enters waiting-near-player, not completed. An explicit **Stop
following** ends it with player-stopped, distinct from arrival/completion.
It remains active until stopped, replaced through the proper diversion policy,
failed, or quit; no arbitrary Scenario inactivity expiry.

`Player.Interact` temporarily suspends locomotion, faces the player where safe,
and restores following on exchange closure only if this execution still owns
movement. This does not auto-resume a suspended Scenario, and closing an old
exchange after a stop/replacement must not resurrect following. Loss of a route
gets at most one bounded recovery attempt before failure/cleanup; target loss or
scene exit ends safely. A failure response is a pending next-interaction option, not a
forced camera takeover or recurring bark.

M3 does not add autonomous need scheduling. Later self-care must coordinate
ownership/safe pauses instead of creating two locomotion controllers.

## 5. Drink water

Conceptual capability ID: `needs.drink_water`.

Start with one use-point on `testbox.water_dispenser` in the existing room.
Resolve by required supported use, not mesh name, color or a hardcoded position.
Bind the selected instance/use-point once and retain that identity throughout
the attempt. If a specific object is requested, validate it rather than silently
substituting another. With several eligible dispensers, use stable deterministic
selection (reachable candidate then distance and stable-ID tie-break).

Furniture runtime adapter needs only instance/use-point IDs, supported-use ID,
the four authored transforms, enabled/available state and host reservation owner.
Availability does not imply an implemented water-inventory system: M3's fixture
may explicitly configure an always-supplied dispenser. No pretend empty/full
simulation; disabled or missing fixtures provide availability failure cases.

Phases: validate/bind and reserve; approach; align without teleporting across
obstacles; prepare cup/pose; drink at validated checkpoints; finish/exit; release.
Recheck the fixture when entering use. A destroyed/disabled/moved fixture
invalidates this attempt; fail/clean up rather than retaining stale transforms.

Hydration is a game-authored effect at actual drinking checkpoints, clamped to
the catalogue's range and applied at most once per execution/checkpoint. No
hydration for accepting, approaching, waiting, or replaying a completion event.
Cancelling after a real sip preserves only effects already earned. Dialogue/LLM
cannot set magnitude or arbitrary stat IDs. The builder must inspect the actual
stat owner: a read-only snapshot adapter is not a writable stats system. If no
authoritative write seam exists, add only the smallest game-owned tested effect
sink for the hydration stat, not a full needs simulator, and distinguish detached
fixture proof from real stat-display/persistence acceptance.

CanTalk/CanPause are phase-specific: approach can stop for dialogue, drinking
cannot speak mid-sip and can pause/cancel normally at a safe checkpoint. `Player.Interact` while
unsafe produces at most a non-modal authored busy acknowledgement and remembers
the attempt; no forced later conversation. A stop request during a sip becomes
pending until its bounded safe exit. Emergency failure/quit always cleans up
without invented hydration and must not wait indefinitely for an animation event.

Use an existing suitable animation/cup attachment if available. A clearly labelled
timed test phase can prove lifecycle/effect logic if no clip exists, but is not
visual drinking acceptance. Do not author a prop/IK animation framework just for
this slice. No player drinking confirmation or bodily tracking is required.

## 6. Authored Scenario fixtures and interaction behavior

Use the same Scenario runner/action boundary as generated interactions, not a
special production test dispatcher. Fixtures may start from explicit developer
selection so semantic classification cannot hide activity failures. Add a small
labelled routing check later; don't bulk change anchors or catalogue families.

| Fixture | Steps and outcomes |
| --- | --- |
| Come here | Authored acknowledgement; request action; close exchange while walking; wait for correlated outcome; expose arrival/failure acknowledgement on the next explicit interaction; end occurrence honestly |
| Follow me | Ask to follow with Yes/No; on Yes request action and close exchange; wait while following; `Player.Interact` can converse, Stop following, or Back; stopping releases movement and closes the occurrence; No starts nothing |
| Drink water | Ask/confirm invitation; request action; close exchange during activity; on completed prepare an optional closing for the next explicit interaction; on unavailable/failed use truthful failure branch; cancellation does not claim she drank |

Every fixture has decline/cancel/failure routes as applicable. All lines and
options carry Japanese speech plus corresponding English display using the
current content contract and supported expressions. No Appetizer/LLM/TTS needed
to run authored fixtures. Example wording is illustrative, not an approved new
schema or completed catalogue migration.

Walking/waiting interaction lines come from authored context-valid pools. Optional future
generation may supply validated cached wording, but never determines the phase
or completion. Don't regenerate on each interaction. Avoid immediate repetition, use a
cooldown, and do not interrupt the player's current dialogue with ambient lines.
Talking does not inherently stop/end the Scenario. Reuse existing Canvas choice
templates; any static stop/status controls must be Editor-authored, not built in
runtime code. Always leave a player-accessible Stop following path through `Player.Interact`.

For arrival/drink completion, don't unexpectedly open a new dialogue or auto-resume
a paused Scenario. Existing approved return-prompt policy still governs a real
temporary diversion: Alterra-only may offer return when permitted; player/together
wait for the next explicit interaction. A normal activity finishing within its own Scenario needs no extra
unrelated Scenario. Authored test Scenarios are repeatable, no rewards/currency.

## 7. Integration boundaries and current evidence

Source inspection2026-10-19 found `IScenarioActivityPort.TryRequest(ActivityRequest,
ScenarioRuntimeSnapshot)` returning accepted/unavailable with a host activity ID
under `Assets/AlterOS/Scenarios/ScenarioActivityPort.cs`. This is an extension
seam, not a navigation executor or proof its Unity presentation is finished.
Integrate through the final Scenario worker contract; don't independently rename
its DTOs or expand v4 protocol. Keep completion/cancel/event wiring compatible
with that worker's final handoff. Content cannot forge activity results.

The 2026-10-19 room handoff reported saved ModelTest geometry and twenty markers;
that was not evidence of runtime use-points/navigation and is superseded for the
current scene by [[Room Testbox Handoff]]. The dispenser and M3 navigation were
later wired through Unity Editor. Preserve Player/camera/UI and startup. Marker
alignment in the historical fixture remains a hypothesis to validate. Never
hand-edit Unity YAML/meta, rebuild the room unnecessarily, or force-close a stale
Editor.

No production Come here/Follow/Drink implementation was established by this design
pass. A narrow filename/source search is not an exhaustive animation/navigation
audit. The implementation planner must inspect installed/current facilities before
choosing a navigation solution; request explicit approval for any new dependency.

Current domain ownership and consultation boundaries are in
[[Multi-Agent Coordination]]. Furniture and Environment owns furniture/room
source and serialized scene wiring; Alterra Behavior owns Scenario/dialogue
orchestration and Action integration. M3 source integration exists, while live
activity acceptance remains pending. Agree on exact shared files and Editor
ownership before any cross-domain changes. No provider-spending call is
authorized by this design.

## 8. Acceptance and stopping conditions

1. Deterministic headless tests: per-attempt state isolation, request idempotency,
   exclusive ownership, snapshot-vs-follow target behavior, distance hysteresis,
   bounded recovery, `Player.Interact` pause/resume and stale-close rejection, every terminal
   cleanup path, reservation invalidation, hydration checkpoint idempotency and
   no effects before actual use. Fixture Scenarios run offline through real port
   interfaces with synthetic movement/effects labelled as such.
2. Current Unity compile and focused EditMode checks, reported separately from
   source harness results; do not fix unrelated parallel work without coordination.
3. Real room checks: Come here stops without following; Follow waits/restarts,
   pauses for interaction and resumes only while owned; Stop works; Drink reaches/binds the
   dispenser, uses correct pose/checkpoints, releases it and applies only earned
   hydration. Test blocked paths, missing/disabled fixture, repeated interactions, duplicate
   events, cancellation, quit and repeated start/stop cycles without leaked locks.
4. Verify normal `SplashScene -> ModelTest -> T` interaction remains intact. Report actual
   movement/collision/animation, UI and stat observations separately. No provider
   key, generation spend, TTS or new performance claim is needed for acceptance.
5. Handoff exact changed files/assets, source vs Editor vs Play Mode evidence,
   configuration and known limits. If navigation, stats or clips are missing,
   report partial/blocked acceptance rather than disguising simulated success.

Out of scope: desk sitting/joining and webcam camera presets, drawing gameplay,
phone/M4, full autonomous needs/routines, home editing/economy, persistent activity
restoration, new sentiment models, generated-content publication and bulk semantic
rebakes. Preserve future extensibility through the action boundary, not speculative
implementations of those systems.

## Document evidence

Fresh read-only builder check2026-10-19 returned **nothing important left**:
no unanswered material planning questions after respecting the explicitly marked
assistant defaults and builder-owned choices. This is a design review, not a
code review or human approval.

Documentation-only design2026-10-19. No scripts, dialogue JSON, scenes, prefabs,
dependencies or provider configuration changed. No Unity, runtime, audio or API
tests run; Obsidian rendered-view inspection not performed. Human full-document
approval received2026-10-19; the separate implementation plan still needs review
before implementation START. No implementation session was assigned.

