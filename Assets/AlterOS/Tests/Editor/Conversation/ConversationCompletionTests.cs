using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using AlterOS.Topics;

namespace AlterOS.Tests
{
    public sealed class ConversationCompletionTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        [Test]
        public void CompletionCountsOnlySuccessfulCompletionAndIsScenarioSpecific()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea",
                ConversationTestData.Scenario("shared", 1), ConversationTestData.Scenario("quiet", 1));
            TopicInstance instance = TopicInstance.Create(definition, "tea-occurrence", "authored");
            var history = new TopicHistory();
            Assert.AreEqual(0, history.GetCompletionCount(instance.instanceId, "shared"));
            TopicPlaybackHandle first = history.BeginPlayback(definition, instance, "shared", Now);
            history.ReportPresentedNode(first, "n1", Now.AddSeconds(1));
            Assert.AreEqual(0, history.GetCompletionCount(instance.instanceId, "shared"));
            Assert.AreEqual("Applied", history.Complete(first, Now.AddSeconds(2)).Status);
            Assert.AreEqual(1, history.GetCompletionCount(instance.instanceId, "shared"));
            Assert.AreEqual(Now.AddSeconds(2), history.GetLastCompletedAt(instance.instanceId, "shared").Value);
            Assert.AreEqual("Duplicate", history.Complete(first, Now.AddSeconds(3)).Status);
            Assert.AreEqual(1, history.GetCompletionCount(instance.instanceId, "shared"));
            Assert.AreEqual(0, history.GetCompletionCount(instance.instanceId, "quiet"));
            Assert.IsFalse(history.IsConsumed(instance.instanceId));
        }

        [Test]
        public void CancelAndCheckpointDoNotCountCompletionOrChangeRepeatability()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea", ConversationTestData.Scenario("shared", 1));
            definition.scenarios[0].consumptionNodeId = "n1";
            TopicInstance instance = TopicInstance.Create(definition, "tea-once", "generated");
            var history = new TopicHistory();
            TopicPlaybackHandle handle = history.BeginPlayback(definition, instance, "shared", Now);
            history.ReportPresentedNode(handle, "n1", Now.AddSeconds(1));
            Assert.IsTrue(history.IsConsumed(instance.instanceId));
            Assert.AreEqual(0, history.GetCompletionCount(instance.instanceId, "shared"));
            history.Cancel(handle);
            Assert.AreEqual(0, history.GetCompletionCount(instance.instanceId, "shared"));
        }

        [Test]
        public void VersionOneHistoryMigratesWithoutInventingScenarioCounts()
        {
            string root = Path.Combine(Path.GetTempPath(), "AlterOS-ConversationHistory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "history.json");
                long ticks = Now.UtcDateTime.Ticks;
                string v1 = "{\"schemaVersion\":1,\"records\":[{\"instanceId\":\"legacy\",\"topicId\":\"tea\",\"replayPolicy\":\"once\",\"startedUtcTicks\":" + ticks +
                    ",\"presentedUtcTicks\":" + ticks + ",\"completedUtcTicks\":" + ticks + ",\"consumedUtcTicks\":" + ticks + ",\"cooldownUntilUtcTicks\":0}]}";
                File.WriteAllText(path, v1);
                byte[] original = File.ReadAllBytes(path);
                TopicHistoryLoadResult loaded = TopicHistoryRepository.Load(path);
                Assert.IsTrue(loaded.Success, loaded.Error);
                Assert.IsTrue(loaded.History.IsConsumed("legacy"));
                Assert.AreEqual(0, loaded.History.GetCompletionCount("legacy", "unknown-scenario"));
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
                TopicHistorySaveResult saved = TopicHistoryRepository.Save(path, loaded.History);
                Assert.IsTrue(saved.Success, saved.Error);
                StringAssert.Contains("\"schemaVersion\":2", File.ReadAllText(path));
                TopicHistoryLoadResult migrated = TopicHistoryRepository.Load(path);
                Assert.IsTrue(migrated.Success, migrated.Error);
                Assert.IsTrue(migrated.History.IsConsumed("legacy"));
                Assert.AreEqual(0, migrated.History.GetCompletionCount("legacy", "unknown-scenario"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void VersionTwoRoundTripPreservesCountAndTimeAndRejectsInvalidEntries()
        {
            string root = Path.Combine(Path.GetTempPath(), "AlterOS-ConversationHistory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TopicDefinition definition = ConversationTestData.Topic("tea", ConversationTestData.Scenario("shared", 1));
                TopicInstance instance = TopicInstance.Create(definition, "tea-occurrence", "authored");
                var history = new TopicHistory();
                TopicPlaybackHandle handle = history.BeginPlayback(definition, instance, "shared", Now);
                history.ReportPresentedNode(handle, "n1", Now.AddSeconds(1));
                history.Complete(handle, Now.AddSeconds(2));
                string path = Path.Combine(root, "history.json");
                TopicHistorySaveResult save = TopicHistoryRepository.Save(path, history);
                Assert.IsTrue(save.Success, save.Error);
                TopicHistoryLoadResult load = TopicHistoryRepository.Load(path);
                Assert.IsTrue(load.Success, load.Error);
                Assert.AreEqual(1, load.History.GetCompletionCount(instance.instanceId, "shared"));
                Assert.AreEqual(Now.AddSeconds(2), load.History.GetLastCompletedAt(instance.instanceId, "shared").Value);

                long ticks = Now.AddSeconds(2).Ticks;
                string completion = "{\"scenarioId\":\"shared\",\"completionCount\":1,\"lastCompletedUtcTicks\":" + ticks + "}";
                string duplicate = "{\"schemaVersion\":2,\"records\":[{\"instanceId\":\"x\",\"topicId\":\"tea\",\"replayPolicy\":\"repeatable\",\"startedUtcTicks\":" + Now.Ticks +
                    ",\"presentedUtcTicks\":0,\"completedUtcTicks\":0,\"consumedUtcTicks\":0,\"cooldownUntilUtcTicks\":0,\"scenarioCompletions\":[" + completion + "," + completion + "]}]}";
                File.WriteAllText(path, duplicate);
                Assert.IsFalse(TopicHistoryRepository.Load(path).Success);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
