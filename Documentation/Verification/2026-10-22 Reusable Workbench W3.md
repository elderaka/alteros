---
title: Reusable Workbench W3 Verification
date: 2026-10-23
tags: [alteros, verification, workbench, loopback]
status: partial-transport-slice
---

# Reusable Workbench W3A transport slice

This is the authorized early transport slice of W3, not the full six-kind
Workbench acceptance. The remaining W3 integration waits for released W0-W2
producer APIs. The existing command host remains the only command executor;
the browser shell does not implement content validation or selection rules.

## Source changes

- `Tools/ContentWorkbench/server.py` serves the local static shell and forwards
  bounded UTF-8 JSON requests to a fixed-at-startup argument list with
  `shell=False`. It binds only to `127.0.0.1`, checks the exact Host and Origin,
  requires a per-process header token, caps request/stdout/stderr sizes,
  limits concurrent command processes, and terminates timed-out/over-output
  children.
- `Tools/ContentWorkbench/launch.ps1` checks for Python and the already-built
  host, then starts the service. It does not rebuild the host.
- `Tools/ContentWorkbench/web/` contains a minimal same-origin JSON request /
  response shell. It submits to the existing command host; it does not contain
  a second validator, authoring store, or executor.
- `Tools/ContentWorkbench/tests/test_server.py` uses a temporary fake child
  process and covers UTF-8 forwarding, loopback binding, Host/Origin/token
  rejection, request bounds, stdout/stderr bounds, child timeout, busy-host
  behavior, and static-shell security headers.

## Evidence (2026-10-23)

- `python -m unittest discover -s Tools/ContentWorkbench/tests -p test_server.py -v`:
  **8/8 passed**. This test file uses only the fake command process; it does not
  read or write the shared authoring database or launch the real command host.
- The pre-W3A Workbench baseline on 2026-10-22 passed **27/27** with
  `python -m unittest discover -s Tools/ContentWorkbench/tests -v`. That suite
  rebuilds `CoreHost/bin/AlterOS.ContentWorkbench.exe`; its hash changed during
  that baseline run. W3A did not rerun the suite or rebuild/replace the current
  executable, as requested for this integration window.
- Unity Editor remained open and untouched. No real host command, browser
  visual review, shared database operation, live UI/CLI parity, or six-kind
  validation/preview was performed.

## W3A review fix round 1 (2026-10-23)

The scoped review found that a reader thread could remain blocked when a child
process inherited stdout/stderr, so closing its buffered pipe could exceed the
request timeout. It also found that server shutdown did not own or stop active
command processes.

- `server.py` now starts each command in an owned process scope: a new POSIX
  session/process group, or a Windows Job Object configured to terminate its
  members when closed. Active scopes are registered with the server. Shutdown
  marks the service as closing, terminates registered scopes, and waits up to
  the bounded cleanup grace for request workers to unregister them.
- Request processing uses one monotonic command deadline. If the host misses it,
  output overflows, or pipe readers do not finish by the deadline, its process
  scope is terminated. Buffered stdout/stderr are closed only after their reader
  threads finish; an incomplete drain returns `host_io_failed` rather than
  blocking in `close()`. The total bound is the configured command timeout plus
  the fixed one-second cleanup grace.
- Fake-host regressions cover a host that exits while a four-second descendant
  retains stdout/stderr (bounded response, descendant termination, and command
  slot recovery) and service shutdown during an active host-plus-descendant
  command (bounded close and both processes stopped).

Exact verification:

- `python -m unittest discover -s Tools/ContentWorkbench/tests -p test_server.py -v`:
  **10/10 passed**, including both new lifecycle regressions.
- PowerShell AST parse of `Tools/ContentWorkbench/launch.ps1`: **passed**.
- `CoreHost/bin/AlterOS.ContentWorkbench.exe` SHA-256 before/after this fix:
  `91E650E0BF71868FD59CA856E0073C36A59843A45E9DDF0E436A4268E99FE928` (unchanged).
- No CoreHost rebuild, real command-host invocation, shared database access,
  browser launch, Unity Editor action, or profile/provider operation was run.

## W3A review fix round 2 (2026-10-23)

The second scoped review found that a Windows host could execute and spawn a
descendant before the service attached it to the Job Object.

- Windows command hosts are now created suspended, assigned to a Job Object,
  then resumed. The primary thread is located with Toolhelp32 and resumed only
  after successful assignment. If Job creation/assignment or resume fails, the
  still-suspended process is terminated and reaped before the request fails;
  it cannot have run code or created an unowned descendant.
- Two Windows-only fake-host regressions cover the ordering deterministically:
  one pauses Job attachment and verifies the host's startup marker remains
  absent until attachment completes, and one forces attachment failure and
  verifies the host stays unstarted and is reaped.

Exact verification:

- `python -m unittest discover -s Tools/ContentWorkbench/tests -p test_server.py -v`:
  **12/12 passed**, including retained-pipe, shutdown-tree, delayed-attachment,
  and forced-assignment-failure tests on Windows.
- PowerShell AST parse of `Tools/ContentWorkbench/launch.ps1`: **passed**.
- Host executable SHA-256 remained
  `91E650E0BF71868FD59CA856E0073C36A59843A45E9DDF0E436A4268E99FE928`.
- No CoreHost rebuild, real command-host invocation, shared database access,
  browser launch, Unity Editor action, or profile/provider operation was run.

## Limits and next gate

This was the W3A pre-integration status; the implementation and evidence below
supersede its statement that the six-kind forms were absent. Preview and reference
gates listed below remain open.

## W3B integration (2026-10-23)

The Workbench now exposes six stable UI kinds through the existing command host.
`schema.describe` maps them to producer source kinds and producer-backed controls;
`content.validate` routes through `ContentAuthoringValidator` plus the existing
Dialogue/Topic/Scenario validators. Revisioned create/read/edit remains the single
SQLite `AuthoringRepository` CAS path. The browser is a local editor over that same
command protocol, not another draft store.

The browser shell now has a catalogue and record list, descriptor-driven forms,
source JSON inspection, localized owned-Text editing versus explicit pinned Text
fields, a Flow graph with kind-specific Scenario step fields, validation diagnostics,
synthetic preview controls for the existing Topic/Scenario preview kinds, and a
source-record reference browser. Legacy Generic Dialogue remains discoverable and
selectable as a separate source contract; new Dialogue remains the default.

The producer Topic source now supports `scenarioRef`, as recorded in
[[2026-10-23 Topic Scenario Reference Producer]]. Workbench integration remains
deferred: the browser must obtain the canonical Flow `ContentRef` from an
importer-backed resolver over the selected source package. It does not synthesize
Flow hashes/revisions from Scenario authoring hashes. Existing embedded Topic entries
are displayed read-only and preserved. The standalone Text listing API is also not
released, and current nested reference fields remain explicit source JSON rather than
a complete typed ContentRef picker. These are concrete integration gaps, not claims
that the producer contract is missing.

### Evidence (2026-10-23)

- `python -m unittest discover -s Tools/ContentWorkbench/tests -p test_ui_commands.py -v`:
  **7/7 passed**. The suite compiles the host to a temporary output directory and
  creates/uses only a disposable synthetic authoring workspace. It covers all six
  descriptors, CLI/HTTP response parity, producer validation dispatch, CAS
  create/read/edit/stale conflict, Action catalog metadata, Topic reference deferral,
  and honest preview capability flags.
- `python -m unittest discover -s Tools/ContentWorkbench/tests -p test_server.py -v`:
  **13/13 passed**. This includes static module allowlisting and the existing bounded
  process/transport security tests. The Windows process-liveness test now queries the
  process exit code; `os.kill(pid, 0)` could report an exited process as present while
  its handle remained open.
- `node --check` passed for `workbench.js`, `draft-editor.js`, `graph-editor.js`, and
  `playground.js`.
- Standalone host build to a fresh `%TEMP%` directory passed, with existing
  reflection-deserialization CS0649 warnings and no compile errors. The temporary
  authoring store was initialized for browser inspection; no real workspace database
  was opened. The browser rendered the catalogue and Scenario editor, and switching a
  step from `end` to `dialogue` changed the visible fields to the producer-declared
  dialogue fields. No draft was saved.
- The shared `CoreHost/bin/AlterOS.ContentWorkbench.exe` SHA-256 remained
  `91E650E0BF71868FD59CA856E0073C36A59843A45E9DDF0E436A4268E99FE928`.
- Unity MCP confirmed `E:/Pleiades/alteros`, Unity `6000.4.4f1`, ModelTest open,
  Editor idle/not compiling, and zero console errors. No Unity source, scene, or
  serialized assets were touched; Unity tests and Play Mode were not run.

### Remaining integration gate

W3B is a partial Workbench integration, not six-kind workflow acceptance. Before
calling the six-kind UI complete, add a producer/importer-backed exact Flow-reference
picker (including Topic entries), a Text source listing contract, and focused typed
reference controls. Then consume the Behavior W2 release and verify shared-runtime
preview scenarios. Current evidence includes static/browser smoke only; it does not
establish live GTE inference, an end-to-end preview lifecycle, semantic routing, or
player-visible acceptance.

## W3B editing fix round 1 (2026-10-24)

The editing review found eight data-integrity/usability defects. This round fixes
them within Workbench-owned source:

- Existing records now save with the repository's original `documentId`, source
  `kind`, `logicalPath`, and expected revision hash. Only an explicit New draft
  derives its ID and `Workbench/<kind>/<id>.json` path from the source identity.
- The form tracks dirty document/raw-JSON state, asks before replacing it, guards
  tab close, preserves local edits on CAS conflict, and binds async save responses to
  their captured document and editor-session generation. Undo/redo is available over
  in-memory edits.
- Adding localized translations starts from current document state. Text/textarea
  arrays use string values. Furniture ID mirrors and function-binding synchronization
  run through the real renderer, retaining unknown binding properties for unchanged
  bindings.
- Browser JSON parsing/stringification preserves unsafe numeric tokens. Int64 fields
  are text-edited with exact BigInt range checks; values outside finite browser range
  fail visibly before the document can be edited/saved.
- Catalogue and reference lists follow all repository cursors (bounded to 1,000
  pages, with repeated-cursor detection). Source validation now always returns
  `readyForPublishedRelease: false`; it does not resolve a selected release's
  dependencies.

### Evidence (2026-10-24)

- `node --test Tools/ContentWorkbench/tests/test_browser_state.mjs Tools/ContentWorkbench/tests/test_browser_renderer.mjs`:
  **8/8 passed** against the actual browser editor-state and form-renderer modules.
  Cases cover repository/source ID divergence, exact stored paths, create-only path
  derivation, in-flight edits/navigation, dirty raw JSON and leave guards, undo/redo,
  101-record pagination, translation preservation, string arrays, Furniture mirror/
  binding sync, and exact Int64/unknown-number tokens including visible overflow.
- `python -m unittest discover -s Tools/ContentWorkbench/tests -p test_ui_commands.py -v`:
  **8/8 passed**. The temporary host build and database are isolated from the shared
  `CoreHost/bin` and authoring workspace. The unresolved pinned-Text fixture passes
  source validation but explicitly cannot report release readiness.
- `python -m unittest discover -s Tools/ContentWorkbench/tests -p test_server.py -v`:
  **13/13 passed**, including static allowlisting of the two new browser modules.
- `node --check` passed for all Workbench browser modules.
- Isolated browser/host/CLI smoke used the disposable store under
  `%TEMP%\alteros-workbench-ui-afd2637d106e41678f3c6c3856feb83a`: an imported-style
  Generic Dialogue with `documentId=src_0062…` and its original `Generic/...` path
  was edited, saved, reopened, and read back from the same ID/path. An external CLI
  revision then caused a real stale-CAS conflict; the browser kept the local text
  and displayed the conflict. Undo/redo was also exercised. No draft was saved to the
  real workspace.
- The standalone host returned `stackLimit=9007199254740993` and unknown nested
  `counter=9223372036854775807` as exact JSON tokens in the disposable store. The
  shared `CoreHost/bin/AlterOS.ContentWorkbench.exe` hash remained
  `91E650E0BF71868FD59CA856E0073C36A59843A45E9DDF0E436A4268E99FE928`.
- No Unity source, scene, serialized asset, shared authoring database, provider, or
  player/profile was accessed. Unity is outside this fix round.

### Remaining gates

The review's eight W3B editing findings are addressed by the source changes and
targeted evidence above. Full acceptance still requires a complete live browser
visual/accessibility pass and the separately deferred importer-backed typed Flow/
Topic reference workflow and Behavior W2 preview gate. The final visual smoke
session hit a browser confirmation modal that the available CUA bridge could not
dismiss; the local server was stopped, and no project data was involved.

## W3B raw-JSON arbitration fix round 2 (2026-10-24)

The remaining editing review found that a pending Source JSON edit could be replaced
by the form renderer's next change callback. The two edit surfaces now arbitrate
explicitly: pending raw text shows Apply/Discard controls, makes the form editor inert,
and blocks validation, preview, save, undo and redo until the choice is made. The
renderer receives a detached document clone and checks its edit guard before every
mutation; the Workbench callback checks again before committing the clone. Applying
parses and records the raw document in history. Discarding remounts the form from the
unchanged bound document. Invalid pending JSON stays present until explicitly
discarded or corrected.

### Evidence (2026-10-24)

- Added an integration regression executing the actual `workbench.js` module with the
  real `draft-editor.js` renderer and editor-state/lossless modules. It covers valid
  and invalid pending source text, attempted form edits while pending, blocked
  history/save state, explicit Apply, undo recovery, and explicit Discard.
- `node --test Tools/ContentWorkbench/tests/test_browser_state.mjs Tools/ContentWorkbench/tests/test_browser_renderer.mjs Tools/ContentWorkbench/tests/test_workbench_raw_json_guard.mjs`:
  **9/9 passed**.
- `node --check` passed for all seven Workbench browser modules.
- Shared `CoreHost/bin/AlterOS.ContentWorkbench.exe` SHA-256 remains
  `91E650E0BF71868FD59CA856E0073C36A59843A45E9DDF0E436A4268E99FE928`.

### Remaining gates

The original eight editing findings and the pending raw-JSON overwrite reproduction
now have focused automated evidence. The earlier isolated browser save/reopen/CAS
conflict smoke remains the browser integration evidence for persistence. No new live
visual pass was possible in this round; a temporary CUA confirmation dialog from the
previous smoke could not be dismissed through the available bridge. The browser
server is stopped, and only the disposable `%TEMP%` workspace was used. Typed Flow/
Topic references, standalone Text listing, W2-backed preview and full visual/
accessibility acceptance remain separate gates.

## W3B renderer/session alias fix round 3 (2026-10-24)

The round-three review found that the first accepted renderer change assigned the
renderer-owned object directly to session state. A second form edit then mutated
that shared object before the raw-source guard, making the first edit appear pending
and suppressing synchronization/history. The Workbench callback now clones every
accepted renderer document before publishing it to session state. The renderer and
session remain detached across successive edits; the raw-source guard stays in place.

### Evidence (2026-10-24)

- Extended the actual `workbench.js` integration regression: after Apply/Undo, edit
  Note, then Stack limit in separate form operations. Both changes synchronize to
  Source JSON; Undo twice and Redo twice restore the expected snapshots; Save carries
  the latest values. The same test retains valid/invalid pending-JSON guard coverage.
- Test-first reproduction failed on the second edit with Source JSON still showing
  the first value. After cloning at the Workbench publication boundary:
  `node --test Tools/ContentWorkbench/tests/test_workbench_raw_json_guard.mjs`
  passed **1/1**.
- Full focused browser-module set:
  `node --test Tools/ContentWorkbench/tests/test_browser_state.mjs Tools/ContentWorkbench/tests/test_browser_renderer.mjs Tools/ContentWorkbench/tests/test_workbench_raw_json_guard.mjs`:
  **9/9 passed**. `node --check` passed for all seven Workbench browser modules.
- Shared Workbench host SHA-256 remains
  `91E650E0BF71868FD59CA856E0073C36A59843A45E9DDF0E436A4268E99FE928`.

### Remaining gates

This closes the renderer/session alias regression for the bounded W3B editing
checkpoint. The existing scoped reviewer should perform round-four disposition.
Typed references, W2 preview/parity and full browser visual/accessibility acceptance
remain separate gates; no new browser or Unity runtime claim is made by these tests.

## W3C importer-backed typed-reference workflow (2026-10-24)

Released a bounded typed-reference resolver to the shared Workbench command host.
The browser asks the user to choose source documents from a paginated, 100-item
authoring list. Each checked row shows the stored revision hash. Resolution sends
the explicit `(documentId, revisionHash)` pins, expected authoring `storeId`, and
expected `storeRevision`. The host reads one `AuthoringRepository.ReadSnapshot`,
checks every pin against that atomic snapshot, projects those exact source bytes,
and invokes `LegacyContentAdapter.ImportDocumentsWithSequenceBuilder` with the
Workbench's existing activity sequence builder. It does not access SQLite directly,
calculate a ContentRef hash in the browser, bake/publish a release, or switch runtime
loaders.

`references.list` requires `targetKind` (`dialogue`, `action`, `scenario`, `topic`,
`furniture`, `item`, or `text`), `sourcePins`, `expectedStoreId`, and
`expectedStoreRevision`. A successful response includes the exact source pins,
snapshot hash/store revision, and importer-produced `ContentRef` store/id/revision/
expectedHash. Direct owning source documents include their own source pin. Text
records derived during import may have no single direct owner; their owner list is
empty rather than guessed. A changed store/source pin, missing source, projection
error, or importer validation error fails closed with diagnostics. Resolving only
reports what the detached importer produced; it does not validate a published
release or create one.

The Topic editor now exposes scenario entries with a stable entry `scenarioId` and
a typed reusable Scenario reference selector. New entries do not receive `nodes`;
the selector consumes only importer-resolved candidates. Existing inline `nodes`
remain read-only and intact, and their Scenario selector is disabled. Replacing
those nodes still requires a separate explicit conversion workflow; no conversion
or deletion is performed here. Candidate controls preserve unknown properties on
the existing reference object.

### Evidence

- Test-first endpoint regressions initially failed for the unimplemented command
  and deferred schema field. After implementation, the command test proves an
  exact Scenario Flow reference, independent Topic entry ID, successful import of a
  Topic that pins that exact Flow, stale-hash conflict behavior, missing-source
  failure, and byte-for-byte CLI/HTTP response parity for the same request.
- `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_*.py'`:
  **50/50 passed**. These tests build disposable command hosts and use temporary
  synthetic authoring stores; they do not modify the project authoring database.
- Node browser module suite: **11/11 passed**, including typed reference selection,
  unknown reference-field preservation, legacy-node protection, and raw-JSON edit
  guards. `node --check` passed for each `Tools/ContentWorkbench/web/*.js` module.
- No Unity Editor import/compile, live browser visual/accessibility inspection,
  Content release publication, or runtime/gameplay reference resolution was run.
  Scope deviation: the final broad Python discovery suite runs older test classes
  that invoke `build-core.ps1` without `-OutputDirectory`, rebuilding the shared
  `CoreHost/bin/AlterOS.ContentWorkbench.exe`. Its SHA-256 immediately before that
  suite was `FA084E45928CCCB8449DB4A4A273F2B41C71244857D7F1CD1A786B0B1B1353C9`;
  its SHA-256 at final inspection is
  `F6B70BCE7EE87E4C885407F13E2F98D706E45A44AC9DC1681767328C5219294E`. No exact
  pre-suite copy was found among the inspected disposable Workbench hosts, and no
  guessed replacement was written. Treat this shared-binary rebuild as an open
  recovery/review issue before accepting W3C.

### Remaining gates

This is the W3C reference-authoring checkpoint, not final W3 acceptance. The same
scoped reviewer should review this checkpoint and the shared-binary scope deviation
before W3C is accepted. W2-backed
preview/runtime parity, full browser visual/accessibility acceptance, authored
catalogue migration/validation owned by the other session, publication, and live
gameplay remain out of scope. Do not call reference inspection release readiness.

## W3C fix round 1 execution plan (2026-10-24)

Bounded plan from the scoped W3C review; accepted W3B behavior and producer/runtime
source are not reopened.

- [x] Isolate `test_commands`, `test_authoring_sqlite`, and
  `test_authoring_interchange` builds to per-class temporary output; add a setup
  regression proving no shared `CoreHost/bin` fallback. Verify shared executable
  and dependency hashes are unchanged before running broad discovery.
- [x] Run the existing `ContentPackageValidator` /
  `ContentReleaseCompiler.ResolveDependencies` path over the detached selected
  package before returning any candidate; report missing/conflicting closure
  dependencies without publishing.
- [x] Connect typed reference candidates to localized Text, Scenario Action, and
  supported host-effect Item fields using actual producer descriptors; preserve
  owned/reference XOR, mirrors, unknown properties, and existing pins.
- [x] Separate result target kind from source kinds; retain and display exact mixed
  source pins across target changes and pagination; invalidate and generation-guard
  browse/page/resolve replies against captured target/store/revision/pins.
- [x] Add callback/host regressions for mixed closure, missing dependencies,
  selection/pagination, typed controls, and delayed replies. The browser callback
  suite and focused temporary-host checks passed before the producer compile window
  changed.
- [ ] After DBMS releases the concurrent producer source, rerun the focused UI-host
  suite and coordinate one full Python discovery run; do not retry before that gate.

Files in scope: `Tools/ContentWorkbench/**` and this verification note only. Do not
edit shared content/behavior source, the workspace authoring DB, the preserved host
binary, Unity state, provider settings, or model files.

### W3C fix-round implementation/evidence (2026-10-24)

The W3C resolver now calls `ContentReleaseCompiler.ResolveDependencies` (which runs
the production `ContentPackageValidator`) against the detached imported package
before it returns any candidate. Resolver fixtures with valid-shaped but absent
Item Text, Scenario Dialogue, Scenario Action, and host-effect Item references all
fail with the production dependency diagnostic. A positive mixed fixture resolves
Topic → Scenario → Action/Dialogue → derived Text using explicit source pins and
does not create a release. No Workbench shadow dependency validator was added.

The descriptor/renderer path now connects localized Dialogue/Item/Furniture Text
references to selected Text candidates, retains existing pinned refs and unknown
fields, keeps owned-vs-reference exclusive, and updates Furniture's mirrored Text
ID. Scenario action/host-effect rows expose their Action reference. Action host
effects expose registered function fields and the nested Item reference/quantity
controls for `player.item.grant`; the sequence JSON editor remains for
`activity_sequence`, with its registered Bark `dialogueId` literal exposed as a
typed Dialogue candidate control. Conditional controls are driven by
producer-descriptor fields.

The source picker now separates result kind from source document kind, builds its
source-kind list from all six producer descriptors, preserves the selected exact
revision set across source kinds/pages/target changes, displays that complete set,
and requires explicit selection of a newer revision after a head changes. Browse,
page and resolver requests capture target/store/store-revision/normalized pins and
generation; any context change clears candidates, and late responses are discarded.
Failures leave no usable prior candidates and the authored document is not mutated.

### W3C fix-round verification (2026-10-24)

- The test-first build-isolation regression failed before the fix because
  `test_commands` invoked `build-core.ps1` without `-OutputDirectory`. After the
  fix, `python -m unittest Tools.ContentWorkbench.tests.test_build_isolation`:
  **1/1 passed**. The three actual legacy classes plus isolation regression passed
  **28/28** with their private temporary outputs.
- Immediately before broad discovery and after it, all six files in
  `Tools/ContentWorkbench/CoreHost/bin` retained matching SHA-256 values. The host
  stayed `F6B70BCE7EE87E4C885407F13E2F98D706E45A44AC9DC1681767328C5219294E`; the
  three legacy classes did not rebuild or replace it. The other unchanged hashes
  were `gilzoide-sqlite-net.dll` `E7E370938925DFF66D9A4D50BFAC6D18CC9505BD2F3F127A232B410491B46E3E`,
  `Gilzoide-SqliteNet-LICENSE.txt` `11807EF18E8F93E2BB89C907088D5194C182A776467620C623A3A9529E8AF559`,
  `netstandard.dll` `9F12554DDF1FA4EF8BEFABBB8616173A3BF020B7512ADA39F21E746DDD1D18FC`,
  `Newtonsoft.Json.dll` `7292D3EB508652D14726749DD27094F2D481AECCF2DB6427B62F68A71460897E`,
  and `sqlite-net-LICENSE.txt` `661D9B5FAFFB891426E973376039EBB753D92ACC4FBA1E8AFDF694C018B277B8`.
- Current browser module suite:
  `node --test Tools/ContentWorkbench/tests/test_browser_state.mjs Tools/ContentWorkbench/tests/test_browser_renderer.mjs Tools/ContentWorkbench/tests/test_workbench_raw_json_guard.mjs`:
  **15/15 passed**. All Workbench browser modules passed `node --check`.
- Before the DBMS producer source changed later in this turn, focused temporary-host
  checks passed for the four missing-dependency cases, the mixed
  Topic/Scenario/Action/Dialogue/Text closure, and actual `schema.describe` fields
  for localized Text, Scenario Action, and nested host-effect Item picks. The Bark
  Dialogue candidate renderer regression passes in Node, but its latest
  `argumentName` descriptor metadata has not been recompiled through the C# host.
  None of those host checks is a fresh compile against the current producer source.
- Broad Python discovery ran **41 tests**, then `test_ui_commands` setup failed in
  its disposable host compile because the concurrently edited shared producer
  `Assets/AlterOS/Content/ContentBuildManifest.cs` does not compile in the host
  source window (missing `System.IO`, missing furniture-registry input, and
  instance/static member errors). The producer file changed again after that build
  failure; the current snapshot was not retried. This source is outside Workbench ownership and
  was not edited. The failed setup wrote only to the test's temp output. Per Main's
  coordination, hold further C#-dependent reruns until the DBMS owner releases the
  producer source; then coordinate one fresh focused/UI and broad verification.
- No shared authoring database, Unity Editor, provider/profile, or model was used.
  W2 preview/runtime parity and visual/accessibility inspection remain unverified.

### W3C fix round 2 (2026-10-24)

The second scoped review identified three live-Workbench defects: importer
references were kept as a flat list although typed fields consume candidates by
target kind; resolver invalidation cleared application state without revoking
already-mounted selectors; and Action variant/function changes retained hidden
incompatible `data` members. The accepted W3B fixes and producer/runtime sources
remain unchanged.

The shared editor now groups resolver results by `targetKind` before mounting the
typed controls. Each mounted reference control captures the resolver generation.
Context changes and resolver start/failure remount the current form without
candidates, and an old control's event handler independently rejects its captured
generation. The authored document and pending raw JSON are not changed by
invalidation.

Changing Action implementation or registered host-effect function now asks for
confirmation, replaces `data` and `capabilityId` with the selected contract's
shape, and records the transition through the existing document history. Undo
restores the complete previous variant, including extension fields. Item grants
start with quantity 1; power-set actions start with desired state `Off`; changing
functions clears arguments that do not belong to the new function.

### Round-two evidence and remaining gate

- Added actual Workbench-window-path fixtures for Topic resolve → typed select →
  save, invalidated old selectors, failed resolver revocation, Action variant
  confirmation/Undo, importer-pinned Item selection, quantity editing, and
  function-specific argument reset. The Action fixture captures the exact
  host-effect document submitted by the UI to `content.validate`; its fake host
  response is not producer validation evidence.
- Red/green check: with the Action transition guard temporarily disabled, its
  regression failed because the UI changed to `host_effect` while retaining the
  previous mode; after restoring the fix, the focused/full browser suite passed.
- Focused browser suite:
  `node --test Tools/ContentWorkbench/tests/test_browser_state.mjs Tools/ContentWorkbench/tests/test_browser_renderer.mjs Tools/ContentWorkbench/tests/test_workbench_raw_json_guard.mjs`:
  **17/17 passed**. `node --check` passed for the changed `workbench.js` and
  `draft-editor.js` and all other Workbench browser modules; the build-isolation
  regression passed **1/1**. The current shared host executable hash remains
  `F6B70BCE7EE87E4C885407F13E2F98D706E45A44AC9DC1681767328C5219294E`.
- Host/producer validation is still pending. The concurrent DBMS producer source
  has another review round in progress; per coordination, do not compile or rerun
  C#-dependent suites until Main signals a stable producer API. The standalone
  build input `Assets/AlterOS/Furniture/FurnitureCatalogueRegistry.cs` is present
  in `build-core.ps1`; it is not a substitute for that fresh compile.
- No Unity Editor, saved scene, authoring database, provider/profile, shared host
  binary, or model was touched. Visual browser inspection, production producer
  validation, host-backed fixture parity, release publication and runtime use
  remain unverified.

### W3D detached six-kind preview integration (2026-10-24)

The shared `preview.run` command now compiles an ephemeral release from one inline
root or an exact pinned root plus explicitly selected source revisions, then
dispatches typed preview adapters through the same command host used by CLI and
the Workbench window. No release is published or installed. The window now sends
the exact source pins chosen in its reference picker, accepts a bounded event
script as JSON, and displays the returned synthetic report.

- Dialogue uses the shared linear Dialogue playback and exact localized Text;
  Item resolves its selected definition/Text without changing inventory;
  Furniture uses the production catalogue registry and function-target validator
  against a detached fixture. The Action form now exposes a primitive/composer
  palette from the registered `ActivitySequenceCatalog`, with typed arguments,
  producer-result bindings and graph-addressable steps. Sequence preview uses the
  production `ActivitySequenceReleaseAdapter` and `ActivitySequenceExecutor` with
  fixture-owned safe ports, including exact target reservation, movement, bark,
  `power.set`, and mock dispenser collection. `player.item.grant` goes through
  `ScenarioHostEffectExecutor` and a synthetic in-memory port with exact pinned
  Action/Item validation, stack bounds and non-durable receipts. Host effects
  previewed as standalone Actions remain inspection-only. Nothing writes player
  saves; unsupported operations remain visibly unsupported.
- Typed Topic is explicitly direct-family selection, not semantic-routing proof.
  It uses `TopicScenarioSelector`, Flow preparation/`ScenarioSession`, and detached
  `TopicHistory`. Its event path acknowledges dialogue, records flow checkpoints,
  closes cancellation without undoing consumed checkpoints, blocks a consumed
  occurrence on reselect, and can inspect a fresh occurrence of the same Topic.
  History observations use authored source-step IDs for the checkpoint while
  retaining generated execution-step IDs for uniqueness; this was found by the
  once-consumption regression.
- CLI/HTTP tests compare complete results for Item, Dialogue, Furniture, Action,
  Topic and Scenario fixtures. The Topic lifecycle fixture verifies checkpoint
  consumption, cancel, consumed reselect, a new occurrence, and completion-time
  cooldown. These are headless host fixtures, not live gameplay.
- Action host-effect preview no longer constructs the same `executionStatus`
  result key twice. A completed Topic continuation keeps its actual end outcome
  rather than replacing it with the startup-only fallback. Locate reserves the
  exact available TV, moves to it, and says “Hey, it's here”; it does not turn on
  power or force a return.
- Furniture catalogue settings are selectable, editable, validated and saved
  under the existing Furniture kind as the fixed singleton
  `furniture.catalogue` at `Furniture/catalogue-settings.json`. The form covers
  catalogue conventions, relationship rules, capabilities and legacy aliases;
  settings remain a source dependency, not a previewable Furniture definition.

### W3D verification and remaining gates

- Focused `WorkbenchEditorCommandTests` for settings create/read/edit/CAS,
  Action host-effect inspection, Locate, TV power-on, Fetch, Topic lifecycle, and
  production Action sequence preview: **7/7 passed**. The tests compiled the host
  into a temporary directory and compared complete CLI/HTTP responses for the
  affected command paths.
- `node --test Tools/ContentWorkbench/tests/*.mjs`: **19/19 passed**, including
  the six-kind Workbench form tests, Furniture settings create/edit/save/reopen,
  unknown-metadata preservation, and catalog-driven Activity Sequence step and
  composer controls. `node --check` passed for all **8** browser JavaScript
  modules. Updated static module allowlist test: **1/1 passed**.
- The real browser connected to an isolated temporary workspace and visibly
  rendered the six-kind navigation, Furniture definition form and point graph.
  Switching its source selector then timed out in the browser-control channel;
  no settings draft was saved there, so actual browser settings save/reopen and
  visible sequence-palette behavior remain unverified. The isolated workspace
  was `C:\Users\lauda\AppData\Local\Temp\alteros-workbench-ui-11cf3f4e24d5407e834d0c564a3e43f0`;
  its database was explicitly initialized and contains no authored test drafts.
  The private server was stopped. The browser-control timeout is not evidence of
  a source defect: the exact Furniture descriptor and both source-list commands
  succeeded through the isolated host, while the visual source-switch/save path
  remains unverified.
- The default host was rebuilt after the UI/settings changes to
  `Tools/ContentWorkbench/CoreHost/bin/AlterOS.ContentWorkbench.exe`; build
  succeeded with `CS0649` warnings on unused legacy archive fields in
  `GeneratedArchiveImporter.cs`. SHA-256:
  `65934C3E6A8D9969B80E1D94A125940C67D50ABF206090922DF913A5F11D7DE6`.
  Normal launch command:
  `powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File Tools/ContentWorkbench/launch.ps1`.
  It uses `Tools/ContentWorkbench/Workspace/authoring.sqlite3`, which exists;
  this task did not initialize, import, or mutate that default store.
- Topic semantic matching from natural-language query text/embeddings is not
  included: the UI names this as direct-family testing, not routing evidence.
- Detached proof fixtures exercise three complete flows through the shared Topic
  selector/history, ScenarioSession, ActivitySequenceReleaseAdapter,
  ActivitySequenceExecutor and typed fixture ports: an exact available TV receives
  `powerOn` with no forced return; a non-TV Locate topic reaches its exact bark/end
  without power or return; and Fetch performs a mock collect, explicit return,
  cheerful authored VN acknowledgement, and exactly one synthetic water-glass
  grant. Additional assertions cover a second TV, a second non-TV target, a second
  Item, cancellation before the consumed checkpoint, replay/once rejection, and a
  no-resources failure with no reservation. This is fixture-level adapter proof;
  it does not establish Unity-world navigation, visual acknowledgement, animation,
  or durable inventory behavior.
- No Unity Editor import/compile, Play Mode, saved-scene access, live GTE inference,
  real host capability, provider/TTS call, Unity visual inspection, release
  publication, or gameplay integration was performed. Preserve the stop before
  live gameplay.

Next gate: retry the actual browser UI pass after the browser-control timeout and
verify Furniture settings source switching, save/reopen and a form-generated
Action preview against CLI. Then proceed to DBMS's isolated selected-release
preparation; keep W5 installation and live Unity integration separately gated.
