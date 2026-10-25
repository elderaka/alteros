using System;
using System.IO;
using NUnit.Framework;

namespace AlterOS.Tests.TopicRuntime
{
    public class TopicHistoryRepositoryTests
    {
        private static readonly DateTimeOffset Now = TopicScenarioSelectionTests.Now;

        private static string DirectoryPath()
        { string path = Path.Combine(Path.GetTempPath(), "AlterOS-TopicTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }

        private static dynamic Consumed(string id = "one")
        {
            dynamic family = TopicTestData.Family();
            dynamic instance = TopicTestData.Call("TopicInstance", "Create", family, id, "generated", null);
            dynamic history = TopicTestData.New("TopicHistory");
            dynamic handle = history.BeginPlayback(family, instance, "a", Now);
            history.ReportPresentedNode(handle, "start", Now); history.Complete(handle, Now.AddSeconds(1));
            return history;
        }

        [Test] public void SaveReloadPreservesConsumptionAndHasNoConversationPayload()
        {
            string root = DirectoryPath();
            try
            {
                string path = Path.Combine(root, "history.json"); dynamic history = Consumed();
                dynamic saved = TopicTestData.Call("TopicHistoryRepository", "Save", path, history);
                Assert.IsTrue(saved.Success, saved.Error); Assert.IsFalse(history.IsDirty);
                dynamic loaded = TopicTestData.Call("TopicHistoryRepository", "Load", path);
                Assert.IsTrue(loaded.Success, loaded.Error); Assert.IsTrue(loaded.History.IsConsumed("one"));
                Assert.IsTrue(TopicTestData.Call("TopicHistoryRepository", "Save", path, loaded.History).Success);
                string json = File.ReadAllText(path);
                StringAssert.DoesNotContain("A drink sounds nice", json);
                StringAssert.DoesNotContain("prompt", json); StringAssert.DoesNotContain("anchors", json);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test] public void CheckpointCancellationAndCooldownSurviveReload()
        {
            string root = DirectoryPath();
            try
            {
                dynamic family = TopicTestData.Family(); family.cooldownSeconds = 60d;
                family.scenarios[0].consumptionNodeId = "start";
                dynamic history = TopicTestData.New("TopicHistory");
                dynamic once = TopicTestData.Call("TopicInstance", "Create", family, "once", "generated", null);
                dynamic handle = history.BeginPlayback(family, once, "a", Now);
                history.ReportPresentedNode(handle, "start", Now); history.Cancel(handle);
                dynamic repeat = TopicTestData.Call("TopicInstance", "Create", family, "repeat", "authored", null);
                handle = history.BeginPlayback(family, repeat, "a", Now);
                history.ReportPresentedNode(handle, "start", Now); history.Complete(handle, Now.AddSeconds(5));
                string path = Path.Combine(root, "history.json");
                Assert.IsTrue(TopicTestData.Call("TopicHistoryRepository", "Save", path, history).Success);
                dynamic restored = TopicTestData.Call("TopicHistoryRepository", "Load", path).History;
                Assert.IsTrue(restored.IsConsumed("once")); Assert.IsFalse(restored.IsConsumed("repeat"));
                Assert.IsNull(TopicTestData.Call("TopicScenarioSelector", "Select", family, repeat,
                    TopicTestData.New("TopicContext"), restored, Now.AddSeconds(64), 0d).SelectedScenario);
                Assert.IsNotNull(TopicTestData.Call("TopicScenarioSelector", "Select", family, repeat,
                    TopicTestData.New("TopicContext"), restored, Now.AddSeconds(65), 0d).SelectedScenario);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test] public void MalformedAndFutureFilesArePreservedRatherThanReset()
        {
            string root = DirectoryPath();
            try
            {
                string path = Path.Combine(root, "history.json");
                foreach (string bad in new[] { "{ broken", "{\"schemaVersion\":99,\"records\":[]}" })
                {
                    File.WriteAllText(path, bad); byte[] before = File.ReadAllBytes(path);
                    dynamic loaded = TopicTestData.Call("TopicHistoryRepository", "Load", path);
                    Assert.IsFalse(loaded.Success); Assert.IsNull(loaded.History);
                    Assert.IsFalse(TopicTestData.Call("TopicHistoryRepository", "Save", path, Consumed()).Success);
                    CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
                }
            }
            finally { Directory.Delete(root, true); }
        }

        [Test] public void MissingFileIsEmptyAndStaleWriterCannotOverwriteNewerHistory()
        {
            string root = DirectoryPath();
            try
            {
                string path = Path.Combine(root, "history.json");
                dynamic missing = TopicTestData.Call("TopicHistoryRepository", "Load", path);
                Assert.IsTrue(missing.Success); Assert.IsTrue(missing.Missing);
                Assert.IsTrue(TopicTestData.Call("TopicHistoryRepository", "Save", path, missing.History).Success);
                dynamic first = TopicTestData.Call("TopicHistoryRepository", "Load", path).History;
                dynamic stale = TopicTestData.Call("TopicHistoryRepository", "Load", path).History;
                dynamic family = TopicTestData.Family();
                dynamic instance = TopicTestData.Call("TopicInstance", "Create", family, "new", "generated", null);
                dynamic handle = first.BeginPlayback(family, instance, "a", Now);
                first.ReportPresentedNode(handle, "start", Now); first.Complete(handle, Now);
                Assert.IsTrue(TopicTestData.Call("TopicHistoryRepository", "Save", path, first).Success);
                byte[] before = File.ReadAllBytes(path);
                Assert.IsFalse(TopicTestData.Call("TopicHistoryRepository", "Save", path, stale).Success);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test] public void FailedSaveAndDuplicateCompletionRemainDirtyUntilSuccessfulRetry()
        {
            string root = DirectoryPath();
            try
            {
                dynamic family = TopicTestData.Family(); dynamic history = TopicTestData.New("TopicHistory");
                dynamic instance = TopicTestData.Call("TopicInstance", "Create", family, "one", "generated", null);
                dynamic handle = history.BeginPlayback(family, instance, "a", Now);
                history.ReportPresentedNode(handle, "start", Now); history.Complete(handle, Now);
                Assert.IsFalse(TopicTestData.Call("TopicHistoryRepository", "Save", root, history).Success);
                Assert.IsTrue(history.IsDirty); Assert.IsTrue(history.Complete(handle, Now).RequiresSave);
                Assert.IsTrue(TopicTestData.Call("TopicHistoryRepository", "Save", Path.Combine(root, "valid.json"), history).Success);
                Assert.IsFalse(history.IsDirty); Assert.IsFalse(history.Complete(handle, Now).RequiresSave);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
