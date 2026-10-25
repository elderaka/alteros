---
title: Content Workbench Shared Contract
date: 2026-10-22
tags: [alteros, contract, authoring, furniture]
status: f1-f4-source-ready-editor-integration-pending
---
# Content workbench shared contract

Related: [[Furniture Mechanics and Catalogue Design]],
[[Furniture Foundation Implementation Plan]], [[Content Workbench Implementation Plan]],
[[Topic Scenario Activity Technical Reference]]. Human authorized these bounded
plans and delegation2026-10-21. This describes interfaces to implement, not an
existing service. Room Testbox owns furniture core; Topic Playground consumes it.

## Decisions and scope

Latest goal/workflow2026-10-22: [[Reusable Content Framework Workflow]] owns the
current six-kind planning scope and three proof scenarios. Minimal Item definition /
inventory grant is now included for Fetch a drink, not deferred out of this goal;
full consumables/clothes/gifting simulation is still not required. Turn on TV ends
at committed On; return/automatic VN continuation is exercised by Fetch instead.
Existing support/readiness facts below remain evidence, not implementation claims.
The workflow's consolidated producer decisions own the planned six-kind source map,
Action discriminator/recipe fields, host-effect authority and versions. In particular,
new independent Dialogue uses planned `dialogue_definition`; retained
`generic_dialogue` remains a supported legacy format. No new source kind is enabled
merely by this note or its descriptor proposal.

The full content-creator target and actual type readiness are recorded in
[[#Workbench-wide target and readiness (2026-10-22)]]. The initial Topic/Scenario
slice below is not a claim that Furniture, Item or Action authoring already exists.

Authoring ownership update 2026-10-22: the human approved SQLite as the sole
editable content/draft authority, with JSON import/export only. The W1 `draft.*`
and `authoring.*` commands now use the shared revisioned repository, and the Editor
build processor reads the configured store's explicit `default` selection. The local
store and review export contain 439 sources at revision 63. The 104 compiled
expression fields were mapped to the 12 registered presets; headless expression,
GTE-hash and bake checks pass. The human reports the corrected Editor build works;
no log was attached, and `SplashScene -> ModelTest` acceptance remains unconfirmed.
See [[Content and Interaction ERD#SQLite authoring authority approved 2026-10-22]],
[[SQLite Authoring Cutover Implementation Plan]] and
[[../Verification/2026-10-22 SQLite Authoring Core]].
Preserve current command/hash-CAS behavior and standalone no-Unity-runtime promise;
do not silently use files when the authoring database is unavailable.

Flow-first graph authoring with a selected-step writing inspector and synthetic
Playground is the first standalone workbench. Support Topics and executable
Scenarios; do not conflate `TopicScenario` variants with `ScenarioDefinition`.
The first fixture is "Wanna sit together?", with two seats and optional table.
All eight furniture definitions come from one registry, not UI hardcoding.

Implementation choice for this bounded slice: plain browser UI and Python standard
library loopback server, with a JSON-command C# host compiling/linking the actual
pure runtime validators/selectors/session and furniture source. No web framework,
node package, second validator implementation, Unity Editor dependency at runtime,
or provider import. Existing bundled Roslyn/.NET Framework tooling may build the
Windows command host; a distributable built host must run without Unity DLLs.
Python/runtime prerequisites must be documented; a self-contained installer is
later packaging, not a hidden promise. No .NET SDK is currently assumed installed.

Room Testbox must keep new portable core under `Assets/AlterOS/Furniture/` free
of UnityEngine types/dependencies. Unity adapters live separately. Topic Playground
owns `Tools/ContentWorkbench/`, including build/CLI/HTTP/UI tests. Do not alter the
whole runtime assembly or install a framework merely to compile the pure subset.

## Content-creator direction and Action dependency (2026-10-22)

The broader direction is a content-creator / modding-oriented workbench: CRUD for
Topics, dialogue, Scenarios, Action definitions and furniture content through the
same validated commands used by the human UI and development agents. This is a
direction, not a claim that all editors exist or an expansion of the authorized
first slice. Keep SQLite authoring, revision/CAS checks, explicit export and
publication gates; do not add a second editable catalogue or direct SQL UI path.

The Flow-first layout remains selected: catalogue navigation and a selected-step
writing inspector support the graph. A mockup is not a working authoring frontend.
Development-agent access should expose schemas, examples, validation diagnostics
and synthetic previews through the shared command boundary, not require mouse
automation or privileged raw database writes. In-game Alterra's generation remains
a separately permissioned, validated content pipeline, not developer access.

The human has canonized Action as registered, trusted C# scripted behavior. Editable
definitions reference that implementation and its supported parameters; running an
Action creates an execution with its own identity/phases. Workbench content editing
does not compile or execute arbitrary supplied C# or make an invented Action valid.
Reusable implementations may serve multiple definitions and Scenario branches.
Final clarification2026-10-22: support both thin trusted C# composition (InspectTV.cs
calling framework builder functions) and SQL-authored ordered typed recipes. Both
produce the same validated sequence for one execution under ActivityHost. The TV
case must not have bespoke movement/reservation/lifecycle logic. A second non-TV
data fixture and edits to wait/order/text must work without new C# or per-recipe
factory registration; C# and data TV fixtures require behavior parity. Workbench
will author validated recipes, never arbitrary code/unregistered callable names.
A Scratch-like visual frontend is optional future tooling, not required in this slice.
The current acceptance target is now Turn on TV: future authoring must discover
registered furniture functions, typed arguments and supported state predicates
from the shared contract rather than invent per-model callable names or write
object state directly. Its function/state contract is under design, not yet a
supported Workbench/schema/runtime feature. See the current amendment in
[[Scripted Actions and Inspect TV Design#Current acceptance case: Turn on TV (2026-10-22)]].

Implement and verify the Activity/Scenario checkpoint contract before teaching the
Workbench new Action continuation semantics. See [[Scenario Conversation Design#Scripted Activity checkpoints and pose safeguard (2026-10-22)]].
The eventual tool should consume the registered action vocabulary, supported
checkpoints/continuations and constraints rather than maintain a hardcoded duplicate.
CRUD does not mean deleting referenced definitions without dependency validation.
General code-mod loading/sandboxing and a full action-script IDE are not approved
features of the current workbench slice.

## Workbench-wide target and readiness (2026-10-22)

The human confirmed the purpose: a human/development-agent Workbench for creating
and editing **Dialogue, Scenario, Topic, Furniture, Item and Action** definitions.
Turn on TV is a proof of the Action/furniture contracts, not the shape of this tool
or a requirement to add per-object UI/code. Reuse the approved ERD/ID/revision /
localization/dependency model rather than creating six unrelated authoring stores.

| Content | Current inspected support | Next boundary |
| --- | --- | --- |
| Dialogue | SQL shared source documents, typed read/write and compiled dialogue/beat/text records; no graphical writing editor | Inspector over authoring revisions and shared validation; never edit compiled rows directly |
| Scenario | Authored source/draft kind, validator, compiled Flow and session runner | Add the Action/checkpoint contract, then graph editing over the same commands |
| Topic | Authored source/draft kind, anchors/conditions, selection and flow references | Selected-snapshot anchor/flow integration and editable inspector |
| Furniture | Shared runtime catalogue/resolver/layout exist; Workbench catalogue.read currently returns furniture_core_unavailable | Wire the actual registry, then a typed definition-authoring adapter; instances/layout remain separate runtime state |
| Item | No general Item authoring kind/adapter found in the inspected registry; furniture inventory is not a complete Item system | Separate minimal Item definition/validation/runtime mapping design before enabling Item CRUD; do not invent it to unblock Actions |
| Action | New registered sequence/composer/function contract is planned, not implemented | [[Scripted Actions and Inspect TV Implementation Plan|Action Framework Foundation plan]], including discovery, SQL definition edits, shared validation and detached preview |

AuthoringContentKinds currently supports dialogue/Topic/Scenario and auxiliary source
kinds, not general furniture/item/action definitions. Draft saves are distinct from
semantic validation and build/publication. Do not advertise unsupported types as
editable because the database can store JSON. Preserve unknown data/read-only views
or report unavailable explicitly, rather than silently discarding unsupported fields.

### Shared tooling contract

- Stable IDs, immutable revisions and explicit typed references; shared localized text
  rows/default display/speech language policy, not type-specific en/jp columns.
- Existing typed read/write/list, CAS, diagnostics, import/export and selection/build
  gates remain the persistence path. Reference-aware delete/retire is a future typed
  operation where not implemented, never raw SQL/cascading UI deletion.
- The Action foundation supplies `schema.describe` metadata from installed trusted
  registries, plus action `content.validate` / `preview.run`; the GUI and development
  agents consume those same contracts, not parallel validators or hardcoded TV lists.
- Pure validator/sequence core runs with detached synthetic ports in the command
  host and Unity adapters in the game. No duplication of action sequencing in JS /
  Python. Preview traces show effects requested/applied only in synthetic state,
  and cannot prove actual animation, pathfinding or player experience.
- A future adapter may enable another content kind once its schema, validation,
  reference/ownership and runtime mapping are real. Domain ownership does not imply
  the Workbench can modify .cs files, Unity serialized assets or player state.

No additional product decision is needed to plan the Action foundation. Item and
full furniture-definition editors need their own adapter slices later; a general
Scratch editor, media/player, clothes/gifting and cloud sync are not prerequisites.
The current Action plan is prepared for review; no runtime START is implied here.

## Furniture API — Room Testbox owns

Namespace `AlterOS.Furniture`. Implement these entry points and typed DTOs:

```csharp
IReadOnlyList<FurnitureDiagnostic> FurnitureCatalogueValidator.Validate(FurnitureCatalogueDocument document);
FurnitureCatalogueSnapshot FurnitureCatalogueRegistry.Load(string cataloguePath);
ArrangementEvaluation FurnitureArrangementResolver.Evaluate(FurnitureArrangementRequest request);
FurnitureChoiceEvaluation FurnitureChoiceEvaluator.Evaluate(FurnitureChoiceRequirement requirement, ArrangementEvaluation arrangement);
```

`FurnitureCatalogueDocument` implements the versioned fields in the furniture
design: `schemaVersion`, `catalogueVersion`, `lengthUnit`, `angleUnit`,
`coordinateConvention`, `definitions`, `relationshipRules`, `capabilities` and
`legacyAliases`. The v1 coordinate convention is
`unity_local_y_up_positive_z_forward`, with metre/degree units and all eight
starter definitions. A definition exposes stable ID/category, fixed unit scale,
local bounds, support kinds, navigation blocking policy and typed functional
points. A point exposes local position/facing and use IDs. Rules expose stable
IDs/kinds, point type refs and catalogue-owned metric thresholds. Capability
descriptors declare point/use requirements only; they are not installed
executors or permission. Legacy aliases explicitly map the existing dispenser
instance/use-point IDs.

Canonical authored source: `Assets/StreamingAssets/Furniture/catalogue.json`.
F1 shipped schema version `1`, catalogue version `1.0.0`.

`FurnitureCatalogueSnapshot`: `schemaVersion`, `catalogueVersion`, `catalogueHash`,
`definitions`, `relationshipRules`, `capabilities`, `diagnostics`, and the additive
`legacyAliases` collection required to preserve the current dispenser mapping.
Invalid entries are inspectable through stable diagnostic code/path/message and
are excluded from usable snapshot collections. Unsupported schema/preamble
returns no usable records. A fully invalid catalogue produces diagnostics, not
a fabricated empty-success result. Catalogue arrays are normalized by stable ID
for a deterministic lowercase SHA-256 hash; callers explicitly call `Load` to
refresh, and existing snapshots remain pinned. There is no auto-reload.

`FurnitureDiagnostic`: `code`, `severity`, `path`, `message`; paths refer to the
offending record/field. Codes are stable, messages are not identifiers.

`FurnitureArrangementRequest`: `catalogue`, `world`, `relationshipRuleId`, and
optional intended role bindings. `FurnitureWorldSnapshot` contains stable
home/room IDs, catalogue hash, evidence-completeness flag, instance/support/point
snapshots, point access evidence, and externally occupied point refs. Instance
snapshots use room-local metre XYZ position, normalized quaternion, dimensionless
scale, support ID, instance revision and enabled state. Point snapshots carry
instance/point/type IDs and local position/facing/use IDs. Support snapshots
carry support ID/kind/frame revision. Access records distinguish reachable,
blocked and unknown and preserve path-clear evidence. Geometry uses portable
numeric structs. Unknown access/support evidence must stay unknown. Synthetic
evidence cannot become a live host capability.

`ArrangementEvaluation`: `state` (`available|unavailable|unknown`),
`arrangementId`, `relationshipRuleId`, `roleBindings`, `revisionVector`,
`revisionToken`, `primaryReasonCode`, `developerDiagnostics`, and the exact
`reservationPointRefs` selected by the resolver. Revision stamps identify the
catalogue hash, rule, supports and instance revisions. Reasons include
`point_occupied`, `wrong_facing`, `height_mismatch`, `blocked_access`,
`missing_compatible_point`, `evidence_unknown`, `support_mismatch`,
`unsupported_support` and `stale_revision`.

`FurnitureChoiceRequirement`: `choiceId`, `relationshipRuleId`,
`unmetBehavior` (`lock|hide`), safe authored `safeReasonKey`, and optional
`expectedArrangementId`/`expectedRevisionToken` captured when the choice was
shown. `FurnitureChoiceEvaluation`: `state` (`available|locked|hidden`), safe
`reasonKey`, bound arrangement/revision and developer diagnostics. A stale
selection evaluates locked with `choice.stale_revision` (or hidden under the
authored hide policy), even if a replacement arrangement is valid; refresh then
requires a new click. Hidden choices expose no player-facing reason. Unknown
never authorizes execution and must not claim a specific object is missing.

`FurnitureReservationSet.TryAcquire(string ownerId, ArrangementEvaluation
arrangement, FurnitureWorldSnapshot current)` returns
`FurnitureReservationResult` (`success`, `reasonCode`, `claimIds`). It validates
all captured revisions and point availability, acquires exact point claims in
stable order as one transaction, and rolls back fully on any failure.
`Release(string ownerId)` is idempotent and removes only that owner's claims.
Claims are point/resource scoped, not instance-wide; contextual-only furniture
is present in the arrangement but absent from its reservation refs unless the
selected action actually uses it.

These DTO spellings are the shared interface fence. Room Testbox may refine
internal record nesting within the design, but must update this contract before
consumers depend on any externally visible shape change. No silent schema forks.
Registered capability descriptors are not installed executors or permission.

### F1 readiness

Implemented portable sources are `Assets/AlterOS/Furniture/FurnitureCatalogue.cs`,
`FurnitureCatalogueValidator.cs` and `FurnitureCatalogueRegistry.cs`. The
canonical catalogue is `Assets/StreamingAssets/Furniture/catalogue.json`.
`Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` passed its filtered and full
F1 source runs (9/9 each) and verified that the compiled portable assembly has
no UnityEngine/UnityEditor references. Unity import/EditMode and runtime
acceptance remain separate and unverified.

### F2 readiness

`FurnitureArrangementResolver.Evaluate(FurnitureArrangementRequest)` supports
`relationship.seat.face_pair` and `relationship.seat_pair.table_context` with
typed role bindings, deterministic candidate ordering, support/facing/height/
distance/access checks, revision stamps and point claim refs. Other registered
relationship kinds remain unavailable/unknown until a resolver is implemented.
`FurnitureChoiceEvaluator.Evaluate(FurnitureChoiceRequirement,
ArrangementEvaluation)` handles available/locked/hidden outcomes, generic copy
for unknown evidence and stale revision revalidation. A stale selection returns
locked (or hidden by authored policy) even when the replacement arrangement is
available, requiring a new click. `FurnitureReservationSet.TryAcquire` compares
all captured revisions, acquires point claims atomically and rolls back on
contention; `Release` is idempotent and owner-scoped.

F2 tests were first run against missing types and failed at compile as expected;
after implementation the targeted source groups passed arrangement11/11,
choice5/5 and reservation5/5. The full
`Tools/FurnitureRuntime/Check-FurnitureRuntime.ps1` suite passes 30/30 and its
reflection check confirms no UnityEngine/UnityEditor references. No Unity import,
scene integration or player behavior is implied. F3 saved-layout commands and
F4 source adapter API is implemented; Unity scene and UI wiring remains pending.

`FurnitureSceneAdapter.ResolveOptions` joins the shared catalogue to the scene's
exact `FurnitureUsePoint` and installed `ActivityHost.SupportedCapabilityIds`.
Legacy dispenser IDs resolve through catalogue aliases. A definition alone never
adds an executor. `FurnitureInteractable` implements the existing Player E
interface and binds actor, capability, definition, instance, functional point,
use-point ID, point revision, catalogue hash and one request identity. The only
current E option is **Ask Alterra to get water**: `needs.drink_water` controls
Alterra's existing dispenser activity and hydration checkpoints; it does not
mean the Player drank.

The E action uses the existing exclusive `ActivityHost` through
`ActivitySceneHost.TryRequestFurnitureUse`. Each exact use-point ID is host-bound
for the request. Repeating one binding/request identity returns its original
result; a different request while busy is unavailable. The standalone request
does not enqueue Scenario accepted/checkpoint/terminal events and has no
dialogue/transcript or drink-completion follow-up. It cannot start while the
conversation bridge reports a running/suspended Scenario. The authored menu
Drink flow retains its Scenario-owned completion, Come here return, readiness
bark and next-E close behavior. Activity acceptance alone is not completion;
existing sip checkpoints remain the sole hydration effect source.

On stale point/catalogue/arrangement binding, E refreshes its target and needs
another click. Locked/hidden choice policy remains in the shared pure model.
Canvas tooltip hover/controller-focus wiring is not yet authored: disabled
buttons need a separate inspectable focus target, and runtime code may only bind
its text/state to an Editor-authored template. Physical seating, Player
hydration, bed/TV/window actions, camera/cutscene behavior and independent
activity initiation remain unavailable.

### F3 saved-layout and command contract

`HomeFurnitureLayoutDocument` stores schema/catalogue/seed versions, stable
home/room IDs, the room-local Y-up/+Z-forward frame, length unit `m`, layout
revision, and instances. Each instance stores stable instance/definition IDs,
room-local XYZ metres, normalized quaternion, dimensionless scale, explicit
support identity/kind/frame revision/attachment point/local attachment pose,
enabled state, persistent condition/resource state and instance revision. Active
activities and furniture reservations are transient and absent from this schema.

The 2026-10-22 inventory continuation adds home layout schema 2 with an
`inventory` array of stable definition IDs and nonnegative stored counts. Schema
1 remains readable as the legacy layout baseline; the portable repository does
not auto-upgrade it. The coordinated SQLite home adapter must initialize the
additional starter stock exactly once while preserving placed instances and
unavailable-definition records. Room Testbox has implemented the schema-2 DTO
and validation, but not the live adapter migration/transaction; see
[[Furniture Inventory and Placement Implementation Plan]] and
[[../Verification/2026-10-22 Furniture Inventory I1 Source]].

`HomeFurnitureRepository.Load(path)` returns a typed load result. Its
`Load(path, catalogue, supports)` overload preserves every saved record and
marks missing definitions/supports as inert orphans with diagnostics.
`LoadOrSeed(path, sceneSeed)` imports the seed only when no save exists; an
existing save is returned without seed auto-migration. `Save(path, document,
expectedHash)` uses compare-and-set; null hash creates only when no save exists.
It returns `saved|conflict|invalid|error`, and failed saves leave the prior file
unchanged. The repository is pure C#; the existing SQLite home-save adapter
still needs the schema-2 inventory initialization/commit bridge. The source of
the first scene seed remains host-owned.

`FurnitureLayoutCommands.Apply(command, current, evidence)` applies
`place|move|rotate|transform|remove` commands using stable instance IDs and expected
instance revisions. Placement evidence carries the catalogue, room bounds,
support snapshots, reservation refs and completeness state. The result contains
an uncommitted snapshot and the original `expectedSaveHash`; callers must save
with compare-and-set and publish the new live snapshot only after `saved`.
Structural bounds/support/collision and active point claims gate edits. Wall
mount room transforms must agree with their support-local attachment pose. A
structurally safe layout can retain unavailable optional relationships.
Furniture with `blocksNavigation:false` (including the starter carpet) does not
create a navigation obstacle.

F3's `Tools/FurnitureRuntime/Fixtures/windows-android-roundtrip-layout.json`
fixture is synthetic source evidence only. Source checks round-trip it in both
directions and compare all IDs, room-local transforms, support refs and state
within position <=0.001m, rotation <=0.1 degrees and scale <=0.001 tolerances.
It does not prove an Android runtime or live Unity room reconstruction. Existing
edited saves are not automatically migrated; migration UI is deferred.

## Authoring command boundary — Topic Playground owns

The Action Framework Foundation A4 additions (schema.describe, action validation /
preview and build/tests) retain this ownership. Alterra Behavior supplies runtime
contracts/fixtures, DBMS supplies repository interfaces, and main dispatches the
bounded tooling window after human approval. No implicit transfer merely because
the GUI is deferred. See [[Scripted Actions and Inspect TV Implementation Plan#A4 Workbench-facing acceptance: discover, validate, preview]].

One JSON request on stdin / one JSON response on stdout; diagnostics to stderr.
The CLI and loopback UI call the same command implementation. No shell-built
commands, user-selected executables, or provider credentials.

```json
{"protocolVersion":1,"requestId":"example-1","operation":"catalogue.read","payload":{}}
```

Response: `protocolVersion`, matching `requestId`, `status`
(`ok|invalid|conflict|busy|not_initialized|recovery_required|unsupported_schema|unavailable`), `result`, `diagnostics`. Unknown operations,
invalid versions/types and oversized requests fail closed. Maximum request body:
1 MiB. Bound preview event lists to256; explicit monotonic time and deterministic
draws. No calls to a provider, model loader, TTS or live game.

| Operation | Payload/result |
| --- | --- |
| `catalogue.read` | No arbitrary path; configured registry source -> furniture snapshot/version/hash |
| `arrangement.evaluate` | Synthetic request -> real resolver result; unavailable until furniture API is ready |
| `draft.list` | SQL draft IDs/types/revision hashes |
| `draft.read` | Kind and ID -> document and content hash |
| `draft.write` | Kind, ID, document, optional editor state and expected hash (`null` for new) -> content hash or conflict; draft semantic errors remain inspectable |
| `authoring.initialize` | No payload -> create the configured authoring store only if it does not exist |
| `authoring.info` | No payload -> store ID, schema version and store revision |
| `authoring.import` | `mode=legacy` or hash-named `mode=export`, expected store ID/revision and operation ID -> import receipt; never runs on open/save/build |
| `authoring.list` | Optional kind, cursor and limit -> one typed document page |
| `authoring.read` | Document ID and optional revision hash -> immutable source document and revision metadata |
| `authoring.write` | Kind, ID, logical path, document, expected hash and operation ID -> new revision or conflict |
| `authoring.selection.read/write` | Named build-set selection; writes use expected revision hash and operation ID |
| `authoring.export` | Explicit document IDs plus expected store ID/revision -> deterministic hash-named export receipt |
| `authoring.backup` | No path -> consistent backup under the configured local workspace |
| `content.validate` | Typed Topic/Scenario draft plus explicit host context -> shared validator diagnostics |
| `preview.run` | Draft, synthetic host context, ordered events/draw/time -> deterministic session trace and pending choices |

Draft kinds: `topic` and `scenario`. Their bounded JSON wrapper and supported optional
fields are stored as immutable SQL revisions; `Drafts/...` is a logical alias, not a
filesystem location. Preserve unknown fields and bilingual content. No conversion
between the two scenario contracts by renaming keys. Complete content kinds share the
same typed repository; imported legacy sources remain exact and read-only until an
authoring save creates a new revision.

Canonical local DB: `Tools/ContentWorkbench/Workspace/authoring.sqlite3`. The host
creates it only through explicit `authoring.initialize`; missing/corrupt/future stores
fail closed. The explicit legacy importer is confined to
`Assets/StreamingAssets/Dialogue`, and export import accepts only a hash-named folder
under `Content/AuthoringExports/`. Normal commands do not scan JSON or accept arbitrary
filesystem paths. Draft IDs remain stable safe identifiers; revision hashes and
transactions provide compare-and-set behavior. Symlink/junction database and backup
roots are rejected. No shared personal content or real saves. No publish-to-
StreamingAssets command exists; only the Editor's validated SQL build/install path
can produce the runtime release.

### Command bootstrap

Build the standalone host once with `Tools/ContentWorkbench/build-core.ps1`, then
send one UTF-8 JSON envelope per invocation through `Tools/ContentWorkbench/workbench.ps1`.
For a clone being imported from its retained Dialogue tree, the explicit sequence is:

1. `authoring.initialize` with an empty payload.
2. `authoring.info`; keep the returned `storeId` and `storeRevision`.
3. `authoring.import` with `mode: "legacy"`, those expected store values and a
   stable `operationId`. The result receipt names every imported document ID.
4. `authoring.selection.write` with `name: "default"`, the receipt's document IDs,
   `expectedHash: null` and a stable `operationId`.

Fresh clones bootstrapping a reviewed snapshot use `mode: "export"` plus its
`snapshotHash` at step 3. The host resolves that hash only under
`Content/AuthoringExports/`; it never chooses the latest export automatically.
The local 2026-10-22 snapshot is recorded in [[SQLite Authoring Cutover Implementation Plan]].

### Editing shared Dialogue

Use `authoring.list` with `kind: "generic_dialogue"` or `"topic_definition"` to
find the stable `documentId` and logical path. Read that ID with `authoring.read`,
edit only the intended fields in its returned `document`, then call `authoring.write`
with the same ID/kind/path, the new document, the returned `contentHash` as
`expectedHash`, and a fresh `operationId`. The exact hash is compare-and-set
protection: a `conflict` means another edit won, so reread and reapply the change.
The command stores a new immutable revision; it does not compile, export or install
that revision. Run the Unity **Build Runtime SQL Content** command to validate and
publish it, and use `authoring.export` when you want a tracked review/fresh-clone
snapshot. Do not edit the SQLite file directly or edit the generated export/retained
legacy JSON as if either were the live source.

The current supported authoring surface is the command host. The Unity Topic
Playground tests routes with synthetic input; it is not a shared Dialogue editor.

## Local service and UI safety

Bind loopback only, random per-launch session token, exact origin/Host validation,
no wildcard CORS, content-type/size checks and CSRF protection for writes. Do not
put secrets in logs; token is local session authorization, not a provider key.
Static assets are served from an explicit allowlist. Render authored text as text,
not executable markup. No unsafe subprocess shell or unauthenticated network API.

Use explicit Reload catalogue to pick up registered definitions; runtime sessions
pin their snapshot/hash. No directory watcher/live runtime reload in v1. This
satisfies automatic discovery without per-definition UI code or a second manifest.
Do not overwrite edited home saves with new starter seeds; migration UI deferred.

## Coordination and readiness

Room milestone F1 delivers tested DTOs/validator/registry/eight JSON entries, then
F2 adds resolver/choice evaluation. Topic Playground may implement its shell/draft
operations and test-only fixture scaffolding before those gates. Until integration,
show furniture services unavailable; never fake production validation success.
Room owns furniture sources and Unity presentation. Playground does not use MCP
or mutate scene/prefab/serialized state. Both preserve M2/M3 follow-ups and private
data boundaries. Shared TODO/Home/feature register edits should be small additions.

Both command-line and UI parity tests must invoke the same shared host, including
unknown/locked/hidden, stale revisions, EN/JP text, failed writes and bad IDs.
Synthetic capabilities are a host-owned fixture allowlist; never derive allowed
actions from the draft being validated. `ScenarioPlaygroundSession.CreateSyntheticContext`
alone is not suitable as an authority boundary because it derives IDs from input.
Game runtime acceptance remains Splash→ModelTest; standalone preview is not proof
of navigation, sitting, animation, audio or Android execution.
