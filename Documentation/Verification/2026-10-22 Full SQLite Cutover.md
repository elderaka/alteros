---
title: Full SQLite Cutover Verification
date: 2026-10-22
tags: [alteros, sqlite, migration, verification]
status: source-unity-compile-verified-editor-acceptance-open
---
# Full SQLite Cutover Verification

## Result

Source and synthetic headless migration work for F1-F6 is implemented. Unity source
import and script compilation passed on 2026-10-22. After expression normalization,
the user reports **Build Runtime SQL Content** works. A later read-only inspection
verified the installed artifact and its exact match to authoring revision 63. Focused
Editor tests, `SplashScene -> ModelTest` player acceptance, Android behavior and
real-profile migration remain open. No scene save, provider call or real user data
access was performed. The later user-authorized Editor force-close/reopen is recorded
below; it is not acceptance evidence.

## Installed artifact read-only check (2026-10-22)

The current runtime asset is
`Assets/StreamingAssets/Content/content.sqlite3` (15,822,848 bytes; SHA-256
`67033644122DBB06884B5479A3BBC0469B23C497FEC54D39B559ED83B416A31F`).
`Tools/ContentData/Check-ContentData.ps1 -Group InspectArtifact -ArtifactPath ...`
completed successfully using read-only repository/SQLite connections. It verified
`integrity=ok`, zero foreign-key violations and that the trusted release opens.

| Field | Inspected value |
| --- | --- |
| Content store | `fa023336-caee-4fa5-b611-442c9268a639` |
| Release | `release_ff01231bf7670a8c4add9f4aafdea80bd7ffdcb10ddd4947` |
| Authoring store revision | 63; 439 selected source documents |
| Current authoring snapshot hash | `02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27` |
| Build-manifest snapshot hash | `02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27` |
| Source manifest hash | `a1fe8d554d1a8b84e0b02d89444fb5bec90b86b1abeb86a19d580b89b91b5f31` |
| Build manifest hash | `a3dc220276a82d5efc6048bece52464005eb7708e698f4519a1238ecd2ff3755` |
| Coverage | 439 sources, 57 Topics, 322 Flows, 928 Dialogues, 1,258 Texts, 6,041 dependencies, 673 embeddings/anchors; 57 routes, 768 dimensions |

An independent Python stdlib SQLite connection opened the file with `mode=ro` and
`query_only=ON`; it confirmed compiled-content `user_version=4`, `integrity_check=ok`
and zero foreign-key violations. The authoring store also passed its read-only
integrity check. This proves the checked file is a valid current release, not that
Unity starts through `SplashScene -> ModelTest`, consumes it at runtime, plays content
correctly, or migrates a player profile. Those checks remain open.

## Unity source and assembly compile checkpoint

Unity `6000.4.4f1` instance `alteros@48a268a4` refreshed the changed scripts and
compiled `AlterOS.Runtime` and `AlterOS.Editor.Tests`. The latest error-filtered
`read_console` query returned zero entries; both compiled assembly outputs were
updated after the refresh. The current Console query also reports 26 warnings in
scene, UI, dialogue and progression scripts. The editor-state resource later returned
`stale_status`, so this evidence is bounded to the successful refresh, rebuilt
assemblies and direct Console result. That source-compile pass started no Unity test
job or Play Mode.

The compile fixes add the installed `Gilzoide.SqliteNet` assembly reference, move the
DTO-only `ConversationModels.cs` into `AlterOS.Runtime` without changing its namespace,
fields, enum values or GUID, and replace unavailable `System.Json` usage with the
already-installed Newtonsoft package. `ContentJson` preserves the observed legacy
parser behavior: ISO timestamps remain strings, duplicate object keys keep the last
value, comments and trailing content reject, and strings retain JSON escaping. The
Editor build processor now reads expression preset names through `SerializedObject`
instead of referencing a scene-side type. A cross-assembly consumer reads
`TopicHistory.StorageRevision` through a public getter while SQL remains its writer.

The focused headless JSON compatibility regression passes 1/1. A probe against
Unity's bundled `System.Json` confirmed last-duplicate-wins and comment rejection.

The coordinator's Unity EditMode job `711636fd43d046a98d1c56ae1cfab778` reported
28/28 tests with seven failures in `ProgressionStorePersistenceTests`. The fixture's
inactive GameObject did not run `Awake` in EditMode, leaving the store uninitialized;
the test now calls its public `Load()` explicitly and assigns its existing store to
`DialogueEffectApplier`. This test-only correction has not been rerun. The failed job
evidence remains intact.

The later combined job `37e843576850489aa8b2fd6d0caeec12` stalled in the Editor and
has no result. Both test-job polling and the tests resource then failed with
`WinError 10053`; the Editor window reported busy. No Unity MCP test-cancel action is
available, so the Editor was not killed. The old test signaled the deferred fake
provider's release file before blocking on the final coordinator task. Both tests are
now `async Task`, use bounded asynchronous file/task waits, and contain no main-thread
spin or synchronous task wait. Their full BCL/fake-host class passes
`Tools/Conversation/Check-UnifiedConversation.ps1 -Filter DirectConversationCoordinatorTests`:
**30 passed, 0 failed**. This does not replace a Unity rerun. The user's later build
invocation confirms the Editor ran the build processor, but the corrected focused
EditMode job remains unverified. The failed/stalled job records remain preserved.

The initial staged catalogue build/import attempt stopped at
`expression_not_registered`. The 12 registered presets are `angry`, `focused`,
`gentle`, `happy`, `neutral`, `playful`, `sad`, `shy`, `soft`, `surprised`,
`thinking`, and `warm`. Across the imported source set, 104 beats use these 12
unregistered IDs: `calm`, `curious`, `excited`, `flustered`, `guarded`, `hurt`,
`proud`, `soft_guarded`, `steady`, `teasing`, `withdrawn`, and `worried`. Examples
include `affection_gesture_002_01/flustered`, `ask_activity_003_02/curious`,
`ask_availability_002_01/teasing`, and `ask_status_002_01/calm`.
`ContentBuildProcessor` correctly failed closed. This was the state before the SQLite
expression correction recorded below.

## Expression correction after the initial Editor failure

On 2026-10-22, the selected SQLite authoring documents were standardized against the
12 actual `AlteraExpressionPreset.expressionName` values. The 104 compiled fields
across 35 Generic and 26 Topic documents now use registered names. The current
revision is 63, exported at
`Content/AuthoringExports/02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27/`.
Headless validation checked 928 selected compiled expression fields with zero
unsupported values, and the full Bake group passed 11/11.

The mappings were `calm -> neutral`, `curious -> thinking`, `excited -> happy`,
`flustered -> shy`, `guarded -> focused`, `hurt -> sad`, `proud -> warm`,
`soft_guarded -> soft`, `steady -> focused`, `teasing -> playful`,
`withdrawn -> sad`, and `worried -> gentle`. Text, routes, IDs, semantic anchors,
vectors, tokenizer and retained legacy JSON were unchanged. The GTE metadata
`contentHash` was updated to
`fb2bd80b47d847c73b2884531f336af0a97f373f2000ea0a9c22ec17982a8888` to match the
current SQL Generic bytes; no embeddings were regenerated. After this correction the
user reports the Editor build works; no post-fix artifact path or hash was supplied
for independent inspection.

## User-authorized Editor recovery

On 2026-10-22 the user explicitly authorized force-closing a stalled Editor.
The coordinator verified that main Unity PID `30136` belonged to
`E:/Pleiades/alteros`, ran Unity `6000.4.4f1`, was not an asset-import worker, and
still displayed `Hold on` after more than eight minutes. Only that main process
was force-closed. AlterOS was relaunched as PID `45128` without requesting Play
Mode or a test run. Other Unity instances/workers were not blanket-terminated;
no lock files or project data were deleted.

The last successful Editor probe before the test run showed `ModelTest` clean,
not playing, not compiling, and without script compilation errors. A fresh
unsaved-state query was unavailable during the stall, so recovery cannot promise
that no other transient Editor state was lost. Source patches were already saved.
The previous stalled test job remains unverified; reopening alone does not prove
the async test fix works in Unity.

A separate current-source responsiveness risk was also rechecked:
`TopicPlaygroundWindow.OnGUI` reads `TopicPlaygroundSession.ModelFingerprint`,
whose getter synchronously hashes the entire `611,121,320`-byte ONNX model on
every access. This is a potential repaint hitch, not evidence that it caused the
deferred-test deadlock. No Playground performance fix or timing reproduction was
performed in this recovery step.

## Staged authored release

Artifact: `.superpowers/sdd/Full SQLite Cutover Implementation Plan/content-full-cutover-2026-10-22.sqlite3`

- Size: 15,888,384 bytes
- SHA-256: `2C292F81B643A9C15DC217AC40FD866A18FE27DB75D6FF2AB823D2D9642F5C5E`
- Store ID: `b27f4850-ecdf-47d8-8ad0-2beaa5fc152b`
- Release ID: `release_f7ac8a5294e73f4cc1ce7555175c9cdaee25a5d494c1f45d`
- Source manifest hash: `3f9655c4730516b0ac78db268812f7fc23bbdc2cad47161504bcf9bf36f32b78`
- Build manifest hash: `c47602a2a4e3b7853d3d2f7a2ebb3144cf98fb5152856e7b4227bb9479a95b8c`
- Index fingerprint: `dbb2127bb33a820f32dbf6ef14948a56de4bb887a5af7dcf96319bd795066414`
- Coverage: 439 source files, 57 Topics, 322 Flows, 928 Dialogues, 1,258 Texts,
  6,041 exact release dependencies, 673 embeddings, 57 routes, 768 dimensions

`InspectArtifact` reopened the pinned release read-only, loaded the semantic
snapshot, verified SQLite integrity and found zero foreign-key violations. The
artifact was in staging at this checkpoint, before expression normalization.
The user later reports the Editor build/import works against the corrected SQLite
selection; its installed path/hash was not supplied for independent inspection.
Normal source import generated `ContentJson.cs.meta`; moving
`ConversationModels.cs` retained its original GUID. No `.meta` was hand-authored.

## Storage ownership and migration

- Game progression, home layouts and Topic history share
  `<persistentDataPath>/Data/alteros-game.sqlite3` (game schema 3).
- An earlier root-level `alteros-game.sqlite3` created by the Topic cutover is
  read-only migration input. The importer preserves its Topic source receipt,
  operation rows and exact backup bytes in the canonical database. It promotes a
  later legacy revision over a canonical database that contains only the same
  imported JSON seed. Divergent histories or non-Topic game data fail closed.
  The source database is retained. Relocation requires the exact schema-v3 user
  table set and rejects unrelated source-backup rows before writing the target.
- Sessions, repetition, ambient state and transcript use the separate private
  `<persistentDataPath>/Data/conversations.sqlite3` database (schema 2).
- Generated archive records, aliases, payload bytes/hashes, source chunks and
  diagnostics use `<persistentDataPath>/Data/personal-content.sqlite3` (schema 1).
  An imported `Ready` status does not make an unvalidated payload routable.
- `runtime-migrations.sqlite3` records per-domain status, source hash, target
  identity/schema/revision and owner receipt. It does not claim a transaction
  across database files. Completed private domains are skipped on restart, the
  shipped content release is revalidated, and a missing committed shared target
  cannot trigger legacy replay.
- Python player memory and its consent/suppression/deletion policy remain separate.
  Windows DPAPI/environment still own provider credentials. No memory or credential
  merge, consent change, upload, generation or speech call was run.

The coordinator synthetic fixture exercises a schema-1 game upgrade, prior-root
Topic SQL promotion, Topic JSON fallback, session JSON, transcript JSONL, generated
archive import, identity mapping, receipt replay, failed target identity, and lost
shared target recovery. Its content callback uses a synthetic marker; the actual
full authored SQL release was verified separately by the ContentData artifact tests,
not by one integrated Unity startup run.

## Verification

| Check | Result | Log |
| --- | --- | --- |
| Persistence, all groups including prior-root Topic SQL relocation and RuntimeMigration | 15/15 passed | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F7-Persistence-All-2026-10-22.log` |
| Unified conversation, including deferred migrated-history provider checks | 197 passed | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F4-UnifiedConversation-2026-10-22.log` |
| Companion Runtime, including deferred memory consent/suppression checks | 171 passed, 2 skipped | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F4-CompanionRuntime-2026-10-22.log` |
| Topic runtime | 33 passed | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F7-TopicRuntime-2026-10-22.log` |
| Topic Playground | 13 passed | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F7-TopicPlayground-2026-10-22.log` |
| Scenario runtime | 67 passed | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F7-ScenarioRuntime-2026-10-22.log` |
| Furniture runtime | 52 passed | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F7-FurnitureRuntime-2026-10-22.log` |
| ContentData Repository / Bake / Publish / Flows / Semantics | 8 / 11 / 6 / 7 / 4 passed | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F7-Content-*.log`, `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F7-Content-Bake-2026-10-22.log` |
| Authored file/SQL route parity | 57 routes x 24 local hours; candidate order/status and selected path matched | Included in `F7-Content-Bake-2026-10-22.log` |
| Progression/dialogue/bridge source compile | Exit 0; policy/schema probes passed; existing source-list/type-conflict warnings remain | `.superpowers/sdd/Full SQLite Cutover Implementation Plan/F7-ProgressionCompile-2026-10-22.log` |

The persistence harness used Unity 6000.4.4f1 bundled Mono, SQLite-net 1.3.2 and
the packaged Windows x64 native plugin. These checks do not establish Unity Editor
import/compilation or player behavior.

The relocation fixtures include an unknown user table and an unrelated legacy
backup row; both are rejected while the canonical Topic target stays at revision 0.
The regression exposed that `ConversationProtocol.BuildJob` normalized transcript
utterances without copying their internal source ID; the resulting memory policy
contained an empty ID even though SQL history retained the correct event identity.
The builder now preserves that identity while the memory protocol continues to send
source IDs in its separate policy field.

The coordinator tests import `migrated-memory-history.jsonl` into SQLite, then use a
deferred local fake helper to change consent or suppression while a memory-aware turn
is pending. The stale result is neither written to the SQL transcript nor presented;
the prior and current player messages remain in chat history. The helper confirms that
only the newly submitted request is synced, while the imported historical source ID is
passed in the separate memory policy. With online generation disabled, the imported
history remains available and does not start the provider. The Companion Runtime tests
exercise the real temporary Python SQLite repository with another deferred fake
provider: consent or suppression changes discard the reply, no current-source memory
is written, and replay of the forgotten source remains suppressed. No credential or
live provider call was used.

## Remaining file and asset access

- Runtime authored route, Topic and coverage reads come from `RuntimeContentStore`.
  `ConversationTopicCatalog.Load(root, coveragePath)` and
  `AuthoredDialogueRouteCatalog.FromDirectory` remain for editor/test tooling; no
  production callsite was found for these file-backed helpers.
- `TopicHistoryRepository` file reads/writes are used by the Editor Playground and
  tests. Runtime imports first check the SQL receipt; they do not reread JSON after
  a committed cutover.
- Session JSON, transcript JSONL and generated archive JSON are importer-only.
  Their source files remain intact.
- GTE model/tokenizer remain `Resources` assets. No route-anchor vector index is
  loaded from `Resources` by the production runtime path.
- TTS audio cache files remain media cache. `DialogueManager.SaveToArchive` is an
  explicit legacy JSON export helper with no in-project callsite; it is not used
  as the generated archive store.

## Next acceptance gate

Rerun the focused synthetic Editor tests when the Editor is responsive, verify normal
startup from the newly built SQL release, then perform synthetic-profile
`SplashScene -> ModelTest` acceptance, including Topics, session/transcript
persistence, generated-payload fail-closed behavior and restart. Do not use a real
player profile or provider key.
