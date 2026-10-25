using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlterOS.Conversation;

namespace AlterOS.Memory
{
    /// <summary>Typed Unity API for keyless local memory commands through the owned helper process.</summary>
    public sealed class PlayerMemoryClient
    {
        private readonly CompanionProcessClient process;
        private readonly string playerId, persistentDataPath;
        private readonly PlayerMemoryPolicyFlag policyFlag;

        public PlayerMemoryClient(CompanionProcessClient ownedProcess, string stablePlayerId, string persistentRoot)
        {
            process = ownedProcess ?? throw new ArgumentNullException(nameof(ownedProcess));
            if (string.IsNullOrWhiteSpace(stablePlayerId) || stablePlayerId.Length > 128)
                throw new ArgumentException("Stable player identity is invalid.", nameof(stablePlayerId));
            if (string.IsNullOrWhiteSpace(persistentRoot) || !System.IO.Path.IsPathRooted(persistentRoot))
                throw new ArgumentException("Persistent data root must be absolute.", nameof(persistentRoot));
            playerId = stablePlayerId;
            persistentDataPath = System.IO.Path.GetFullPath(persistentRoot);
            policyFlag = new PlayerMemoryPolicyFlag(persistentDataPath);
        }

        public async Task<PlayerMemoryReadyV3> StartAsync(CancellationToken cancellationToken)
        {
            PlayerMemoryReadyV3 ready = await process.StartMemoryAsync(playerId, persistentDataPath, cancellationToken).ConfigureAwait(false);
            try
            {
                PlayerMemoryResultV3 consent = await GetConsentAsync(0, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                if (consent.consent != null && consent.consent.enabled) policyFlag.MarkRequired();
            }
            catch (Exception) { /* Startup remains usable; existing marker still prevents an unsafe downgrade. */ }
            return ready;
        }

        public bool MemoryPolicyRequired => policyFlag.IsRequired;
        public string PlayerId => playerId;
        public string PersistentDataPath => persistentDataPath;

        public Task<PlayerMemoryReadyV3> StartForGenerationAsync(CancellationToken cancellationToken)
        { return process.StartMemoryAsync(playerId, persistentDataPath, true, cancellationToken); }

        public Task<PlayerMemoryResultV3> SetConsentAsync(bool enabled, int consentRevision,
            DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            if (enabled) policyFlag.MarkRequired();
            return Send(PlayerMemoryProtocol.SerializeConsentEnvelope(NewRequestId(), playerId, consentRevision, nowUtc, enabled), cancellationToken);
        }

        public Task<PlayerMemoryResultV3> GetConsentAsync(int consentRevision, DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeConsentReadEnvelope(NewRequestId(), playerId, consentRevision, nowUtc), cancellationToken); }

        public Task<PlayerMemoryResultV3> SetProfileAsync(PlayerMemoryProposal proposal, PlayerMemoryEvidence evidence,
            int consentRevision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeProfileSetEnvelope(NewRequestId(), playerId, consentRevision, nowUtc, proposal, evidence), cancellationToken); }

        public Task<PlayerMemoryResultV3> ConfirmAsync(PlayerMemoryProposal proposal, PlayerMemoryEvidence evidence,
            PlayerMemoryEvidence confirmation, int consentRevision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeConfirmEnvelope(NewRequestId(), playerId, consentRevision, nowUtc,
            proposal, evidence, confirmation), cancellationToken); }

        public Task<PlayerMemoryResultV3> GetContextAsync(string query, int consentRevision,
            DateTimeOffset nowUtc, CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeContextEnvelope(NewRequestId(), playerId, consentRevision, nowUtc, query), cancellationToken); }

        public Task<PlayerMemoryResultV3> ListAsync(string cursor, int limit, bool includeSuperseded,
            int consentRevision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeListEnvelope(NewRequestId(), playerId, consentRevision, nowUtc,
            cursor, limit, includeSuperseded), cancellationToken); }

        public Task<PlayerMemoryResultV3> ForgetAsync(string memoryId, int consentRevision,
            DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            policyFlag.MarkRequired();
            return Send(PlayerMemoryProtocol.SerializeForgetEnvelope(NewRequestId(), playerId, consentRevision, nowUtc, memoryId), cancellationToken);
        }

        public Task<PlayerMemoryResultV3> SaveOnboardingProgressAsync(string stepId, bool completed, int consentRevision,
            DateTimeOffset nowUtc, CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeOnboardingEnvelope(NewRequestId(), playerId, consentRevision, nowUtc, stepId, completed), cancellationToken); }

        public Task<PlayerMemoryResultV3> GetOnboardingProgressAsync(int consentRevision, DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeOnboardingReadEnvelope(NewRequestId(), playerId, consentRevision, nowUtc), cancellationToken); }

        public Task<PlayerMemoryResultV3> AcknowledgeQuestionAsync(string intent, string sessionId, string eventId,
            DateTimeOffset presentedAtUtc, int consentRevision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeQuestionAckEnvelope(NewRequestId(), playerId, consentRevision, nowUtc,
            intent, sessionId, eventId, presentedAtUtc), cancellationToken); }

        public Task<PlayerMemoryResultV3> SyncSourcesAsync(long afterSequence, long throughSequence, IReadOnlyList<PlayerMemorySourceSyncEvent> events,
            int consentRevision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        { return Send(PlayerMemoryProtocol.SerializeSourceSyncEnvelope(NewRequestId(), playerId, consentRevision, nowUtc,
            afterSequence, throughSequence, events), cancellationToken); }

        private async Task<PlayerMemoryResultV3> Send(string envelope, CancellationToken cancellationToken)
        {
            string requestId = PlayerMemoryProtocol.ReadRequestId(envelope);
            return await process.SendMemoryCommandAsync(requestId, envelope, cancellationToken).ConfigureAwait(false);
        }

        private static string NewRequestId() { return "memory-" + Guid.NewGuid().ToString("N"); }
    }
}
