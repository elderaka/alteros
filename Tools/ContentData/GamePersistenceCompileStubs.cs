namespace AlterOS.Persistence
{
    // The content-data harness compiles host adapters without the complete save/backup runtime.
    public sealed class GameRecoveryCandidate
    {
        public string backupId;
        public GameSaveIdentity identity;
        public long gameRevision;
        public long layoutRevision;
    }
}
