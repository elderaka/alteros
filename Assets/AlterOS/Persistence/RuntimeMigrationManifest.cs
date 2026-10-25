using System;
using System.Collections.Generic;

namespace AlterOS.Persistence
{
    /// <summary>Host-selected, one-shot domain preparation used by the startup migration journal.</summary>
    public sealed class RuntimeMigrationDomainRequest
    {
        public string domain;
        public string targetPath;
        public string targetIdentity;
        public int schemaVersion;
        public bool validateOnResume;
        public Func<RuntimeMigrationDomainResult> prepare;
    }

    public sealed class RuntimeMigrationDomainResult
    {
        public string state;
        public string sourceHash;
        public string targetIdentity;
        public int schemaVersion;
        public long targetRevision;
        public string receiptId;
        public string error;
    }

    public sealed class RuntimeMigrationConfiguration
    {
        public string journalPath;
        public IList<RuntimeMigrationDomainRequest> domains = new List<RuntimeMigrationDomainRequest>();
    }

    /// <summary>Read-only snapshot of the local bootstrap journal.</summary>
    public sealed class RuntimeMigrationManifest
    {
        public int schemaVersion = 1;
        public bool isReady;
        public long revision;
        public string status;
        public string updatedAtUtc;
        public string error;
        public readonly List<RuntimeMigrationDomainStatus> domains = new List<RuntimeMigrationDomainStatus>();
    }

    public sealed class RuntimeMigrationDomainStatus
    {
        public string domain;
        public string status;
        public string sourceHash;
        public string targetIdentity;
        public int schemaVersion;
        public long targetRevision;
        public string receiptId;
        public string error;
        public string updatedAtUtc;
    }
}
