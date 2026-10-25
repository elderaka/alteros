using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Dialogue;
using AlterOS.Topics;

namespace AlterOS.Tests
{
    public sealed class ConversationContractTests
    {
        [Test]
        public void RequestParsesAndRequiresChannelAndText()
        {
            ConversationRequest request = ConversationContractValidator.ParseRequest(
                "{\"schemaVersion\":1,\"requestId\":\"r1\",\"sessionId\":\"s1\",\"channel\":\"direct\",\"text\":\"hello\"}");
            Assert.AreEqual("direct", request.channel);
            Assert.AreEqual("hello", request.text);
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseRequest(
                "{\"schemaVersion\":1,\"requestId\":\"r1\",\"sessionId\":\"s1\",\"text\":\"hello\"}"));
        }

        [Test]
        public void RequestRejectsUnknownDuplicateWrongTypeAndOversizeFields()
        {
            string prefix = "{\"schemaVersion\":1,\"requestId\":\"r1\",\"sessionId\":\"s1\",\"channel\":\"direct\",\"text\":\"hello\"";
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseRequest(prefix + ",\"routePath\":\"x\"}"));
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseRequest(prefix + ",\"text\":\"again\"}"));
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseRequest(prefix.Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\"") + "}"));
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseRequest(
                "{\"schemaVersion\":1,\"requestId\":\"r1\",\"sessionId\":\"s1\",\"channel\":\"direct\",\"text\":\"" + new string('x', 2049) + "\"}"));
        }

        [Test]
        public void RequestRequiresActiveResponseForActiveTopic()
        {
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseRequest(
                "{\"schemaVersion\":1,\"requestId\":\"r1\",\"sessionId\":\"s1\",\"channel\":\"phone\",\"text\":\"yes\",\"activeTopicId\":\"tea\"}"));
        }

        [Test]
        public void RequestEnforcesChannelAndWhitespaceRulesInMemory()
        {
            var request = new ConversationRequest { requestId = "r", sessionId = "s", channel = "telepathy", text = "hello" };
            Assert.IsNotEmpty(ConversationContractValidator.ValidateRequest(request));
            request.channel = "phone";
            request.text = " \r\n ";
            Assert.IsNotEmpty(ConversationContractValidator.ValidateRequest(request));
        }

        [Test]
        public void ZeroSuggestionsAreValidAndLabelPromptArePlainFields()
        {
            ConversationJudgment judgment = ConversationContractValidator.ParseJudgment(
                JudgmentJson("clarify", "", "{\"nodes\":[" + NodeJson("n1", "Which one?", "") + "]}", "[]", "[]"));
            Assert.AreEqual(0, judgment.suggestedReplies.Count);
            Assert.AreEqual("clarify", judgment.kind);

            string suggestion = "[{\"label\":\"Tea?\",\"prompt\":\"Shall we make tea?\"}]";
            judgment = ConversationContractValidator.ParseJudgment(
                JudgmentJson("novel_candidate", "", "{\"nodes\":[" + NodeJson("n1", "Tea sounds good.", "") + "]}", suggestion, "[]"));
            Assert.AreEqual("Tea?", judgment.suggestedReplies[0].label);
            Assert.AreEqual("Shall we make tea?", judgment.suggestedReplies[0].prompt);
            Assert.IsNull(typeof(SuggestedPlayerReply).GetField("action"));
        }

        [Test]
        public void JudgmentRejectsUnknownAndMalformedNodes()
        {
            string valid = JudgmentJson("clarify", "", "{\"nodes\":[" + NodeJson("n1", "Which one?", "") + "]}", "[]", "[]");
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseJudgment(valid.Replace("\"kind\":\"clarify\"", "\"kind\":\"invent\"")));
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseJudgment(valid.Replace("\"choices\":[]", "\"choices\":[{\"label\":\"Run\",\"nextId\":\"x\"}]")));
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseJudgment(valid.Replace("\"text\":\"Which one?\"", "\"text\":\"" + new string('x', 601) + "\"")));
            ConversationJudgment unsupportedCue = ConversationContractValidator.ParseJudgment(valid.Replace("\"expression\":\"\"", "\"expression\":\"unsupported\""));
            var request = new ConversationRequest { requestId = "r1", sessionId = "s1", channel = "direct", text = "which?" };
            var candidates = new TopicCandidateSet { indexIdentity = "synthetic-v1", source = "synthetic" };
            Assert.IsNotEmpty(ConversationContractValidator.ValidateJudgment(request, candidates, unsupportedCue, new HashSet<string>()));
        }

        [Test]
        public void JudgmentEnforcesLinearNextIdsSuggestionBoundsAndProtocolLimit()
        {
            string twoNodes = "{\"nodes\":[" + NodeJson("n1", "First.", "elsewhere") + "," + NodeJson("n2", "Second.", "") + "]}";
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseJudgment(
                JudgmentJson("clarify", "", twoNodes, "[]", "[]")));
            string fourSuggestions = "[" + string.Join(",", new[]
            {
                "{\"label\":\"a\",\"prompt\":\"a\"}", "{\"label\":\"b\",\"prompt\":\"b\"}",
                "{\"label\":\"c\",\"prompt\":\"c\"}", "{\"label\":\"d\",\"prompt\":\"d\"}"
            }) + "]";
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseJudgment(
                JudgmentJson("clarify", "", "{\"nodes\":[" + NodeJson("n1", "ok", "") + "]}", fourSuggestions, "[]")));
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseRequest(new string(' ', ConversationContractValidator.MaximumProtocolBytes + 1)));
        }

        [Test]
        public void JudgmentRejectsTotalReplyTextAboveTheSharedLimit()
        {
            var nodes = new List<string>();
            for (int index = 0; index < 32; index++)
                nodes.Add(NodeJson("n" + index, new string('x', 400), index == 31 ? "" : "n" + (index + 1)));
            string reply = "{\"nodes\":[" + string.Join(",", nodes) + "]}";
            Assert.Throws<FormatException>(() => ConversationContractValidator.ParseJudgment(
                JudgmentJson("clarify", "", reply, "[]", "[]")));
        }

        [Test]
        public void AuthoredJudgmentMustNameAPreparedCandidateAndMatchRequest()
        {
            ConversationJudgment judgment = ConversationContractValidator.ParseJudgment(
                JudgmentJson("existing", "tea", "null", "[]", "[\"tea\"]"));
            var request = new ConversationRequest { requestId = "r1", sessionId = "s1", channel = "phone", text = "tea" };
            var candidates = new TopicCandidateSet
            {
                indexIdentity = "synthetic-v1", source = "synthetic",
                cards = new List<TopicCandidateCard> { new TopicCandidateCard { topicId = "tea", authoredAvailability = "ready" } }
            };
            Assert.IsEmpty(ConversationContractValidator.ValidateJudgment(request, candidates, judgment, new HashSet<string>()));
            judgment.requestId = "r2";
            Assert.IsNotEmpty(ConversationContractValidator.ValidateJudgment(request, candidates, judgment, new HashSet<string>()));
            judgment.requestId = "r1";
            candidates.cards[0].authoredAvailability = "unchecked";
            Assert.IsNotEmpty(ConversationContractValidator.ValidateJudgment(request, candidates, judgment, new HashSet<string>()));
        }

        [Test]
        public void ChannelMetadataDefaultsToEitherAndRejectsUnknownValues()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea", ConversationTestData.Scenario("direct", 1));
            definition.scenarios[0].channels = new List<string>();
            Assert.IsEmpty(TopicDefinitionValidator.Validate(definition));
            definition.scenarios[0].channels.Add("phone");
            Assert.IsEmpty(TopicDefinitionValidator.Validate(definition));
            definition.scenarios[0].channels.Add("telepathy");
            Assert.IsNotEmpty(TopicDefinitionValidator.Validate(definition));
        }

        internal static string JudgmentJson(string kind, string topicId, string reply, string suggestions, string references)
        {
            return "{\"schemaVersion\":1,\"requestId\":\"r1\",\"kind\":\"" + kind + "\",\"topicId\":\"" + topicId +
                "\",\"referencedTopicIds\":" + references + ",\"reply\":" + reply + ",\"suggestedReplies\":" + suggestions + "}";
        }

        internal static string NodeJson(string id, string text, string nextId)
        {
            return "{\"id\":\"" + id + "\",\"speaker\":\"alterra\",\"text\":\"" + text + "\",\"displayText\":\"\",\"language\":\"\",\"voice\":\"\",\"audioKey\":\"\",\"nextId\":\"" + nextId +
                "\",\"presentation\":{\"expression\":\"\",\"animation\":\"\"},\"choices\":[]}";
        }
    }

    internal static class ConversationTestData
    {
        internal static TopicDefinition Topic(string topicId, params TopicScenario[] scenarios)
        {
            return new TopicDefinition { topicId = topicId, anchors = new List<string> { topicId }, scenarios = new List<TopicScenario>(scenarios) };
        }

        internal static TopicScenario Scenario(string scenarioId, int priority, string channel = null)
        {
            var scenario = new TopicScenario
            {
                scenarioId = scenarioId, priority = priority, baseWeight = 1,
                nodes = new List<CanonicalDialogueNode> { new CanonicalDialogueNode { id = "n1", text = "Authored." } }
            };
            if (channel != null) scenario.channels.Add(channel);
            return scenario;
        }
    }
}
