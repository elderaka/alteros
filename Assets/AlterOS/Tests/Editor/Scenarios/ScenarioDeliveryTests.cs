using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Scenarios;

namespace AlterOS.Tests.Scenarios
{
    [DataContract]
    internal sealed class ScenarioPairedOutputFixtures
    {
        [DataMember(Name = "cases")]
        public ScenarioPairedOutputCase[] cases;
    }

    [DataContract]
    internal sealed class ScenarioPairedOutputCase
    {
        [DataMember(Name = "name")] public string name;
        [DataMember(Name = "headFixture")] public string headFixture;
        [DataMember(Name = "mainFixture")] public string mainFixture;
        [DataMember(Name = "headDisplayOverride")] public string headDisplayOverride;
        [DataMember(Name = "mainDisplayOverride")] public string mainDisplayOverride;
        [DataMember(Name = "prohibitedPhrase")] public string prohibitedPhrase;
        [DataMember(Name = "constraintCode")] public string constraintCode;
        [DataMember(Name = "expectedRejection")] public string expectedRejection;
    }

    public sealed class ScenarioDeliveryTests
    {
        [Test]
        public void PreparedOpeningUsesFiveSecondBoundaryExactlyOnce()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 10.0);
            Assert.IsEmpty(coordinator.Tick(14.999));
            IReadOnlyList<ScenarioEffect> atDeadline = coordinator.Tick(15.0);
            Assert.AreEqual("show_dialogue", atDeadline[0].type);
            Assert.AreEqual("fallback", atDeadline[0].stage);
            Assert.AreEqual("fallback-0", atDeadline[0].beatId);
            Assert.IsEmpty(coordinator.Tick(16.0));
        }

        [Test]
        public void PreparedOpeningDeadlineCanStartBeforeTheProviderJobIsReady()
        {
            var coordinator = new ScenarioTurnCoordinator();
            var input = new ScenarioTurnInput("turn-cats-1", "turn-cats-1", 1,
                Runtime("turn-cats-1"), new[] { Beat("fallback-0", "Let me think.") });

            coordinator.Begin(input, 10.0);

            Assert.IsEmpty(coordinator.Tick(14.999));
            Assert.AreEqual("fallback", coordinator.Tick(15.0).Single().stage);
        }

        [Test]
        public void MainFirstSuppressesHeadAndAnAcknowledgedBeatAdvancesOnlyOnce()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            IReadOnlyList<ScenarioEffect> body = coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1"));
            ScenarioEffect bodyBeat = body.Single(effect => effect.type == "show_dialogue");
            Assert.AreEqual("main", bodyBeat.stage);
            Assert.AreEqual("answer", bodyBeat.beatId);
            Assert.IsEmpty(coordinator.Receive(Stage("head-valid.json", "turn-cats-1"), Runtime("turn-cats-1")));
            Assert.IsTrue(coordinator.AcknowledgeBeat(bodyBeat.stageId, bodyBeat.beatId).Any(effect => effect.type == "turn_segment_complete"));
            Assert.IsEmpty(coordinator.AcknowledgeBeat(bodyBeat.stageId, bodyBeat.beatId));
        }

        [Test]
        public void SuccessfulReceiptBeforeMainPresentationCompletesOnlyAfterTheLastBeatIsAcknowledged()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioEffect mainBeat = coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1"))
                .Single(effect => effect.type == "show_dialogue");

            Assert.IsEmpty(coordinator.Receive(Receipt("turn-cats-1", "ready", "ready", "completed"), Runtime("turn-cats-1")));
            IReadOnlyList<ScenarioEffect> finished = coordinator.AcknowledgeBeat(mainBeat.stageId, mainBeat.beatId);

            Assert.AreEqual(1, finished.Count(effect => effect.type == "turn_segment_complete"));
            Assert.AreEqual(1, finished.Count(effect => effect.type == "turn_complete"));
            Assert.IsEmpty(coordinator.AcknowledgeBeat(mainBeat.stageId, mainBeat.beatId));
            Assert.IsEmpty(coordinator.Receive(Receipt("turn-cats-1", "ready", "ready", "completed"), Runtime("turn-cats-1")));
        }

        [Test]
        public void SuccessfulReceiptAfterMainPresentationCompletesExactlyOnce()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioEffect mainBeat = coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1"))
                .Single(effect => effect.type == "show_dialogue");
            Assert.IsTrue(coordinator.AcknowledgeBeat(mainBeat.stageId, mainBeat.beatId)
                .Any(effect => effect.type == "turn_segment_complete"));

            IReadOnlyList<ScenarioEffect> finished = coordinator.Receive(
                Receipt("turn-cats-1", "ready", "ready", "completed"), Runtime("turn-cats-1"));

            Assert.AreEqual(1, finished.Count(effect => effect.type == "turn_complete"));
            Assert.IsEmpty(coordinator.Receive(Receipt("turn-cats-1", "ready", "ready", "completed"), Runtime("turn-cats-1")));
        }

        [Test]
        public void MainBeforeHeadVisibilityReplacesTheUnacknowledgedHead()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioEffect pendingHead = coordinator.Receive(Stage("head-valid.json", "turn-cats-1"), Runtime("turn-cats-1")).Single();
            ScenarioEffect bodyBeat = coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1"))
                .Single(effect => effect.type == "show_dialogue");
            Assert.AreEqual("main", bodyBeat.stage);
            Assert.IsEmpty(coordinator.AcknowledgeBeat(pendingHead.stageId, pendingHead.beatId));
        }

        [Test]
        public void MainWaitsForEveryVisibleHeadBeatBoundary()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            IReadOnlyList<ScenarioEffect> firstHead = coordinator.Receive(Stage("head-two-beats.json", "turn-cats-1"), Runtime("turn-cats-1"));
            Assert.AreEqual("head", firstHead[0].stage);
            IReadOnlyList<ScenarioEffect> secondHead = coordinator.AcknowledgeBeat(firstHead[0].stageId, firstHead[0].beatId);
            Assert.AreEqual("follow-up", secondHead.Single().beatId);
            Assert.IsEmpty(coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1")));
            IReadOnlyList<ScenarioEffect> nextHead = coordinator.AcknowledgeBeat(secondHead[0].stageId, secondHead[0].beatId);
            ScenarioEffect mainBeat = nextHead.Single(effect => effect.type == "show_dialogue" && effect.stage == "main");
            Assert.AreEqual("answer", mainBeat.beatId);
        }

        [Test]
        public void FirstVisibleHeadCommitsOpeningButDoesNotAdvanceItsResumeCheckpoint()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioEffect head = coordinator.Receive(Stage("head-valid.json", "turn-cats-1"), Runtime("turn-cats-1")).Single();

            Assert.IsTrue(coordinator.MarkBeatVisible(head.stageId, head.beatId));
            Assert.IsEmpty(coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1")));
            Assert.IsEmpty(coordinator.Tick(5.0), "The visible head must not be replaced by the local fallback.");

            ScenarioEffect saved = coordinator.Dismiss().Single(effect => effect.type == "late_result_saved");
            Assert.AreEqual(0, saved.beatIndex, "Visibility commits ordering, but not the player-advance checkpoint.");

            var continued = new ScenarioTurnCoordinator();
            continued.Begin(Input("turn-cats-2"), 0);
            ScenarioEffect visibleHead = continued.Receive(Stage("head-valid.json", "turn-cats-2"), Runtime("turn-cats-2")).Single();
            Assert.IsTrue(continued.MarkBeatVisible(visibleHead.stageId, visibleHead.beatId));
            Assert.IsEmpty(continued.Receive(Stage("main-cats.json", "turn-cats-2"), Runtime("turn-cats-2")));
            ScenarioEffect main = continued.AcknowledgeBeat(visibleHead.stageId, visibleHead.beatId)
                .Single(effect => effect.type == "show_dialogue" && effect.stage == "main");
            Assert.AreEqual("answer", main.beatId);
        }

        [Test]
        public void FallbackWinsAtDeadlineAndLateHeadCannotReplaceIt()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioEffect fallback = coordinator.Tick(5.0).Single();
            Assert.IsEmpty(coordinator.Receive(Stage("head-valid.json", "turn-cats-1"), Runtime("turn-cats-1")));
            IReadOnlyList<ScenarioEffect> afterFallback = coordinator.AcknowledgeBeat(fallback.stageId, fallback.beatId);
            Assert.IsEmpty(afterFallback);
            Assert.IsFalse(afterFallback.Any(effect => effect.stage == "head"));
        }

        [Test]
        public void SameTickBodyAndFallbackRaceHasOneOpening()
        {
            foreach (bool fallbackTicksFirst in new[] { false, true })
            {
                var coordinator = new ScenarioTurnCoordinator();
                coordinator.Begin(Input("turn-cats-1"), 0);
                if (!fallbackTicksFirst)
                {
                    ScenarioEffect body = coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1"))
                        .Single(effect => effect.type == "show_dialogue");
                    coordinator.AcknowledgeBeat(body.stageId, body.beatId);
                    Assert.IsEmpty(coordinator.Tick(5.0));
                }
                else
                {
                    ScenarioEffect fallback = coordinator.Tick(5.0).Single();
                    Assert.AreEqual("fallback", fallback.stage);
                    Assert.IsEmpty(coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1")));
                    Assert.IsTrue(coordinator.AcknowledgeBeat(fallback.stageId, fallback.beatId)
                        .Any(effect => effect.type == "show_dialogue" && effect.stage == "main"));
                }
            }
        }

        [Test]
        public void DeadlineReplacesAnUnacknowledgedStageInsteadOfAssumingItWasVisible()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioEffect uncommittedMain = coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1"))
                .Single(effect => effect.type == "show_dialogue");
            ScenarioEffect fallback = coordinator.Tick(5.0).Single();
            Assert.AreEqual("fallback", fallback.stage);
            Assert.IsEmpty(coordinator.AcknowledgeBeat(uncommittedMain.stageId, uncommittedMain.beatId));
            Assert.IsTrue(coordinator.AcknowledgeBeat(fallback.stageId, fallback.beatId)
                .Any(effect => effect.type == "show_dialogue" && effect.stage == "main"));
        }

        [Test]
        public void FailedMainAfterHeadOffersRecoveryWithoutASecondOpening()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioEffect head = coordinator.Receive(Stage("head-valid.json", "turn-cats-1"), Runtime("turn-cats-1")).Single();
            coordinator.AcknowledgeBeat(head.stageId, head.beatId);
            ScenarioTurnReceipt failed = Receipt("turn-cats-1", "ready", "failed");
            IReadOnlyList<ScenarioEffect> effects = coordinator.Receive(failed, Runtime("turn-cats-1"));
            Assert.IsTrue(effects.Any(effect => effect.type == "turn_recovery"));
            Assert.IsFalse(effects.Any(effect => effect.type == "show_dialogue"));
        }

        [Test]
        public void NewTurnInvalidatesOldStagesAndDismissedTurnNeverAutoPresentsLateBody()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            IReadOnlyList<ScenarioEffect> replaced = coordinator.Begin(Input("turn-cats-2"), 1);
            Assert.IsTrue(replaced.Any(effect => effect.type == "supersede_turn" && effect.outcome == "turn-cats-1"));
            Assert.IsEmpty(coordinator.Receive(Stage("head-valid.json", "turn-cats-1"), Runtime("turn-cats-1")));
            coordinator.Dismiss();
            IReadOnlyList<ScenarioEffect> late = coordinator.Receive(Stage("main-cats.json", "turn-cats-2"), Runtime("turn-cats-2"));
            Assert.IsTrue(late.Any(effect => effect.type == "late_result_saved"));
            Assert.IsFalse(late.Any(effect => effect.type == "show_dialogue" || effect.type == "scenario_main_ready"));
        }

        [Test]
        public void DismissingAnUnacknowledgedMainStagePreservesItsCheckpointWithoutReplayingIt()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioEffect visible = coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1"))
                .Single(effect => effect.type == "show_dialogue");

            IReadOnlyList<ScenarioEffect> dismissed = coordinator.Dismiss();

            ScenarioEffect saved = dismissed.Single(effect => effect.type == "late_result_saved");
            Assert.AreSame(visible.scenarioStage, saved.scenarioStage);
            Assert.AreEqual("main", saved.stage);
            Assert.IsFalse(dismissed.Any(effect => effect.type == "show_dialogue"));
        }

        [Test]
        public void DismissalCheckpointStartsAtTheFirstUnacknowledgedMainBeat()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioStageResult stage = Stage("main-cats.json", "turn-cats-1");
            stage.scenario.steps[0].beats.Add(ScenarioTestData.Beat("answer-2", "And another thought."));
            ScenarioEffect first = coordinator.Receive(stage, Runtime("turn-cats-1")).Single(effect => effect.type == "show_dialogue");
            ScenarioEffect second = coordinator.AcknowledgeBeat(first.stageId, first.beatId)
                .Single(effect => effect.type == "show_dialogue");

            ScenarioEffect saved = coordinator.Dismiss().Single(effect => effect.type == "late_result_saved");

            Assert.AreEqual("answer-2", second.beatId);
            Assert.AreEqual(1, saved.beatIndex);
        }

        [Test]
        public void QuitCancelsOutstandingTurnAndIgnoresLateStageCallbacks()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            Assert.IsTrue(coordinator.Quit().Any(effect => effect.type == "cancel_turn"));
            Assert.IsEmpty(coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), Runtime("turn-cats-1")));
        }

        [Test]
        public void ChangedConsentOrGroundedRevisionRejectsPendingStage()
        {
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioRuntimeSnapshot changed = new ScenarioRuntimeSnapshot("", "run-1", "turn-cats-1", 2, "",
                new[] { "cats" }, new[] { "gentle", "thinking", "happy" }, new ScenarioCapabilitySnapshot[0],
                new ScenarioObjectSnapshot[0], new string[0]);
            Assert.IsTrue(coordinator.Receive(Stage("main-cats.json", "turn-cats-1"), changed)
                .Any(effect => effect.type == "stage_rejected"));
        }

        [Test]
        public void UnsafePairedMainOutputIsRejectedWithoutReplacingVisibleOpening()
        {
            foreach (ScenarioPairedOutputCase item in PairedFixtures().cases)
            {
                if (string.IsNullOrEmpty(item.mainFixture)) continue;
                ScenarioGroundedConstraint[] constraints = string.IsNullOrEmpty(item.prohibitedPhrase) ? new ScenarioGroundedConstraint[0] :
                    new[] { new ScenarioGroundedConstraint(item.name, item.constraintCode, new[] { item.prohibitedPhrase }) };
                var coordinator = new ScenarioTurnCoordinator();
                coordinator.Begin(Input("turn-cats-1", constraints), 0);
                ScenarioEffect head = coordinator.Receive(Stage(item.headFixture, "turn-cats-1", item.headDisplayOverride), Runtime("turn-cats-1")).Single();
                coordinator.AcknowledgeBeat(head.stageId, head.beatId);
                IReadOnlyList<ScenarioEffect> effects = coordinator.Receive(Stage(item.mainFixture, "turn-cats-1", item.mainDisplayOverride), Runtime("turn-cats-1"));
                Assert.IsTrue(effects.Any(effect => effect.type == "scenario_rejected" && effect.outcome == item.expectedRejection), item.name);
                Assert.IsTrue(effects.Any(effect => effect.type == "turn_recovery"));
                Assert.IsFalse(effects.Any(effect => effect.stage == "main" && effect.type == "show_dialogue"));
            }
        }

        [Test]
        public void HeadAskingAQuestionIsRejectedBeforeItCanDependOnAnUnansweredPlayerReply()
        {
            ScenarioPairedOutputCase item = Array.Find(PairedFixtures().cases, value => value.name == "unanswered-head-question");
            var coordinator = new ScenarioTurnCoordinator();
            coordinator.Begin(Input("turn-cats-1"), 0);
            ScenarioStageResult questionHead = Stage(item.headFixture, "turn-cats-1", item.headDisplayOverride);
            Assert.IsTrue(coordinator.Receive(questionHead, Runtime("turn-cats-1"))
                .Any(effect => effect.type == "scenario_rejected" && effect.outcome == item.expectedRejection));
        }

        private static ScenarioTurnInput Input(string turnId, IEnumerable<ScenarioGroundedConstraint> constraints = null)
        {
            ConversationJobV2 conversation = (ConversationJobV2)ConversationProtocol.ParseEnvelope(ScenarioTestData.ConversationFixture("turn-valid.json")).payload;
            string envelope = ConversationProtocol.SerializeTurnEnvelope(turnId, conversation);
            int start = envelope.IndexOf("\"payload\":", StringComparison.Ordinal) + "\"payload\":".Length;
            string conversationJson = envelope.Substring(start, envelope.Length - start - 1);
            string runtime = "{\"scenarioId\":\"\",\"runId\":\"run-1\",\"turnId\":\"" + turnId + "\",\"contextRevision\":1," +
                "\"checkpointStepId\":\"\",\"knownTopicIds\":[\"cats\"],\"knownIntentIds\":[]," +
                "\"allowedExpressions\":[\"gentle\",\"thinking\",\"happy\"],\"capabilities\":[],\"objects\":[],\"waitConditions\":[]}";
            string wire = "{\"protocolVersion\":4,\"type\":\"scenario_turn\",\"requestId\":\"" + turnId + "\",\"payload\":{" +
                "\"turnId\":\"" + turnId + "\",\"turnVersion\":1,\"operation\":\"begin\",\"onlineEnabled\":true," +
                "\"conversation\":" + conversationJson + ",\"memory\":null,\"runtime\":" + runtime + ",\"answer\":null}}";
            ScenarioTurnJobV4 job = (ScenarioTurnJobV4)ScenarioTurnProtocol.ParseEnvelope(wire).payload;
            return new ScenarioTurnInput(turnId, job, new[] { Beat("fallback-0", "Let me think.") }, constraints);
        }

        private static ScenarioRuntimeSnapshot Runtime(string turnId)
        {
            ScenarioRuntimeSnapshot source = ScenarioTestData.RuntimeSnapshot();
            return ScenarioTestData.WithTurn(source, turnId);
        }

        private static ScenarioStageResult Stage(string fixture, string turnId, string replaceDisplay = null)
        {
            string json = ScenarioTestData.Fixture(fixture).Replace("turn-cats-1", turnId);
            if (replaceDisplay != null) json = json.Replace("I do like cats.", replaceDisplay).Replace("Mm-hm.", replaceDisplay);
            ScenarioStageResult stage = (ScenarioStageResult)ScenarioTurnProtocol.ParseEnvelope(json).payload;
            return stage;
        }

        private static ScenarioPairedOutputFixtures PairedFixtures()
        {
            string path = Path.Combine("Tools", "ScenarioRuntime", "Fixtures", "v4", "paired-output-cases.json");
            var serializer = new DataContractJsonSerializer(typeof(ScenarioPairedOutputFixtures));
            using (var source = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path))))
                return (ScenarioPairedOutputFixtures)serializer.ReadObject(source);
        }

        private static ScenarioTurnReceipt Receipt(string turnId, string head, string main, string terminalReason = "failed")
        {
            string json = "{\"protocolVersion\":4,\"type\":\"scenario_complete\",\"requestId\":\"" + turnId + "\",\"payload\":{" +
                "\"turnId\":\"" + turnId + "\",\"turnVersion\":1," +
                "\"head\":{\"stage\":\"head\",\"status\":\"" + head + "\",\"usage\":{\"inputTokens\":0,\"outputTokens\":0,\"totalTokens\":0,\"costUsd\":null}}," +
                "\"main\":{\"stage\":\"main\",\"status\":\"" + main + "\",\"usage\":{\"inputTokens\":0,\"outputTokens\":0,\"totalTokens\":0,\"costUsd\":null}}," +
                "\"terminalReason\":\"" + terminalReason + "\"}}";
            return (ScenarioTurnReceipt)ScenarioTurnProtocol.ParseEnvelope(json).payload;
        }

        private static ScenarioBeat Beat(string id, string display)
        { return new ScenarioBeat { id = id, text = "うん。", displayText = display, language = "japanese", expression = "thinking" }; }
    }
}
