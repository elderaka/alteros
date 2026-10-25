using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using AlterOS.Dialogue;
using AlterOS.Semantics;
using AlterOS.Stats;
using AlterOS.Topics;
using UnityEngine;

namespace AlterOS.Editor
{
    public sealed class TopicPlaygroundRouteAttempt
    {
        public string RouteId { get; internal set; }
        public AuthoredDialogueSelectionResult Selection { get; internal set; }
    }

    public sealed class TopicPlaygroundLifecycleEvent
    {
        public string Action { get; internal set; }
        public string NodeId { get; internal set; }
        public string Status { get; internal set; }
        public string TimeUtc { get; internal set; }
        public bool IsConsumed { get; internal set; }
        public bool RequiresSave { get; internal set; }
        public bool PendingSave { get; internal set; }
        public string PersistenceError { get; internal set; }
    }

    public sealed class TopicPlaygroundFamilyQueryResult
    {
        public string Query { get; internal set; }
        public string TopicFixtureSet { get; internal set; }
        public bool SemanticRoutingAttempted { get; internal set; }
        public string RoutingModeLabel { get; internal set; }
        public string FamilySelectionReason { get; internal set; }
        public string ScenarioSelectionReason { get; internal set; }
        public string SelectedFamilyId { get; internal set; }
        public TopicFamilySemanticMatch[] FamilyMatches { get; internal set; }
        public int LocalHour { get; internal set; }
        public List<SyntheticStatInput> SyntheticStats { get; } = new List<SyntheticStatInput>();
        public List<SyntheticFactInput> SyntheticFacts { get; } = new List<SyntheticFactInput>();
        public bool ObjectsComplete { get; internal set; }
        public List<SyntheticObjectInput> SyntheticObjects { get; } = new List<SyntheticObjectInput>();
        public DateTimeOffset SelectionTimeUtc { get; internal set; }
        public double Draw { get; internal set; }
        public TopicPlaygroundTopicRun TopicRun { get; internal set; }
        internal TopicPlaygroundTopicHarness Harness { get; set; }
        public TopicDefinition Family { get; internal set; }
        public TopicInstance Instance { get; internal set; }
        public DialoguePlayback Playback { get; internal set; }
        public float QueryDurationMs { get; internal set; }
        public string PersistenceStatus { get; internal set; }
        public bool HistoryDirty { get; internal set; }
        public bool PendingSave { get; internal set; }
        public List<TopicPlaygroundLifecycleEvent> LifecycleEvents { get; } = new List<TopicPlaygroundLifecycleEvent>();
        public List<CanonicalDialogueNode> Transcript { get; } = new List<CanonicalDialogueNode>();

        public CanonicalDialogueNode Choose(int choiceIndex)
        {
            CanonicalDialogueNode next = Harness.Choose(TopicRun, choiceIndex);
            if (next != null)
                Transcript.Add(next);
            return next;
        }

        public CanonicalDialogueNode Continue()
        {
            CanonicalDialogueNode next = Harness.Continue(TopicRun);
            if (next != null)
                Transcript.Add(next);
            return next;
        }
    }

    public sealed class TopicPlaygroundQueryResult
    {
        public string Query { get; internal set; }
        public int LocalHour { get; internal set; }
        public string FixtureSet { get; internal set; }
        public List<SyntheticStatInput> SyntheticStats { get; } = new List<SyntheticStatInput>();
        public DialogueRouteMatch[] Matches { get; internal set; }
        public TopicRouteDecision Decision { get; internal set; }
        public string FallbackReason { get; internal set; }
        public string SelectedRouteId { get; internal set; }
        public string SelectedPath { get; internal set; }
        public float QueryDurationMs { get; internal set; }
        public DialoguePlayback Playback { get; internal set; }
        public List<CanonicalDialogueNode> Transcript { get; } = new List<CanonicalDialogueNode>();
        public List<TopicPlaygroundRouteAttempt> Attempts { get; } = new List<TopicPlaygroundRouteAttempt>();

        public CanonicalDialogueNode Choose(int choiceIndex)
        {
            CanonicalDialogueNode next = Playback.Choose(choiceIndex);
            if (next != null)
                Transcript.Add(next);
            return next;
        }

        public CanonicalDialogueNode Continue()
        {
            CanonicalDialogueNode next = Playback.Continue();
            if (next != null)
                Transcript.Add(next);
            return next;
        }
    }

    public sealed class TopicPlaygroundSession : IDisposable
    {
        readonly GteSemanticQueryRunner m_Gte;
        readonly AuthoredDialogueRouteCatalog m_Catalog;
        readonly string m_DialogueRoot;
        readonly string m_ProjectRoot;
        readonly TopicPlaygroundModelFingerprint m_ModelFingerprint;
        readonly Dictionary<string, float[]> m_TopicAnchorEmbeddings = new Dictionary<string, float[]>(StringComparer.Ordinal);
        TopicPlaygroundTopicHarness m_TopicHarness = new TopicPlaygroundTopicHarness();
        string m_TopicScratchHistoryPath;
        string m_TopicPersistenceStatus = "Detached in-memory history; no save requested.";

        public TopicPlaygroundSession(GteSemanticQueryRunner gte)
        {
            m_Gte = gte ?? throw new ArgumentNullException(nameof(gte));
            m_DialogueRoot = Path.Combine(Application.streamingAssetsPath, "Dialogue");
            m_ProjectRoot = Directory.GetParent(Application.dataPath).FullName;
            string genericRoot = Path.Combine(m_DialogueRoot, "Generic");
            m_Catalog = AuthoredDialogueRouteCatalog.FromDirectory(genericRoot);
            if (m_Catalog.RouteCount == 0)
                throw new InvalidOperationException($"No authored Generic routes were found under '{genericRoot}'.");
            m_ModelFingerprint = new TopicPlaygroundModelFingerprint(Path.Combine(
                m_ProjectRoot, TopicPlaygroundModelFingerprint.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        public int RouteCount => m_Gte.RouteIndex.RouteCount;
        public int AnchorCount => m_Gte.RouteIndex.AnchorCount;
        public string Backend => m_Gte.Backend.ToString();
        public string ModelId => m_Gte.ModelId;
        public TopicPlaygroundModelFingerprintSnapshot ModelFingerprintSnapshot => m_ModelFingerprint.Snapshot;
        public string ModelFingerprint => ModelFingerprintSnapshot.Fingerprint;
        public string IndexFingerprint => m_Gte.IndexFingerprint;
        public string TopicScratchHistoryPath => m_TopicScratchHistoryPath ?? string.Empty;
        public string TopicPersistenceStatus => m_TopicPersistenceStatus;
        public bool TopicHistoryDirty => m_TopicHarness.History.IsDirty;
        public bool CanStartAnotherTopicOccurrence => m_TopicHarness.CanStartAnotherOccurrence;

        public void InvalidateModelFingerprint()
        {
            m_ModelFingerprint.Invalidate();
        }

        public IEnumerator QuerySteps(
            string text,
            TopicPlaygroundContext context,
            Action<TopicPlaygroundQueryResult> onReady,
            Action<string> onStage = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Enter a natural-language query first.", nameof(text));
            if (context == null)
                throw new ArgumentNullException(nameof(context), "Build synthetic context through TopicPlaygroundContextBuilder first.");
            if (onReady == null)
                throw new ArgumentNullException(nameof(onReady));

            string fixtureRoot = GetFixtureRoot(context.FixtureSet);
            if (!string.IsNullOrEmpty(context.FixtureSet) && !Directory.Exists(fixtureRoot))
                throw new DirectoryNotFoundException($"Synthetic fixture set '{context.FixtureSet}' was selected, but its directory does not exist: '{fixtureRoot}'.");

            Stopwatch timer = Stopwatch.StartNew();
            DialogueRouteMatch[] matches = null;
            IEnumerator ranking = m_Gte.RankSteps(
                text,
                TopicRoutePolicy.DefaultMinimumSimilarity,
                value => matches = value,
                onStage);
            try
            {
                while (ranking.MoveNext())
                    yield return null;
            }
            finally
            {
                (ranking as IDisposable)?.Dispose();
            }

            TopicRouteDecision decision = TopicRoutePolicy.Decide(
                matches,
                TopicRoutePolicy.DefaultMinimumSimilarity,
                TopicRoutePolicy.DefaultFallbackRouteId);

            var result = new TopicPlaygroundQueryResult
            {
                Query = text,
                LocalHour = context.LocalHour,
                FixtureSet = context.FixtureSet,
                Matches = matches,
                Decision = decision,
                SelectedRouteId = decision.SelectedRouteId,
                FallbackReason = decision.UsedFallback ? decision.Reason.ToString() : string.Empty,
            };
            result.SyntheticStats.AddRange(context.CopyStatInputs());

            AuthoredDialogueSelectionResult selection = SelectRoute(decision.SelectedRouteId, context);
            result.Attempts.Add(new TopicPlaygroundRouteAttempt
            {
                RouteId = decision.SelectedRouteId,
                Selection = selection,
            });

            if (selection.SelectedDialogue == null && !string.Equals(
                    decision.SelectedRouteId,
                    TopicRoutePolicy.DefaultFallbackRouteId,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.FallbackReason = $"No eligible authored variant for '{decision.SelectedRouteId}'.";
                result.SelectedRouteId = TopicRoutePolicy.DefaultFallbackRouteId;
                selection = SelectRoute(result.SelectedRouteId, context);
                result.Attempts.Add(new TopicPlaygroundRouteAttempt
                {
                    RouteId = result.SelectedRouteId,
                    Selection = selection,
                });
            }

            if (selection.SelectedDialogue == null)
                throw new InvalidOperationException($"No eligible fallback dialogue '{result.SelectedRouteId}' exists for synthetic local hour {context.LocalHour}.");

            result.SelectedPath = selection.SelectedPath;
            result.Playback = new DialoguePlayback(selection.SelectedDialogue);
            result.Transcript.Add(result.Playback.Current);
            timer.Stop();
            result.QueryDurationMs = (float)timer.Elapsed.TotalMilliseconds;
            onReady(result);
        }

        public List<TopicDefinition> LoadTopicFamilies(string fixtureSet)
        {
            string root = GetTopicFixtureRoot(fixtureSet);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"Synthetic Topic fixture set '{fixtureSet}' does not exist: '{root}'.");

            string[] paths = Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(paths, StringComparer.Ordinal);
            var families = new List<TopicDefinition>(paths.Length);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (path.StartsWith(m_DialogueRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Synthetic Topic fixtures cannot be loaded from StreamingAssets.");
                TopicDefinition family = JsonUtility.FromJson<TopicDefinition>(File.ReadAllText(path));
                List<string> errors = TopicDefinitionValidator.Validate(family);
                if (errors.Count > 0)
                    throw new InvalidDataException($"Invalid synthetic Topic family '{path}': {string.Join(" ", errors.ToArray())}");
                if (!ids.Add(family.topicId))
                    throw new InvalidDataException($"Duplicate Topic family ID '{family.topicId}' in fixture set '{fixtureSet}'.");
                families.Add(family);
            }
            if (families.Count == 0)
                throw new InvalidDataException($"Synthetic Topic fixture set '{fixtureSet}' contains no family JSON files.");
            return families;
        }

        public void ConfigureTopicHistoryScratch(string explicitPath)
        {
            if (string.IsNullOrWhiteSpace(explicitPath))
                throw new ArgumentException("Choose an explicit scratch history file path.", nameof(explicitPath));
            if (m_TopicHarness.History.IsDirty)
                throw new InvalidOperationException("Scratch history must be configured before starting a Topic occurrence.");

            string scratchRoot = GetTopicHistoryScratchRoot();
            if (ContainsReparsePoint(scratchRoot))
                throw new InvalidOperationException("Playground scratch persistence cannot use a temp path containing a symbolic link or junction: " + scratchRoot);
            Directory.CreateDirectory(scratchRoot);
            string fullPath = ValidateTopicHistoryScratchPath(explicitPath, scratchRoot);

            TopicHistoryLoadResult loaded = TopicHistoryRepository.Load(fullPath);
            if (!loaded.Success)
                throw new InvalidDataException("Scratch Topic history was not loaded; original file was preserved: " + loaded.Error);

            m_TopicHarness = new TopicPlaygroundTopicHarness(loaded.History);
            m_TopicScratchHistoryPath = fullPath;
            m_TopicPersistenceStatus = loaded.Missing
                ? "Scratch history path ready (new file; no player save involved)."
                : "Loaded explicit Playground scratch history (no player save involved).";
        }

        public IEnumerator QueryTopicFamilySteps(
            string text,
            TopicPlaygroundContext context,
            string topicFixtureSet,
            bool semanticRouting,
            string directFamilyId,
            string instanceId,
            string source,
            string replayOverride,
            double draw,
            DateTimeOffset selectionTimeUtc,
            Action<TopicPlaygroundFamilyQueryResult> onReady,
            Action<string> onStage = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Enter a natural-language query first.", nameof(text));
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (onReady == null)
                throw new ArgumentNullException(nameof(onReady));
            if (double.IsNaN(draw) || double.IsInfinity(draw) || draw < 0d || draw >= 1d)
                throw new ArgumentOutOfRangeException(nameof(draw), "Supply a finite draw from 0 inclusive to 1 exclusive.");

            Stopwatch timer = Stopwatch.StartNew();
            List<TopicDefinition> families = LoadTopicFamilies(topicFixtureSet);
            TopicDefinition selectedFamily = null;
            TopicFamilySemanticMatch[] matches = new TopicFamilySemanticMatch[0];

            if (semanticRouting)
            {
                float[] queryEmbedding = null;
                IEnumerator queryEncoding = m_Gte.EncodeSteps(text, value => queryEmbedding = value, onStage);
                try
                {
                    while (queryEncoding.MoveNext())
                        yield return null;
                }
                finally
                {
                    (queryEncoding as IDisposable)?.Dispose();
                }

                var vectors = new List<TopicFamilyAnchorVector>();
                foreach (TopicDefinition family in families)
                {
                    foreach (string anchor in family.anchors)
                    {
                        string key = family.topicId + "\n" + anchor;
                        float[] embedding;
                        if (!m_TopicAnchorEmbeddings.TryGetValue(key, out embedding))
                        {
                            IEnumerator anchorEncoding = m_Gte.EncodeSteps(anchor, value => embedding = value,
                                stage => onStage?.Invoke("Encoding shared Topic anchor: " + family.topicId));
                            try
                            {
                                while (anchorEncoding.MoveNext())
                                    yield return null;
                            }
                            finally
                            {
                                (anchorEncoding as IDisposable)?.Dispose();
                            }
                            m_TopicAnchorEmbeddings[key] = embedding;
                        }
                        vectors.Add(new TopicFamilyAnchorVector { TopicId = family.topicId, Anchor = anchor, Embedding = embedding });
                    }
                }

                matches = TopicFamilySemanticMatcher.Rank(queryEmbedding, vectors);
                if (matches.Length > 0 && matches[0].BestSimilarity >= TopicFamilySemanticMatcher.DefaultMinimumSimilarity &&
                    matches[0].SupportCount > 0)
                    selectedFamily = families.Find(family => family.topicId == matches[0].TopicId);
            }
            else
            {
                selectedFamily = families.Find(family => string.Equals(family.topicId, directFamilyId, StringComparison.Ordinal));
                if (selectedFamily == null)
                    throw new InvalidDataException($"Direct test family '{directFamilyId}' was not found in synthetic fixture set '{topicFixtureSet}'.");
            }

            var result = new TopicPlaygroundFamilyQueryResult
            {
                Query = text,
                TopicFixtureSet = topicFixtureSet,
                SemanticRoutingAttempted = semanticRouting,
                RoutingModeLabel = semanticRouting ? "Semantic Topic routing (shared anchors)" : "Direct family isolation test (semantic routing not run)",
                FamilySelectionReason = semanticRouting
                    ? selectedFamily == null ? "No family passed the semantic minimum and support check." : "Top shared-anchor family passed the semantic threshold."
                    : "Direct family selected for isolated scenario testing; this is not semantic-routing proof.",
                SelectedFamilyId = selectedFamily == null ? string.Empty : selectedFamily.topicId,
                FamilyMatches = matches,
                LocalHour = context.LocalHour,
                SelectionTimeUtc = selectionTimeUtc,
                Draw = draw,
            };
            result.SyntheticStats.AddRange(context.CopyStatInputs());
            result.SyntheticFacts.AddRange(context.CopyFactInputs());
            result.ObjectsComplete = context.ObjectsComplete;
            result.SyntheticObjects.AddRange(context.CopyObjectInputs());

            if (selectedFamily != null)
            {
                TopicInstance instance = TopicInstance.Create(
                    selectedFamily,
                    instanceId,
                    source,
                    string.IsNullOrWhiteSpace(replayOverride) ? null : replayOverride);
                TopicPlaygroundTopicRun topicRun = m_TopicHarness.Select(
                    selectedFamily, instance, TopicPlaygroundTopicContextAdapter.Create(context), selectionTimeUtc, draw);
                result.Family = selectedFamily;
                result.Instance = instance;
                result.TopicRun = topicRun;
                result.Harness = m_TopicHarness;
                result.ScenarioSelectionReason = topicRun.Selection.Reason;
                if (topicRun.Selection.SelectedScenario != null)
                {
                    result.Playback = topicRun.Playback;
                    result.Transcript.Add(result.Playback.Current);
                    PersistTopicHistory(result);
                }
                else
                {
                    CaptureTopicHistoryState(result);
                }
            }
            else
            {
                CaptureTopicHistoryState(result);
            }

            timer.Stop();
            result.QueryDurationMs = (float)timer.Elapsed.TotalMilliseconds;
            onReady(result);
        }

        public TopicPlaygroundLifecycleEvent AcknowledgeCurrentNode(TopicPlaygroundFamilyQueryResult result, DateTimeOffset atUtc)
        {
            if (result == null || result.TopicRun == null || result.Playback == null || result.Playback.Current == null)
                throw new InvalidOperationException("There is no current Topic node to acknowledge.");
            string nodeId = result.Playback.Current.id;
            TopicPlaybackResult outcome = m_TopicHarness.Present(result.TopicRun, nodeId, atUtc);
            return RecordTopicLifecycle(result, "present", nodeId, atUtc, outcome);
        }

        public TopicPlaygroundLifecycleEvent CompleteTopic(TopicPlaygroundFamilyQueryResult result, DateTimeOffset atUtc)
        {
            if (result == null || result.TopicRun == null || result.Playback == null || !result.Playback.IsComplete)
                throw new InvalidOperationException("Traverse beyond the terminal dialogue node before marking completion.");
            TopicPlaybackResult outcome = m_TopicHarness.Complete(result.TopicRun, atUtc);
            return RecordTopicLifecycle(result, "complete", string.Empty, atUtc, outcome);
        }

        public TopicPlaygroundLifecycleEvent CancelTopic(TopicPlaygroundFamilyQueryResult result, DateTimeOffset atUtc)
        {
            TopicPlaybackResult outcome = m_TopicHarness.Cancel(result == null ? null : result.TopicRun);
            return RecordTopicLifecycle(result, "cancel", string.Empty, atUtc, outcome);
        }

        public TopicPlaygroundLifecycleEvent FailTopic(TopicPlaygroundFamilyQueryResult result, DateTimeOffset atUtc)
        {
            TopicPlaybackResult outcome = m_TopicHarness.Fail(result == null ? null : result.TopicRun);
            return RecordTopicLifecycle(result, "fail", string.Empty, atUtc, outcome);
        }

        public IEnumerator ExecuteFamilyScenarioStep(
            TopicPlaygroundFamilyQueryResult current,
            TopicPlaygroundScenarioStep step,
            TopicPlaygroundScenario scenario,
            TopicPlaygroundContext context,
            DateTimeOffset scenarioBaseTimeUtc,
            Action<TopicPlaygroundFamilyQueryResult> onResult,
            Action<string> onStage = null)
        {
            if (current == null || scenario == null || context == null)
                throw new ArgumentNullException("Family scenario step requires a current result, scenario, and detached context.");
            if (step == null)
                throw new InvalidDataException("Scenario steps cannot contain null entries.");
            if (double.IsNaN(step.secondsAfterStart) || double.IsInfinity(step.secondsAfterStart) || step.secondsAfterStart < 0d)
                throw new InvalidDataException("secondsAfterStart must be finite and nonnegative.");

            DateTimeOffset atUtc;
            try { atUtc = scenarioBaseTimeUtc.AddSeconds(step.secondsAfterStart); }
            catch (ArgumentOutOfRangeException) { throw new InvalidDataException("Scenario step time is outside the supported date range."); }

            string action = (step.action ?? string.Empty).Trim().ToLowerInvariant();
            switch (action)
            {
                case "choose": current.Choose(step.index); yield break;
                case "continue": current.Continue(); yield break;
                case "present": AcknowledgeCurrentNode(current, atUtc); yield break;
                case "complete": CompleteTopic(current, atUtc); yield break;
                case "cancel": CancelTopic(current, atUtc); yield break;
                case "fail": FailTopic(current, atUtc); yield break;
                case "retry": case "reselect":
                    if (current.Instance == null)
                        throw new InvalidDataException("Cannot retry because semantic family routing selected no family.");
                    break;
                case "newoccurrence":
                    if (current.Instance == null || string.IsNullOrWhiteSpace(step.instanceId) ||
                        string.Equals(step.instanceId, current.Instance.instanceId, StringComparison.Ordinal))
                        throw new InvalidDataException("newOccurrence requires a different nonempty instanceId.");
                    break;
                default:
                    throw new InvalidDataException("Unsupported family scenario action '" + step.action + "'.");
            }

            string instanceId = action == "newoccurrence" ? step.instanceId : current.Instance.instanceId;
            IEnumerator query = QueryTopicFamilySteps(
                scenario.query,
                context,
                scenario.topicFixtureSet,
                string.Equals(scenario.topicMode, "semantic-family", StringComparison.Ordinal),
                scenario.topicFamilyId,
                instanceId,
                scenario.source,
                scenario.replayPolicy,
                scenario.draw,
                atUtc,
                onResult,
                onStage);
            try
            {
                while (query.MoveNext())
                    yield return null;
            }
            finally
            {
                (query as IDisposable)?.Dispose();
            }
        }

        TopicPlaygroundLifecycleEvent RecordTopicLifecycle(
            TopicPlaygroundFamilyQueryResult result,
            string action,
            string nodeId,
            DateTimeOffset atUtc,
            TopicPlaybackResult outcome)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            string persistenceError = PersistTopicHistory(result);
            var trace = new TopicPlaygroundLifecycleEvent
            {
                Action = action,
                NodeId = nodeId,
                Status = outcome.Status,
                TimeUtc = atUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                IsConsumed = outcome.IsConsumed,
                RequiresSave = outcome.RequiresSave,
                PendingSave = !string.IsNullOrEmpty(m_TopicScratchHistoryPath) && m_TopicHarness.History.IsDirty,
                PersistenceError = persistenceError,
            };
            result.LifecycleEvents.Add(trace);
            CaptureTopicHistoryState(result);
            return trace;
        }

        string PersistTopicHistory(TopicPlaygroundFamilyQueryResult result)
        {
            if (!string.IsNullOrEmpty(m_TopicScratchHistoryPath) && m_TopicHarness.History.IsDirty)
            {
                bool saveSucceeded = false;
                string saveError = null;
                try
                {
                    ValidateTopicHistoryScratchPath(m_TopicScratchHistoryPath, GetTopicHistoryScratchRoot());
                    TopicHistorySaveResult save = TopicHistoryRepository.Save(m_TopicScratchHistoryPath, m_TopicHarness.History);
                    saveSucceeded = save.Success;
                    saveError = save.Error;
                }
                catch (Exception exception)
                {
                    saveError = exception.Message;
                }
                if (saveSucceeded)
                    m_TopicPersistenceStatus = "Saved to explicit scratch file: " + m_TopicScratchHistoryPath;
                else
                    m_TopicPersistenceStatus = "PENDING SAVE (not durable): " + saveError;
                CaptureTopicHistoryState(result);
                return saveSucceeded ? null : saveError;
            }

            CaptureTopicHistoryState(result);
            return null;
        }

        static string GetTopicHistoryScratchRoot()
        {
            return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AlterOS-TopicPlayground"));
        }

        static string ValidateTopicHistoryScratchPath(string explicitPath, string scratchRoot)
        {
            string fullPath = Path.GetFullPath(explicitPath);
            string fullRoot = Path.GetFullPath(scratchRoot);
            string prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Directory.Exists(fullPath))
                throw new InvalidOperationException("Playground persistence is restricted to an explicit file below the dedicated temp scratch folder: " + fullRoot);
            if (ContainsReparsePoint(fullRoot) || ContainsReparsePoint(fullPath))
                throw new InvalidOperationException("Playground scratch persistence cannot read or write through a symbolic link or junction: " + fullPath);
            return fullPath;
        }

        static bool ContainsReparsePoint(string fullPath)
        {
            string normalized = Path.GetFullPath(fullPath);
            string current = Path.GetPathRoot(normalized);
            if (string.IsNullOrEmpty(current))
                throw new InvalidOperationException("Scratch path has no filesystem root: " + normalized);

            if (ExistingPathIsReparsePoint(current))
                return true;

            string relative = normalized.Substring(current.Length);
            string[] components = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string component in components)
            {
                current = Path.Combine(current, component);
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        return true;
                }
                catch (FileNotFoundException)
                {
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    break;
                }
            }

            return false;
        }

        static bool ExistingPathIsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }

        void CaptureTopicHistoryState(TopicPlaygroundFamilyQueryResult result)
        {
            result.PersistenceStatus = string.IsNullOrEmpty(m_TopicScratchHistoryPath)
                ? "Detached in-memory only; no player save is read or written."
                : m_TopicPersistenceStatus;
            result.HistoryDirty = m_TopicHarness.History.IsDirty;
            result.PendingSave = !string.IsNullOrEmpty(m_TopicScratchHistoryPath) && result.HistoryDirty;
        }

        AuthoredDialogueSelectionResult SelectRoute(string routeId, TopicPlaygroundContext context)
        {
            var candidates = new List<AuthoredDialogueVariantCandidate>();
            string fixtureRouteRoot = string.IsNullOrEmpty(context.FixtureSet)
                ? null
                : Path.Combine(GetFixtureRoot(context.FixtureSet), routeId);
            string[] fixtureFiles = fixtureRouteRoot != null && Directory.Exists(fixtureRouteRoot)
                ? Directory.GetFiles(fixtureRouteRoot, "*.json", SearchOption.TopDirectoryOnly)
                : new string[0];

            if (fixtureFiles.Length > 0)
            {
                Array.Sort(fixtureFiles, StringComparer.Ordinal);
                foreach (string fullPath in fixtureFiles)
                {
                    string relativePath = "SYNTHETIC FIXTURE " + context.FixtureSet + "/Generic/" + routeId + "/" + Path.GetFileName(fullPath);
                    candidates.Add(new AuthoredDialogueVariantCandidate(relativePath, File.ReadAllText(fullPath)));
                }
            }
            else
            {
                foreach (string relativePath in m_Catalog.GetSequencePaths(routeId))
                {
                    string fullPath = Path.Combine(m_DialogueRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    string json = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
                    candidates.Add(new AuthoredDialogueVariantCandidate(relativePath, json));
                }
            }

            return AuthoredDialogueVariantSelector.Select(routeId, candidates, context.LocalHour, context.CopyStats());
        }

        string GetFixtureRoot(string fixtureSet)
        {
            return Path.Combine(m_ProjectRoot, "Tools", "TopicPlayground", "Fixtures", fixtureSet, "Generic");
        }

        string GetTopicFixtureRoot(string fixtureSet)
        {
            if (string.IsNullOrWhiteSpace(fixtureSet) ||
                fixtureSet.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                fixtureSet.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                fixtureSet.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                fixtureSet == "." || fixtureSet == "..")
                throw new ArgumentException("Topic fixture set must be a single safe folder name.", nameof(fixtureSet));
            return Path.Combine(m_ProjectRoot, "Tools", "TopicPlayground", "Fixtures", fixtureSet, "Topics");
        }

        public void Dispose()
        {
            m_ModelFingerprint.Dispose();
            m_Gte.Dispose();
        }
    }
}
