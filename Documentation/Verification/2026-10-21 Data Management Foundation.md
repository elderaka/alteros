---
title: Data Management Foundation Verification
date: 2026-10-21
tags: [alteros, persistence, verification]
status: source-and-headless-verified-editor-pending
---
# Data Management Foundation Verification

Related: [[../Tracking/Data Management Design]],
[[../Tracking/Data Management Implementation Plan]],
[[../Tracking/Data Management Handoff]],
[[2026-10-21 Data Management Prerequisites]].

## Scope

This evidence covers the approved D0-D6 desktop game-data foundation in
`E:/Pleiades/alteros`. Automated databases and progression inputs were synthetic
and created beneath unique system temporary directories. No real player database,
memory database, transcript, credential, provider, or account was read or changed.
The Room Testbox scene and prefabs were not edited.

The game database path is
`<Application.persistentDataPath>/Data/alteros-game.sqlite3`. SQLite-net 1.3.2 is
pinned in the UPM manifest/lock to commit
`08248bd5884d8eb932a837aa56d4ff456daf913f` (MIT). Personal memory, conversation
transcript, Topic history, generated archives, and credentials retain their prior
owners.

## Checks run on 2026-10-21

| Check | Result | Evidence boundary |
| --- | --- | --- |
| Prior Editor package smoke in [[2026-10-21 Data Management Prerequisites]] | 7/7 assertions passed | Supplied prerequisite record; not this implementation's tests |
| Fresh Windows x64 native probe | 6/6 passed | Synthetic DB: native load, create/open, parameterized Unicode value, rollback, backup, close/reopen and version lookup; SQLite 3.50.1 |
| `Tools/Persistence/Check-GamePersistence.ps1` | 7/7 groups passed | Unity 6000.4.4f1 bundled Mono compiler, real SQLite-net managed binding and packaged Windows x64 native plugin; test execution in an isolated PowerShell process |
| `Tools/Persistence/Check-ProgressionSourceCompile.ps1` | Compile succeeded: 0 errors, 52 warnings; static schema/effect-boundary probes passed | Bundled compiler over persistence, furniture, progression and dialogue integration sources against cached Unity assemblies. Synthetic probes accepted a complete schema-4 snapshot and a small authored delta; rejected an incomplete snapshot, malformed schema header, dialogue credits, oversized delta, and reward effect; retained missing schema version as legacy schema 0. A source-policy probe confirms only catalogue-selected, canonically validated file-backed routes reach the authored-effects path; public/provider JSON, generic legacy paths, local choices, and test starts remain untrusted. Duplicate-type warnings come from cached Unity references, with existing obsolete API and unused-field warnings. Not Unity project compilation. |
| Authored dialogue JSON compatibility scan | 439 JSON files; 240 stat/effect records; maximum 10 combined changes at one node; 0 policy violations | Static parsing of `Assets/StreamingAssets/Dialogue`; confirms current authored examples fit the new limits, not runtime playback proof |
| Unity MCP read-only Editor probe after edits | Timed out | No Editor action was forced; the active Unity process was left running |
| `DialogueRouteRecoveryTests.cs` standalone source compile | Succeeded with 0 errors | Unity 6000.4.4f1 bundled compiler checked the updated regression source against cached Unity/NUnit assemblies. This does not compile the Unity test asmdef or run the tests. |
| Unity EditMode, Play Mode, Windows player, Android | Not run / unverified | No acceptance claim |

The seven headless groups cover repository identity/snapshots/idempotency/stale
revisions; transaction rollback; future/corrupt database preservation; Unicode
one-time import and source backup; dirty checkpoint retry; home seed/revision/pose/
orphan retention; and consistent backup, staging, explicit approval, and recovery.

New Unity Editor regressions cover incomplete schema-4 snapshot preservation,
malformed schema headers, bounded dialogue effects, rejection of provider
sequence effects, untrusted public/dynamic/generic/local starts, and the validated
authored-route permission path. Those tests have not run because Unity's MCP bridge
timed out; the seven headless groups do not cover these Unity integration cases.

## Findings resolved during implementation

- The package's high-level SQLite backup helper opens its destination through an
  ANSI path overload. `SqliteGameRepository` now uses the existing UTF-8 SQLite
  open/backup APIs so backup destinations support Unicode paths. The synthetic
  migration test uses a Japanese directory and spaces in its legacy JSON/database
  filenames.
- A failed SQLite connection setup could leak its handle before a `using` scope
  received it. `OpenConnection` now closes the partially configured handle before
  rethrowing, preserving corrupt database bytes for recovery/export.
- Current schema-4 progression is structurally validated before normalization.
  Malformed headers and incomplete snapshots are left byte-for-byte unchanged and
  require recovery; older schema versions retain their normalization path.
- Dialogue effects now require the validated file-backed catalogue route and a
  host allowlist. Operations are limited to 12 combined changes, add-only known
  stat IDs, and at most 5 units per stat delta. Dialogue cannot change credits or
  call reward/memory effects. The system stat test route now uses bounded player
  rest changes instead of credit minting.
- A follow-up review found that live AI JSON and caller-supplied sequences could
  reach the authored-effects path. Only `TryPlayAuthoredRoute` now enables it, after
  catalogue selection and canonical validation. `PlayFromJson`, public
  `PlayCinematic`, generic file playback, local sequence choices, and test JSON use
  the untrusted start path. The source-policy probe passed; new Editor regressions
  are awaiting Unity import and execution.

## Unity Test Runner limitation

Earlier Unity Test Runner requests timed out after the bridge reported
`C:/Users/lauda/AppData/LocalLow/DefaultCompany/alteros/TestResults.xml` as its
result path. That file was not opened or removed. The bridge also timed out on a
fresh read-only Editor probe after source changes. No Unity project compile, EditMode
result, scene action, or Play Mode behavior is asserted here.

## Next acceptance

After source review, reconnect Unity 6000.4.4f1 in the correct project and allow
normal import. Inspect Console/compile status, then run the focused Editor
persistence/store/effect regressions using synthetic roots. Next run a Windows
standalone player against a synthetic profile, followed by normal
`SplashScene -> ModelTest` persistence acceptance without provider use. Android and
full personal-memory recovery are separate work.
