using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AlterOS.Recording
{
    /// <summary>Consent-bound local capture. Constructing it does not open or create a database.</summary>
    public sealed class InteractionRecorder
    {
        private readonly object gate = new object();
        private readonly string databasePath;
        private readonly Func<DateTime> utcNow;
        private RecordingRepository repository;
        private RecordingPolicy activePolicy;
        private string sessionId = string.Empty;

        public string DatabasePath { get { return databasePath; } }
        public bool IsEnabled { get { lock (gate) return repository != null && !string.IsNullOrEmpty(sessionId); } }
        public string CurrentSessionId { get { lock (gate) return sessionId; } }
        public ContributionPreview Contributions { get { return new ContributionPreview(this); } }

        public InteractionRecorder(string databasePath, Func<DateTime> utcNow = null)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A private recording database path is required.", "databasePath");
            this.databasePath = Path.GetFullPath(databasePath);
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public RecordingResult Enable(RecordingPolicy policy)
        {
            lock (gate)
            {
                if (repository != null && !string.IsNullOrEmpty(sessionId))
                    return new RecordingResult { state = "already_enabled", sessionId = sessionId };
                string error;
                if (!ValidatePolicy(policy, out error))
                    return new RecordingResult { state = "invalid", errorCode = "recording_policy_invalid", message = error };
                try
                {
                    var candidate = new RecordingRepository(databasePath);
                    DateTime now = AsUtc(utcNow());
                    candidate.PruneExpired(now);
                    string newSessionId = candidate.BeginSession(CopyPolicy(policy), now);
                    repository = candidate;
                    activePolicy = CopyPolicy(policy);
                    sessionId = newSessionId;
                    return new RecordingResult { state = "enabled", sessionId = sessionId };
                }
                catch (Exception exception)
                {
                    repository = null;
                    activePolicy = null;
                    sessionId = string.Empty;
                    return new RecordingResult { state = "unavailable", errorCode = "recording_store_unavailable", message = SafeMessage(exception) };
                }
            }
        }

        public RecordingResult Disable()
        {
            lock (gate)
            {
                if (repository == null || string.IsNullOrEmpty(sessionId))
                    return new RecordingResult { state = "disabled" };
                RecordingRepository endingRepository = repository;
                string endingSessionId = sessionId;
                repository = null;
                activePolicy = null;
                sessionId = string.Empty;
                try
                {
                    endingRepository.EndSession(endingSessionId, AsUtc(utcNow()));
                    return new RecordingResult { state = "disabled" };
                }
                catch (Exception exception)
                {
                    return new RecordingResult { state = "unavailable", errorCode = "recording_disable_failed", message = SafeMessage(exception) };
                }
            }
        }

        public RecordingResult Record(InteractionObservation observation)
        {
            lock (gate)
            {
                if (repository == null || activePolicy == null || string.IsNullOrEmpty(sessionId))
                    return new RecordingResult { state = "disabled", errorCode = "recording_disabled" };
                string error;
                if (!ValidateObservation(observation, activePolicy, AsUtc(utcNow()), out error))
                    return new RecordingResult
                    {
                        state = error == "Raw text is outside the active recording scope." ||
                            error == "Metadata is outside the active recording scope." ? "scope_rejected" : "invalid",
                        errorCode = "interaction_observation_invalid", message = error
                    };
                try
                {
                    DateTime occurredAt = string.IsNullOrWhiteSpace(observation.occurredAtUtc)
                        ? AsUtc(utcNow()) : ParseUtc(observation.occurredAtUtc);
                    DateTime capturedAt = AsUtc(utcNow());
                    repository.PruneExpired(capturedAt);
                    string metadataJson = activePolicy.captureMetadata ? EncodeMetadata(observation.metadata) : "{}";
                    return repository.Record(sessionId, activePolicy, observation, metadataJson, occurredAt, capturedAt);
                }
                catch (Exception exception)
                {
                    return new RecordingResult { state = "unavailable", errorCode = "record_write_failed", message = SafeMessage(exception) };
                }
            }
        }

        public RecordingResult DeleteRecords(IEnumerable<string> recordIds)
        {
            string[] ids = (recordIds ?? Enumerable.Empty<string>()).ToArray();
            if (ids.Length == 0) return new RecordingResult { state = "deleted" };
            lock (gate)
            {
                if (!File.Exists(databasePath)) return new RecordingResult { state = "deleted" };
                try
                {
                    var candidate = repository ?? new RecordingRepository(databasePath);
                    int deleted = candidate.DeleteRecords(ids);
                    return new RecordingResult { state = "deleted", message = deleted.ToString(CultureInfo.InvariantCulture) };
                }
                catch (Exception exception)
                {
                    return new RecordingResult { state = "unavailable", errorCode = "record_delete_failed", message = SafeMessage(exception) };
                }
            }
        }

        internal object Gate { get { return gate; } }
        internal RecordingRepository ActiveRepository { get { return repository; } }
        internal RecordingPolicy ActivePolicy { get { return activePolicy; } }
        internal string ActiveSessionId { get { return sessionId; } }
        internal DateTime NowUtc { get { return AsUtc(utcNow()); } }

        private static bool ValidatePolicy(RecordingPolicy policy, out string error)
        {
            error = string.Empty;
            if (policy == null || string.IsNullOrWhiteSpace(policy.policyRevision) || policy.policyRevision.Length > 64 ||
                (!policy.captureMetadata && !policy.captureRawText) || policy.retentionDays < 1 || policy.retentionDays > 30 ||
                policy.byteCap < 1 || policy.byteCap > 33554432 || policy.recordCap < 1 || policy.recordCap > 10000)
            {
                error = "Recording requires an explicit field scope, revision, 1-30 day retention, 1-32 MiB byte cap, and 1-10,000 record cap.";
                return false;
            }
            return true;
        }

        private static bool ValidateObservation(InteractionObservation observation, RecordingPolicy policy, DateTime nowUtc, out string error)
        {
            error = string.Empty;
            if (observation == null || !AllowedFields.TryGetValue(observation.kind ?? string.Empty, out HashSet<string> fields))
            { error = "The observation kind is unknown."; return false; }
            if (!string.IsNullOrEmpty(observation.sourceEventId) && observation.sourceEventId.Length > 128)
            { error = "The source event ID exceeds its bound."; return false; }
            if (!string.IsNullOrEmpty(observation.sourceEventId) && !policy.captureMetadata)
            { error = "Source event IDs are outside the active metadata scope."; return false; }
            if (!string.IsNullOrEmpty(observation.rawText))
            {
                if (!policy.captureRawText) { error = "Raw text is outside the active recording scope."; return false; }
                if (observation.kind != "submitted_input" || observation.rawText.Length > 2048)
                { error = "Raw text is only permitted for accepted submitted input within 2,048 characters."; return false; }
            }
            Dictionary<string, string> metadata = observation.metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
            if (metadata.Count > 24) { error = "The observation contains too many metadata fields."; return false; }
            if (metadata.Count > 0 && !policy.captureMetadata)
            { error = "Metadata is outside the active recording scope."; return false; }
            foreach (KeyValuePair<string, string> field in metadata)
            {
                if (!fields.Contains(field.Key) || IsSecretsField(field.Key) || string.IsNullOrEmpty(field.Value) || field.Value.Length > 1024)
                { error = "The observation contains an unknown, secret-like, empty, or oversized metadata field."; return false; }
            }
            int recordBytes = Encoding.UTF8.GetByteCount(EncodeMetadata(metadata)) +
                Encoding.UTF8.GetByteCount(observation.rawText ?? string.Empty) +
                Encoding.UTF8.GetByteCount(observation.kind ?? string.Empty) +
                Encoding.UTF8.GetByteCount(observation.sourceEventId ?? string.Empty);
            if (recordBytes > 16384)
            { error = "The complete observation exceeds its 16 KiB record bound."; return false; }
            if (!string.IsNullOrWhiteSpace(observation.occurredAtUtc))
            {
                DateTime occurred;
                if (!TryParseUtc(observation.occurredAtUtc, out occurred) || occurred > nowUtc)
                { error = "Observation time must be a valid non-future UTC timestamp."; return false; }
            }
            return true;
        }

        private static readonly Dictionary<string, HashSet<string>> AllowedFields = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            { "submitted_input", Set("scenarioId", "stepId", "inputKind") },
            { "presented_options", Set("scenarioId", "stepId", "optionIds", "availability", "order") },
            { "answer_selected", Set("scenarioId", "stepId", "answerId") },
            { "beat_presented", Set("scenarioId", "stepId", "beatId", "index") },
            { "beat_skipped", Set("scenarioId", "stepId", "beatId", "index") },
            { "routing_decision", Set("routeId", "version", "score", "reason") },
            { "action_result", Set("actionId", "capabilityId", "outcome") },
            { "feedback", Set("scenarioId", "stepId", "kind") }
        };

        private static HashSet<string> Set(params string[] values) { return new HashSet<string>(values, StringComparer.Ordinal); }

        private static bool IsSecretsField(string value)
        {
            string key = (value ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            return key.Contains("password") || key.Contains("secret") || key.Contains("token") ||
                key.Contains("credential") || key.Contains("apikey") || key.Contains("authorization") ||
                key.Contains("cookie") || key.Contains("keystroke");
        }

        private static string EncodeMetadata(IDictionary<string, string> metadata)
        {
            if (metadata == null || metadata.Count == 0) return "{}";
            return "{" + string.Join(",", metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => RecordingRepository.JsonString(pair.Key) + ":" + RecordingRepository.JsonString(pair.Value)).ToArray()) + "}";
        }

        private static RecordingPolicy CopyPolicy(RecordingPolicy policy)
        {
            return new RecordingPolicy
            {
                policyRevision = policy.policyRevision, captureMetadata = policy.captureMetadata,
                captureRawText = policy.captureRawText, retentionDays = policy.retentionDays,
                byteCap = policy.byteCap, recordCap = policy.recordCap
            };
        }

        private static bool TryParseUtc(string value, out DateTime result)
        {
            DateTimeOffset parsed;
            bool valid = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed) &&
                parsed.Offset == TimeSpan.Zero;
            result = valid ? parsed.UtcDateTime : default(DateTime);
            return valid;
        }

        private static DateTime ParseUtc(string value)
        {
            DateTime parsed;
            if (!TryParseUtc(value, out parsed)) throw new InvalidDataException("The observation timestamp is not UTC.");
            return parsed;
        }

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value :
                value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string SafeMessage(Exception exception)
        { return exception == null ? "Unknown local recording error." : exception.GetType().Name + ": " + exception.Message; }
    }
}
