---
title: 2026-10-16 BYOK Setup
tags: [alteros, verification, credentials]
---
# Session 0: provider credential setup

Scope: OpenRouter provider/key setup only. This does not implement or enable Live
Agent, background Topic generation, model selection, tool calls, or automatic
TTS. The existing TTS adapter continues to share the OpenRouter key; saving a
key does not turn speech generation on.

## Existing owner

`Assets/AlterOS/Security/OpenRouterCredentialStore.cs` stores an OpenRouter key
using current-user Windows DPAPI in `Application.persistentDataPath` and reads
`OPENROUTER_API_KEY` as a development override. `Assets/Scripts/TTS/TTSSystem.cs`
and the existing dialogue path already consume this shared store.

The Session 0 UI is `Assets/Scripts/UI/OpenRouterSettingsPanel.cs`, with its
Canvas-authored view at `Assets/Prefabs/UI/OpenRouterSettingsPanel.prefab`,
attached to `AlterOSHUDCanvas` in `ModelTest`. P opens the panel in the room;
Escape/Close dismiss it. The credential field masks input and clears after save
or close. The live UI reports environment override, saved, absent, and
unreadable-key status without displaying secret material.

Save uses a same-directory temporary encrypted file and atomic replace/move so a
failed replacement leaves the previous credential intact. The explicit Test
key action calls OpenRouter's `GET /api/v1/key` endpoint. It doesn't generate
text/audio or display/log the raw provider response. A draft key may be checked
before save. Environment key precedence is unchanged and is disclosed in UI.

## Verification snapshot

- Unity compilation completed with no errors after the concurrent Playground
  imports. Existing project deprecation warnings remain; the new settings
  controller no longer adds `FindObjectOfType` warnings.
- The Canvas prefab has the controller, all eight serialized UI references, and
  a password-masked single-line key field. Its instance is attached under
  `AlterOSHUDCanvas` in `ModelTest`.
- Only `Assets/Scenes/ModelTest.unity` was saved. The unnamed blank scene remains
  loaded and was not saved or closed.
- The connection check uses OpenRouter's documented `GET /api/v1/key` endpoint;
  it only verifies the current key and does not generate content.
  [OpenRouter endpoint reference](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-key)
- Windows DPAPI is current-user protection, not protection from malicious code
  already running as that user. The game now clears decrypted/encoded temporary
  byte buffers after use.
- Not checked: rendered Game View, actual P/Escape/player-control behavior,
  Windows DPAPI save/reload/replace/clear on a real credential, or the live
  OpenRouter request. No key was entered, saved, or transmitted. No Unity tests
  were run or added in this Session 0 pass.

Do not edit the concurrent Topic Playground work as part of this BYOK slice.
