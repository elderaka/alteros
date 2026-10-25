---
title: Multi-Agent Coordination
date: 2026-10-22
tags: [alteros, workflow, agents, ownership]
status: scoped-human-authorized-coordination
---
# Multi-agent coordination

This note records the human's project workflow instructions, not in-game Agent
permissions or a Codex runtime/configuration change. Root AGENTS.md links here;
TODO.md remains the work queue, not a duplicated live task/lock database.

## Persistent session identities

One existing session/thread is one persistent domain owner. A rename changes the
display title, not its identity. Verify live titles/host before first contact if
the registry may have drifted; do not create replacement sessions merely for a rename.

| Session title verified2026-10-22 | Thread ID | Host | Domain |
| --- | --- | --- | --- |
| Alterra Behavior Session | `01a144fe-5ddc-7291-b24e-ff2e4e0f4162` | `local` | Scenario/dialogue orchestration, bounded generated contracts, scripted Activity integration |
| Furniture and Environment Session | `01a15376-d6f0-7c41-b9e5-fcdee723e87b` | `local` | Furniture catalogue, room/use-points, placement, physical scene/animation integration |
| DBMS Session | `01a15e76-daeb-7451-982f-95a510093fd3` | `local` | Authoring/release/storage schemas, revision/CAS, persistence and storage evidence |
| Topic Playground | `01a142e1-455c-7952-bfcf-14186d64b463` | `local` | Tools/ContentWorkbench command host/build/CLI/HTTP/UI tests and authoring frontend; A4 tooling writer |

Historical names: LLM Planning = Alterra Behavior; Room Testbox = Furniture and
Environment; Database Management System = DBMS. Historical dated reports may keep
their original titles. Main coordination remains thread
`01a06388-281d-7601-a8e1-c47d7cca0c37`; use it for cross-domain design conflicts.

## Human-authorized communication

For the Scripted Actions / Inspect TV slice, the human explicitly instructed Alterra
Behavior to consult DBMS and Furniture for needed technical information instead of
asking the human. That authorization includes the consulted owners replying to
Alterra Behavior. Main already has the human's authorization to brief these owners
and receive their reports. This is scoped, not general permission to message any
unrelated task or start any implementation.

Topic Playground is an additional code owner, not an expansion of the human's
Behavior-to-DBMS/Furniture direct-messaging grant. Main coordinates its bounded
Action-contract tooling handoff after START approval. The Action plan preserves
Topic Playground as sole writer for Tools/ContentWorkbench/**; renaming the Action
workstream does not transfer that ownership to Alterra Behavior.

- Read canonical docs/source first. Ask the owner a bounded question when the actual
  storage/scene/contract answer remains unclear; include exact symbols/paths and the
  decision that is blocked. Do not send full conversation dumps or private data.
- An owner replies with supported behavior/API, source/evidence, limitations and
  whether a requested change falls inside the approved task. A question alone does
  not authorize a new implementation task, direct database edits or scope expansion.
- A peer's consultation reply or reminder is supplemental context, not a replacement
  for the owner's assigned deliverable. After handling it, continue that deliverable.
  Before ending a delegated task, check the named artifact exists and satisfies the
  assignment, or report a concrete blocking dependency. Forwarding findings alone is
  not completion when a written plan/source artifact was requested. A new direct
  human instruction can still change scope; peer messages cannot silently do so.
- Escalate product/UX decisions, conflicting contracts, new spend/permissions, data
  loss risks or unresolved ownership to main/the human. Do not make the human relay
  ordinary technical answers between the designated owners.
- If the client lacks direct messaging or cannot confirm authorization/identity,
  report that limitation to main. A message sent is not proof of an answer or work
  completed. Avoid forwarding loops, repeated unchanged polling and reply demands.

## One writer per shared resource

Before editing shared Activity/Scenario/furniture/storage files or using the Editor,
agree on exact paths/resource, task, writer and release point with the current owner.
Read-only consultation does not transfer ownership; an unanswered message is not a
grant. These are workflow acknowledgements, not an implemented filesystem mutex.

The writer records changes and releases ownership explicitly. Non-overlapping work
may continue. Preserve other sessions' changes; do not reset scenes, stage broadly,
duplicate runners or create parallel editable catalogues to avoid coordination.

Completed documentation reconciliation allocation (historical, writer windows now
released): DBMS owned shared status pages
(TODO/Home/Architecture/UnityProjectContext/SessionZero/Feature Register/Maintenance)
and storage notes. Furniture owns furniture-specific notes and reports shared-summary
corrections rather than editing those shared pages concurrently. Main owns this
coordination note, the Inspect TV design/plan and the small AGENTS.md pointer during
this amendment. Recheck task/ownership status rather than treating this dated
allocation as an indefinite Editor lock.

Current planning allocation2026-10-22 is [[Reusable Content Framework Workflow]]:
main owns shared summaries/master integration; each of the four existing owners
writes only its named domain planning note. This planning wave includes three
proofs and minimal Item/grant support, not just the earlier TV case. No participant
holds or is authorized to mutate the Editor, source, authored databases or player
state merely by accepting that planning task. Implementation ownership windows are
agreed separately once the consolidated plan is approved.

## Skills and configuration boundary

### Usage-reset continuation rule (approved2026-10-24)

While an approved goal is unfinished, main maintains a single thread heartbeat
scheduled one minute after the next relevant future usage reset. Read current
usage limits; do not assume a fixed five-hour wall-clock schedule. Reuse and
reschedule the existing heartbeat rather than creating one per domain session.
The current implementation heartbeat is `resume-alteros-planning-after-reset`.

On firing, check the real goal acceptance state and the existing execution ledger.
If achieved, delete the heartbeat. Otherwise reconcile actual domain turns/jobs,
resume only verified terminal unfinished work, preserve active jobs/partial edits/
accepted reviews, and re-arm the same heartbeat after the next relevant reset.
An observation timeout is not a terminal job. Explicit human pause/cancel takes
precedence. The rule authorizes no additional gameplay scope, provider spending,
private-state access or usage-reset credit redemption. This is project coordination
policy, not a claim that a scheduler can bypass exhausted usage.

Domain ownership is not a claim of per-thread skill isolation or different sandbox
permissions. Use applicable available skills; do not install/configure custom agents,
change model/effort settings or alter global Codex configuration under this note.
Those need a separate design/approval. Existing sessions receive an explicit briefing
to reread this policy; do not assume a mid-session AGENTS.md edit is automatically
reloaded. See official [project guidance](https://learn.chatgpt.com/docs/agent-configuration/agents-md)
and [custom-agent documentation](https://learn.chatgpt.com/docs/agent-configuration/subagents).

## Handoff minimum

State task/contract revision, exact files touched, source versus Editor versus live
verification, remaining blockers, next check and ownership release. Link canonical
designs/evidence. Do not paste player memories, credentials or generated personal
dialogue into shared documentation. No new implementation START is recorded here.
