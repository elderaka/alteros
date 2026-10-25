using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using AlterOS.Scenarios;

namespace AlterOS.Tests.Scenarios
{
    public sealed class ScenarioPresentationTests
    {
        [Test]
        public void MainClosingDialogueLeavesScenarioRunningAndWrapUpEndsIt()
        {
            ScenarioDefinition definition = new ScenarioDefinition
            {
                scenarioId = "scenario-main-close",
                topicId = "cats",
                entryStepId = "main",
                steps = new List<ScenarioStep>
                {
                    new ScenarioStep { id = "main", kind = "dialogue", beats = new List<ScenarioBeat> { ScenarioTestData.Beat("main-1", "Let's talk cats.") }, nextStepId = "action", closeExchange = true },
                    new ScenarioStep { id = "action", kind = "action", capabilityId = "fixture.activity.drawing_together",
                        arguments = new List<ScenarioActionArgument> { new ScenarioActionArgument { name = "targetObjectId", value = "fixture-drawing-table" } },
                        successNextStepId = "end", failureNextStepId = "end" },
                    new ScenarioStep { id = "end", kind = "end", outcome = "completed" }
                }
            };
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            var presenter = new ScenarioConversationPresentationModel();

            presenter.StartAfterCompletedDialogue(definition, "main", "run-1", context);

            Assert.IsFalse(presenter.Current.exchangeOpen);
            Assert.IsTrue(presenter.Current.scenarioActive);
            Assert.AreEqual("action", presenter.Current.stepId);
            Assert.IsEmpty(presenter.Current.answerOptions);

            presenter.WrapUp(context);
            Assert.IsFalse(presenter.Current.scenarioActive);
        }

        [Test]
        public void DismissResumeReplaysOnlyTheUnacknowledgedOpeningBeats()
        {
            ScenarioDefinition definition = new ScenarioDefinition
            {
                scenarioId = "resume-main", topicId = "cats", entryStepId = "main",
                steps = new List<ScenarioStep>
                {
                    new ScenarioStep { id = "main", kind = "dialogue", beats = new List<ScenarioBeat>
                    {
                        ScenarioTestData.Beat("seen", "Already seen."), ScenarioTestData.Beat("remaining", "Still to say.")
                    }, nextStepId = "end" },
                    new ScenarioStep { id = "end", kind = "end", outcome = "completed", closeExchange = true }
                }
            };
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            var presenter = new ScenarioConversationPresentationModel();

            IReadOnlyList<ScenarioEffect> effects = presenter.StartAfterUnacknowledgedBeat(definition, "main", 1, "run-1", context);

            ScenarioEffect resumed = effects.Single(effect => effect.type == "show_dialogue");
            CollectionAssert.AreEqual(new[] { "remaining" }, resumed.step.beats.ConvertAll(beat => beat.id));
            Assert.IsTrue(presenter.AcknowledgeDialogueBeat("main", "remaining"));
            Assert.IsTrue(presenter.CompleteDialogue("run-1", "main").Any(effect => effect.type == "end_scenario"));
        }

        [Test]
        public void DismissResumeReplaysOnlyUnacknowledgedContinuationBeats()
        {
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            presenter.Start(ScenarioTestData.QuestionDefinition(), "run-1", context);
            presenter.SelectAnswer(presenter.Current.answerOptions[0].stableId, context);

            Assert.IsTrue(presenter.AcknowledgeDialogueBeat("close", "thanks"));
            presenter.DismissExchange();

            IReadOnlyList<ScenarioEffect> resumed = presenter.ResumeForInteraction(context);

            Assert.IsTrue(resumed.Any(effect => effect.type == "end_scenario"));
            Assert.IsFalse(presenter.Current.scenarioActive);
        }

        [Test]
        public void DismissResumeTrimsAcknowledgedBeatFromMultiBeatContinuation()
        {
            ScenarioDefinition definition = ScenarioTestData.QuestionDefinition();
            definition.steps.Find(step => step.id == "close").beats.Add(ScenarioTestData.Beat("last", "Last beat."));
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            presenter.Start(definition, "run-1", context);
            presenter.SelectAnswer(presenter.Current.answerOptions[0].stableId, context);

            Assert.IsTrue(presenter.AcknowledgeDialogueBeat("close", "thanks"));
            presenter.DismissExchange();
            IReadOnlyList<ScenarioEffect> resumed = presenter.ResumeForInteraction(context);

            ScenarioEffect dialogue = resumed.Single(effect => effect.type == "show_dialogue");
            CollectionAssert.AreEqual(new[] { "last" }, dialogue.step.beats.ConvertAll(beat => beat.id));
        }

        [Test]
        public void DismissDuringPartlyDisplayedBeatResumesThatBeatUntilAdvanceBoundary()
        {
            ScenarioDefinition definition = ScenarioTestData.QuestionDefinition();
            definition.steps.Find(step => step.id == "close").beats.Add(ScenarioTestData.Beat("last", "Last beat."));
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            presenter.Start(definition, "run-1", context);
            presenter.SelectAnswer(presenter.Current.answerOptions[0].stableId, context);

            // First-visible transcript acknowledgement is deliberately not the advance checkpoint.
            presenter.DismissExchange();
            IReadOnlyList<ScenarioEffect> resumed = presenter.ResumeForInteraction(context);

            ScenarioEffect dialogue = resumed.Single(effect => effect.type == "show_dialogue");
            CollectionAssert.AreEqual(new[] { "thanks", "last" }, dialogue.step.beats.ConvertAll(beat => beat.id));
            Assert.IsEmpty(presenter.CompleteDialogue("run-1", "close"));
            Assert.IsTrue(presenter.Current.scenarioActive);
        }

        [Test]
        public void DialogueCannotCompleteUntilEveryBeatHasDurableAcknowledgement()
        {
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            presenter.Start(ScenarioTestData.QuestionDefinition(), "run-1", context);
            presenter.SelectAnswer(presenter.Current.answerOptions[0].stableId, context);

            Assert.IsEmpty(presenter.CompleteDialogue("run-1", "close"));
            Assert.IsTrue(presenter.AcknowledgeDialogueBeat("close", "thanks"));
            Assert.IsTrue(presenter.CompleteDialogue("run-1", "close").Any(effect => effect.type == "end_scenario"));
        }

        [Test]
        public void AskOptionsHoldExchangeOpenAndTypedAnswerFollowsScenarioSuccessor()
        {
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();

            presenter.Start(ScenarioTestData.QuestionDefinition(), "run-1", context);

            Assert.IsTrue(presenter.Current.exchangeOpen);
            Assert.AreEqual("What do you like?", presenter.Current.question.displayText);
            Assert.AreEqual("Playful", presenter.Current.answerOptions[0].label);
            Assert.IsTrue(presenter.CanSelectAnswer(presenter.Current.answerOptions[0].stableId, context));
            Assert.IsTrue(presenter.CanSubmitFreeText("A bounded answer", context));
            Assert.IsFalse(presenter.CanSubmitFreeText(new string('x', 601), context));
            IReadOnlyList<ScenarioEffect> effects = presenter.SubmitFreeText("I love their purrs", context);
            Assert.AreEqual("show_dialogue", effects[0].type);
            Assert.AreEqual("close", presenter.Current.stepId);
        }

        [Test]
        public void PresentationExposesFreeTextPermissionOnlyForAnAskThatAllowsIt()
        {
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            presenter.Start(ScenarioTestData.QuestionDefinition(), "run-1", context);
            Assert.IsTrue(presenter.Current.acceptsFreeText);

            presenter.SelectAnswer(presenter.Current.answerOptions[0].stableId, context);
            Assert.IsFalse(presenter.Current.acceptsFreeText);
        }

        [Test]
        public void AskOptionIdentityIsStableAndStaleButtonCannotAnswerNewQuestion()
        {
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            presenter.Start(ScenarioTestData.QuestionDefinition(), "run-1", context);
            string oldOptionId = presenter.Current.answerOptions[0].stableId;

            IReadOnlyList<ScenarioEffect> accepted = presenter.SelectAnswer(oldOptionId, context);
            Assert.AreEqual("show_dialogue", accepted[0].type);
            Assert.AreEqual("close", presenter.Current.stepId);

            presenter.WrapUp(context);
            presenter.Start(ScenarioTestData.QuestionDefinition(), "run-2", ScenarioTestData.WithRun(context, "run-2"));
            Assert.IsEmpty(presenter.SelectAnswer(oldOptionId, ScenarioTestData.WithRun(context, "run-2")));
            Assert.AreEqual("run-2", presenter.Current.runId);
            Assert.AreEqual("ask", presenter.Current.stepId);
        }

        [Test]
        public void DismissDoesNotEndScenarioAndNextInteractionReopensCurrentQuestion()
        {
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            presenter.Start(ScenarioTestData.QuestionDefinition(), "run-1", context);

            presenter.DismissExchange();
            Assert.IsFalse(presenter.Current.exchangeOpen);
            Assert.IsTrue(presenter.Current.scenarioActive);

            IReadOnlyList<ScenarioEffect> resumed = presenter.ResumeForInteraction(context);
            Assert.AreEqual("ask_player", resumed[0].type);
            Assert.IsTrue(presenter.Current.exchangeOpen);
            Assert.AreEqual("ask", presenter.Current.stepId);
            Assert.AreEqual("favorite-trait", presenter.Current.questionId);
            Assert.AreEqual(1, presenter.Current.answerOptions.Count);
        }

        [Test]
        public void SuggestedTopicsWaitForNextInteractionAndUseCurrentEligibility()
        {
            var presenter = new ScenarioConversationPresentationModel();
            ScenarioRuntimeSnapshot first = ScenarioTestData.RuntimeSnapshot();
            presenter.ReplaceTopicOffers(new[] { new ScenarioTopicOffer { offerId = "cats-next", label = "More about cats", topicId = "cats" } }, first.turnId);

            Assert.IsEmpty(presenter.GetTopicOffers(first));
            ScenarioRuntimeSnapshot next = ScenarioTestData.WithTurn(first, "turn-2");
            Assert.AreEqual("cats-next", presenter.GetTopicOffers(next)[0].offerId);
            Assert.IsEmpty(presenter.GetTopicOffers(ScenarioTestData.WithKnownTopics(next, "tea")));
        }
    }
}
