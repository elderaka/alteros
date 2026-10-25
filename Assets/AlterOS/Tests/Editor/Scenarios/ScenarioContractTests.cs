using System;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Scenarios;

namespace AlterOS.Tests.Scenarios
{
    public sealed class ScenarioContractTests
    {
        [Test]
        public void CatAndSyntheticDrawingMainStagesParseAsTypedScenarios()
        {
            ScenarioProtocolMessage catsMessage = ScenarioTurnProtocol.ParseEnvelope(ScenarioTestData.Fixture("main-cats.json"));
            ScenarioProtocolMessage drawingMessage = ScenarioTurnProtocol.ParseEnvelope(ScenarioTestData.Fixture("main-drawing-simulated.json"));
            ScenarioStageResult cats = (ScenarioStageResult)catsMessage.payload;
            ScenarioStageResult drawing = (ScenarioStageResult)drawingMessage.payload;

            Assert.AreEqual("main", cats.stage);
            Assert.AreEqual("cats", cats.scenario.topicId);
            Assert.AreEqual("ask", cats.scenario.steps[1].kind);
            Assert.AreEqual("Thanks for telling me.", cats.scenario.steps[2].beats[0].displayText);
            Assert.AreEqual("drawing_together", drawing.scenario.topicId);
            Assert.AreEqual("fixture.activity.drawing_together", drawing.scenario.steps[1].capabilityId);
            Assert.IsEmpty(ScenarioDefinitionValidator.Validate(drawing.scenario, ScenarioTestData.RuntimeSnapshot()));
        }

        [Test]
        public void MainStageMustNameTheScenarioEntryDialogueAsItsInitialSegment()
        {
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(
                ScenarioTestData.Fixture("main-cats.json").Replace("\"segmentStepId\": \"opening\"", "\"segmentStepId\": \"closing\"")));
        }

        [Test]
        public void ProtocolRejectsUnknownStagesAndDuplicateProperties()
        {
            string dessert = ScenarioTestData.Fixture("main-cats.json").Replace("\"stage\": \"main\"", "\"stage\": \"dessert\"");
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(dessert));
            string duplicate = ScenarioTestData.Fixture("head-valid.json").Replace("\"stage\": \"head\"", "\"stage\": \"head\", \"stage\": \"dessert\"");
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(duplicate));
        }

        [Test]
        public void V4TurnNestsTheExistingV2ConversationWithoutChangingItsContract()
        {
            ConversationProtocolMessage oldTurn = ConversationProtocol.ParseEnvelope(ScenarioTestData.ConversationFixture("turn-valid.json"));
            string oldWire = ConversationProtocol.SerializeTurnEnvelope(oldTurn.requestId, (ConversationJobV2)oldTurn.payload);
            int payloadIndex = oldWire.IndexOf("\"payload\":", StringComparison.Ordinal) + "\"payload\":".Length;
            string jobPayload = oldWire.Substring(payloadIndex, oldWire.Length - payloadIndex - 1);
            const string requestId = "scenario-turn-1";
            string runtime = "{\"scenarioId\":\"\",\"runId\":\"run-1\",\"turnId\":\"scenario-turn-1\",\"contextRevision\":2," +
                "\"checkpointStepId\":\"\",\"knownTopicIds\":[\"player_tired\"],\"knownIntentIds\":[],\"allowedExpressions\":[\"gentle\"]," +
                "\"capabilities\":[],\"objects\":[],\"waitConditions\":[]}";
            string wire = "{\"protocolVersion\":4,\"type\":\"scenario_turn\",\"requestId\":\"" + requestId + "\",\"payload\":{" +
                "\"turnId\":\"" + requestId + "\",\"turnVersion\":1,\"operation\":\"begin\",\"onlineEnabled\":true,\"conversation\":" + jobPayload +
                ",\"memory\":null,\"runtime\":" + runtime + ",\"answer\":null}}";

            ScenarioTurnJobV4 parsed = (ScenarioTurnJobV4)ScenarioTurnProtocol.ParseEnvelope(wire).payload;

            Assert.AreEqual("begin", parsed.operation);
            Assert.AreEqual("direct", parsed.conversation.request.channel);
            Assert.AreEqual("player_tired", parsed.conversation.candidates.cards[0].topicId);
        }

        [Test]
        public void ContractRejectsInvalidGraphLanguageCapabilityAndAmbiguousObjectBinding()
        {
            string duplicateStep = ScenarioTestData.Fixture("main-cats.json").Replace("\"id\": \"end\"", "\"id\": \"closing\"");
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(duplicateStep));
            string missingTarget = ScenarioTestData.Fixture("main-drawing-simulated.json").Replace("\"failureNextStepId\": \"unavailable\"", "\"failureNextStepId\": \"missing\"");
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(missingTarget));
            string wrongLanguage = ScenarioTestData.Fixture("main-cats.json").Replace("\"language\": \"japanese\"", "\"language\": \"english\"");
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(wrongLanguage));
            string unknownCapability = ScenarioTestData.Fixture("main-drawing-simulated.json").Replace("fixture.activity.drawing_together", "arbitrary.external.write");
            ScenarioDefinition unknown = ((ScenarioStageResult)ScenarioTurnProtocol.ParseEnvelope(unknownCapability).payload).scenario;
            Assert.IsNotEmpty(ScenarioDefinitionValidator.Validate(unknown, ScenarioTestData.RuntimeSnapshot()));
            ScenarioRuntimeSnapshot ambiguous = ScenarioTestData.WithAdditionalObject(ScenarioTestData.RuntimeSnapshot(),
                new ScenarioObjectSnapshot("fixture-drawing-table", "use_point"));
            ScenarioDefinition drawing = ((ScenarioStageResult)ScenarioTurnProtocol.ParseEnvelope(ScenarioTestData.Fixture("main-drawing-simulated.json")).payload).scenario;
            Assert.IsNotEmpty(ScenarioDefinitionValidator.Validate(drawing, ambiguous));
        }

        [Test]
        public void ContractEnforcesScenarioOfferQuestionAndBeatLimits()
        {
            ScenarioDefinition cats = ((ScenarioStageResult)ScenarioTurnProtocol.ParseEnvelope(ScenarioTestData.Fixture("main-cats.json")).payload).scenario;
            Assert.IsEmpty(ScenarioDefinitionValidator.Validate(ScenarioTestData.WithStepCount(cats, 64), ScenarioTestData.RuntimeSnapshot()));
            Assert.IsNotEmpty(ScenarioDefinitionValidator.Validate(ScenarioTestData.WithStepCount(cats, 65), ScenarioTestData.RuntimeSnapshot()));
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(ScenarioTestData.Fixture("head-five-beats.json")));
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(ScenarioTestData.Fixture("main-seventeen-beats.json")));
            Assert.Throws<FormatException>(() => ScenarioTurnProtocol.ParseEnvelope(ScenarioTestData.Fixture("offers-four.json")));
        }

        [Test]
        public void UnknownScenarioTopicIsRejectedWhenHostProvidedNoTopicCandidates()
        {
            ScenarioDefinition cats = ((ScenarioStageResult)ScenarioTurnProtocol.ParseEnvelope(ScenarioTestData.Fixture("main-cats.json")).payload).scenario;
            ScenarioRuntimeSnapshot runtime = ScenarioTestData.WithKnownTopics(ScenarioTestData.RuntimeSnapshot());
            Assert.IsNotEmpty(ScenarioDefinitionValidator.Validate(cats, runtime));
        }
    }
}
