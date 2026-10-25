---
title: Agent Tools
tags: [alteros, catalogue, agent]
status: bounded-activity-port-implemented-general-tools-proposed
reviewed: 2026-10-22
---
# Agent Tools

Related: [[Feature Register]], [[State and Effects]], [[Runtime Observability]], [[Design Decisions]].

> [!warning]
> The general-purpose tools below remain candidates, not executable API names.
> A narrower Scenario `request_activity` port now exists through the M3
> ActivityInteractionBridge/ActivityHost. It accepts only actual host capabilities
> and bound objects; it is not arbitrary code, database access or a universal
> tool dispatcher. Source/Editor evidence is not live activity acceptance.

SQLite owns persistence behind host facades, not model authority. Never expose
raw SQL, private DB paths or unrestricted queries as a consequence of migration.
See [[M3 Furniture and Activities Handoff]], [[Content Data Integration Handoff]]
and [[Unified Conversation M2 Handoff]] for current ownership.

## Initial capability register

| Catalogue ID | Capability | Permission / prerequisites | Authoritative outcome | Status |
| --- | --- | --- | --- | --- |
| CAP-01 | Read permitted game context | Minimal non-secret snapshot; current session | No state mutation | Proposed |
| CAP-02 | Start or manage a study timer | Explicit request or enabled routine; valid duration; game validates conflicts | Game activity/timer lifecycle | Proposed; timer widget alone is not a tool |
| CAP-03 | Create or update an in-game plan/task | Player intent; bounded validated fields | Task store operation | Proposed; existing task APIs are not an Agent dispatcher |
| CAP-04 | Offer a shared break or drink | Initiative ceiling, attention gate, cooldown; acceptance before shared activity | Offer/activity state, not arbitrary rewards | Proposed |
| CAP-05 | Record activity completion/check-in | Valid activity and evidence; duplicate guard | Fixed game-owned effects once | Proposed; player claim and Alterra action remain separate |

Outside apps, filesystem access, arbitrary code execution, arbitrary stat deltas, currency minting, and silent paid recursive work are outside the first Agent scope.

## Required record before registering a tool

Document the stable tool ID, actual handler/source directory, input schema and limits, allowed caller/mode, read/write scope, consent requirement, preconditions, cancellation/deadline, retry policy, idempotency key, validated outputs/errors, fixed effect owner, audit fields, and acceptance tests.

A capability ID here is not a final tool name. Choose concrete names and schemas during implementation, then link the handler and tests. Documentation never becomes an allowlist automatically.

## Tool-call timeline (planned)

Record request/session/activity ID, tool ID, validation decision/reason, consent state, start/end, bounded result/error, cancellation/staleness decision, and whether an authoritative effect was applied. Do not log keys, complete provider payloads, or private player text by default.

A model saying “I started your timer” is not evidence. The tool result and game activity event establish whether it actually happened.
