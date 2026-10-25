---
title: 2026-10-21 Data Management Prerequisites
date: 2026-10-21
tags: [alteros, persistence, verification]
status: binding-installed-editor-smoke-passed-player-validation-pending
---
# Data management prerequisites

## Approved installation and fresh smoke follow-up

Human approved the specific dependency. Unity Package Manager job
`a16eadb4bbba4f47bc9f46f071f255e0` succeeded: com.gilzoide.sqlite-net1.3.2,
pinned to `08248bd5884d8eb932a837aa56d4ff456daf913f`. Manifest and lock confirm
the same hash. Package installed without samples or project registry changes.

After import, in-memory Editor probe reported compiling=false,
scriptCompilationFailed=false, loaded assembly `Gilzoide.SqliteNet`; Console
error query returned zero entries. ModelTest remained clean, outside Play Mode.

Reproducible diagnostic method body:
`Tools/Persistence/SqliteBindingSmoke.cs.txt`. Pass its complete contents to Unity
MCP execute_code in the pinned AlterOS instance; it creates its own synthetic
directory and never reads persistentDataPath or player files. This is an Editor
smoke, not NUnit/EditMode suite execution or game persistence implementation.

Seven assertions passed using native SQLite3.50.1:

1. Native database open and schema creation.
2. Parameterized Unicode/SQL-looking text round-trip (literal text, not execution).
3. Transaction rollback leaves original count unchanged.
4. Transaction commit (verified by subsequent backup/reopen row count).
5. SQLite Backup API with open WAL-mode source; copy has all three committed rows
   and passes integrity_check.
6. Close/reopen retains committed values.
7. Reopened source passes integrity_check.

Retained synthetic evidence directory:
`C:/Users/lauda/AppData/Local/Temp/AlterOS SQLite Smoke f2c9207cac2341af927c01afdbae2f1e`.
No real saves/credentials touched; no scene/runtime script or serialized asset edit.
Windows x86/x64/ARM64 and Android ARM32/ARM64/x86/x64 native libraries are present
in the installed package. Presence is not proof of player/device compatibility.

Next session may proceed with approved binding in D1 source/tests; D0 Windows
player native loading, standalone headless harness compatibility and target-device
tests remain pending. No full player build, PlayMode, migration, recovery failure,
disk-full/concurrency or paid-provider tests ran. Other existing source suites were
not rerun for this package-only spike. Earlier prerequisite sections below are
historical pre-install findings, superseded by this follow-up where applicable.

Related: [[../Tracking/Data Management Design]],
[[../Tracking/Data Management Implementation Plan]].

The human approved the plan and requested verification. Implementation remains
assigned to a future session; this check did not start migration or runtime work.

## Fresh Unity evidence

- MCP instance pinned to `alteros@48a268a4`; project root verified as
  `E:/Pleiades/alteros`, Unity 6000.4.4f1, StandaloneWindows64.
- Resource state: ModelTest, not playing, no tests/import/compile in progress.
  The resource reported stale_status, so freshness was checked by execute_code.
- In-memory read-only code returned the correct root, compiling=false,
  EditorUtility.scriptCompilationFailed=false, one clean saved ModelTest scene.
- Console query restricted to errors returned zero entries. Warning query returned
  existing CS0618 object-finding API warnings; Console was not cleared.
- Loaded assembly search found zero SQLite-named assemblies. No SQLite-related
  assets/packages were found in Assets, Packages or Library/PackageCache by filename
  search. Manifest declares no SQLite binding; AlterOS.Runtime has only inference
  references. These checks establish absence of a usable declared/loaded binding,
  not impossibility of another unrecognized binary.
- Unity installation contains Mono.Data.Sqlite tooling assemblies. These were not
  copied, loaded or considered evidence of redistributable player/native support.

## Proposed dependency, not installed or runtime-tested

Recommend `com.gilzoide.sqlite-net` 1.3.2 for the isolated D0 spike. Versioned
upstream package declares Unity 2021.2 minimum and MIT license; versioned README
declares Windows/Android support, SQLite-net 1.9.172 and bundled native SQLite3
Multiple Ciphers 2.1.3 based on SQLite 3.50.1. These are maintainer claims, not
AlterOS platform acceptance. No encryption/security guarantee follows from them.

`git ls-remote` resolved tag 1.3.2 to
`08248bd5884d8eb932a837aa56d4ff456daf913f` on this date. Prefer commit-pinned UPM
installation if approved. Retain MIT notices and inspect third-party/icon license
requirements (README also lists CC BY 4.0 icons); do not import samples by default.
Native binary inventory, backup API, security maintenance and player/IL2CPP tests
still require the installed-package spike. Reject the candidate if those fail.

Sources inspected:

- [Versioned package manifest](https://raw.githubusercontent.com/gilzoide/unity-sqlite-net/1.3.2/package.json)
- [Versioned README](https://raw.githubusercontent.com/gilzoide/unity-sqlite-net/1.3.2/README.md)
- [Versioned license](https://raw.githubusercontent.com/gilzoide/unity-sqlite-net/1.3.2/LICENSE.txt)

## Limits and next gate

No packages/scripts/scenes/settings changed, no build/EditMode/PlayMode/database
test run, no player files or credentials read. Existing compile baseline was queried,
not a fresh recompile. Next: approve this concrete dependency, then install through
Unity Package Manager and perform synthetic open/parameter/rollback/reopen/backup
tests, Windows player native loading and focused compilation. Android remains
unverified until a target build/device check. Never migrate real saves for a spike.
