# Local recording and contribution framework

The local recording API is opt-in and starts disabled. A caller must construct an
`InteractionRecorder`, present a clear permission choice, call `Enable` with a validated
policy, and explicitly register the recorder with the conversation host. No code
initializes recording from game startup or from the presence of a saved BYOK key.

Metadata and raw text are separate policy scopes. Raw text is limited to accepted
submitted input and is omitted from contribution exports unless the preview explicitly
includes it. Record kinds and metadata fields are allowlisted; unknown fields, secret-like
keys, oversized observations, cap overruns and observations outside the enabled interval
are rejected. Retention, byte cap and record cap are persisted per session. Disable stops
new writes and invalidates unexported contribution batches.

`ContributionPreview` freezes selected membership and projected bytes under a SHA-256
hash. Confirmation requires that exact hash. Export writes the confirmed local batch to
a caller-supplied absolute path without network delivery. An export does not imply upload,
server acknowledgement, model training permission, anonymity or withdrawal support.

Verify the synthetic recording group with:

```powershell
.\Tools\Persistence\Check-GamePersistence.ps1 -Group Recording
```

## Not implemented

- Player-facing Canvas controls for recording opt-in, scope selection and Contribute.
- Network transport, delivery retries, server receipts or model-training consent.
- Server-side withdrawal/deletion policy after a user moves an export elsewhere.
- Unity Editor import tests, Play Mode review or player acceptance.
