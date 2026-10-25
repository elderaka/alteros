using System;
using System.Collections.Generic;
using NUnit.Framework;
using AlterOS.Conversation;
using AlterOS.Dialogue;
using AlterOS.Topics;

namespace AlterOS.Tests
{
    public sealed class UnifiedConversationPlannerTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        [Test]
        public void ExistingJudgmentReturnsAuthoredScenarioAndIgnoresGeneratedProse()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("authored", 1)), "tea", "ready");
            ConversationPlan plan = Plan(fixture, Judgment("existing", "tea", null, "tea"));
            Assert.AreEqual("authored", plan.outcome);
            Assert.AreEqual("tea", plan.topicId);
            Assert.AreEqual("authored", plan.scenarioId);
            Assert.IsNull(plan.reply);
            Assert.IsFalse(fixture.history.IsDirty);
        }

        [Test]
        public void RepeatableCompletionDoesNotForceGenerationAndUnseenScenarioWins()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea", ConversationTestData.Scenario("seen", 1), ConversationTestData.Scenario("fresh", 1));
            Fixture fixture = MakeFixture(definition, "tea", "ready");
            TopicPlaybackHandle handle = fixture.history.BeginPlayback(definition, fixture.instances["tea"], "seen", Now);
            fixture.history.ReportPresentedNode(handle, "n1", Now.AddSeconds(1));
            fixture.history.Complete(handle, Now.AddSeconds(2));
            fixture.candidates.cards[0].authoredAvailability = "ready";
            ConversationPlan plan = Plan(fixture, Judgment("existing", "tea", null, "tea"), Now.AddSeconds(3), 0.9);
            Assert.AreEqual("authored", plan.outcome);
            Assert.AreEqual("fresh", plan.scenarioId);
        }

        [Test]
        public void ContinuationReturnsValidatedBeatsInsteadOfRestartingAuthoredIntroduction()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("intro", 1)), "tea", "ready");
            ConversationRequest request = Request();
            request.activeResponseId = "response-1";
            request.activeTopicId = "tea";
            ConversationJudgment judgment = Judgment("continue", "", Reply("following up", "joy"), "tea");
            ConversationPlan plan = Plan(fixture, judgment, request);
            Assert.AreEqual("generated", plan.outcome);
            Assert.AreEqual("tea", plan.topicId);
            Assert.AreEqual("following up", plan.reply.nodes[0].text);
            Assert.IsFalse(plan.isNovelCandidate);
            Assert.AreNotSame(judgment.reply.nodes[0], plan.reply.nodes[0]);
        }

        [Test]
        public void LocalFallbackHonorsAHostBoundActiveTopicSelection()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("authored", 1)), "tea", "ready");
            ConversationRequest request = Request();
            request.activeResponseId = "response-scenario";
            request.activeTopicId = "tea";

            ConversationPlan plan = UnifiedConversationPlanner.Plan(request, fixture.candidates, null,
                fixture.catalogue, fixture.instances, new TopicContext(), fixture.history, Now, 0.5,
                new HashSet<string>(StringComparer.Ordinal));

            Assert.AreEqual("authored", plan.outcome);
            Assert.AreEqual("tea", plan.topicId);
        }

        [Test]
        public void BlockedReferenceCannotBeUsedToBypassGateButUnrelatedBlockDoesNotSuppressNovelReply()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("tea", 1)), "tea", "ready");
            fixture.candidates.cards.Add(new TopicCandidateCard { topicId = "dispenser", authoredAvailability = "blocked", examples = new List<string>() });
            ConversationPlan blocked = Plan(fixture, Judgment("novel_candidate", "", Reply("drink water", ""), "dispenser"));
            Assert.AreEqual("unavailable", blocked.outcome);
            ConversationPlan unrelated = Plan(fixture, Judgment("novel_candidate", "", Reply("I saw a cat", ""), "tea"));
            Assert.AreEqual("generated", unrelated.outcome);
            Assert.IsTrue(unrelated.isNovelCandidate);
        }

        [Test]
        public void ContinuationSurvivesConsumedAuthoredReplayAndKnownUncoveredDoesNotBecomeNovel()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea", ConversationTestData.Scenario("intro", 1, "direct"));
            definition.scenarios[0].consumptionNodeId = "n1";
            definition.defaultReplayPolicy = TopicReplayPolicy.Once;
            Fixture fixture = MakeFixture(definition, "tea", "blocked");
            TopicPlaybackHandle once = fixture.history.BeginPlayback(definition, fixture.instances["tea"], "intro", Now);
            fixture.history.ReportPresentedNode(once, "n1", Now.AddSeconds(1));
            fixture.history.Complete(once, Now.AddSeconds(2));
            Assert.IsTrue(fixture.history.IsConsumed("tea-instance"));
            Assert.IsNull(TopicScenarioSelector.SelectForConversation(definition, fixture.instances["tea"],
                new TopicContext(), fixture.history, Now.AddSeconds(3), 0, "direct").SelectedScenario);
            ConversationRequest request = Request(); request.activeResponseId = "response-1"; request.activeTopicId = "tea";
            ConversationPlan continuation = Plan(fixture, Judgment("continue", "", Reply("follow up", ""), "tea"), request, Now.AddSeconds(3));
            Assert.AreEqual("generated", continuation.outcome);
            ConversationPlan uncovered = Plan(fixture, Judgment("existing_uncovered", "tea", null, "tea"));
            Assert.AreEqual("unavailable", uncovered.outcome);
            Assert.AreEqual("KnownTopicCoverageDeferred", uncovered.diagnosticCode);
        }

        [Test]
        public void GeneratedBranchesRecheckEveryReferenceAndHostTopicExceptActiveContinuation()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("intro", 1)), "tea", "ready");
            fixture.catalogue.Add("dispenser", ConversationTestData.Topic("dispenser", ConversationTestData.Scenario("drink", 1)));
            fixture.instances.Add("dispenser", TopicInstance.Create(fixture.catalogue["dispenser"], "dispenser-instance", "authored"));
            fixture.candidates.cards.Add(new TopicCandidateCard { topicId = "dispenser", authoredAvailability = "blocked", examples = new List<string>() });
            ConversationRequest request = Request(); request.activeResponseId = "response-1"; request.activeTopicId = "tea";

            fixture.candidates.cards[0].authoredAvailability = "blocked";
            Assert.AreEqual("unavailable", Plan(fixture, Judgment("novel_candidate", "", Reply("new topic", ""), ""), request).outcome);
            fixture.candidates.cards[0].authoredAvailability = "ready";
            Assert.AreEqual("unavailable", Plan(fixture, Judgment("continue", "", Reply("follow up", ""), "tea,dispenser"), request).outcome);
            Assert.AreEqual("unavailable", Plan(fixture, Judgment("clarify", "", Reply("which one?", ""), "dispenser"), request).outcome);
            fixture.candidates.cards.Add(new TopicCandidateCard { topicId = "missing", authoredAvailability = "ready", examples = new List<string>() });
            Assert.AreEqual("unavailable", Plan(fixture, Judgment("clarify", "", Reply("which topic?", ""), "missing"), request).outcome);
            Assert.AreEqual("generated", Plan(fixture, Judgment("continue", "", Reply("follow up", ""), "tea"), request).outcome);
        }

        [Test]
        public void MalformedInMemoryJudgmentsReturnUnavailableInsteadOfThrowing()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("intro", 1)), "tea", "ready");
            ConversationJudgment nullReferences = Judgment("novel_candidate", "", Reply("hello", ""), "");
            nullReferences.referencedTopicIds = null;
            Assert.DoesNotThrow(() => Assert.AreEqual("unavailable", Plan(fixture, nullReferences).outcome));

            ConversationJudgment nullFollowingNode = Judgment("clarify", "", Reply("hello", ""), "");
            nullFollowingNode.reply.nodes.Add(null);
            Assert.DoesNotThrow(() => Assert.AreEqual("unavailable", Plan(fixture, nullFollowingNode).outcome));

            ConversationJudgment cue = Judgment("clarify", "", Reply("hello", "joy"), "");
            Assert.AreEqual("unavailable", UnifiedConversationPlanner.Plan(Request(), fixture.candidates, cue, fixture.catalogue,
                fixture.instances, new TopicContext(), fixture.history, Now, 0, null).outcome);
            TopicCandidateSet nullCards = new TopicCandidateSet { indexIdentity = "synthetic-v1", source = "synthetic", cards = null };
            Assert.DoesNotThrow(() => Assert.IsNotEmpty(ConversationContractValidator.ValidateJudgment(Request(), nullCards,
                Judgment("clarify", "", Reply("hello", ""), ""), new HashSet<string>())));
        }

        [Test]
        public void ClarificationAndNovelRepliesRemainProvisionalAndDoNotPublish()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("tea", 1)), "tea", "ready");
            int definitionCount = fixture.catalogue.Count;
            ConversationPlan clarify = Plan(fixture, Judgment("clarify", "", Reply("What do you mean?", ""), ""));
            Assert.AreEqual("clarify", clarify.outcome);
            ConversationPlan novel = Plan(fixture, Judgment("novel_candidate", "", Reply("I love cats.", ""), ""));
            Assert.AreEqual("generated", novel.outcome);
            Assert.IsTrue(novel.isNovelCandidate);
            Assert.AreEqual(definitionCount, fixture.catalogue.Count);
            Assert.IsFalse(fixture.history.IsDirty);
        }

        [Test]
        public void NonEnglishSuggestionsAreDroppedWithoutDiscardingTheGeneratedReply()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("tea", 1)), "tea", "ready");
            ConversationJudgment judgment = Judgment("clarify", "", Reply("What kind of tea do you like?", ""), "");
            judgment.suggestedReplies.Add(new SuggestedPlayerReply { label = "次は何?", prompt = "Tell me more about it." });
            judgment.suggestedReplies.Add(new SuggestedPlayerReply { label = "Continue", prompt = "Tell me another detail." });

            ConversationPlan plan = Plan(fixture, judgment);

            Assert.AreEqual("clarify", plan.outcome);
            Assert.AreEqual("What kind of tea do you like?", plan.reply.nodes[0].text);
            Assert.AreEqual(1, plan.suggestedReplies.Count);
            Assert.AreEqual("Continue", plan.suggestedReplies[0].label);
        }

        [Test]
        public void WeakLocalMatchAndInvalidJudgmentFailClosed()
        {
            Fixture fixture = MakeFixture(ConversationTestData.Topic("tea", ConversationTestData.Scenario("tea", 1)), "tea", "ready");
            fixture.candidates.cards[0].similarity = 0.2f;
            Assert.AreEqual("unavailable", Plan(fixture, null).outcome);
            ConversationPlan invalid = Plan(fixture, Judgment("existing", "outside", null, "outside"));
            Assert.AreEqual("InvalidJudgment", invalid.diagnosticCode);
        }

        [Test]
        public void PhoneAndDirectUseSameCompletionEvidenceButTheirOwnScenarios()
        {
            TopicDefinition definition = ConversationTestData.Topic("tea",
                ConversationTestData.Scenario("direct", 1, "direct"), ConversationTestData.Scenario("phone", 1, "phone"));
            Fixture fixture = MakeFixture(definition, "tea", "ready");
            TopicPlaybackHandle direct = fixture.history.BeginPlayback(definition, fixture.instances["tea"], "direct", Now);
            fixture.history.ReportPresentedNode(direct, "n1", Now.AddSeconds(1));
            fixture.history.Complete(direct, Now.AddSeconds(2));
            ConversationRequest phoneRequest = Request(); phoneRequest.channel = "phone";
            ConversationPlan phone = Plan(fixture, Judgment("existing", "tea", null, "tea"), phoneRequest, Now.AddSeconds(3));
            Assert.AreEqual("phone", phone.scenarioId);
            Assert.AreEqual(1, fixture.history.GetCompletionCount("tea-instance", "direct"));
        }

        private static ConversationPlan Plan(Fixture fixture, ConversationJudgment judgment, DateTimeOffset? now = null, double draw = 0)
        { return Plan(fixture, judgment, Request(), now ?? Now, draw); }

        private static ConversationPlan Plan(Fixture fixture, ConversationJudgment judgment, ConversationRequest request, DateTimeOffset? now = null, double draw = 0)
        {
            return UnifiedConversationPlanner.Plan(request, fixture.candidates, judgment, fixture.catalogue, fixture.instances,
                new TopicContext(), fixture.history, now ?? Now, draw, new HashSet<string> { "joy" });
        }

        private static ConversationRequest Request()
        { return new ConversationRequest { requestId = "r1", sessionId = "s1", channel = "direct", text = "hello" }; }

        private static ConversationJudgment Judgment(string kind, string topicId, ConversationReply reply, string references)
        {
            return new ConversationJudgment
            {
                requestId = "r1", kind = kind, topicId = topicId, reply = reply,
                referencedTopicIds = string.IsNullOrEmpty(references) ? new List<string>() : new List<string> { references },
                suggestedReplies = new List<SuggestedPlayerReply>()
            };
        }

        private static ConversationReply Reply(string text, string expression)
        {
            return new ConversationReply
            {
                nodes = new List<CanonicalDialogueNode>
                {
                    new CanonicalDialogueNode { id = "reply-1", speaker = "alterra", text = text,
                        presentation = new DialoguePresentation { expression = expression, animation = "" }, choices = new List<CanonicalDialogueChoice>() }
                }
            };
        }

        private static Fixture MakeFixture(TopicDefinition definition, string topicId, string availability)
        {
            var fixture = new Fixture();
            TopicInstance instance = TopicInstance.Create(definition, topicId + "-instance", "authored");
            fixture.catalogue.Add(topicId, definition);
            fixture.instances.Add(topicId, instance);
            fixture.candidates = new TopicCandidateSet
            {
                indexIdentity = "synthetic-v1", source = "synthetic",
                cards = new List<TopicCandidateCard>
                {
                    new TopicCandidateCard { topicId = topicId, prompt = definition.prompt, examples = new List<string> { topicId }, similarity = 0.9f,
                        supportCount = 1, authoredAvailability = availability }
                }
            };
            return fixture;
        }

        private sealed class Fixture
        {
            public readonly Dictionary<string, TopicDefinition> catalogue = new Dictionary<string, TopicDefinition>();
            public readonly Dictionary<string, TopicInstance> instances = new Dictionary<string, TopicInstance>();
            public TopicCandidateSet candidates;
            public readonly TopicHistory history = new TopicHistory();
        }
    }
}
