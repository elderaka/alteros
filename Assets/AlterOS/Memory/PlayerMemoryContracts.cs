using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AlterOS.Conversation;

namespace AlterOS.Memory
{
    public sealed class PlayerMemoryTurnPolicyV3
    {
        public readonly string playerId, currentPlayerSourceId, authorizedQuestionIntent;
        public readonly int consentRevision;
        public readonly DateTimeOffset nowUtc;
        public readonly bool? focusAllowed, quiet;
        public readonly bool explicitRecall, memoryPolicyRequired;
        public readonly ReadOnlyCollection<string> presentedHistorySourceIds;

        public PlayerMemoryTurnPolicyV3(string id, int revision, DateTimeOffset now, string currentSource,
            bool? focus, bool? isQuiet, bool recall, string questionIntent, bool policyRequired,
            IEnumerable<string> historySourceIds)
        {
            playerId = id; consentRevision = revision; nowUtc = now; currentPlayerSourceId = currentSource;
            focusAllowed = focus; quiet = isQuiet; explicitRecall = recall; authorizedQuestionIntent = questionIntent ?? "";
            memoryPolicyRequired = policyRequired;
            presentedHistorySourceIds = Array.AsReadOnly(new List<string>(historySourceIds ?? new string[0]).ToArray());
        }
    }

    public sealed class PlayerMemoryTurnV3
    {
        public readonly ConversationJobV2 conversation;
        public readonly PlayerMemoryTurnPolicyV3 memory;
        internal PlayerMemoryTurnV3(ConversationJobV2 job, PlayerMemoryTurnPolicyV3 policy)
        { conversation = job; memory = policy; }
    }

    public sealed class PlayerMemoryEvidence
    {
        public string playerId, sessionId, sourceId, actor, scope, sourceKind, text;
        public DateTimeOffset? eventAtUtc;
        public bool explicitlyConfirmed;
    }

    public sealed class PlayerMemoryProposal
    {
        public string category, key, value, scope, sourceId, evidenceQuote;
    }

    public sealed class PlayerMemoryConsentV3
    {
        public readonly string playerId;
        public readonly bool enabled;
        public readonly int revision;
        internal PlayerMemoryConsentV3(string id, bool isEnabled, int consentRevision)
        { playerId = id; enabled = isEnabled; revision = consentRevision; }
    }

    public sealed class PlayerMemoryBootstrapV3
    {
        public readonly string apiKey, playerId, memoryRoot;
        internal PlayerMemoryBootstrapV3(string key, string id, string root)
        { apiKey = key; playerId = id; memoryRoot = root; }
    }

    public sealed class PlayerMemoryCommandV3
    {
        public readonly string operation, playerId, nowUtc;
        public readonly int consentRevision;
        public readonly PlayerMemoryCommandDataV3 data;
        internal PlayerMemoryCommandV3(string op, string id, int revision, string timestamp, PlayerMemoryCommandDataV3 payload)
        { operation = op; playerId = id; consentRevision = revision; nowUtc = timestamp; data = payload; }
    }

    public abstract class PlayerMemoryCommandDataV3
    {
        public readonly string operation;
        protected PlayerMemoryCommandDataV3(string op) { operation = op; }
    }

    public sealed class PlayerMemoryConsentDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly bool? enabled;
        internal PlayerMemoryConsentDataV3(bool? value) : base("consent") { enabled = value; }
    }

    public sealed class PlayerMemorySourceSyncDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly long afterSequence, throughSequence;
        public readonly ReadOnlyCollection<PlayerMemorySourceSyncEvent> events;
        internal PlayerMemorySourceSyncDataV3(long after, long through, IEnumerable<PlayerMemorySourceSyncEvent> items) : base("source_sync")
        { afterSequence = after; throughSequence = through; events = Array.AsReadOnly(new List<PlayerMemorySourceSyncEvent>(items).ToArray()); }
    }

    public sealed class PlayerMemoryProfileSetDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly PlayerMemoryProposal proposal;
        public readonly PlayerMemoryEvidence evidence;
        internal PlayerMemoryProfileSetDataV3(PlayerMemoryProposal item, PlayerMemoryEvidence source) : base("profile_set")
        { proposal = item; evidence = source; }
    }

    public sealed class PlayerMemoryConfirmDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly PlayerMemoryProposal proposal;
        public readonly PlayerMemoryEvidence evidence, confirmation;
        internal PlayerMemoryConfirmDataV3(PlayerMemoryProposal item, PlayerMemoryEvidence source, PlayerMemoryEvidence answer) : base("confirm")
        { proposal = item; evidence = source; confirmation = answer; }
    }

    public sealed class PlayerMemoryContextDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly string query;
        internal PlayerMemoryContextDataV3(string text) : base("context") { query = text; }
    }

    public sealed class PlayerMemoryListDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly string cursor;
        public readonly int limit;
        public readonly bool includeSuperseded;
        internal PlayerMemoryListDataV3(string pageCursor, int pageLimit, bool includeOld) : base("list")
        { cursor = pageCursor; limit = pageLimit; includeSuperseded = includeOld; }
    }

    public sealed class PlayerMemoryForgetDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly string memoryId;
        internal PlayerMemoryForgetDataV3(string id) : base("forget") { memoryId = id; }
    }

    public sealed class PlayerMemoryOnboardingDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly string stepId;
        public readonly bool completed;
        internal PlayerMemoryOnboardingDataV3(string step, bool done) : base("onboarding") { stepId = step; completed = done; }
    }

    public sealed class PlayerMemoryOnboardingReadDataV3 : PlayerMemoryCommandDataV3
    {
        internal PlayerMemoryOnboardingReadDataV3() : base("onboarding_read") { }
    }

    public sealed class PlayerMemoryQuestionAckDataV3 : PlayerMemoryCommandDataV3
    {
        public readonly string intent, sessionId, eventId;
        public readonly DateTimeOffset presentedAtUtc;
        internal PlayerMemoryQuestionAckDataV3(string questionIntent, string session, string eventIdentity, DateTimeOffset presentedAt) : base("question_ack")
        { intent = questionIntent; sessionId = session; eventId = eventIdentity; presentedAtUtc = presentedAt; }
    }

    public sealed class PlayerMemoryReadyV3
    {
        public readonly string runtimeVersion, modelId;
        public readonly bool memoryAvailable, generationAvailable;
        internal PlayerMemoryReadyV3(string runtime, string model, bool memory, bool generation)
        { runtimeVersion = runtime; modelId = model; memoryAvailable = memory; generationAvailable = generation; }
    }

    public sealed class PlayerMemoryRecordV3
    {
        public readonly string memoryId, playerId, category, key, value, actor, scope, sourceKind, sourceId,
            sourceText, eventAtUtc, recordedAtUtc, validUntilUtc, freshUntilUtc, status, supersedesId;
        internal PlayerMemoryRecordV3(string id, string player, string recordCategory, string recordKey, string recordValue,
            string recordActor, string recordScope, string kind, string source, string excerpt, string eventTime,
            string recordedTime, string validTime, string freshTime, string recordStatus, string supersedes)
        {
            memoryId = id; playerId = player; category = recordCategory; key = recordKey; value = recordValue;
            actor = recordActor; scope = recordScope; sourceKind = kind; sourceId = source; sourceText = excerpt;
            eventAtUtc = eventTime; recordedAtUtc = recordedTime; validUntilUtc = validTime;
            freshUntilUtc = freshTime; status = recordStatus; supersedesId = supersedes;
        }
    }

    public sealed class PlayerMemoryContextV3
    {
        public readonly ReadOnlyCollection<PlayerMemoryRecordV3> core, current, recalled;
        public readonly ReadOnlyCollection<string> suppressedSourceIds;
        internal PlayerMemoryContextV3(IEnumerable<PlayerMemoryRecordV3> coreRecords,
            IEnumerable<PlayerMemoryRecordV3> currentRecords, IEnumerable<PlayerMemoryRecordV3> recalledRecords,
            IEnumerable<string> suppressed)
        {
            core = Array.AsReadOnly(new List<PlayerMemoryRecordV3>(coreRecords).ToArray());
            current = Array.AsReadOnly(new List<PlayerMemoryRecordV3>(currentRecords).ToArray());
            recalled = Array.AsReadOnly(new List<PlayerMemoryRecordV3>(recalledRecords).ToArray());
            suppressedSourceIds = Array.AsReadOnly(new List<string>(suppressed).ToArray());
        }
    }

    public sealed class PlayerMemoryPageV3
    {
        public readonly ReadOnlyCollection<PlayerMemoryRecordV3> items;
        public readonly string nextCursor;
        public readonly bool hasMore;
        internal PlayerMemoryPageV3(IEnumerable<PlayerMemoryRecordV3> records, string cursor, bool more)
        { items = Array.AsReadOnly(new List<PlayerMemoryRecordV3>(records).ToArray()); nextCursor = cursor; hasMore = more; }
    }

    public sealed class PlayerMemoryResultV3
    {
        public readonly string status, reasonCode, stepId;
        public readonly PlayerMemoryConsentV3 consent;
        public readonly PlayerMemoryRecordV3 memory;
        public readonly PlayerMemoryContextV3 context;
        public readonly PlayerMemoryPageV3 page;
        public readonly long? lastSequence;
        public readonly int? count;
        public readonly bool? completed;
        internal PlayerMemoryResultV3(string outcome, string reason, string step,
            PlayerMemoryConsentV3 consentState, PlayerMemoryRecordV3 record,
            PlayerMemoryContextV3 memoryContext, PlayerMemoryPageV3 recordsPage,
            long? sequence, int? resultCount, bool? isComplete)
        {
            status = outcome; reasonCode = reason; stepId = step; consent = consentState; memory = record;
            context = memoryContext; page = recordsPage; lastSequence = sequence; count = resultCount; completed = isComplete;
        }
    }

    public sealed class PlayerMemoryErrorV3
    {
        public readonly string code;
        public readonly bool retryable;
        internal PlayerMemoryErrorV3(string errorCode, bool canRetry) { code = errorCode; retryable = canRetry; }
    }

    public sealed class PlayerMemorySourceSyncEvent
    {
        public readonly long sequence;
        public readonly PlayerMemoryEvidence evidence;
        public PlayerMemorySourceSyncEvent(long eventSequence, PlayerMemoryEvidence sourceEvidence)
        { sequence = eventSequence; evidence = sourceEvidence; }
    }
}
