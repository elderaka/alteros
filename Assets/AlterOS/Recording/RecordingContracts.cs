using System;
using System.Collections.Generic;

namespace AlterOS.Recording
{
    [Serializable]
    public sealed class RecordingPolicy
    {
        public string policyRevision;
        public bool captureMetadata;
        public bool captureRawText;
        public int retentionDays;
        public long byteCap;
        public int recordCap;
    }

    [Serializable]
    public sealed class InteractionObservation
    {
        public string kind;
        public string sourceEventId;
        public string occurredAtUtc;
        public Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        public string rawText;
    }

    [Serializable]
    public sealed class RecordingResult
    {
        public string state;
        public string recordId;
        public string sessionId;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class ContributionSelection
    {
        public DateTime? fromUtc;
        public DateTime? toUtc;
        public List<string> includeRecordIds = new List<string>();
        public List<string> excludeRecordIds = new List<string>();
        public bool includeRawText;
    }

    [Serializable]
    public sealed class ContributionPreviewResult
    {
        public string state;
        public string previewId;
        public string contentHash;
        public int recordCount;
        public long byteCount;
        public bool includesRawText;
        public string warning;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class ContributionResult
    {
        public string state;
        public string contributionId;
        public string contentHash;
        public string errorCode;
        public string message;
    }

    [Serializable]
    public sealed class ContributionExportResult
    {
        public string state;
        public string contributionId;
        public string path;
        public string contentHash;
        public string errorCode;
        public string message;
    }
}
