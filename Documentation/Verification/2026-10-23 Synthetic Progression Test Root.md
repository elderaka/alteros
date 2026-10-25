# Synthetic Progression Test Root

**Date:** 2026-10-23  
**Status:** Editor-only source and focused test fixture are ready for Unity Editor execution; the suite has not been run.

## What changed

- `AlteraProgressionStore` exposes an Editor-only persistence-root override. Player builds retain the original `Application.persistentDataPath` behavior; the existing per-instance `testPersistenceRoot` reflection seam remains available to current tests.
- The override accepts only an absolute, empty path under the OS temporary directory. It rejects the player data path, project path, reparse-point/junction paths, nonempty roots, and concurrent test roots. An invalid test request remains fail-closed in Editor rather than falling back to player data.
- The Editor helper stores the opt-in in Unity `SessionState`, restores it across Editor domain reload before scene `Awake`, and clears it after returning to Edit Mode. It has no menu, player setting, or runtime UI.
- Focused NUnit Editor tests cover pre-`Awake` routing to a synthetic SQLite path, invalid/missing/profile paths, nonempty/nested roots, and the unchanged default-root choice when no test override is configured. The fail-closed restore test explicitly expects the intended `Debug.LogError` through `LogAssert.Expect`, avoiding a Unity Test Framework unexpected-error failure.

## Files

- `Assets/Scripts/Progression/AlteraProgressionStore.cs`
- `Assets/Editor/ProgressionTestPersistenceRoot.cs`
- `Assets/AlterOS/Tests/Editor/Persistence/ProgressionTestPersistenceRootTests.cs`

## Verification and limits

- Source review confirms the persistence-root override and static state are enclosed by `UNITY_EDITOR`; no scene or serialized asset changed.
- The second bounded source review confirmed the three original findings were addressed and identified one test-only issue: the expected fail-closed error log needed an explicit expectation. A third scoped review accepted the exact `LogAssert.Expect` correction and released the fixture for Unity Editor compile/testing. Unity Editor execution and Play Mode remain pending.
- The Unity Editor and test runner were not opened in this slice, and no profile path was read or written. The new NUnit Editor tests were not executed here.
- Next isolated acceptance: call `ProgressionTestPersistenceRoot.PrepareForNextPlayMode(<new absolute empty temp directory>)` before starting `SplashScene`, verify progression and SQLite use only that root through startup, then return to Edit Mode and verify the override clears. The human's live game test remains separate.
