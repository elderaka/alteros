using System;
using AlterOS.Dialogue;
using NUnit.Framework;

namespace AlterOS.Tests.TopicRuntime
{
    public class TopicHistoryTests
    {
        private static readonly DateTimeOffset Now = TopicScenarioSelectionTests.Now;
        private static dynamic Instance(object family, string id = "one", string policy = "once")
        { return TopicTestData.Call("TopicInstance", "Create", family, id, "generated", policy); }

        [Test] public void CancelBeforeDeliveryDoesNotConsumeOrStartCooldown()
        {
            dynamic family = TopicTestData.Family(); family.cooldownSeconds = 60d;
            dynamic instance = Instance(family); dynamic history = TopicTestData.New("TopicHistory");
            dynamic handle = history.BeginPlayback(family, instance, "a", Now);
            history.Cancel(handle);
            Assert.IsFalse(history.IsConsumed("one"));
            Assert.IsNotNull(TopicTestData.Call("TopicScenarioSelector", "Select", family, instance,
                TopicTestData.New("TopicContext"), history, Now, 0d).SelectedScenario);
        }

        [Test] public void NormalCompletionConsumesAllSiblingScenariosButNotAnotherInstance()
        {
            dynamic family = TopicTestData.Family(); family.scenarios.Add(TopicTestData.Scenario("b", 10, 1));
            dynamic instance = Instance(family); dynamic history = TopicTestData.New("TopicHistory");
            dynamic handle = history.BeginPlayback(family, instance, "a", Now);
            Assert.AreEqual("Rejected", history.Complete(handle, Now).Status);
            Assert.AreEqual("Applied", history.ReportPresentedNode(handle, "start", Now).Status);
            Assert.IsFalse(history.IsConsumed("one"));
            Assert.AreEqual("Applied", history.Complete(handle, Now.AddSeconds(5)).Status);
            Assert.IsTrue(history.IsConsumed("one"));
            Assert.IsNull(TopicTestData.Call("TopicScenarioSelector", "Select", family, instance,
                TopicTestData.New("TopicContext"), history, Now.AddSeconds(10), 0d).SelectedScenario);
            Assert.IsNotNull(TopicTestData.Call("TopicScenarioSelector", "Select", family, Instance(family, "two"),
                TopicTestData.New("TopicContext"), history, Now.AddSeconds(10), 0d).SelectedScenario);
        }

        [Test] public void DeliveredCheckpointPersistsAcrossCancelButUnreachedCheckpointDoesNot()
        {
            dynamic family = TopicTestData.Family();
            family.scenarios[0].nodes[0].nextId = "checkpoint";
            family.scenarios[0].nodes.Add(new CanonicalDialogueNode { id = "checkpoint", text = "Here is the important information." });
            family.scenarios[0].consumptionNodeId = "checkpoint";
            dynamic history = TopicTestData.New("TopicHistory"); dynamic instance = Instance(family);
            dynamic handle = history.BeginPlayback(family, instance, "a", Now);
            Assert.AreEqual("Rejected", history.ReportPresentedNode(handle, "checkpoint", Now).Status);
            history.ReportPresentedNode(handle, "start", Now); history.Cancel(handle);
            Assert.IsFalse(history.IsConsumed("one"));
            handle = history.BeginPlayback(family, instance, "a", Now.AddSeconds(1));
            history.ReportPresentedNode(handle, "start", Now.AddSeconds(1));
            history.ReportPresentedNode(handle, "checkpoint", Now.AddSeconds(2)); history.Cancel(handle);
            Assert.IsTrue(history.IsConsumed("one"));
        }

        [Test] public void RepeatableCooldownStartsOnlyOnCompletionAndDuplicateDoesNotExtendIt()
        {
            dynamic family = TopicTestData.Family(); family.cooldownSeconds = 60d;
            dynamic instance = Instance(family, "one", "repeatable"); dynamic history = TopicTestData.New("TopicHistory");
            dynamic handle = history.BeginPlayback(family, instance, "a", Now);
            history.ReportPresentedNode(handle, "start", Now); history.Cancel(handle);
            handle = history.BeginPlayback(family, instance, "a", Now.AddSeconds(1));
            history.ReportPresentedNode(handle, "start", Now.AddSeconds(1));
            history.Complete(handle, Now.AddSeconds(10));
            Assert.IsFalse(history.IsConsumed("one"));
            Assert.AreEqual("Duplicate", history.Complete(handle, Now.AddSeconds(60)).Status);
            dynamic context = TopicTestData.New("TopicContext");
            Assert.IsNull(TopicTestData.Call("TopicScenarioSelector", "Select", family, instance, context, history, Now.AddSeconds(69), 0d).SelectedScenario);
            Assert.IsNotNull(TopicTestData.Call("TopicScenarioSelector", "Select", family, instance, context, history, Now.AddSeconds(70), 0d).SelectedScenario);
        }

        [Test] public void SupersededForeignAndWrongNodeReportsCannotConsumeSuccessor()
        {
            dynamic family = TopicTestData.Family(); dynamic history = TopicTestData.New("TopicHistory");
            dynamic first = history.BeginPlayback(family, Instance(family), "a", Now);
            dynamic next = history.BeginPlayback(family, Instance(family, "two"), "a", Now);
            Assert.AreEqual("Rejected", history.Complete(first, Now).Status);
            Assert.AreEqual("Rejected", history.ReportPresentedNode(next, "invented", Now).Status);
            dynamic foreign = TopicTestData.New("TopicHistory");
            Assert.AreEqual("Rejected", foreign.ReportPresentedNode(next, "start", Now).Status);
            Assert.IsFalse(history.IsConsumed("one")); Assert.IsFalse(history.IsConsumed("two"));
        }

        [Test] public void RepeatedDeliveryCannotMoveCompletionBeforeLatestAcknowledgement()
        {
            dynamic family = TopicTestData.Family(); dynamic history = TopicTestData.New("TopicHistory");
            dynamic handle = history.BeginPlayback(family, Instance(family), "a", Now);
            history.ReportPresentedNode(handle, "start", Now);
            history.ReportPresentedNode(handle, "start", Now.AddSeconds(10));
            Assert.AreEqual("Rejected", history.Complete(handle, Now.AddSeconds(5)).Status);
            Assert.AreEqual("Applied", history.Complete(handle, Now.AddSeconds(10)).Status);
        }

        [Test] public void PlaybackDefinitionIsFrozenAndBackwardsTimeCannotComplete()
        {
            dynamic family = TopicTestData.Family(); dynamic history = TopicTestData.New("TopicHistory");
            dynamic handle = history.BeginPlayback(family, Instance(family), "a", Now);
            family.scenarios[0].consumptionNodeId = "start";
            history.ReportPresentedNode(handle, "start", Now);
            Assert.IsFalse(history.IsConsumed("one"));
            Assert.AreEqual("Rejected", history.Complete(handle, Now.AddSeconds(-1)).Status);
            history.Fail(handle); Assert.IsFalse(history.IsConsumed("one"));
        }
    }
}
