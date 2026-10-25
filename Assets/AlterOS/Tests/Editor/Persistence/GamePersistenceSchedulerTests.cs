using System;
using AlterOS.Persistence;
using NUnit.Framework;

namespace AlterOS.Tests.Persistence
{
    public sealed class GamePersistenceSchedulerTests
    {
        [Test]
        public void CleanSchedulerDoesNotWriteAndDirtyStateCheckpointsWithinFiveSeconds()
        {
            int writes = 0;
            DateTime start = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
            var scheduler = new GamePersistenceScheduler(() => Committed(++writes));
            Assert.IsFalse(scheduler.Tick(start));
            Assert.AreEqual(0, writes);

            scheduler.MarkDirty();
            Assert.IsTrue(scheduler.Tick(start));
            Assert.AreEqual(1, writes);
            Assert.AreEqual("durable", scheduler.Status);

            scheduler.MarkDirty();
            Assert.IsFalse(scheduler.Tick(start.AddSeconds(4)));
            Assert.IsTrue(scheduler.Tick(start.AddSeconds(5)));
            Assert.AreEqual(2, writes);
        }

        [Test]
        public void BackwardsClockDoesNotDeferDirtyCheckpointIndefinitely()
        {
            int writes = 0;
            var scheduler = new GamePersistenceScheduler(() => Committed(++writes));
            DateTime baseline = new DateTime(2026, 10, 21, 0, 0, 10, DateTimeKind.Utc);
            scheduler.MarkDirty();
            Assert.IsTrue(scheduler.Tick(baseline));
            scheduler.MarkDirty();
            Assert.IsFalse(scheduler.Tick(baseline.AddSeconds(-1)));
            Assert.IsTrue(scheduler.Tick(baseline.AddSeconds(4)));
            Assert.AreEqual(2, writes);
        }

        [Test]
        public void PauseFlushWritesImmediatelyAndDuplicateCleanFlushDoesNothing()
        {
            int writes = 0;
            var scheduler = new GamePersistenceScheduler(() => Committed(++writes),
                () => new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc));
            scheduler.MarkDirty();
            Assert.IsTrue(scheduler.Flush());
            Assert.IsFalse(scheduler.Flush());
            Assert.AreEqual(1, writes);
        }

        [Test]
        public void FailedFlushLeavesDirtyStateAndCanRetry()
        {
            int writes = 0;
            var scheduler = new GamePersistenceScheduler(() => ++writes == 1
                ? new GameCommitResult { state = GamePersistenceStates.Unavailable }
                : Committed(writes));
            scheduler.MarkDirty();
            Assert.IsFalse(scheduler.Flush());
            Assert.IsTrue(scheduler.IsDirty);
            Assert.AreEqual("unavailable", scheduler.Status);
            Assert.IsTrue(scheduler.Flush());
            Assert.IsFalse(scheduler.IsDirty);
            Assert.AreEqual(2, writes);
        }

        private static GameCommitResult Committed(int revision)
        {
            return new GameCommitResult
            {
                state = GamePersistenceStates.Committed,
                receipt = new GameCommitReceipt { gameRevision = revision, layoutRevision = 0 }
            };
        }
    }
}
