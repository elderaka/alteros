# Content Workbench Implementation Plan

> Purpose clarification2026-10-22: the full target covers Dialogue, Scenario, Topic,
> Furniture, Item and Action authoring through shared typed contracts. This W1-W5
> plan remains the original bounded Topic/Scenario UI slice, not completed support
> for every type. See [[Content Workbench Shared Contract#Workbench-wide target and readiness (2026-10-22)]].
> [[Scripted Actions and Inspect TV Implementation Plan|Action Framework Foundation]]
> prepares reusable operations, discoverable descriptors and headless Action preview;
> Turn on TV is one integration fixture. Item/full Furniture editing are later
> adapters, not prerequisites for implementing that foundation. No new START here.

> Storage amendment 2026-10-22: [[SQLite Authoring Cutover Implementation Plan]]
> carries the approved SQL-authoring target and is implemented through source and
> headless checks. The command host no longer stores drafts in JSON files. Earlier
> JSON-draft steps below are implementation history, not permission to add a second
> editable authority. Unity Editor acceptance remains open; see the linked current
> verification note before claiming runtime acceptance.

> **For agentic workers:** Use `executing-plans` task-by-task in the existing Topic Playground session. Human authorized finishing these plans and assigning implementation2026-10-21. Steps use checkbox syntax; this is implementation START for the bounded standalone first slice.

**Goal:** A local Flow-first Topic/Scenario workbench with bilingual editing, furniture discovery, validation, safe drafts and deterministic synthetic preview, callable by human UI or agent CLI.

**Architecture:** Plain browser UI talks to a local Python standard-library server which invokes one C# JSON-command implementation. That host reuses the actual pure production validators/Topic selection/Scenario session and Room Testbox furniture core. No duplicate routing, eligibility or permission logic in JavaScript/Python.

**Tech Stack:** Existing Python runtime (stdlib only), HTML/CSS/JavaScript, portable C# host built with existing Roslyn/.NET Framework facilities. No npm/web framework/new SDK requirement. Build tools may use installed Unity compiler; built host runtime must not reference Unity DLLs or need an open Editor.

**Spec:** Approved Flow-first mockup/conversation and `Content Workbench Shared Contract.md`; furniture source spec is `Furniture Mechanics and Catalogue Design.md`; existing Topic/Scenario Playground handoffs establish reusable semantics.

> Direction amendment 2026-10-22: the broader content-creator scope and trusted
> C# Action versus editable definition boundary are journaled in [[Content Workbench Shared Contract#Content-creator direction and Action dependency (2026-10-22)]].
> The newly discussed checkpoint/branch continuation mechanic must be implemented
> and verified before extending the Workbench to author it. This adds no new START,
> code-mod executor, or implicit expansion of W1-W5.

## Global constraints

- Root `E:/Pleiades/alteros`; preserve shared/untracked edits. No broad stage/commit or automatic worktree isolation that loses other sessions' source.
- UI is tooling, not in-game HUD. No Unity MCP/scene/prefab/meta changes in this session; Room Testbox alone owns those.
- All editable content is draft/synthetic; no user memories, saves, API keys, provider/TTS/model calls or production publication.
- Outside-Unity operation must be real, not a mockup calling fake validators. Show services unavailable until their implementation is ready.
- Two first-class formats: canonical TopicDefinition and executable ScenarioDefinition. Preserve metadata/optional fields; do not silently rename or convert contracts.
- One furniture registry. Explicit Reload for new snapshots; no duplicate editable furniture manifest or UI per type.
- First UI is Flow first with selected-step writing inspector, not two independent editors. Real navigation/animation is not proved by preview.

## Review focus

1. Hostile local HTTP/paths/content: auth/CSRF/origin checks, no traversal or script execution (W1/W2/W3).
2. Lost metadata or concurrent writes: round trip plus CAS conflicts, no overwrite (W1/W3).
3. Draft self-authorizes invented capability: explicit synthetic host context, shared validators, fail closed (W1/W4).
4. Missing furniture core or changed catalogue: unavailable state and snapshot-pinned preview, not fake green (W1/W4).
5. UI/CLI drift and inability to run without Unity: same command host, end-to-end parity and runtime dependency checks (W2/W4/W5).

## Ownership and prerequisites

Own `Tools/ContentWorkbench/`, new `Tools/ContentWorkbench/CoreHost/`,
`web/`, `tests/`, `Fixtures/`, SQL-backed draft commands, build/launcher scripts and README.
Do not edit Room Testbox's `Assets/AlterOS/Furniture/` or canonical
`StreamingAssets/Furniture/catalogue.json`. Core sources referenced read-only
come from current Stats/Dialogue/Topics/Scenarios and the Room-owned furniture API.
No editing live Generic corpus, anchors or existing Playground behavior.

Read the last interrupted Topic Playground TMP report as historical context, not
proof of a current compile failure. This task does not authorize broad Unity fixes.
Document missing prerequisites honestly. Do not install SDKs/packages globally.

## W1 — Shared headless command and draft core

**Files:** `CoreHost/WorkbenchCommandHost.cs`, `WorkbenchCommands.cs`,
`WorkbenchDraftStore.cs`, `WorkbenchPreview.cs`, `build-core.ps1`,
`workbench.ps1`; `tests/test_commands.py`, `Fixtures/requests/`.

**Interfaces:** `WorkbenchCommandHandler.Handle(WorkbenchRequest request, WorkbenchEnvironment environment)`
returns `WorkbenchResponse`; request/response fields and operation payloads are
exactly the shared contract. Environment owns the trusted SQLite authoring database,
configured import/export roots and read-only synthetic fixtures. CLI reads one
request JSON from stdin and writes one response JSON, no logging to stdout.
Diagnostic records use shared code/path/severity.
Build only the needed pure source subset; no project-wide assembly migration.

- [ ] Red integration tests against the executable host: round-trip request ID; unsupported version/operation returns invalid; catalogue missing returns unavailable; registry reads no arbitrary request path; no Unity DLL runtime dependency.
- [ ] Red draft tests: EN/JP and optional metadata preserved on read/write; semantic-invalid draft retains diagnosable data; invalid JSON rejected without overwrite; same-hash CAS succeeds, stale hash conflicts; traversal/root escape and symlink/junction escape reject; failed save preserves old bytes.
- [ ] Run `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_commands.py'`; confirm meaningful failures before implementing.
- [ ] Implement bounded DTO/JSON adapter and draft store, using standard .NET serialization facilities with lossless draft preservation. Shared validators determine semantic validity; do not port their rules.
- [ ] Implement content.validate for Topic/Scenario via actual validators. Preview capability/expression/condition allowlists come from trusted explicit synthetic fixture, never the authored document being tested. Reuse actual Topic selector/history and ScenarioSession/presentation; no copied switch/state machine.
- [ ] Before F1/F2 are ready, implement the transport/files/shell against test fixtures only and return furniture unavailable in production requests. Once F1/F2 exist, reference the exact source/APIs; verify API/schema fence rather than adding a permanent fake backend.
- [ ] Green commands/draft tests and runtime dependency check. The built Windows host must launch without Editor or Unity DLLs; package Python/host prerequisites clearly, no self-contained installer claim.

## W2 — Loopback UI service and agent entry point

**Files:** `server.py`, `tests/test_server.py`, `launch.ps1`, `README.md`.

**Interfaces:** HTTP `GET /api/catalogue` calls catalogue.read;
`POST /api/command` accepts exactly the command envelope. Both delegate to the
same executable host used by `workbench.ps1`; server adds no eligibility logic.
Static routes allow only named `web/` assets. Bind127.0.0.1 on an ephemeral port;
emit complete local URL with random session auth. Child process argv is fixed,
shell=false, request via stdin, bounded time/size. No provider imports/credentials.

- [ ] Red tests: missing/incorrect auth, cross-origin Host/Origin, CSRF write, invalid content type, oversized body and unknown static path reject; authored script text stays inert; stopped/missing child returns unavailable without an empty-success body.
- [ ] Run `python -m unittest discover -s Tools/ContentWorkbench/tests -p 'test_server.py'`.
- [ ] Implement loopback-only serving, session authorization and strict request limits from shared contract, bounded child timeouts, clean shutdown and useful diagnostics. No wildcard CORS or externally exposed listener.
- [ ] Green tests; a keyless launch/stop smoke must touch only synthetic draft storage and must not enter Unity or access provider secrets.

## W3 — Flow-first graph, bilingual inspector and catalogue browser

**Files:** `web/index.html`, `web/workbench.css`, `web/workbench.js`,
`web/graph-editor.js`, `web/draft-editor.js`, UI interaction tests.

**Interfaces:** UI uses the envelope through W2; no direct arbitrary file writes.
Graph presents executable Scenario steps (dialogue/ask/action/wait/branch/end).
Topic editor presents family anchors/menu/variants/conditions and canonical node
choices without pretending those are the same step contract. Selection inspector
edits Japanese speech/English display/expression/choices and supported fields.

- [ ] Red UI cases: create Topic/Scenario draft, select/edit EN/JP beat, add/remove/link steps and ask choices, undo/redo, find an invalid successor, preserve fields after save/reload, handle hash conflict without discarding either edit.
- [ ] Create a synthetic sit-together Scenario and Topic fixture under this task's fixtures. The sitting action is explicitly simulated, not a live supported capability.
- [ ] Implement Flow-first navigation + catalogue + writing inspector. Draft validation and error paths navigate to offending fields/nodes. Unsupported future fields are preserved/read-only or explicitly rejected, never silently dropped. Shared registry feeds furniture browser/requirements; no per-type hardcoded catalogue.
- [ ] Provide draft save/reload, dirty-state warning, local undo/redo and conflict review; no publish-to-game button pretending integration exists. Graph layout is editor metadata separate from executable content, with stable node IDs and no random renaming.
- [ ] Verify 320px/desktop layout, keyboard use and hover/focus locked-reason affordance. Use an actual available browser test tool; if no browser harness is available, document the gap and don't claim a visual pass. No dependency installation just for cosmetic polish.

## W4 — Playground, explanations and shared furniture integration

**Files:** `web/playground.js`, `tests/test_preview.py`, `tests/test_parity.py`,
`Fixtures/sit-together/` with no-table/table/wrong-facing/occupied/missing-evidence cases.

**Interfaces:** catalogue.read and arrangement.evaluate use Room F1/F2. preview.run
accepts explicit synthetic context/events; returns ordered current step/effects,
candidate/gate/priority/weight evidence, visible/locked/hidden choices and unknown
values. No numerical confidence invented when semantic inference isn't running.

- [ ] Red tests: two seats/no table keeps base available and table option locked; valid table unlocks its option; unrelated table doesn't; occupied/unknown/stale refresh cannot advance; no unavailable capability is granted from draft data.
- [ ] Red lifecycle tests: ask answers are run/step/question-bound; cancellation/dismiss/reopen resumes only unacknowledged beats; action acceptance != completion; failed simulated action selects failure; end releases synthetic claims once; reset never touches a real save.
- [ ] Integrate true Room APIs and current Topic/Scenario runtimes. Use explicit dry-run allowlists and labelled simulated effects. Don't rely on ScenarioPlaygroundSession.CreateSyntheticContext for validating authority, because it derives capability IDs from draft content.
- [ ] Run the same fixture through CLI and HTTP/UI command path; assert identical IDs, states, reasons, branch results and deterministic traces. Repeat with catalogue version changed; preview stays pinned or explicitly resets, never silently swaps state.
- [ ] Provide explicit Reload catalogue. A newly registered valid definition appears without UI code changes; invalid definitions remain diagnosable and unexecutable. No watcher/live Unity reload or automatic publication.
- [ ] GTE/NL routing is a later opt-in adapter unless current headless inference integration works without Editor and preserves the same semantic model/anchors. First slice must have honest direct-family mode and no fake similarity or automatic rebake/model download.

## W5 — Acceptance, documentation and handoff

- [ ] Run `python -m unittest discover -s Tools/ContentWorkbench/tests`; no skips disguised as passes. Run existing Topic Runtime, Topic Playground, Scenario and conversation source suites relevant to changed adapters; report exact failures and what wasn't run.
- [ ] Prove launch without Unity running and with no Unity DLL dependencies; CLI can create/read/validate/preview a draft, with no API key. Do not stop/close a user's Editor to obtain this proof; use dependency/process evidence or another safe test environment and state its limits.
- [ ] Browser pass: catalogue -> create Topic/Scenario -> edit -> validate -> preview -> save -> reopen; locked hints on pointer/focus; conflict/error state; responsive layout. Human gameplay/real nav/audio remains outside standalone acceptance.
- [ ] README documents build prerequisites, launch/stop, API/CLI examples, draft location, schema versions, Reload, permissions, safety limits and no production publish. Add `Content Workbench Handoff.md`, dated evidence and concise TODO update.
- [ ] Return what actually works, commands/results, dependencies, integration gates and manual gaps. Do not claim mod distribution/installer/Android UI, physical sitting, live sync or M2/M3 game acceptance.

## Shared checkout and stop conditions

Room owns furniture core and Unity mutations; this session owns workbench. Do not
rename its DTOs/IDs or fix its tests unilaterally. If F1/F2 is unavailable, continue
independent shell/draft tests, record unavailable furniture integration and leave
that task incomplete. Hand off a concrete blocked gate rather than fake success.
No broad source migration, runtime schema fork, provider spend or production write.

Self-review2026-10-21: interfaces/path ownership and dependency order checked;
five review risks have named cases. Existing runtime behavior is reused, not
reimplemented. This is a bounded functional workbench slice, not a mockup upgrade
being presented as a complete game/mod publishing platform.
