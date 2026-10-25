---
title: 2026-10-22 Documentation Reconciliation
date: 2026-10-22
tags: [alteros, documentation, verification, handoff]
status: source-doc-review-runtime-acceptance-pending
---
# Documentation reconciliation and LLM/database compatibility

## Scope and evidence

User requested stale-document cleanup, then an assessment of whether LLM Planning
needs changes after the database work. This review used current source, project
notes and read-only reports from Database Management System and LLM Planning.
It changed documentation/instructions only: no product code, Unity asset, personal
profile, credential, database migration, model bake or provider request.

Historical dated verification and route snapshots are retained; this is not a new
test pass or fresh catalogue export. Current assignments/next gates remain in
root TODO and the linked plans, not another independent work queue here.

## Reconciled stale claims

| Earlier claim | Current supported statement | Evidence / owner |
| --- | --- | --- |
| Furniture/use points are only proposed; replacement room not assigned | Portable F1–F4 source and room/dispenser wiring exist; inventory/placement I1–I6 is separately approved/dispatched | [[../Tracking/Furniture Foundation Handoff]], [[../Tracking/M3 Furniture and Activities Handoff]], [[../Tracking/Furniture Inventory and Placement Implementation Plan]] |
| M2/Agent conversation and mode-selection UI still need implementing | M2/M2.1 and negotiated v4 source integration exist; no player-facing Topic/Agent switch is the target | [[../Tracking/Unified Conversation M2 Handoff]], [[../Tracking/Scenario Conversation Handoff]] |
| Transcript is live JSONL; legacy JSON folders remain production runtime authority | Transcript uses private SQL facade; content/semantic loaders use a pinned SQL release. JSON/JSONL remain retained import/export evidence | `ConversationTranscript.cs`, `DialogueRouter.cs`, [[../Tracking/Full SQLite Cutover Implementation Plan]] |
| All database work is finished | Runtime source cutover has acceptance gates; authoring A0–A6 is a separately active task, not completed draft command migration | [[../Tracking/SQLite Authoring Cutover Implementation Plan]], [[../Tracking/Content Data Integration Handoff]] |
| Shared SQL release is ready to play because it was staged | Shipped `Assets/StreamingAssets/Content/content.sqlite3` is absent; expression validation blocks installation | Read-only Test-Path false; dated cutover handoff records 104 beats / 12 unsupported IDs |
| BYOK settings UI is absent | P-key Canvas panel and shared DPAPI/env store exist; visual, secure-storage and live checks remain separate | [[2026-10-16 BYOK Setup]], `OpenRouterSettingsPanel.cs` |
| All actions are absent, or drawing is available because it was requested | M3 exposes only actual bounded host capabilities; no drawing executor is installed | `DirectConversationBridge.CaptureActivityCapabilities`, `ActivityInteractionBridge`, M3 handoff |
| E is the future inventory rotation key, or T remapping is already proved | Current input asset still binds E interaction; approved T interaction/E inventory and held-item mapping are assigned, not yet accepted | `Assets/InputSystem_Actions.inputactions`, furniture inventory design/plan |
| All Topic Playground compilation remains blocked | Fingerprint worker source 15/15, Unity compile zero errors and focused EditMode 1/1 were reported; actual GTE/profiling/parity remains unverified | [[2026-10-22 Topic Playground Responsiveness]] |
| External SOUL/IDENTITY edits automatically change live prompts | Runtime loads packaged persona.json through prompts.py; external prompt-maintenance references are historical/separate | `Tools/CompanionRuntime/src/alteros_companion/prompts.py` |
| Old no-force-close instructions are still universal | Current AGENTS allows recovery of a verified stalled exact-project Editor, with evidence/dirty-state checks; stale MCP alone is not a hang | Root AGENTS.md and later explicit human approval |

Updated entry points/current references: AGENTS, TODO, Home, Architecture,
UnityProjectContext, SessionZero, Feature Register, Maintenance, Dialogue Catalogue,
State and Effects, Agent Tools, Unified Conversation M2 Handoff, Scenario
Conversation Handoff, Player Memory Handoff, original M2 plan precedence note,
Live Conversation Journal and Character Grounding/Generation Contract.

No route-note hashes/counts were regenerated. Maintenance now distinguishes the
dated Generic snapshot from future shared SQL exports; neither is a private-user
catalogue or a second editable authority.

## Does LLM Planning need an update?

Yes: it needs the new storage/integration baseline, not an automatic prompt or
LangGraph rewrite. The session `01a144fe-5ddc-7291-b24e-ff2e4e0f4162` was idle at
read time. Its latest completed report covers the earlier RAG candidate filter
and rebuilt v4 helper, with 194 C# / 173 Python checks, and explicitly leaves v4
helper/provider failure returning unavailable. That report predates the later
database cutover. Those are session-reported dated checks, not rerun here.

| Boundary | What LLM follow-up must preserve/check |
| --- | --- |
| Transcript | Continue using `ConversationTranscript.Append/ReadPage/ReadRecent`. Paths passed to its constructor are SQL database paths now. Never reintroduce JSONL appends or another writer. |
| Memory | Preserve imported sequence/cursor and event/source identity, consent revoke/forget suppression, retained chat versus memory distinctions. `PlayerMemoryTranscriptSync.BuildPage` maps only player request events as bounded unclassified evidence. Python memory remains a separate database/owner. |
| Retrieval/content | Consume pinned validated release candidates/revisions/coverage. Do not scan authoring drafts or grant gameplay from an unvalidated generated row. Missing release/readiness stays explicit; no hidden legacy-file fallback. |
| Startup/private stores | Respect migration readiness before session/archive usage. Missing committed targets cannot be treated as empty defaults or cause reimport of retained stale sources. |
| Activities | Continue passing capability requests through the existing host, exact objects/revisions and permissions; inventory/catalogue presence alone never installs an executor. |
| Provider contracts | Storage changes alone do not require a new v2/v3/v4 wire schema, weaker bilingual VN validation, extra paid calls or rebuilding an unchanged Python helper. Real regressions may justify a bounded change after tests. |

A suitable future LLM session task is a synthetic compatibility/regression pass
after database closeout: rerun relevant conversation/memory/Scenario tests; verify
storage-unavailable, restart, duplicate/presentation checkpoint and consent/forget
races; fix only demonstrated consumer regressions. Keep the pre-existing v4
provider-failure authored/active-topic fallback issue a separate explicit scope,
preserving host eligibility and active-response identity if it is approved.

This review refreshed the handoff but did not send a new implementation START,
interrupt LLM Planning or authorize it to edit the active workers' stores.

## Active ownership and coordination limits

- Database Management System `01a15e76-daeb-7451-982f-95a510093fd3` is implementing
  authoring A0–A6. Its read-only report says the standalone repository group passed
  11/11 and importer work is underway; those are not full-plan completion. It
  also reported the earlier Workbench baseline at 15 failures / 17 tests due to
  standalone reference resolution and parked Playground-owned changes pending
  file handoff. This review neither reran that suite nor granted file ownership.
- Room Testbox `01a15376-d6f0-7c41-b9e5-fcdee723e87b` is implementing inventory/
  placement I1–I6. Its refined request needs merge ordering for
  `FurniturePersistenceAdapter.cs`, `Tools/Persistence/Check-GamePersistence.ps1`,
  `Tools/Persistence/HeadlessPersistenceTests.cs` and the existing/new persistence
  tests. Furniture DTO/validator/layout commands remain Room-owned, but the
  active persistence adapter consumes them. Its source inspection found no
  justified change to GameSchemaMigrations/GameHistoryContracts/SqliteGameRepository
  merely to add stock+layout+causal events to the existing transaction.
- Exclusive Editor access is still a separate coordination gate for scene/input/
  Canvas work. No ownership transfer, Editor lock or source merge was performed
  by this documentation task. Continue uncontested source work without broadening.

## Validation performed and not performed

Performed: targeted stale-claim searches; direct source inspection of SQL content
loading, transcript facade/import paths, source-sync mapping, startup migration
configuration, actual input binding and packaged persona loading; read-only
session reports; documentation link/format checks recorded by the coordinator.

Static validation: 16 affected Markdown notes checked, zero unresolved wikilinks
and balanced fenced blocks in that set. Dotted milestone names were resolved as
Markdown notes rather than mistaken for file extensions; a broken multiline
Content Data Integration Handoff link was repaired. This does not check rendered
Obsidian layout or every historical file in the vault. A final read-only LLM
Planning check after the edits still showed the same idle session/latest report.

Not performed: C#/Python suites, native SQLite execution, current Unity import/
Console/test jobs, Play Mode, actual keyboard/UI review, GTE load/inference,
provider/TTS calls, real profile inspection/migration, Obsidian rendered view or
full-vault archival purge. Existing dated evidence is not promoted to current
runtime acceptance. Stale historical material is labelled/linked, not deleted.
