using System;

namespace AlterOS.Persistence
{
    public sealed class GamePersistenceScheduler
    {
        private static readonly TimeSpan MaximumCheckpointInterval = TimeSpan.FromSeconds(5);
        private readonly Func<GameCommitResult> commitDirtySnapshot;
        private readonly Func<DateTime> utcNow;
        private DateTime? lastCheckpointUtc;

        public bool IsDirty { get; private set; }
        public string Status { get; private set; }

        public GamePersistenceScheduler(Func<GameCommitResult> commitDirtySnapshot, Func<DateTime> utcNow = null)
        {
            if (commitDirtySnapshot == null) throw new ArgumentNullException(nameof(commitDirtySnapshot));
            this.commitDirtySnapshot = commitDirtySnapshot;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            Status = "durable";
        }

        public void MarkDirty()
        {
            IsDirty = true;
            if (Status != "unavailable") Status = "dirty";
        }

        public bool Tick(DateTime utcTime)
        {
            if (!IsDirty) return false;
            DateTime now = AsUtc(utcTime);
            if (lastCheckpointUtc.HasValue && now < lastCheckpointUtc.Value)
                lastCheckpointUtc = now;
            if (lastCheckpointUtc.HasValue && now - lastCheckpointUtc.Value < MaximumCheckpointInterval)
                return false;
            return CommitAt(now);
        }

        public bool Flush()
        {
            if (!IsDirty) return false;
            return CommitAt(AsUtc(utcNow()));
        }

        public void MarkCommitted(DateTime utcTime)
        {
            IsDirty = false;
            Status = "durable";
            lastCheckpointUtc = AsUtc(utcTime);
        }

        private bool CommitAt(DateTime now)
        {
            GameCommitResult result;
            try
            {
                result = commitDirtySnapshot();
            }
            catch
            {
                result = null;
            }

            if (result == null || !result.IsDurable)
            {
                IsDirty = true;
                Status = "unavailable";
                return false;
            }

            IsDirty = false;
            Status = "durable";
            lastCheckpointUtc = now;
            return true;
        }

        private static DateTime AsUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}
