using System.Collections.Generic;
using AlterOS.Activities;
using NUnit.Framework;

namespace AlterOS.Tests.Activities
{
    public sealed class HydrationCheckpointSinkTests
    {
        [Test]
        public void OnlyRegisteredCheckpointAppliesConfiguredAmountOnce()
        {
            var applied = new List<float>();
            var sink = new HydrationCheckpointSink(amount => { applied.Add(amount); return true; });
            Assert.IsTrue(sink.Register("activity-1", new[] { "sip-1", "sip-2" }, 12f));
            Assert.IsFalse(sink.TryApply("activity-1", "invented"));
            Assert.IsTrue(sink.TryApply("activity-1", "sip-1"));
            Assert.IsTrue(sink.TryApply("activity-1", "sip-1"));
            CollectionAssert.AreEqual(new[] { 12f }, applied);
        }

        [Test]
        public void ReleaseRemovesGrantWithoutForgettingDuplicateHistory()
        {
            int writes = 0;
            var sink = new HydrationCheckpointSink(_ => { writes++; return true; });
            Assert.IsTrue(sink.Register("activity-1", new[] { "sip-1" }, 10f));
            Assert.IsTrue(sink.TryApply("activity-1", "sip-1"));
            sink.Release("activity-1");
            Assert.IsTrue(sink.TryApply("activity-1", "sip-1"));
            Assert.IsFalse(sink.TryApply("activity-1", "sip-2"));
            Assert.IsFalse(sink.Register("activity-1", new[] { "sip-1" }, 10f));
            Assert.AreEqual(1, writes);
        }

        [Test]
        public void InvalidGrantIsRejected()
        {
            var sink = new HydrationCheckpointSink(_ => true);
            Assert.IsFalse(sink.Register("", new[] { "sip-1" }, 10f));
            Assert.IsFalse(sink.Register("activity-1", new[] { "sip-1", "sip-1" }, 10f));
            Assert.IsFalse(sink.Register("activity-2", new[] { "sip-1" }, float.NaN));
        }
    }
}
