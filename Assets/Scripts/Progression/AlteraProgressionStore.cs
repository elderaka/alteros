using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Content;
using AlterOS.Dialogue;
using AlterOS.Furniture;
using AlterOS.Persistence;
using AlterOS.Scenarios;
using UnityEngine;

namespace AlterOS.Progression
{
    [Serializable]
    public class AlteraStatsState
    {
        public int bondLevel = 1;
        public float bondXp = 0f;
        public float affinity = 0f;
        public float trust = 0f;
        public float comfort = 50f;
        public float energy = 70f;
        public float hydration = 50f;
        public bool hydrationKnown;
        public float engagement = 50f;
        public float stimulation = 45f;
        public float attention = 50f;
        public float reassurance = 50f;
        public string mood = "calm";
        public string availability = "present";
    }

    [Serializable]
    public class PlayerProgressState
    {
        public int credits = 0;
        public int completedTasks = 0;
        public int focusSessionsCompleted = 0;
        public int streak = 0;
        public float productivityXp = 0f;
        public float reliability = 50f;
        public float focusEnergy = 70f;
        public float stress = 20f;
        public float rest = 70f;
        public string currentMode = "free";
    }

    [Serializable]
    public class EnvironmentStatsState
    {
        public float comfort = 50f;
        public float cleanliness = 50f;
        public float lighting = 50f;
        public float noise = 20f;
        public float temperature = 50f;
        public float coziness = 50f;
        public float personalization = 0f;
        public float focusAtmosphere = 50f;
        public string timeOfDay = "unknown";
        public string weather = "clear";
        public string roomMood = "neutral";
    }

    [Serializable]
    public class ProgressionFlag
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class CustomProgressionStat
    {
        public string target;
        public string key;
        public float value;
    }

    [Serializable]
    public class PlayerTaskState
    {
        public string id;
        public string label;
        public bool completed;
        public string source;
        public string createdUtc;
        public string completedUtc;
    }

    [Serializable]
    public class AlterOSProgressionState
    {
        public int schemaVersion = 4;
        public string lastUpdatedUtc;
        public AlteraStatsState altera = new AlteraStatsState();
        public PlayerProgressState player = new PlayerProgressState();
        public EnvironmentStatsState environment = new EnvironmentStatsState();
        public List<ProgressionFlag> flags = new List<ProgressionFlag>();
        public List<ProgressionFlag> unlocks = new List<ProgressionFlag>();
        public List<CustomProgressionStat> customStats = new List<CustomProgressionStat>();
        public List<PlayerTaskState> tasks = new List<PlayerTaskState>();
    }

    public class AlteraProgressionStore : MonoBehaviour
    {
        [SerializeField] private string saveFileName = "alteros_progression.json";
        [SerializeField] private bool saveImmediately = true;

        public AlterOSProgressionState State { get; private set; }
        public bool IsInitialized { get; private set; }
        public string PersistenceStatus { get; private set; } = "unavailable";
        public GameSaveIdentity Identity { get; private set; }
        public long DurableGameRevision { get; private set; }
        public event Action<AlterOSProgressionState> Changed;

        public IFurnitureHomeStore CreateFurnitureHomeStore(Func<string, HomeFurnitureLayoutDocument> sceneSeedProvider)
        {
            if (!IsInitialized || repository == null) return null;
            return new FurniturePersistenceAdapter(repository, sceneSeedProvider);
        }

        public IScenarioHostEffectPort CreateScenarioHostEffectPort(IContentReleaseReader contentStore)
        {
            if (!CanMutate || State == null || repository == null || contentStore == null) return null;
            return new ScenarioItemGrantPersistenceAdapter(contentStore, repository);
        }

        public IFurniturePowerStore CreateFurniturePowerStore(Func<string, HomeFurnitureLayoutDocument> sceneSeedProvider)
        {
            if (!CanMutate || State == null || repository == null) return null;
            FurniturePersistenceAdapter homeStore = CreateFurnitureHomeStore(sceneSeedProvider) as FurniturePersistenceAdapter;
            return homeStore == null ? null : new FurniturePowerPersistenceAdapter(repository, homeStore);
        }

        private IGameRepository repository;
        private GameBackupService backupService;
        private ProgressionJsonImporter progressionImporter;
        private GamePersistenceScheduler persistenceScheduler;
        private bool writesBlocked;
        private bool CanMutate => IsInitialized && !writesBlocked;

#if UNITY_EDITOR
        [System.NonSerialized] private string testPersistenceRoot;
        private static readonly object editorTestPersistenceRootGate = new object();
        private static string editorSessionTestPersistenceRoot;
        private static bool editorTestPersistenceRootRequested;
        private const string EditorTestPersistenceMarkerName = ".alteros-editor-test-root";

        public static string SetEditorTestPersistenceRoot(string root)
        {
            lock (editorTestPersistenceRootGate)
            {
                string fullRoot = null;
                if (editorTestPersistenceRootRequested)
                {
                    try { fullRoot = NormalizeEditorTestPersistenceRoot(root); }
                    catch { throw new InvalidOperationException("An Editor test persistence root request is already active."); }
                    if (!string.IsNullOrEmpty(editorSessionTestPersistenceRoot) &&
                        string.Equals(fullRoot, editorSessionTestPersistenceRoot, StringComparison.OrdinalIgnoreCase))
                        return fullRoot;
                    throw new InvalidOperationException("An Editor test persistence root is already active.");
                }

                // Mark the opt-in before validation so a rejected test request cannot
                // fall through to the player's normal save path if its caller continues.
                editorTestPersistenceRootRequested = true;
                try
                {
                    fullRoot = ValidateEditorTestPersistenceRootPath(root);
                    if (Directory.Exists(fullRoot) && Directory.GetFileSystemEntries(fullRoot).Length != 0)
                        throw new InvalidOperationException("The Editor test persistence root must be empty.");

                    Directory.CreateDirectory(fullRoot);
                    editorSessionTestPersistenceRoot = fullRoot;
                    return fullRoot;
                }
                catch
                {
                    editorSessionTestPersistenceRoot = null;
                    throw;
                }
            }
        }

        public static string RestoreEditorTestPersistenceRoot(string root, string sessionToken)
        {
            lock (editorTestPersistenceRootGate)
            {
                editorTestPersistenceRootRequested = true;
                try
                {
                    string fullRoot = ValidateEditorTestPersistenceRootPath(root);
                    if (!Guid.TryParseExact(sessionToken, "N", out _) || !Directory.Exists(fullRoot))
                        throw new InvalidOperationException("The saved Editor test persistence root is not a valid prepared session.");
                    string markerPath = Path.Combine(fullRoot, EditorTestPersistenceMarkerName);
                    if (!File.Exists(markerPath) || (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0 ||
                        !string.Equals(File.ReadAllText(markerPath), sessionToken, StringComparison.Ordinal))
                        throw new InvalidOperationException("The saved Editor test persistence root marker does not match its session.");
                    if (!string.IsNullOrEmpty(editorSessionTestPersistenceRoot) &&
                        !string.Equals(editorSessionTestPersistenceRoot, fullRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("A different Editor test persistence root is already active.");
                    editorSessionTestPersistenceRoot = fullRoot;
                    return fullRoot;
                }
                catch
                {
                    editorSessionTestPersistenceRoot = null;
                    throw;
                }
            }
        }

        public static void MarkEditorTestPersistenceRootUnavailable()
        {
            lock (editorTestPersistenceRootGate)
            {
                editorSessionTestPersistenceRoot = null;
                editorTestPersistenceRootRequested = true;
            }
        }

        public static void ClearEditorTestPersistenceRoot()
        {
            lock (editorTestPersistenceRootGate)
            {
                editorSessionTestPersistenceRoot = null;
                editorTestPersistenceRootRequested = false;
            }
        }

        private static string NormalizeEditorTestPersistenceRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                throw new ArgumentException("An absolute temporary test persistence root is required.", "root");
            return NormalizeEditorPath(root);
        }

        private static string ValidateEditorTestPersistenceRootPath(string root)
        {
            string fullRoot = NormalizeEditorTestPersistenceRoot(root);
            string tempRoot = NormalizeEditorPath(Path.GetTempPath());
            string playerRoot = NormalizeEditorPath(Application.persistentDataPath);
            string projectRoot = NormalizeEditorPath(Directory.GetCurrentDirectory());
            if (!IsPathWithin(fullRoot, tempRoot) || PathsOverlap(fullRoot, playerRoot) || PathsOverlap(fullRoot, projectRoot))
                throw new ArgumentException("The test persistence root must be isolated beneath the system temporary directory.", "root");
            if (HasReparsePointBetween(fullRoot, tempRoot))
                throw new ArgumentException("The test persistence root cannot pass through a symbolic link or junction.", "root");
            return fullRoot;
        }

        private static string NormalizeEditorPath(string path)
        { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }

        private static bool IsPathWithin(string path, string parent)
        {
            string parentPrefix = parent + Path.DirectorySeparatorChar;
            return path.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsOverlap(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
                IsPathWithin(left, right) || IsPathWithin(right, left);
        }

        private static bool HasReparsePointBetween(string path, string boundary)
        {
            string current = path;
            while (true)
            {
                if (!string.Equals(current, boundary, StringComparison.OrdinalIgnoreCase) && !IsPathWithin(current, boundary))
                    return true;
                if (Directory.Exists(current) &&
                    (new DirectoryInfo(current).Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
                if (string.Equals(current, boundary, StringComparison.OrdinalIgnoreCase)) return false;
                current = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(current)) return true;
            }
        }
#endif

        private string PersistentRoot => GetPersistentRoot();
        private string SavePath => Path.GetFullPath(Path.Combine(PersistentRoot, saveFileName));

        private void Awake()
        {
            Load();
        }

        private void Update()
        {
            if (persistenceScheduler == null) return;
            if (writesBlocked)
            {
                PersistenceStatus = "recovery_required";
                return;
            }
            persistenceScheduler.Tick(DateTime.UtcNow);
            PersistenceStatus = persistenceScheduler.Status;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) FlushPersistence();
        }

        private void OnApplicationQuit()
        {
            FlushPersistence();
        }

        public void Load()
        {
            try
            {
                EnsurePersistence();
                GameLoadResult loaded = repository.Load();
                if (loaded.state != GamePersistenceStates.Ready)
                {
                    SetPersistenceFailure(loaded.state, loaded.message);
                    return;
                }

                ProgressionImportResult import = progressionImporter.ImportOnce(SavePath, repository);
                if (import.state != GamePersistenceStates.NoSource && import.state != GamePersistenceStates.Imported &&
                    import.state != GamePersistenceStates.AlreadyImported)
                {
                    SetPersistenceFailure(import.state, import.message);
                    return;
                }

                loaded = repository.Load();
                if (loaded.state != GamePersistenceStates.Ready)
                {
                    SetPersistenceFailure(loaded.state, loaded.message);
                    return;
                }

                AlterOSProgressionState candidate = null;
                if (loaded.progressionJson == null)
                {
                    if (File.Exists(SavePath))
                    {
                        SetPersistenceFailure(GamePersistenceStates.RecoveryRequired,
                            "Legacy progression exists but was not imported; defaults were not published.");
                        return;
                    }
                    candidate = new AlterOSProgressionState();
                    candidate.altera.hydrationKnown = true;
                    EnsureState(candidate);
                    GameCommitResult initialized = CommitSnapshot(candidate, loaded.gameRevision,
                        Guid.NewGuid().ToString("D"), "new-game:" + ProgressionFingerprint(JsonUtility.ToJson(candidate)), false, null);
                    if (!initialized.IsDurable) return;
                    loaded = repository.Load();
                    if (loaded.state != GamePersistenceStates.Ready || loaded.progressionJson == null)
                    {
                        SetPersistenceFailure(GamePersistenceStates.RecoveryRequired,
                            "The new-game snapshot could not be reloaded after its commit.");
                        return;
                    }
                }

                if (loaded.progressionJson != null)
                {
                    int snapshotSchemaVersion;
                    if (!TryReadProgressionSchemaVersion(loaded.progressionJson, out snapshotSchemaVersion) ||
                        snapshotSchemaVersion < 0 || snapshotSchemaVersion > 4)
                    {
                        SetPersistenceFailure(GamePersistenceStates.RecoveryRequired,
                            "Progression data is malformed or uses a future schema; the existing bytes were preserved.");
                        return;
                    }
                    candidate = JsonUtility.FromJson<AlterOSProgressionState>(loaded.progressionJson);
                    if (candidate == null)
                    {
                        SetPersistenceFailure(GamePersistenceStates.RecoveryRequired,
                            "Progression data is malformed or uses a future schema; the existing bytes were preserved.");
                        return;
                    }
                    candidate.schemaVersion = snapshotSchemaVersion;
                    if (snapshotSchemaVersion == 4 && !HasCompleteSchema4Shape(loaded.progressionJson))
                    {
                        SetPersistenceFailure(GamePersistenceStates.RecoveryRequired,
                            "Schema-4 progression is incomplete or invalid; the existing snapshot bytes were preserved.");
                        return;
                    }
                    EnsureState(candidate);
                    string normalized = JsonUtility.ToJson(candidate);
                    if (!string.Equals(normalized, loaded.progressionJson, StringComparison.Ordinal))
                    {
                        string operationId = Guid.NewGuid().ToString("D");
                        string operationPayload = "progression-normalization:" + ProgressionFingerprint(normalized);
                        GameCommitResult normalizedResult = CommitSnapshot(candidate, loaded.gameRevision,
                            operationId, operationPayload, false, null);
                        if (!normalizedResult.IsDurable) return;
                        loaded.gameRevision = normalizedResult.receipt.gameRevision;
                    }
                }

                if (candidate == null)
                {
                    SetPersistenceFailure(GamePersistenceStates.RecoveryRequired,
                        "Progression did not produce a publishable state.");
                    return;
                }

                State = candidate;
                Identity = loaded.identity;
                DurableGameRevision = loaded.gameRevision;
                IsInitialized = true;
                writesBlocked = false;
                PersistenceStatus = "durable";
                persistenceScheduler.MarkCommitted(DateTime.UtcNow);
                NotifyChanged();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AlteraProgressionStore] Failed to load progression: {ex.Message}");
                SetPersistenceFailure(GamePersistenceStates.RecoveryRequired, ex.Message);
            }
        }

        public void Save()
        {
            if (!CanMutate || State == null) return;
            AlterOSProgressionState candidate = CloneState(State);
            string json = JsonUtility.ToJson(candidate);
            CommitSnapshot(candidate, DurableGameRevision, Guid.NewGuid().ToString("D"),
                "progression-save:" + ProgressionFingerprint(json), true, null, true);
        }

        private void EnsurePersistence()
        {
            if (repository != null) return;
            string root = PersistentRoot;
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string legacyPath = Path.GetFullPath(Path.Combine(root, saveFileName));
            if (!legacyPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The legacy progression path must stay under the trusted persistent root.");

            string dataRoot = Path.Combine(root, "Data");
            repository = new SqliteGameRepository(Path.Combine(dataRoot, "alteros-game.sqlite3"));
            backupService = new GameBackupService(repository, Path.Combine(dataRoot, "Backups"));
            progressionImporter = new ProgressionJsonImporter(backupService);
            persistenceScheduler = new GamePersistenceScheduler(PersistDirtySnapshot);
        }

        private string GetPersistentRoot()
        {
#if UNITY_EDITOR
            if (editorTestPersistenceRootRequested)
            {
                if (!string.IsNullOrWhiteSpace(editorSessionTestPersistenceRoot)) return editorSessionTestPersistenceRoot;
                throw new InvalidOperationException("The Editor test persistence root request is invalid or unavailable.");
            }
            if (!string.IsNullOrWhiteSpace(testPersistenceRoot)) return testPersistenceRoot;
#endif
            return Application.persistentDataPath;
        }

        private void FlushPersistence()
        {
            if (persistenceScheduler == null || writesBlocked) return;
            persistenceScheduler.Flush();
            PersistenceStatus = persistenceScheduler.Status;
        }

        private GameCommitResult PersistDirtySnapshot()
        {
            if (!CanMutate || State == null || repository == null)
                return new GameCommitResult { state = GamePersistenceStates.Unavailable, message = "Progression has not initialized." };
            AlterOSProgressionState candidate = CloneState(State);
            return CommitSnapshot(candidate, DurableGameRevision, Guid.NewGuid().ToString("D"),
                "progression-checkpoint:" + ProgressionFingerprint(JsonUtility.ToJson(candidate)), false, null);
        }

        private GameCommitResult CommitSnapshot(AlterOSProgressionState candidate, long expectedRevision,
            string operationId, string operationPayload, bool clearDirty, System.Collections.Generic.List<GameEventRecord> events,
            bool markDirtyOnFailure = false,
            System.Collections.Generic.List<GameEventHistoryContext> eventContexts = null,
            System.Collections.Generic.List<GameStatChangeRecord> statChanges = null)
        {
            if (repository == null || candidate == null)
                return new GameCommitResult { state = GamePersistenceStates.Unavailable, message = "Progression storage is unavailable." };

            candidate.lastUpdatedUtc = DateTime.UtcNow.ToString("O");
            var request = new GameCommitRequest
            {
                operationId = operationId,
                operationPayload = operationPayload,
                expectedGameRevision = expectedRevision,
                progressionJson = JsonUtility.ToJson(candidate),
                events = events ?? new System.Collections.Generic.List<GameEventRecord>(),
                eventContexts = eventContexts ?? new System.Collections.Generic.List<GameEventHistoryContext>(),
                statChanges = statChanges ?? new System.Collections.Generic.List<GameStatChangeRecord>()
            };
            GameCommitResult result = repository.Commit(request);
            if (result.state == GamePersistenceStates.Committed)
            {
                State = candidate;
                DurableGameRevision = result.receipt.gameRevision;
                writesBlocked = false;
                PersistenceStatus = "durable";
                if (clearDirty && persistenceScheduler != null)
                    persistenceScheduler.MarkCommitted(DateTime.UtcNow);
                if (IsInitialized) NotifyChanged();
                return result;
            }
            if (result.state == GamePersistenceStates.Duplicate)
            {
                GameLoadResult durable = repository.Load();
                if (durable.state == GamePersistenceStates.Ready && durable.progressionJson != null)
                {
                    AlterOSProgressionState durableState = JsonUtility.FromJson<AlterOSProgressionState>(durable.progressionJson);
                    if (durableState != null)
                    {
                        EnsureState(durableState);
                        State = durableState;
                        DurableGameRevision = durable.gameRevision;
                        PersistenceStatus = "durable";
                        if (clearDirty && persistenceScheduler != null)
                            persistenceScheduler.MarkCommitted(DateTime.UtcNow);
                        if (IsInitialized) NotifyChanged();
                    }
                    else
                    {
                        writesBlocked = true;
                        PersistenceStatus = "recovery_required";
                        return new GameCommitResult
                        {
                            state = GamePersistenceStates.RecoveryRequired,
                            errorCode = "duplicate_snapshot_invalid",
                            message = "The operation receipt exists but its progression snapshot is invalid."
                        };
                    }
                }
                else
                {
                    writesBlocked = true;
                    PersistenceStatus = "recovery_required";
                    return new GameCommitResult
                    {
                        state = GamePersistenceStates.RecoveryRequired,
                        errorCode = "duplicate_snapshot_missing",
                        message = "The operation receipt exists but its progression snapshot is missing."
                    };
                }
                return result;
            }

            PersistenceStatus = result.state == GamePersistenceStates.RecoveryRequired
                ? "recovery_required" : "unavailable";
            if (result.state == GamePersistenceStates.RecoveryRequired) writesBlocked = true;
            if (markDirtyOnFailure && persistenceScheduler != null) persistenceScheduler.MarkDirty();
            Debug.LogWarning($"[AlteraProgressionStore] Progression commit failed ({result.state}): {result.message}");
            return result;
        }

        private GameCommitResult MutateCandidate(string operationId, string operationPayload,
            Func<bool> mutation, List<GameEventRecord> events,
            List<GameEventHistoryContext> eventContexts = null, List<GameStatChangeRecord> statChanges = null)
        {
            if (!CanMutate || State == null || repository == null)
                return UnavailableResult("Progression has not initialized.");

            GameCommitReceipt existing = repository.FindReceipt(operationId);
            if (existing != null)
                // The SQL repository compares this stable legacy envelope with the receipt's
                // v1-compatible hash. No effect or diagnostic history is replayed here.
                return CommitSnapshot(CloneState(State), DurableGameRevision, operationId,
                    operationPayload, false, events);

            AlterOSProgressionState original = State;
            AlterOSProgressionState candidate = CloneState(original);
            State = candidate;
            bool changed;
            try
            {
                EnsureState(candidate);
                changed = mutation();
            }
            catch (Exception exception)
            {
                State = original;
                return new GameCommitResult
                {
                    state = GamePersistenceStates.Invalid,
                    errorCode = "progression_candidate_invalid",
                    message = exception.Message
                };
            }

            if (!changed)
            {
                State = original;
                return new GameCommitResult { state = GamePersistenceStates.Ready };
            }

            GameCommitResult result = CommitSnapshot(candidate, DurableGameRevision, operationId,
                operationPayload, true, events, false, eventContexts, statChanges);
            if (!result.IsDurable) State = original;
            return result;
        }

        private void MarkDirtyAndNotify()
        {
            if (persistenceScheduler != null)
            {
                persistenceScheduler.MarkDirty();
                PersistenceStatus = persistenceScheduler.Status;
            }
            else
            {
                PersistenceStatus = "dirty";
            }
            NotifyChanged();
        }

        private void SetPersistenceFailure(string state, string message)
        {
            PersistenceStatus = state == GamePersistenceStates.RecoveryRequired
                ? "recovery_required" : "unavailable";
            if (state == GamePersistenceStates.RecoveryRequired) writesBlocked = true;
            if (!IsInitialized) State = null;
            if (!string.IsNullOrEmpty(message))
                Debug.LogWarning($"[AlteraProgressionStore] Persistence is {PersistenceStatus}: {message}");
        }

        private static GameCommitResult UnavailableResult(string message)
        {
            return new GameCommitResult
            {
                state = GamePersistenceStates.Unavailable,
                errorCode = "progression_unavailable",
                message = message
            };
        }

        private static AlterOSProgressionState CloneState(AlterOSProgressionState state)
        {
            if (state == null) return null;
            AlterOSProgressionState clone = JsonUtility.FromJson<AlterOSProgressionState>(JsonUtility.ToJson(state));
            if (clone == null) throw new InvalidDataException("Progression candidate could not be cloned.");
            EnsureState(clone);
            return clone;
        }

        private static string ProgressionFingerprint(string json)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json ?? string.Empty));
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) result.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        public bool HasFlag(string key)
        {
            if (!IsInitialized || string.IsNullOrWhiteSpace(key) || State?.flags == null)
            {
                return false;
            }

            return State.flags.Exists(flag => flag != null && string.Equals(flag.key, key, StringComparison.OrdinalIgnoreCase));
        }

        public TriggerContext BuildTriggerContext()
        {
            if (!IsInitialized || State == null) return TriggerContext.CreateDefault();
            EnsureState();
            TriggerContext context = TriggerContext.CreateDefault();
            if (State?.altera != null)
            {
                context.bondLevel = State.altera.bondLevel;
                context.affinity = State.altera.affinity;
                context.trust = State.altera.trust;
                context.comfort = State.altera.comfort;
                context.energy = State.altera.energy;
                context.engagement = State.altera.engagement;
                context.stimulation = State.altera.stimulation;
                context.attention = State.altera.attention;
                context.reassurance = State.altera.reassurance;
                context.mood = State.altera.mood;
                context.availability = State.altera.availability;
            }

            if (State?.environment != null)
            {
                context.environmentComfort = State.environment.comfort;
                context.environmentCleanliness = State.environment.cleanliness;
                context.environmentCoziness = State.environment.coziness;
                context.environmentFocusAtmosphere = State.environment.focusAtmosphere;
                context.roomMood = State.environment.roomMood;
            }

            if (State?.flags != null)
            {
                foreach (ProgressionFlag flag in State.flags)
                {
                    if (flag != null && !string.IsNullOrWhiteSpace(flag.key))
                    {
                        context.flags.Add(flag.key);
                        context.flagValues.Add(new DialogueTag { key = flag.key, value = flag.value });
                    }
                }
            }

            if (State?.unlocks != null)
            {
                foreach (ProgressionFlag unlock in State.unlocks)
                {
                    if (unlock != null && !string.IsNullOrWhiteSpace(unlock.key))
                    {
                        context.unlocks.Add(new DialogueTag { key = unlock.key, value = unlock.value });
                    }
                }
            }

            PopulateTriggerStats(context);
            return context;
        }

        private void PopulateTriggerStats(TriggerContext context)
        {
            if (context == null)
            {
                return;
            }

            if (State?.altera != null)
            {
                context.SetStat("altera", "bond_level", State.altera.bondLevel);
                context.SetStat("altera", "bond_xp", State.altera.bondXp);
                context.SetStat("altera", "affinity", State.altera.affinity);
                context.SetStat("altera", "trust", State.altera.trust);
                context.SetStat("altera", "comfort", State.altera.comfort);
                context.SetStat("altera", "energy", State.altera.energy);
                float hydrationValue;
                if (TryGetHydration(out hydrationValue))
                    context.SetStat("altera", "hydration", hydrationValue);
                context.SetStat("altera", "engagement", State.altera.engagement);
                context.SetStat("altera", "stimulation", State.altera.stimulation);
                context.SetStat("altera", "attention", State.altera.attention);
                context.SetStat("altera", "reassurance", State.altera.reassurance);
            }

            if (State?.player != null)
            {
                context.SetStat("player", "credits", State.player.credits);
                context.SetStat("player", "completed_tasks", State.player.completedTasks);
                context.SetStat("player", "focus_sessions_completed", State.player.focusSessionsCompleted);
                context.SetStat("player", "streak", State.player.streak);
                context.SetStat("player", "productivity_xp", State.player.productivityXp);
                context.SetStat("player", "reliability", State.player.reliability);
                context.SetStat("player", "focus_energy", State.player.focusEnergy);
                context.SetStat("player", "stress", State.player.stress);
                context.SetStat("player", "rest", State.player.rest);
            }

            if (State?.environment != null)
            {
                context.SetStat("environment", "comfort", State.environment.comfort);
                context.SetStat("environment", "cleanliness", State.environment.cleanliness);
                context.SetStat("environment", "lighting", State.environment.lighting);
                context.SetStat("environment", "noise", State.environment.noise);
                context.SetStat("environment", "temperature", State.environment.temperature);
                context.SetStat("environment", "coziness", State.environment.coziness);
                context.SetStat("environment", "personalization", State.environment.personalization);
                context.SetStat("environment", "focus_atmosphere", State.environment.focusAtmosphere);
            }

            if (State?.customStats == null)
            {
                return;
            }

            foreach (CustomProgressionStat customStat in State.customStats)
            {
                if (customStat != null && !string.IsNullOrWhiteSpace(customStat.key))
                {
                    context.SetStat(customStat.target, customStat.key, customStat.value);
                }
            }
        }

        public void RefreshDerivedState(bool save = true)
        {
            if (!CanMutate || State == null) return;
            EnsureState();
            AlterOSProgressionState candidate = CloneState(State);
            StatMoodResolver.Resolve(candidate);
            if (save && saveImmediately)
            {
                CommitSnapshot(candidate, DurableGameRevision, Guid.NewGuid().ToString("D"),
                    "derived-state:" + ProgressionFingerprint(JsonUtility.ToJson(candidate)), true, null);
                return;
            }
            State = candidate;
            MarkDirtyAndNotify();
        }

        public float GetStat(string target, string stat, float fallback = 0f)
        {
            if (!IsInitialized || State == null) return fallback;
            EnsureState();
            return GetStatInternal(NormalizeTarget(target), NormalizeKey(stat), fallback);
        }

        public bool SetStat(string target, string stat, float value)
        {
            if (!CanMutate || State == null) return false;
            string normalizedTarget = NormalizeTarget(target);
            string normalizedStat = NormalizeKey(stat);
            if (!saveImmediately)
            {
                bool changed = TrySetStatInternal(normalizedTarget, normalizedStat, value);
                if (changed) MarkDirtyAndNotify();
                return changed;
            }
            GameCommitResult result = TrySetStat(Guid.NewGuid().ToString("D"), normalizedTarget, normalizedStat, value);
            return result.IsDurable;
        }

        public GameCommitResult TrySetStat(string operationId, string target, string stat, float value)
        {
            if (!CanMutate || State == null)
                return UnavailableResult("Progression has not initialized.");
            string normalizedTarget = NormalizeTarget(target);
            string normalizedStat = NormalizeKey(stat);
            string operationPayload = "set-stat:" + ProgressionFingerprint(normalizedTarget + ":" + normalizedStat + ":" +
                value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            string eventId = GameOperationId.FromStableKey(operationId + ":stat-set-event");
            float before = GetStatInternal(normalizedTarget, normalizedStat, float.NaN);
            List<GameEventRecord> events = new List<GameEventRecord>();
            List<GameEventHistoryContext> contexts = new List<GameEventHistoryContext>();
            List<GameStatChangeRecord> history = new List<GameStatChangeRecord>();
            string ownerKind, ownerId, statId;
            bool historyEligible = TryResolveHistoryStat(normalizedTarget, normalizedStat, out ownerKind, out ownerId, out statId);
            if (historyEligible)
            {
                string now = DateTime.UtcNow.ToString("O");
                events.Add(new GameEventRecord
                {
                    eventId = eventId, operationId = operationId, kind = "stat_set", actorId = "host",
                    targetId = statId, occurredAtUtc = now, recordedAtUtc = now, schemaVersion = 1, payloadJson = "{}"
                });
                contexts.Add(new GameEventHistoryContext
                {
                    eventId = eventId, causeKind = "source_operation", causeReferenceId = operationId
                });
            }
            return MutateCandidate(operationId, operationPayload,
                () =>
                {
                    bool changed = TrySetStatInternal(normalizedTarget, normalizedStat, value);
                    if (changed && historyEligible)
                    {
                        float after = GetStatInternal(normalizedTarget, normalizedStat, float.NaN);
                        if (!SameKnownValue(before, after))
                            history.Add(CreateStatHistory(operationId, eventId, 0, ownerKind, ownerId, statId, before, after, false));
                    }
                    return changed;
                }, events, contexts, history);
        }

        public bool TryGetHydration(out float value)
        {
            if (!IsInitialized || State == null)
            {
                value = 0f;
                return false;
            }
            EnsureState();
            value = State.altera.hydration;
            return State.altera.hydrationKnown && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 100f;
        }

        /// <summary>Applies a bounded, game-authored hydration checkpoint; unknown legacy hydration stays unknown.</summary>
        public bool TryApplyActivityHydrationCheckpoint(float amount)
        {
            return TryApplyActivityHydrationCheckpoint(Guid.NewGuid().ToString("D"), amount);
        }

        public bool TryApplyActivityHydrationCheckpoint(string operationId, float amount)
        {
            if (!CanMutate || State == null) return false;
            EnsureState();
            if (!State.altera.hydrationKnown || float.IsNaN(State.altera.hydration) || float.IsInfinity(State.altera.hydration) ||
                State.altera.hydration < 0f || State.altera.hydration > 100f || float.IsNaN(amount) ||
                float.IsInfinity(amount) || amount <= 0f || amount > 100f)
                return false;
            string operationPayload = "activity-hydration:" + amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            string now = DateTime.UtcNow.ToString("O");
            string eventId = GameOperationId.FromStableKey(operationId + ":activity-hydration-event");
            var events = new System.Collections.Generic.List<GameEventRecord>
            {
                new GameEventRecord
                {
                    eventId = eventId, operationId = operationId,
                    kind = "activity_hydration_checkpoint", actorId = Identity == null ? string.Empty : Identity.alterraId,
                    targetId = "hydration", occurredAtUtc = now, recordedAtUtc = now, schemaVersion = 1,
                    payloadJson = "{\"amount\":" + amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}"
                }
            };
            var contexts = new System.Collections.Generic.List<GameEventHistoryContext>
            {
                new GameEventHistoryContext { eventId = eventId, causeKind = "source_operation", causeReferenceId = operationId }
            };
            var history = new System.Collections.Generic.List<GameStatChangeRecord>();
            float before = State.altera.hydration;
            GameCommitResult result = MutateCandidate(operationId, operationPayload, () =>
            {
                State.altera.hydration = ClampMeter(State.altera.hydration + amount);
                float after = State.altera.hydration;
                history.Add(CreateStatHistory(operationId, eventId, 0, "alterra", Identity.alterraId,
                    "alterra.hydration", before, after, false));
                return true;
            }, events, contexts, history);
            return result.IsDurable;
        }

        public bool AddStat(string target, string stat, float amount)
        {
            string normalizedTarget = NormalizeTarget(target);
            string normalizedStat = NormalizeKey(stat);
            float current = GetStat(normalizedTarget, normalizedStat);
            return SetStat(normalizedTarget, normalizedStat, current + amount);
        }

        public PlayerTaskState AddTask(string label, string id = null, string source = "dialogue")
        {
            string operationId = Guid.NewGuid().ToString("D");
            return TryAddTask(operationId, label, id, source);
        }

        public PlayerTaskState TryAddTask(string operationId, string label, string id = null, string source = "dialogue")
        {
            if (!CanMutate || State == null || string.IsNullOrWhiteSpace(label)) return null;
            string stableTaskId = string.IsNullOrWhiteSpace(id) ? operationId : id.Trim();
            string operationPayload = "task-create:" + ProgressionFingerprint(label.Trim() + ":" + stableTaskId + ":" + (source ?? "dialogue"));
            PlayerTaskState created = null;
            GameCommitResult result = MutateCandidate(operationId, operationPayload, () =>
            {
                created = AddTaskInternal(label, stableTaskId, source);
                return created != null;
            }, null);
            if (!result.IsDurable || created == null || State == null || State.tasks == null) return null;
            return State.tasks.Find(task => task != null && task.id == stableTaskId);
        }

        public bool SetTaskCompleted(string id, bool completed)
        {
            return TrySetTaskCompleted(Guid.NewGuid().ToString("D"), id, completed).IsDurable;
        }

        public GameCommitResult TrySetTaskCompleted(string operationId, string id, bool completed)
        {
            if (!CanMutate || State == null || string.IsNullOrWhiteSpace(id))
                return UnavailableResult("Progression has not initialized or the task ID is missing.");
            string operationPayload = "task-complete:" + ProgressionFingerprint(id + ":" + completed);
            return MutateCandidate(operationId, operationPayload, () =>
            {
                if (State.tasks == null) return false;
                PlayerTaskState task = State.tasks.Find(candidate => candidate != null &&
                    string.Equals(candidate.id, id, StringComparison.OrdinalIgnoreCase));
                if (task == null || task.completed == completed) return false;
                task.completed = completed;
                task.completedUtc = completed ? DateTime.UtcNow.ToString("O") : string.Empty;
                return true;
            }, null);
        }

        public void ApplyStatChanges(List<StatDelta> statChanges)
        {
            ApplyStatChanges(statChanges, true);
        }

        public void ApplyStatChanges(List<StatDelta> statChanges, bool save)
        {
            if (!CanMutate || State == null || statChanges == null || statChanges.Count == 0) return;
            if (save && saveImmediately)
            {
                GameCommitResult result = TryApplyDialogueChanges(Guid.NewGuid().ToString("D"), statChanges, null);
                if (!result.IsDurable && result.state != GamePersistenceStates.Ready)
                    Debug.LogWarning($"[AlteraProgressionStore] Stat changes were not committed ({result.state}).");
                return;
            }

            AlterOSProgressionState candidate = CloneState(State);
            AlterOSProgressionState original = State;
            State = candidate;
            try
            {
                foreach (StatDelta delta in statChanges) ApplyStatChange(delta);
                State = candidate;
            }
            catch
            {
                State = original;
                throw;
            }
            MarkDirtyAndNotify();
        }

        public void ApplyEffects(List<DialogueEffect> effects)
        {
            if (effects == null || effects.Count == 0) return;
            GameCommitResult result = TryApplyDialogueChanges(Guid.NewGuid().ToString("D"), null, effects);
            if (!result.IsDurable && result.state != GamePersistenceStates.Ready)
                Debug.LogWarning($"[AlteraProgressionStore] Dialogue effects were not committed ({result.state}).");
        }

        public GameCommitResult TryApplyDialogueChanges(string operationId,
            List<StatDelta> statChanges, List<DialogueEffect> effects)
        {
            if (!CanMutate || State == null)
                return UnavailableResult("Progression has not initialized.");
            if ((statChanges == null || statChanges.Count == 0) && (effects == null || effects.Count == 0))
                return new GameCommitResult { state = GamePersistenceStates.Ready };
            string validationError;
            if (!ValidateDialogueMutation(statChanges, effects, out validationError))
            {
                return new GameCommitResult
                {
                    state = GamePersistenceStates.Invalid,
                    errorCode = "dialogue_effect_rejected",
                    message = validationError
                };
            }

            string operationPayload = "dialogue-effects:" + ProgressionFingerprint(JsonUtility.ToJson(new DialogueOperationPayload
            {
                statChanges = statChanges ?? new List<StatDelta>(),
                effects = effects ?? new List<DialogueEffect>()
            }));
            string now = DateTime.UtcNow.ToString("O");
            string eventId = GameOperationId.FromStableKey(operationId + ":dialogue-effects-event");
            var events = new System.Collections.Generic.List<GameEventRecord>
            {
                new GameEventRecord
                {
                    eventId = eventId, operationId = operationId,
                    kind = "dialogue_effects_applied", actorId = "host", targetId = "progression",
                    occurredAtUtc = now, recordedAtUtc = now, schemaVersion = 1,
                    payloadJson = "{\"statChangeCount\":" + (statChanges == null ? 0 : statChanges.Count) +
                        ",\"effectCount\":" + (effects == null ? 0 : effects.Count) + "}"
                }
            };
            var contexts = new System.Collections.Generic.List<GameEventHistoryContext>
            {
                new GameEventHistoryContext { eventId = eventId, causeKind = "source_operation", causeReferenceId = operationId }
            };
            var history = new System.Collections.Generic.List<GameStatChangeRecord>();
            return MutateCandidate(operationId, operationPayload, () =>
            {
                int changeIndex = 0;
                foreach (StatDelta delta in statChanges ?? new List<StatDelta>())
                {
                    string target = NormalizeTarget(delta.target);
                    string stat = NormalizeKey(delta.stat);
                    string ownerKind, ownerId, statId;
                    bool historyEligible = TryResolveHistoryStat(target, stat, out ownerKind, out ownerId, out statId);
                    float before = GetStatInternal(target, stat, float.NaN);
                    ApplyStatChange(delta);
                    float after = GetStatInternal(target, stat, float.NaN);
                    if (historyEligible && !SameKnownValue(before, after))
                        history.Add(CreateStatHistory(operationId, eventId, changeIndex, ownerKind, ownerId, statId, before, after, false));
                    changeIndex++;
                }
                foreach (DialogueEffect effect in effects ?? new List<DialogueEffect>()) ApplyEffect(effect);
                return true;
            }, events, contexts, history);
        }

        public GameCommitResult TryApplySimulationCheckpoint(string operationId, List<StatDelta> statChanges,
            string ruleId, string ruleVersion, DateTime intervalStartUtc, DateTime intervalEndUtc)
        {
            if (!CanMutate || State == null)
                return UnavailableResult("Progression has not initialized.");
            if (statChanges == null || statChanges.Count == 0 || statChanges.Count > 12 ||
                string.IsNullOrWhiteSpace(ruleId) || ruleId.Length > 128 || string.IsNullOrWhiteSpace(ruleVersion) || ruleVersion.Length > 64 ||
                intervalStartUtc.Kind != DateTimeKind.Utc || intervalEndUtc.Kind != DateTimeKind.Utc || intervalEndUtc <= intervalStartUtc)
                return new GameCommitResult { state = GamePersistenceStates.Invalid, errorCode = "invalid_simulation_checkpoint" };

            foreach (StatDelta delta in statChanges)
            {
                string ownerKind, ownerId, statId;
                if (delta == null || !string.Equals(delta.op ?? "add", "add", StringComparison.OrdinalIgnoreCase) ||
                    float.IsNaN(delta.amount) || float.IsInfinity(delta.amount) ||
                    !TryResolveHistoryStat(NormalizeTarget(delta.target), NormalizeKey(delta.stat), out ownerKind, out ownerId, out statId))
                    return new GameCommitResult { state = GamePersistenceStates.Invalid, errorCode = "invalid_simulation_stat" };
            }

            string start = intervalStartUtc.ToString("O");
            string end = intervalEndUtc.ToString("O");
            string operationPayload = "simulation-checkpoint:" + ProgressionFingerprint(JsonUtility.ToJson(
                new SimulationCheckpointPayload { ruleId = ruleId, ruleVersion = ruleVersion, intervalStartUtc = start,
                    intervalEndUtc = end, statChanges = statChanges }));
            string eventId = GameOperationId.FromStableKey(operationId + ":simulation-checkpoint-event");
            string now = intervalEndUtc.ToString("O");
            var events = new System.Collections.Generic.List<GameEventRecord>
            {
                new GameEventRecord
                {
                    eventId = eventId, operationId = operationId, kind = "simulation_checkpoint", actorId = "host",
                    targetId = ruleId, occurredAtUtc = now, recordedAtUtc = now, schemaVersion = 1, payloadJson = "{}"
                }
            };
            var contexts = new System.Collections.Generic.List<GameEventHistoryContext>
            {
                new GameEventHistoryContext
                {
                    eventId = eventId, causeKind = "simulation_checkpoint", simulationRuleId = ruleId,
                    simulationRuleVersion = ruleVersion, intervalStartUtc = start, intervalEndUtc = end
                }
            };
            var history = new System.Collections.Generic.List<GameStatChangeRecord>();
            return MutateCandidate(operationId, operationPayload, () =>
            {
                int index = 0;
                foreach (StatDelta delta in statChanges)
                {
                    string target = NormalizeTarget(delta.target);
                    string stat = NormalizeKey(delta.stat);
                    string ownerKind, ownerId, statId;
                    TryResolveHistoryStat(target, stat, out ownerKind, out ownerId, out statId);
                    float before = GetStatInternal(target, stat, float.NaN);
                    ApplyStatChange(delta);
                    float after = GetStatInternal(target, stat, float.NaN);
                    if (!SameKnownValue(before, after))
                        history.Add(CreateStatHistory(operationId, eventId, index, ownerKind, ownerId, statId, before, after, true));
                    index++;
                }
                StatMoodResolver.Resolve(State);
                return true;
            }, events, contexts, history);
        }

        /// <summary>Best-effort Scenario journal; failure is isolated from Scenario progression.</summary>
        public bool TryRecordScenarioHistoryObservation(ScenarioHistoryObservation observation)
        {
            if (!CanMutate || repository == null || observation == null ||
                string.IsNullOrWhiteSpace(observation.operationKey) || string.IsNullOrWhiteSpace(observation.runId) ||
                string.IsNullOrWhiteSpace(observation.kind) || !IsAllowedScenarioHistoryKind(observation.kind)) return false;

            try
            {
                string operationId = GameOperationId.FromStableKey("scenario-history:" + observation.operationKey);
                string eventId = GameOperationId.FromStableKey(operationId + ":event");
                string occurredAt = string.IsNullOrWhiteSpace(observation.occurredAtUtc)
                    ? DateTime.UtcNow.ToString("O") : observation.occurredAtUtc;
                string targetId = !string.IsNullOrEmpty(observation.answerOptionId) ? observation.answerOptionId :
                    !string.IsNullOrEmpty(observation.stepId) ? observation.stepId : observation.scenarioId;
                var events = new List<GameEventRecord>
                {
                    new GameEventRecord
                    {
                        eventId = eventId, operationId = operationId, kind = observation.kind,
                        actorId = observation.kind == "scenario_answer_accepted" ? "player" : "host",
                        targetId = targetId ?? string.Empty, occurredAtUtc = occurredAt, schemaVersion = 1, payloadJson = "{}"
                    }
                };
                var contexts = new List<GameEventHistoryContext>
                {
                    new GameEventHistoryContext
                    {
                        eventId = eventId, runId = observation.runId, invocationId = observation.invocationId,
                        stepExecutionId = observation.stepExecutionId, actionExecutionId = observation.actionExecutionId,
                        causeKind = "source_operation", causeReferenceId = operationId
                    }
                };
                var checkpoints = new List<GameActionCheckpointRecord>();
                if (observation.kind == "scenario_activity_result")
                {
                    string checkpointEventId = GameOperationId.FromStableKey(operationId + ":confirmed-checkpoint-event");
                    string actionOutcome = observation.outcome == "succeeded" ? "succeeded" : "failed";
                    events.Add(new GameEventRecord
                    {
                        eventId = checkpointEventId, operationId = operationId, kind = "action_checkpoint_confirmed",
                        actorId = "host", targetId = observation.actionExecutionId ?? string.Empty,
                        occurredAtUtc = occurredAt, schemaVersion = 1, payloadJson = "{}"
                    });
                    contexts.Add(new GameEventHistoryContext
                    {
                        eventId = checkpointEventId, runId = observation.runId, invocationId = observation.invocationId,
                        stepExecutionId = observation.stepExecutionId, actionExecutionId = observation.actionExecutionId,
                        causeKind = "source_operation", causeReferenceId = operationId
                    });
                    checkpoints.Add(new GameActionCheckpointRecord
                    {
                        checkpointId = operationId, eventId = checkpointEventId,
                        actionExecutionId = observation.actionExecutionId, outcome = actionOutcome, occurredAtUtc = occurredAt
                    });
                }
                var answers = new List<GameAnswerRecord>();
                if (observation.kind == "scenario_answer_accepted")
                    answers.Add(new GameAnswerRecord
                    {
                        answerId = operationId, eventId = eventId, stepExecutionId = observation.stepExecutionId,
                        answerKind = observation.answerKind, optionId = observation.answerKind == "option" ? observation.answerOptionId : null,
                        submittedAtUtc = occurredAt
                    });

                var scenarioRuns = new List<GameScenarioRunRecord>();
                if (observation.kind == "scenario_started")
                    scenarioRuns.Add(new GameScenarioRunRecord
                    { runId = observation.runId, scenarioId = observation.scenarioId, startedAtUtc = occurredAt });
                else if (observation.kind == "scenario_ended")
                    scenarioRuns.Add(new GameScenarioRunRecord
                    { runId = observation.runId, scenarioId = observation.scenarioId, endedAtUtc = occurredAt, outcome = observation.outcome });

                var flowInvocations = new List<GameFlowInvocationRecord>();
                if (observation.invocationStarted)
                    flowInvocations.Add(new GameFlowInvocationRecord
                    {
                        invocationId = observation.invocationId, runId = observation.runId,
                        parentInvocationId = observation.parentInvocationId, callExecutionId = observation.callExecutionId,
                        flowId = observation.flowId, flowStoreId = observation.flowStoreId,
                        flowRevision = observation.flowRevision, flowHash = observation.flowHash, startedAtUtc = occurredAt
                    });

                var stepExecutions = new List<GameStepExecutionRecord>();
                if (observation.kind == "scenario_step_presented" || observation.kind == "scenario_action_requested")
                    stepExecutions.Add(new GameStepExecutionRecord
                    {
                        executionId = observation.stepExecutionId, invocationId = observation.invocationId,
                        stepId = observation.stepId, visitOrdinal = observation.visitOrdinal, presentedAtUtc = occurredAt
                    });
                else if (observation.kind == "scenario_answer_accepted" || observation.kind == "scenario_activity_result" ||
                    observation.kind == "scenario_step_completed")
                    stepExecutions.Add(new GameStepExecutionRecord
                    {
                        executionId = observation.stepExecutionId, invocationId = observation.invocationId,
                        stepId = observation.stepId, visitOrdinal = observation.visitOrdinal,
                        outcome = observation.kind == "scenario_answer_accepted" ? "answered" :
                            observation.kind == "scenario_activity_result" ? "activity_" + observation.outcome : observation.outcome,
                        completedAtUtc = occurredAt
                    });

                var actionExecutions = new List<GameActionExecutionRecord>();
                if (observation.kind == "scenario_action_requested")
                    actionExecutions.Add(new GameActionExecutionRecord
                    {
                        actionExecutionId = observation.actionExecutionId, runId = observation.runId,
                        invocationId = observation.invocationId, stepExecutionId = observation.stepExecutionId,
                        capabilityId = observation.capabilityId, status = "requested", startedAtUtc = occurredAt
                    });
                else if (observation.kind == "scenario_activity_result")
                    actionExecutions.Add(new GameActionExecutionRecord
                    {
                        actionExecutionId = observation.actionExecutionId, runId = observation.runId,
                        invocationId = observation.invocationId, stepExecutionId = observation.stepExecutionId,
                        capabilityId = observation.capabilityId, status = observation.outcome,
                        endedAtUtc = occurredAt
                    });

                var payload = new ScenarioHistoryPayload
                {
                    kind = observation.kind, runId = observation.runId, scenarioId = observation.scenarioId,
                    invocationId = observation.invocationId, stepExecutionId = observation.stepExecutionId,
                    parentInvocationId = observation.parentInvocationId, callExecutionId = observation.callExecutionId,
                    flowId = observation.flowId, flowStoreId = observation.flowStoreId,
                    flowRevision = observation.flowRevision, flowHash = observation.flowHash, visitOrdinal = observation.visitOrdinal,
                    actionExecutionId = observation.actionExecutionId, capabilityId = observation.capabilityId,
                    invocationStarted = observation.invocationStarted, stepId = observation.stepId,
                    answerKind = observation.answerKind, answerOptionId = observation.answerOptionId, outcome = observation.outcome
                };
                AlterOSProgressionState candidate = CloneState(State);
                var request = new GameCommitRequest
                {
                    operationId = operationId, operationPayload = "scenario-history:" + ProgressionFingerprint(JsonUtility.ToJson(payload)),
                    expectedGameRevision = DurableGameRevision, progressionJson = JsonUtility.ToJson(candidate),
                    events = events, eventContexts = contexts, actionCheckpoints = checkpoints, answers = answers,
                    scenarioRuns = scenarioRuns, flowInvocations = flowInvocations, stepExecutions = stepExecutions,
                    actionExecutions = actionExecutions
                };
                GameCommitResult result = repository.Commit(request);
                if (result.state != GamePersistenceStates.Committed || result.receipt == null) return result.state == GamePersistenceStates.Duplicate;
                State = candidate;
                DurableGameRevision = result.receipt.gameRevision;
                PersistenceStatus = "durable";
                if (persistenceScheduler != null) persistenceScheduler.MarkCommitted(DateTime.UtcNow);
                return true;
            }
            catch { return false; }
        }

        private static bool IsAllowedScenarioHistoryKind(string kind)
        {
            return kind == "scenario_started" || kind == "scenario_step_presented" ||
                kind == "scenario_step_completed" || kind == "scenario_action_requested" ||
                kind == "scenario_answer_accepted" || kind == "scenario_activity_result" || kind == "scenario_ended";
        }

        private static bool ValidateDialogueMutation(List<StatDelta> statChanges,
            List<DialogueEffect> effects, out string error)
        {
            const int maximumChanges = 12;
            const float maximumAbsoluteDelta = 5f;
            error = string.Empty;
            int statCount = statChanges == null ? 0 : statChanges.Count;
            int effectCount = effects == null ? 0 : effects.Count;
            if (statCount + effectCount > maximumChanges)
            {
                error = "A dialogue operation exceeds the twelve-change host limit.";
                return false;
            }

            if (statChanges != null)
            {
                foreach (StatDelta delta in statChanges)
                {
                    if (delta == null || string.IsNullOrWhiteSpace(delta.target) || delta.target.Length > 32 ||
                        string.IsNullOrWhiteSpace(delta.stat) || delta.stat.Length > 64 ||
                        !string.IsNullOrEmpty(delta.op) && delta.op.Length > 16 ||
                        !IsAllowedDialogueStat(delta) ||
                        float.IsNaN(delta.amount) || float.IsInfinity(delta.amount) ||
                        Math.Abs((double)delta.amount) > maximumAbsoluteDelta ||
                        !string.IsNullOrWhiteSpace(delta.op) &&
                        !string.Equals(delta.op.Trim(), "add", StringComparison.OrdinalIgnoreCase))
                    {
                        error = "A dialogue stat change is unsupported or outside its bounded add-only contract.";
                        return false;
                    }
                }
            }

            if (effects != null)
            {
                foreach (DialogueEffect effect in effects)
                {
                    if (!IsAllowedDialogueEffect(effect))
                    {
                        error = "A dialogue effect is unsupported or exceeds its bounded host contract.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool IsAllowedDialogueStat(StatDelta delta)
        {
            string target = NormalizeTarget(delta.target);
            string stat = NormalizeKey(delta.stat);
            if (target == "altera")
            {
                return stat == "affinity" || stat == "trust" || stat == "bond_xp" || stat == "bondxp" ||
                    stat == "comfort" || stat == "energy" || stat == "engagement" || stat == "stimulation" ||
                    stat == "attention" || stat == "reassurance" || stat == "boundary_strain";
            }
            if (target == "player")
            {
                return stat == "productivity_xp" || stat == "productivityxp" || stat == "reliability" ||
                    stat == "focus_energy" || stat == "focusenergy" || stat == "stress" || stat == "rest" ||
                    stat == "taskprogress" || stat == "focusprogress";
            }
            if (target == "environment")
            {
                return stat == "comfort" || stat == "cleanliness" || stat == "lighting" || stat == "noise" ||
                    stat == "temperature" || stat == "coziness" || stat == "personalization" ||
                    stat == "focus_atmosphere" || stat == "focusatmosphere";
            }
            return false;
        }

        private static bool IsAllowedDialogueEffect(DialogueEffect effect)
        {
            if (effect == null || float.IsNaN(effect.amount) || float.IsInfinity(effect.amount) || effect.amount != 0f)
                return false;
            if (string.IsNullOrWhiteSpace(effect.type) || effect.type.Length > 32)
                return false;

            string type = (effect.type ?? string.Empty).Trim().ToLowerInvariant();
            if (type == "flag" || type == "unlock")
            {
                return IsBoundedToken(effect.key, 128) &&
                    (string.Equals(effect.value, "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(effect.value, "false", StringComparison.OrdinalIgnoreCase));
            }
            if (type == "task" || type == "task_create")
            {
                if (!string.IsNullOrEmpty(effect.key) && effect.key.Length > 128) return false;
                string label = string.IsNullOrWhiteSpace(effect.value) ? effect.key : effect.value;
                return !string.IsNullOrWhiteSpace(label) && label.Length <= 200;
            }
            if (type == "mood" || type == "availability" || type == "room_mood")
            {
                return !string.IsNullOrWhiteSpace(effect.value) && IsBoundedToken(effect.value, 48) &&
                    (string.IsNullOrEmpty(effect.key) || IsBoundedToken(effect.key, 64));
            }
            // Reward/currency and memory effects require their own game or memory owner.
            return false;
        }

        private static bool IsBoundedToken(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength) return false;
            foreach (char character in value)
            {
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.'))
                    return false;
            }
            return true;
        }

        private void ApplyStatChange(StatDelta delta)
        {
            if (delta == null || string.IsNullOrWhiteSpace(delta.stat))
            {
                return;
            }

            string target = NormalizeTarget(delta.target);
            string stat = NormalizeKey(delta.stat);
            float current = GetStatInternal(target, stat);
            float next = ApplyOperation(current, delta);
            TrySetStatInternal(target, stat, next);
        }

        private bool TryResolveHistoryStat(string target, string stat, out string ownerKind, out string ownerId, out string statId)
        {
            ownerKind = target == "altera" ? "alterra" : target;
            ownerId = ownerKind == "alterra" ? Identity?.alterraId :
                ownerKind == "environment" ? Identity?.homeId :
                ownerKind == "player" ? (Identity?.legacyPlayerId ?? Identity?.accountId ?? Identity?.saveId) : null;
            string canonical = stat ?? string.Empty;
            if (canonical == "bondxp") canonical = "bond_xp";
            else if (canonical == "bondlevel") canonical = "bond_level";
            else if (canonical == "completedtasks") canonical = "completed_tasks";
            else if (canonical == "focussessionscompleted") canonical = "focus_sessions_completed";
            else if (canonical == "productivityxp") canonical = "productivity_xp";
            else if (canonical == "focusenergy") canonical = "focus_energy";
            else if (canonical == "focusatmosphere") canonical = "focus_atmosphere";
            statId = ownerKind + "." + canonical;
            return ownerId != null && GameHistoryStatIds.IsSupported(ownerKind, statId);
        }

        private static GameStatChangeRecord CreateStatHistory(string operationId, string eventId, int index,
            string ownerKind, string ownerId, string statId, float before, float after, bool aggregated)
        {
            bool beforeKnown = IsFinite(before);
            bool afterKnown = IsFinite(after);
            return new GameStatChangeRecord
            {
                changeId = GameOperationId.FromStableKey(operationId + ":stat-change:" + index),
                eventId = eventId, ownerKind = ownerKind, ownerId = ownerId, statId = statId,
                beforeKnown = beforeKnown, beforeValue = beforeKnown ? (float?)before : null,
                afterKnown = afterKnown, afterValue = afterKnown ? (float?)after : null,
                appliedDelta = beforeKnown && afterKnown ? (float?)(after - before) : null,
                aggregated = aggregated
            };
        }

        private static bool SameKnownValue(float left, float right)
        {
            bool leftKnown = IsFinite(left), rightKnown = IsFinite(right);
            return leftKnown == rightKnown && (!leftKnown || Math.Abs((double)left - right) <= 0.000001);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ApplyEffect(DialogueEffect effect)
        {
            if (effect == null || string.IsNullOrWhiteSpace(effect.type))
            {
                return;
            }

            string type = effect.type.Trim().ToLowerInvariant();
            if (type == "flag")
            {
                if (string.IsNullOrWhiteSpace(effect.key))
                {
                    return;
                }

                Upsert(State.flags, effect.key, string.IsNullOrWhiteSpace(effect.value) ? "true" : effect.value);
            }
            else if (type == "unlock")
            {
                if (string.IsNullOrWhiteSpace(effect.key))
                {
                    return;
                }

                Upsert(State.unlocks, effect.key, string.IsNullOrWhiteSpace(effect.value) ? "true" : effect.value);
            }
            else if (type == "reward" && string.Equals(effect.key, "credits", StringComparison.OrdinalIgnoreCase))
            {
                State.player.credits = Mathf.Max(0, State.player.credits + Mathf.RoundToInt(effect.amount));
            }
            else if (type == "task" || type == "task_create")
            {
                string label = !string.IsNullOrWhiteSpace(effect.value) ? effect.value : effect.key;
                AddTaskInternal(label, null, "dialogue_effect");
            }
            else if (type == "mood")
            {
                State.altera.mood = effect.value;
            }
            else if (type == "availability")
            {
                State.altera.availability = string.IsNullOrWhiteSpace(effect.value) ? "present" : effect.value;
            }
            else if (type == "room_mood")
            {
                State.environment.roomMood = string.IsNullOrWhiteSpace(effect.value) ? "neutral" : effect.value;
            }
        }

        private void EnsureState()
        {
            EnsureState(State);
        }

        private static bool HasCompleteSchema4Shape(string json)
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), false))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ProgressionSchema4Shape));
                    ProgressionSchema4Shape shape = serializer.ReadObject(stream) as ProgressionSchema4Shape;
                    return shape != null && shape.schemaVersion == 4 && shape.altera != null &&
                        shape.player != null && shape.environment != null && shape.flags != null &&
                        shape.unlocks != null && shape.customStats != null && shape.tasks != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadProgressionSchemaVersion(string json, out int schemaVersion)
        {
            schemaVersion = -1;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), false))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ProgressionSchemaHeader));
                    ProgressionSchemaHeader header = serializer.ReadObject(stream) as ProgressionSchemaHeader;
                    if (header == null) return false;
                    schemaVersion = header.schemaVersion;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureState(AlterOSProgressionState state)
        {
            if (state == null) return;
            if (state.altera == null) state.altera = new AlteraStatsState();
            if (state.player == null) state.player = new PlayerProgressState();
            if (state.environment == null) state.environment = new EnvironmentStatsState();
            if (state.flags == null) state.flags = new List<ProgressionFlag>();
            if (state.unlocks == null) state.unlocks = new List<ProgressionFlag>();
            if (state.customStats == null) state.customStats = new List<CustomProgressionStat>();
            if (state.tasks == null) state.tasks = new List<PlayerTaskState>();

            if (string.IsNullOrWhiteSpace(state.altera.mood)) state.altera.mood = "calm";
            if (state.schemaVersion < 4) state.schemaVersion = 4;
            if (string.IsNullOrWhiteSpace(state.altera.availability)) state.altera.availability = "present";
            if (string.IsNullOrWhiteSpace(state.player.currentMode)) state.player.currentMode = "free";
            if (string.IsNullOrWhiteSpace(state.environment.timeOfDay)) state.environment.timeOfDay = "unknown";
            if (string.IsNullOrWhiteSpace(state.environment.weather)) state.environment.weather = "clear";
            if (string.IsNullOrWhiteSpace(state.environment.roomMood)) state.environment.roomMood = "neutral";
        }

        private PlayerTaskState AddTaskInternal(string label, string id, string source)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return null;
            }

            PlayerTaskState task = new PlayerTaskState
            {
                id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                label = label.Trim(),
                completed = false,
                source = string.IsNullOrWhiteSpace(source) ? "dialogue" : source.Trim(),
                createdUtc = DateTime.UtcNow.ToString("O"),
                completedUtc = string.Empty
            };

            State.tasks.Add(task);
            return task;
        }

        private static string NormalizeTarget(string target)
        {
            string normalized = NormalizeKey(target);
            if (string.IsNullOrEmpty(normalized))
            {
                return "altera";
            }

            if (normalized == "env" || normalized == "room")
            {
                return "environment";
            }

            return normalized;
        }

        private static string NormalizeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key)
                ? string.Empty
                : key.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        }

        private float GetStatInternal(string target, string stat, float fallback = 0f)
        {
            float missing = float.NaN;
            float known;
            if (target == "player")
            {
                known = GetPlayerStat(stat, missing);
                return float.IsNaN(known) ? GetCustomStat(target, stat, fallback) : known;
            }

            if (target == "environment")
            {
                known = GetEnvironmentStat(stat, missing);
                return float.IsNaN(known) ? GetCustomStat(target, stat, fallback) : known;
            }

            known = GetAlteraStat(stat, missing);
            return float.IsNaN(known) ? GetCustomStat(target, stat, fallback) : known;
        }

        private float GetAlteraStat(string stat, float fallback)
        {
            if (stat == "bond_level" || stat == "bondlevel") return State.altera.bondLevel;
            if (stat == "bond_xp" || stat == "bondxp") return State.altera.bondXp;
            if (stat == "affinity") return State.altera.affinity;
            if (stat == "trust") return State.altera.trust;
            if (stat == "comfort") return State.altera.comfort;
            if (stat == "energy") return State.altera.energy;
            if (stat == "hydration") return State.altera.hydrationKnown ? State.altera.hydration : fallback;
            if (stat == "engagement") return State.altera.engagement;
            if (stat == "stimulation") return State.altera.stimulation;
            if (stat == "attention") return State.altera.attention;
            if (stat == "reassurance") return State.altera.reassurance;
            return fallback;
        }

        private float GetPlayerStat(string stat, float fallback)
        {
            if (stat == "credits") return State.player.credits;
            if (stat == "completed_tasks" || stat == "completedtasks") return State.player.completedTasks;
            if (stat == "focus_sessions_completed" || stat == "focussessionscompleted") return State.player.focusSessionsCompleted;
            if (stat == "streak") return State.player.streak;
            if (stat == "productivity_xp" || stat == "productivityxp") return State.player.productivityXp;
            if (stat == "reliability") return State.player.reliability;
            if (stat == "focus_energy" || stat == "focusenergy") return State.player.focusEnergy;
            if (stat == "stress") return State.player.stress;
            if (stat == "rest") return State.player.rest;
            return fallback;
        }

        private float GetEnvironmentStat(string stat, float fallback)
        {
            if (stat == "comfort") return State.environment.comfort;
            if (stat == "cleanliness") return State.environment.cleanliness;
            if (stat == "lighting") return State.environment.lighting;
            if (stat == "noise") return State.environment.noise;
            if (stat == "temperature") return State.environment.temperature;
            if (stat == "coziness") return State.environment.coziness;
            if (stat == "personalization") return State.environment.personalization;
            if (stat == "focus_atmosphere" || stat == "focusatmosphere") return State.environment.focusAtmosphere;
            return fallback;
        }

        private float GetCustomStat(string target, string stat, float fallback)
        {
            if (State?.customStats == null || string.IsNullOrWhiteSpace(stat))
            {
                return fallback;
            }

            CustomProgressionStat existing = State.customStats.Find(item =>
                item != null &&
                string.Equals(NormalizeTarget(item.target), target, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeKey(item.key), stat, StringComparison.OrdinalIgnoreCase));
            return existing != null ? existing.value : fallback;
        }

        private bool TrySetStatInternal(string target, string stat, float value)
        {
            if (target == "player")
            {
                return TrySetPlayerStat(stat, value) || TrySetCustomStat(target, stat, value);
            }

            if (target == "environment")
            {
                return TrySetEnvironmentStat(stat, value) || TrySetCustomStat(target, stat, value);
            }

            return TrySetAlteraStat(stat, value) || TrySetCustomStat(target, stat, value);
        }

        private bool TrySetCustomStat(string target, string stat, float value)
        {
            if (string.IsNullOrWhiteSpace(stat))
            {
                return false;
            }

            if (State.customStats == null)
            {
                State.customStats = new List<CustomProgressionStat>();
            }

            CustomProgressionStat existing = State.customStats.Find(item =>
                item != null &&
                string.Equals(NormalizeTarget(item.target), target, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeKey(item.key), stat, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.value = value;
                return true;
            }

            State.customStats.Add(new CustomProgressionStat
            {
                target = target,
                key = stat,
                value = value
            });
            return true;
        }

        private bool TrySetAlteraStat(string stat, float value)
        {
            if (stat == "bond_level" || stat == "bondlevel")
            {
                State.altera.bondLevel = Mathf.Max(1, Mathf.RoundToInt(value));
                return true;
            }

            if (stat == "bond_xp" || stat == "bondxp") State.altera.bondXp = Mathf.Max(0f, value);
            else if (stat == "affinity") State.altera.affinity = Mathf.Max(0f, value);
            else if (stat == "trust") State.altera.trust = Mathf.Max(0f, value);
            else if (stat == "comfort") State.altera.comfort = ClampMeter(value);
            else if (stat == "energy") State.altera.energy = ClampMeter(value);
            else if (stat == "engagement") State.altera.engagement = ClampMeter(value);
            else if (stat == "stimulation") State.altera.stimulation = ClampMeter(value);
            else if (stat == "attention") State.altera.attention = ClampMeter(value);
            else if (stat == "reassurance") State.altera.reassurance = ClampMeter(value);
            else return false;

            return true;
        }

        private bool TrySetPlayerStat(string stat, float value)
        {
            if (stat == "credits") State.player.credits = Mathf.Max(0, Mathf.RoundToInt(value));
            else if (stat == "completed_tasks" || stat == "completedtasks") State.player.completedTasks = Mathf.Max(0, Mathf.RoundToInt(value));
            else if (stat == "focus_sessions_completed" || stat == "focussessionscompleted") State.player.focusSessionsCompleted = Mathf.Max(0, Mathf.RoundToInt(value));
            else if (stat == "streak") State.player.streak = Mathf.Max(0, Mathf.RoundToInt(value));
            else if (stat == "productivity_xp" || stat == "productivityxp") State.player.productivityXp = Mathf.Max(0f, value);
            else if (stat == "reliability") State.player.reliability = ClampMeter(value);
            else if (stat == "focus_energy" || stat == "focusenergy") State.player.focusEnergy = ClampMeter(value);
            else if (stat == "stress") State.player.stress = ClampMeter(value);
            else if (stat == "rest") State.player.rest = ClampMeter(value);
            else return false;

            return true;
        }

        private bool TrySetEnvironmentStat(string stat, float value)
        {
            if (stat == "comfort") State.environment.comfort = ClampMeter(value);
            else if (stat == "cleanliness") State.environment.cleanliness = ClampMeter(value);
            else if (stat == "lighting") State.environment.lighting = ClampMeter(value);
            else if (stat == "noise") State.environment.noise = ClampMeter(value);
            else if (stat == "temperature") State.environment.temperature = ClampMeter(value);
            else if (stat == "coziness") State.environment.coziness = ClampMeter(value);
            else if (stat == "personalization") State.environment.personalization = ClampMeter(value);
            else if (stat == "focus_atmosphere" || stat == "focusatmosphere") State.environment.focusAtmosphere = ClampMeter(value);
            else return false;

            return true;
        }

        private static float ClampMeter(float value)
        {
            return Mathf.Clamp(value, 0f, 100f);
        }

        private static void Upsert(List<ProgressionFlag> list, string key, string value)
        {
            ProgressionFlag existing = list.Find(flag => flag != null && string.Equals(flag.key, key, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.value = value;
            }
            else
            {
                list.Add(new ProgressionFlag { key = key, value = value });
            }
        }

        private static float ApplyOperation(float current, StatDelta delta)
        {
            string op = string.IsNullOrWhiteSpace(delta.op) ? "add" : delta.op.Trim().ToLowerInvariant();
            if (op == "set") return delta.amount;
            if (op == "min") return Mathf.Min(current, delta.amount);
            if (op == "max") return Mathf.Max(current, delta.amount);
            return current + delta.amount;
        }

        [DataContract]
        private sealed class ProgressionSchemaHeader
        {
            [DataMember(Name = "schemaVersion", IsRequired = false)] public int schemaVersion;
        }

        [DataContract]
        private sealed class ProgressionSchema4Shape
        {
            [DataMember(Name = "schemaVersion", IsRequired = true)] public int schemaVersion;
            [DataMember(Name = "lastUpdatedUtc", IsRequired = false)] public string lastUpdatedUtc;
            [DataMember(Name = "altera", IsRequired = true)] public ProgressionSchema4AlteraShape altera;
            [DataMember(Name = "player", IsRequired = true)] public ProgressionSchema4PlayerShape player;
            [DataMember(Name = "environment", IsRequired = true)] public ProgressionSchema4EnvironmentShape environment;
            [DataMember(Name = "flags", IsRequired = true)] public List<ProgressionSchema4FlagShape> flags;
            [DataMember(Name = "unlocks", IsRequired = true)] public List<ProgressionSchema4FlagShape> unlocks;
            [DataMember(Name = "customStats", IsRequired = true)] public List<ProgressionSchema4CustomStatShape> customStats;
            [DataMember(Name = "tasks", IsRequired = true)] public List<ProgressionSchema4TaskShape> tasks;
        }

        [DataContract]
        private sealed class ProgressionSchema4AlteraShape
        {
            [DataMember(Name = "bondLevel", IsRequired = true)] public int bondLevel;
            [DataMember(Name = "bondXp", IsRequired = true)] public float bondXp;
            [DataMember(Name = "affinity", IsRequired = true)] public float affinity;
            [DataMember(Name = "trust", IsRequired = true)] public float trust;
            [DataMember(Name = "comfort", IsRequired = true)] public float comfort;
            [DataMember(Name = "energy", IsRequired = true)] public float energy;
            [DataMember(Name = "hydration", IsRequired = false)] public float hydration;
            [DataMember(Name = "hydrationKnown", IsRequired = false)] public bool hydrationKnown;
            [DataMember(Name = "engagement", IsRequired = true)] public float engagement;
            [DataMember(Name = "stimulation", IsRequired = true)] public float stimulation;
            [DataMember(Name = "attention", IsRequired = true)] public float attention;
            [DataMember(Name = "reassurance", IsRequired = true)] public float reassurance;
            [DataMember(Name = "mood", IsRequired = true)] public string mood;
            [DataMember(Name = "availability", IsRequired = true)] public string availability;
        }

        [DataContract]
        private sealed class ProgressionSchema4PlayerShape
        {
            [DataMember(Name = "credits", IsRequired = true)] public int credits;
            [DataMember(Name = "completedTasks", IsRequired = true)] public int completedTasks;
            [DataMember(Name = "focusSessionsCompleted", IsRequired = true)] public int focusSessionsCompleted;
            [DataMember(Name = "streak", IsRequired = true)] public int streak;
            [DataMember(Name = "productivityXp", IsRequired = true)] public float productivityXp;
            [DataMember(Name = "reliability", IsRequired = true)] public float reliability;
            [DataMember(Name = "focusEnergy", IsRequired = true)] public float focusEnergy;
            [DataMember(Name = "stress", IsRequired = true)] public float stress;
            [DataMember(Name = "rest", IsRequired = true)] public float rest;
            [DataMember(Name = "currentMode", IsRequired = true)] public string currentMode;
        }

        [DataContract]
        private sealed class ProgressionSchema4EnvironmentShape
        {
            [DataMember(Name = "comfort", IsRequired = true)] public float comfort;
            [DataMember(Name = "cleanliness", IsRequired = true)] public float cleanliness;
            [DataMember(Name = "lighting", IsRequired = true)] public float lighting;
            [DataMember(Name = "noise", IsRequired = true)] public float noise;
            [DataMember(Name = "temperature", IsRequired = true)] public float temperature;
            [DataMember(Name = "coziness", IsRequired = true)] public float coziness;
            [DataMember(Name = "personalization", IsRequired = true)] public float personalization;
            [DataMember(Name = "focusAtmosphere", IsRequired = false)] public float focusAtmosphere;
            [DataMember(Name = "timeOfDay", IsRequired = true)] public string timeOfDay;
            [DataMember(Name = "weather", IsRequired = true)] public string weather;
            [DataMember(Name = "roomMood", IsRequired = true)] public string roomMood;
        }

        [DataContract]
        private sealed class ProgressionSchema4FlagShape
        {
            [DataMember(Name = "key", IsRequired = true)] public string key;
            [DataMember(Name = "value", IsRequired = true)] public string value;
        }

        [DataContract]
        private sealed class ProgressionSchema4CustomStatShape
        {
            [DataMember(Name = "target", IsRequired = true)] public string target;
            [DataMember(Name = "key", IsRequired = true)] public string key;
            [DataMember(Name = "value", IsRequired = true)] public float value;
        }

        [DataContract]
        private sealed class ProgressionSchema4TaskShape
        {
            [DataMember(Name = "id", IsRequired = true)] public string id;
            [DataMember(Name = "label", IsRequired = true)] public string label;
            [DataMember(Name = "completed", IsRequired = true)] public bool completed;
            [DataMember(Name = "source", IsRequired = true)] public string source;
            [DataMember(Name = "createdUtc", IsRequired = true)] public string createdUtc;
            [DataMember(Name = "completedUtc", IsRequired = true)] public string completedUtc;
        }

        [Serializable]
        private sealed class DialogueOperationPayload
        {
            public List<StatDelta> statChanges = new List<StatDelta>();
            public List<DialogueEffect> effects = new List<DialogueEffect>();
        }

        [Serializable]
        private sealed class SimulationCheckpointPayload
        {
            public string ruleId;
            public string ruleVersion;
            public string intervalStartUtc;
            public string intervalEndUtc;
            public List<StatDelta> statChanges = new List<StatDelta>();
        }

        [Serializable]
        private sealed class ScenarioHistoryPayload
        {
            public string kind;
            public string runId;
            public string scenarioId;
            public string invocationId;
            public string parentInvocationId;
            public string callExecutionId;
            public string flowId;
            public string flowStoreId;
            public string flowRevision;
            public string flowHash;
            public string stepExecutionId;
            public int visitOrdinal;
            public string actionExecutionId;
            public string capabilityId;
            public bool invocationStarted;
            public string stepId;
            public string answerKind;
            public string answerOptionId;
            public string outcome;
        }

        private void NotifyChanged()
        {
            Changed?.Invoke(State);
        }
    }
}
