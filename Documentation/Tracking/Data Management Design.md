---
title: Data Management Design
date: 2026-10-21
tags: [alteros, persistence, sqlite, privacy, design]
status: approved-product-decisions-technical-proposal
---
# Data Management Design

Related: [[Data Management Implementation Plan]], [[Player Memory Design]],
[[Furniture Inventory and Debug Design]], [[Furniture Mechanics and Catalogue Design]],
[[Unified Conversation Design]], [[Architecture]].

## Authority and status

Scope follow-up2026-10-22: the human approved full runtime SQLite cutover including
Topic consumption, conversation sessions and transcript. [[Full SQLite Cutover Implementation Plan]]
supersedes earlier initial-slice preservation of those file stores, but not the logical
owner/consent boundaries. Existing personal-memory SQLite and secure credentials
stay separate; no wholesale merging or new online/capture permission follows.

This consolidates main-session human decisions on 2026-10-21. Product rules below
are approved. File structure, checkpoint timing, backup counts and schema details
are implementation recommendations, not independently approved gameplay rules.
At the time of consolidation, no runtime implementation, migration or cloud service
had been performed. The current implementation snapshot is recorded below.

## Approved product rules

- One persistent Alterra per account, not one per device. Local play may begin
  without an account; stable local character/home identity survives later linking.
- A conflicting existing account companion is never automatically merged or
  overwritten. Explicitly select which survives account linking and back up/export
  the other before replacement. An archive is recovery data, not a second active slot.
- Continuous persistence: no ordinary player-facing save/load slots or Save button.
  Meaningful changes commit automatically. Internal checkpoints/backups are allowed.
- Desktop owns the simulation and works offline. Initial mobile is a free,
  account-linked, online-only companion/controller, not another simulation.
- Mobile live interaction requires the desktop running and connected. Otherwise
  show labelled last-known state/history. Messages are pending until acknowledged;
  expired activity commands must not unexpectedly execute later.
- Desktop direct talk and mobile phone messages use the same Alterra/history but
  distinct channels. Opening mobile never creates another needs-decay loop.
- Standalone mobile, authority handover, cloud recovery and server-hosted simulation
  are roadmap work, not requirements for the first persistence slice.
- SQLite is the local persistence standard for mutable durable state. Shared authored
  dialogue/drafts use the revisioned SQLite authoring repository and compiled SQL
  release; JSON is explicit import/export evidence. Other game definitions and core
  persona keep their existing owners and versioned file/catalogue formats. Credentials
  use secure storage.
- The LLM cannot directly execute SQL, change ownership, mint currency or set stats.
  Validated host operations apply effects; model narration is not evidence.
- Personal-memory capture/use and permission to share relevant information with
  an AI provider are separate controls. Personal memory entering a prompt needs
  both permissions and online-generation consent. Ordinary saving is unconditional.
- Deleting chat does not automatically delete independently retained memories;
  disclose that distinction. Explicit forgetting suppresses recall and automatic
  reconstruction from old sources. Time passing reduces freshness, not necessarily
  historical retention. Forgetting a gift never deletes its owned physical item.
- Device-local bounded recovery backups first. Restoration must not resurrect
  forgotten information; no forensic secure-erasure/encryption promise.
- Offline save modification is tolerated. Hosted AI credits/entitlements are
  server-authoritative and cannot be minted from gameplay currency or local saves.

## Ownership and storage

| Domain | Authority | Persistence direction |
| --- | --- | --- |
| Shared authored dialogue, routing metadata and drafts | SQLite authoring repository | Revisioned SQLite schema 1; compile an immutable pinned SQL release; JSON only for explicit import/export. Approved editing UI: Flow-first graph + selected-step inspector, sharing typed commands with agents; W2-W5 remain open |
| Item/stat/activity definitions and core persona | Existing project content owners | Existing versioned catalogues/files; outside this dialogue-content cutover |
| Alterra needs, emotional axes, preferences, relationship/progression | Desktop game rules | Local game SQLite, with live in-memory working state |
| Player game progression and environment state | Desktop game rules | Local game SQLite; not inferred player psychology |
| Hidden sentiment evidence | Evidence owner | Separate provenance/confidence/expiry; never permission or permanent fact |
| Inventory, ownership, furniture transforms/resources | Validated game commands | Transactional local game SQLite; exact item/instance IDs |
| Meaningful gameplay experiences | Host-confirmed event publisher | Timestamped event records; not every frame or every tiny stat change |
| Personal memory | Existing consent-aware repository | Existing sidecar SQLite retained initially |
| Conversation sessions and transcript | Session facade / coordinator transcript owner | Separate private conversations SQLite database; legacy JSON/JSONL are import evidence |
| Account, hosted balance, purchases | Future backend | No local authority; display cache only |
| Provider credentials | Platform secure storage | Existing Windows DPAPI/environment boundary unchanged |
| Models, TTS audio, visual assets | Asset/cache owner | Files; references/metadata where needed |

Preference is persistent character data separate from needs/mood and authored
identity. Specific item/tag/activity tastes can affect reactions; contextual desire
is not a permanent taste change. No preference evolution algorithm is approved here.

## Current source snapshot (not gameplay proof)

Implementation follow-up 2026-10-22: `AlteraProgressionStore` now gates state
publication and mutation on SQLite initialization, imports the existing schema-4
JSON once with retained source bytes and a migration receipt, and commits discrete
progression changes before publishing them. `SqliteGameRepository`,
`GamePersistenceScheduler`, `ProgressionJsonImporter`, `GameBackupService`, and
`FurniturePersistenceAdapter` implement the desktop game-data boundary. The
database path is `<Application.persistentDataPath>/Data/alteros-game.sqlite3`.
Game schema 3 adds Topic occurrence/completion history to this same database. The
earlier root-level Topic database is a read-only migration source; its Topic rows,
source backup and receipts promote into the canonical database. Synthetic headless
checks pass; Unity import/EditMode, Play Mode, and player
acceptance remain open. Schema-4 snapshots are shape-validated before default
normalization; malformed/incomplete snapshots keep their exact stored JSON and
require recovery. Dialogue effects are bounded and only SQL release Generic routes
selected through the catalogue and passed through canonical validation can use the
authored-effects path. Public `PlayCinematic`, `PlayFromJson`, generic legacy
`Play(path)`, local sequence-choice, and test-JSON starts remain untrusted. See
[[Data Management Handoff]] and [[State and Effects]].

- `ProgressionTickSystem` keeps its existing tick/decay rules and marks continuous
  changes dirty; it does not add offline catch-up.
- `HomeFurnitureLayout` remains the owner of the existing layout DTOs and exact
  metre/quaternion/support fields. The SQLite adapter reuses its validation and
  preserves unsupported definitions as inert records; no inventory gameplay was
  added.
- `Tools/CompanionRuntime/src/alteros_companion/memory_repository.py` uses Python
  sqlite3. Existing private memory path is `Memory/player-memory.sqlite3` under
  persistentDataPath. It remains separate; no bulk merge/migration in slice D1.
- Existing session/player identifiers are held in PlayerPrefs; preserve their values
  when mapping them into private conversation SQL. Do not silently create an unrelated player.
- `Packages/manifest.json` pins `com.gilzoide.sqlite-net` 1.3.2 at
  `08248bd5884d8eb932a837aa56d4ff456daf913f`; the SQLite package is only the game
  repository adapter and is not a replacement for the Python memory owner.

Full cutover implementation follow-up 2026-10-22: game schema 3 adds normalized
Topic occurrence/completion history and one-time import receipts. The startup
coordinator promotes Topic state from the earlier root-level game database into
`<persistentDataPath>/Data/alteros-game.sqlite3`, or imports the legacy JSON when no
prior SQL Topic domain exists. `ConversationSessionStore` imports session JSON into
`<persistentDataPath>/Data/conversations.sqlite3`, then commits detached revisioned
snapshots for sessions, bounded turns, repetition and ambient history. Schema 2 adds
transcript events to the same private database; the coordinator remains the sole
append owner and the JSONL importer stores original bytes in bounded chunks. Generated
archive schema 1 is separate under `Data/personal-content.sqlite3`. A SQLite migration
journal reconciles owner receipts, pins target identities, skips completed domains on
restart and blocks replay after a committed target is lost. Private conversation/archive
databases stay outside game-only backups and Python player memory.

Synthetic persistence passes 15/15, with unified conversation 197/197, Companion Runtime
171 passed/2 skipped, and the fresh full-corpus ContentData suites green. The local
authoring store is revision 63 with 439 imported/selected sources; the installed
439-source SQL artifact at `Assets/StreamingAssets/Content/content.sqlite3` has SHA-256
`67033644122DBB06884B5479A3BBC0469B23C497FEC54D39B559ED83B416A31F`. Read-only
inspection verified SQLite integrity, zero foreign-key violations and a valid trusted
release. Its build-manifest snapshot hash matches authoring snapshot
`02f05e100d6e517086ef3cefe59acc9692dff28a2fa857c2b1b3740574bb0c27` (revision 63).
This does not prove startup/player acceptance or real-profile migration.
[[../Verification/2026-10-22 Full SQLite Cutover]] has the evidence details.

## Proposed local technical contract

Use a game-owned repository independent of Unity presentation and the agent
process. Recommended new path: `<persistentDataPath>/Data/alteros-game.sqlite3`.
One serialized writer owns commits; consumers receive snapshots/results, not raw
connections or arbitrary table writes. Use parameterized SQL and a trusted root.
Never hold a transaction across model/network/navigation waits.

Stable saveId, alterraId and homeId exist before account linking; accountId is
nullable and deviceId is not character identity. Existing memory playerId remains
mapped without changing its namespace. Store schema version separately from
content versions, home revision and event/operation identity. UTC timestamps are
metadata, not conflict-resolution authority. Unknown stat values remain unknown.

Discrete host commands carry operationId and expected revision. Commit resulting
state, relevant event and receipt atomically, then acknowledge durable success and
publish the new committed snapshot. Same ID/same payload returns its receipt;
same ID/different payload fails. A stale revision fails without partial mutation.
No external caller receives a general-purpose write API.

Use distinct expectedGameRevision and expectedLayoutRevision; preserve unchanged
domains. Look up receipts before rejecting stale revisions so a retry after later
commits still receives its original result. Discrete effects use detached candidate
state, then commit, then publish. Failed candidates cannot leak into later dirty
checkpoints through publicly mutable live state.

For continuous values, retain current simulation rules. Recommended first slice:
persist dirty stat snapshots at most five seconds apart, plus pause/quit best-effort
flush and before discrete actions that consume those values. No per-frame writes.
This internal checkpoint is not a Save feature. Abrupt termination can lose up to
the last checkpoint interval of uncommitted continuous progress; never claim zero
loss. Timestamp/rate reconstruction requires deterministic, versioned simulation
rules and is deferred rather than inventing offline decay now.

Storage failure exposes unavailable/unsaved status, keeps last durable revision,
and does not report transactional action success. Corruption, future schemas or
invalid legacy data must not silently create defaults over the old save. New game
defaults are legal only when no existing save/source is present. Side effects
such as visuals may be speculative, but ownership/rewards cannot become committed
outside the durable operation boundary.

## Migration and recovery

Import existing progression JSON once using a source hash and migration receipt.
Preserve original bytes and all schema-4 fields, including custom stats, known
flags, tasks and unlocks. Never reinterpret gameplay credits as hosted balance.
Fail closed on malformed/future input; do not delete sources or dual-write forever.
Migration commit and receipt are atomic; repeat startup cannot reimport old values.
Keep existing memory, chat, Topic history and generated archives unchanged initially.
Bootstrap metadata/empty DB creation does not mean gameplay is initialized. Detect
legacy sources and finish import before publishing defaults or accepting mutations.
A crash after bootstrap must resume migration, not bypass malformed legacy input.

Recommended initial retention: five rotating game-DB checkpoints (no more than
one per 30 minutes), plus a pre-migration backup retained until migration acceptance.
Use a consistent SQLite backup mechanism, not copying a live main file while
ignoring its journal/WAL. Validate backup/recovery in staging before replacement;
recovery must not silently roll back visible durable progress.

Existing consent/suppression lives inside the memory SQLite DB; an independent
restore-proof ledger does not currently exist. A future memory-recovery slice must
add a suppression ledger outside rotating backups, controlled by the memory owner.
Before enabling recall after any future memory/history
restore, apply current suppression/deletion policy. Missing/inconsistent ledger
means personal recall/export stays disabled, not permission to resurrect data.
The initial game-only backup must exclude personal-memory DB/transcript/credentials.
Full memory restore/export and cross-device deletion propagation require their own
implementation; do not label game-only backup as full account recovery.
Game-only does not mean non-personal: task labels/event payloads can contain player
text. Backups remain private and never grant permission to inject data into prompts.

## Item presentation consumer contract

Presenting an owned item takes priority over the ordinary talk menu (planned T,
not inventory E). Existing uninterruptible activity/safety rules still apply.
Use optional item presentation Topic references with category/generic fallback;
not every object needs a unique Topic. Available choices may include shared use,
gift or cancel. Presenting alone neither transfers ownership nor consumes it.
Acceptance/decline/defer use host-validated conditions; completed gifting records
exact item, actors, outcome and time with ownership transfer. Activity start and
completion are different events. Recall consumes confirmed records only.
This defines a future consumer; inventory/gifting UI and executor implementation
remain a separate session, not implied by creating persistence tables.

## Future connected contract (not implemented)

Clients exchange bounded commands/acknowledged revisions through authenticated
service endpoints, never remote SQLite connections or last-write-wins file copies.
Desktop is initial simulation authority; other clients are views/controllers.
Disconnect cannot prove another machine stopped simulating, so do not promise
anti-fork guarantees. Replacing a registered desktop requires explicit authority
replacement; stale device revisions cannot overwrite newer accepted cloud state.
Billing/credit reservations, provider secrets, limits and idempotent settlement
are future server responsibilities; local game state grants none of them.

## References

- [SQLite intended uses](https://www.sqlite.org/whentouse.html).
- [SQLite network limitations](https://www.sqlite.org/useovernet.html).
- [SQLite transactions](https://www.sqlite.org/lang_transaction.html).
- [OWASP paid-resource consumption](https://api-security.owasp.org/editions/2023/en/0xa4-unrestricted-resource-consumption/).

## Verification boundary for the initial consolidation

The initial consolidation was documentation only. Subsequent source/headless checks
are not Unity Editor, Play Mode, player, or live-profile acceptance. No live player
data or keys were read; no provider call, upload, or real-profile migration was run.
See [[Full SQLite Cutover Implementation Plan]] for current gates and handoff.
