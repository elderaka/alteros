# Unified Conversation M2 handoff

Updated: 2026-10-22 (source/storage/documentation reconciliation). Earlier test
counts retain their dates; no new Unity/provider/player check was run.

## 2026-10-22 database compatibility update

- `ConversationTranscript` now fronts `SqliteTranscriptStore` in private
  `<persistentDataPath>/Data/conversations.sqlite3`; JSONL is a retained one-time
  import source, not a live writer. Use Append/ReadPage/ReadRecent, not file appends
  or direct Python reads of the Unity database. Constructor paths now mean SQL
  database paths, not JSONL filenames.
- Preserve event/source IDs, sequence/cursors, consent/forget suppression and
  durable-response-before-presentation semantics. `PlayerMemoryTranscriptSync`
  maps player requests into bounded unclassified evidence; Python memory retains
  its separate owner/database and existing protocol.
- Candidates/coverage/embeddings come from a pinned validated SQL release. The
  installed artifact passed read-only trusted-release, SQLite integrity and
  foreign-key inspection; its manifest matches authoring revision 63 and the
  439-document selected snapshot. The earlier expression-validation blocker was
  corrected. This does not prove normal startup, runtime consumption, player
  dialogue playback or profile migration; keep those acceptance gates open. See
  [[../Verification/2026-10-22 Full SQLite Cutover]]. Do not bypass a future
  storage-unavailable result with legacy JSON or paid provider requests.
- Session/repetition/ambient and generated archive facades are migration-gated;
  archive storage alone does not validate, index or publish generated content.
- SQL authoring A0–A6 and furniture I1–I6 have separate assigned owners. Consume
  their finalized facades/revisions, not another writer. Storage changes alone
  require no provider/Pydantic schema or prompt rewrite, nor a helper rebuild
  without changed Python inputs.
- M3 captures actual bounded capabilities through the existing activity host;
  drawing remains unavailable. T interaction/E inventory is approved/assigned,
  but the source input asset still uses E at this checkpoint.

See [[../Verification/2026-10-22 Documentation Reconciliation]] for the bounded
LLM integration follow-up and evidence/ownership limits.

## 2026-10-20 routing/helper correction

- The online authored fast-path remains >=0.850. If it does not match,
  provider RAG receives all ranked top-eight candidates and availability data;
  weak retrieval is context, not authority. In the current v4 Scenario path,
  helper/provider failure returns unavailable without attempting local authored
  fallback or host-bound active-Topic continuation; this is a known follow-up.
- `DirectConversationBridge` resolves its opt-in Editor helper only from
  `Build/CompanionRuntimeV4/AlterOSCompanion/AlterOSCompanion.exe`; historical
  experiment folders can no longer shadow the v4 protocol.
- Current checks: Unified Conversation194/194 and Companion Runtime173/173,
  including the v4 helper readiness smoke with an empty key. No API key or
  provider call was used. Current Unity import/EditMode and Play Mode remain
  unverified.

## Current result

The saved `ModelTest` path contains the unified direct-conversation bridge behind
its scene gate, with online generation default-off. Unity owns candidate
retrieval, eligibility, response identity, validation/revalidation, history
presentation and all gameplay effects. The Python helper makes bounded provider
requests only after explicit online opt-in (parallel head/main stages in v4,
unlike the earlier one-shot path); generated replies
are validated Japanese VN beats with English display text, not arbitrary actions.
The coordinator remains the sole transcript writer through the SQL facade.
The private local SQL store is not encrypted at rest.

The additive M2.1 source path now sends memory-aware protocol v3 turns after
keyless local consent/checkpoint/source synchronization. It filters forgotten
sources from memory and recent presented history, revalidates consent and
suppression after a provider wait, and refuses unsafe v2 downgrade after memory
policy is required. Raw transcript player messages are unclassified; unsupported
memory proposals are dropped without invalidating an otherwise valid reply.
Memory storage remains local and consent-controlled; online-generation consent
is separate.

Generated v2 reply JSON uses `reply.beats`; authored Topic JSON and the internal
canonical dialogue runner keep their existing `nodes` arrays. The strict Python
and Unity parsers reject the old generated-reply `nodes` key rather than
accepting two spellings. The current schema/prompt smoke returned two beats and
passed strict parsing plus host validation. Suggested reply labels and prompts
must match the current English display language; the host drops non-Latin
suggestions but preserves the reply. See [[../Verification/2026-10-19 Agent Reply Schema Fix]]
and [[../Verification/2026-10-19 Suggested Reply Language]].

Authored first-meeting data and a pure session state model exist, but first-E
interception and Canvas profile/settings controls are not wired. The pure
proactive-question policy is tested but not enabled by the live coordinator;
focus/quiet evidence, question history/settings retrieval, and pending proposal
confirmation are incomplete.

## Historical source and package checks (2026-10-19)

- `Tools/Conversation/Check-PlayerMemory.ps1`: Python129/129 and C# conversation/
  memory source checks106/106.
- Topic Runtime33/33; Topic Playground13/13; promoted catalogue57 families,
  161 scenarios, 161 migration rows validated.
- Fresh Windows onedir helper is at
  `Build/CompanionRuntimeM2Task6FinalAcceptance/AlterOSCompanion/AlterOSCompanion.exe`.
  Keyless startup, onboarding write, clean shutdown and restart/read passed from
  a path containing spaces. Exact SHA256 is in
  [[../Verification/2026-10-19 Player Memory M2.1 Implementation]].
- Existing `CS0649` DTO warnings were emitted by the headless C# compiler; no
  errors. This does not establish Unity Editor compilation.
- Suggested-reply language follow-up: Python **140 passed, 1 skipped**; the
  conversation/memory C# source harness **115 passed**. Editor helper
  `Build/CompanionRuntimeM2EnglishSuggestions` bootstrapped and shut down using
  a synthetic-only credential; no provider call. See
  [[../Verification/2026-10-19 Suggested Reply Language]].

## Provider and runtime boundaries

The prior [[../Verification/2026-10-18 Conversation Smoke]] documented successful
synthetic calls on one route, followed by DigitalOcean structured-output tests
whose `message.content` was null and failed VN validation. The later capped
2026-10-19 smoke documented above succeeded with the corrected `reply.beats`
contract on a synthetic turn. This is narrow provider/parser evidence, not full
Unity or product acceptance. Do not loosen the VN contract, silently switch
routes, add retries, or use a saved key. Any further live diagnosis needs
separate explicit spend approval.

The read-only Unity Console request timed out. No Unity Editor import/compile,
EditMode job, Play Mode, Canvas visual review, startup/E test, or player test was
done here. The Editor was not closed/restarted and no scene/prefab/meta files
were edited. The human's `Splash -> ModelTest -> E -> authored/generated
dialogue/follow-up` test remains theirs and is still pending.

## Next gates

1. Restore/confirm the correct Unity Editor without discarding open content;
   allow normal import/compile and inspect the Console.
2. Wire and inspect first-E onboarding plus the Canvas memory/profile/settings
   controls, including opt-in disclosure, list/edit/forget and retained-history
   distinction. Keep online consent separate.
3. Complete question history/settings and pending-proposal confirmation wiring;
   keep proactive questions disabled while focus/quiet are unknown.
4. Re-run relevant EditMode tests, then have the user conduct the final
   player-facing flow. Test generated dialogue only after the provider's
   structured-content failure is resolved under separate authorization.

Do not expand this handoff to phone UI, TTS, new tools, sentiment, cloud sync or
autonomous activity. Follow current AGENTS.md for verified-hang recovery; stale
MCP alone is not a hang or permission to close unrelated Editors.
