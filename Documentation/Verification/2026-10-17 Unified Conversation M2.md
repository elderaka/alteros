# Unified Conversation M2 verification

Date: 2026-10-17  
Scope: direct E-channel conversation source integration and acceptance boundary.  
Project: `E:\Pleiades\alteros`, Unity `6000.4.4f1`.

## Result

M2 source and saved-scene integration are in place. Runtime acceptance is
incomplete. In particular, the Unity Editor stopped responding during the full
190-test EditMode request; no Play Mode or player-visible behavior is claimed.

## Implemented

- Promoted the reviewed staged catalogue into
  `Assets/StreamingAssets/Dialogue/Topics/`. The promotion copied content
  byte-for-byte and validated 57 families, 161 scenarios and 161 migration
  rows. No anchor/index rebake was done.
- Added the direct conversation coordinator and Unity bridge. The host owns
  candidate retrieval/context, response identity, validation, current-state
  revalidation and transcript writes. The online path is one request per turn;
  a late successful reply is stored once but not auto-presented after the
  player dismisses or changes the active context.
- Saved `Assets/Scenes/ModelTest.unity` with the explicit M2 bridge gate,
  references, editor-only helper opt-in and an online toggle defaulting off.
  Authored replies use the production Topic playback/history hooks. Generated
  replies map to validated bilingual VN nodes and optional suggestions bound
  to the response identity. This direct generated path is text-only and skips
  TTS synthesis and prefetch.
- `ConversationTranscript` remains the sole writer for M2 direct history;
  legacy `ConversationSessionStore` is not dual-written. Transcript data is
  local plaintext and is not guaranteed encrypted at rest.

## Checks and evidence

- Initial M2 C# source check before review fixes: 70/70.
- Python sidecar tests: 42/42.
- Topic Playground checks: 13/13.
- Topic Runtime checks: 33/33.
- Production Topic catalogue: 57 families / 161 scenarios; migration map
  161/161 validated. Promotion PowerShell syntax passed.
- Unity refresh/compile: zero C# error entries in the latest query.
- Focused Unity EditMode selection (coordinator, catalog, transcript): 16/16,
  job `75e2c3dd12d242bb8d4389d71cd6af2b`.
- Fresh-context review found and drove two narrow fixes: protocol history now
  canonicalizes the runtime `altera` label to `alterra` for later requests;
  explicit turn cancellation is distinguished from the 60s deadline and
  appends a `cancelled` status. A third regression closes the narrow race where
  a provider result had completed but cancellation happened before presentation;
  that result is retained as late and suppressed from the active UI. Regressions
  were first observed failing, then the full C# source suite passed72/72.
  Python42/42, Playground13/13, and
  Topic Runtime33/33 passed in the same final source-validation pass (Python
  was run separately after correcting the virtualenv path).
- Full `AlterOS.Editor.Tests` EditMode request: 190 tests were selected, but
  Unity process PID 33804 became unresponsive and the MCP bridge timed out
  repeatedly. No result was returned. The process was not force-killed, and
  the prior focused run is not reported as a full-suite pass.
- No API key was entered, read for this task, or sent; no OpenRouter request
  was made. No TTS request was made.

## Not verified

- Recovery of the currently unresponsive Unity Editor and a fresh full Editor
  suite on current source.
- Play Mode through the normal `SplashScene -> ModelTest` startup, physical E
  input, online-toggle appearance/usability, offline authored response,
  generated response display, or suggested-reply interaction.
- Visual layout, GTE live inference, TTS, performance/latency, or generated
  dialogue quality. Compilation and source checks do not prove these.
- Any paid provider behavior. The proposed live smoke budget in the plan is
  not an authorization or a provider-enforced spending cap.

## Privacy and operation notes

The on-device transcript is append-only JSONL under
`Application.persistentDataPath`; it contains conversation/event content and
is not encrypted at rest. The managed helper is launched only through the
bridge path. Production expects the packaged sibling executable; the Editor
development path is explicit opt-in. Online generation defaults off and
requires the existing BYOK key. No automatic provider retries are configured.

The zero-C#-error Unity query and focused16/16 EditMode result predate the
review fixes above. The final source harness compiles/tests those C# changes,
but Unity import/compile and EditMode must be rerun after Editor recovery.

## Next gate

Recover the Editor safely without discarding user content, confirm project and
scene, allow normal import/compile, then rerun the full EditMode selection.
After that, perform a bounded no-key Play Mode check through Splash -> E -> an
offline authored reply, and visually inspect the toggle/status controls. Keep
online generation disabled. A separate user approval is required before any
provider spend or API request. See [[../Tracking/Unified Conversation M2 Handoff]]
for the task boundary.

## SHA-256 checkpoints

These identify the saved scene and representative M2 source at handoff time;
they are integrity references, not Git change detection (the repository has no
baseline commit).

| File | SHA-256 |
| --- | --- |
| `Assets/Scenes/ModelTest.unity` | `2D2A361D01AEFA1DBF399154192DD06DBB8743D8908322150ED6E7ED109D7E53` |
| `Assets/Scripts/Dialogue/DirectConversationBridge.cs` | `ABB6A8016E22ED9A8F73D417B8FD382DE7E70F68B079FEA4FD0A410B855905B3` |
| `Assets/AlterOS/Conversation/DirectConversationCoordinator.cs` | `C78B7E41CD0B1BB5F65E67DC90B617FF7D9C1D8C94295C9EDE563CB17B5DEBE7` |
| `Assets/AlterOS/Conversation/ConversationTranscript.cs` | `DCF4660C97E6F57391FCC2F840EB84ADC4B50B73BBC254A94F0325F4B320B9EB` |
| `Tools/CompanionRuntime/src/alteros_companion/graph.py` | `7DD059CFB3BFB52FBFC4C3566A346FA383ACFFA71B047BAAFC9FA68F8A1BAA13` |
