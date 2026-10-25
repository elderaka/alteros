using NUnit.Framework;
using AlterOS.Scenarios;

namespace AlterOS.Tests.Scenarios
{
    public sealed class ScenarioQuestionTests
    {
        [Test]
        public void PendingQuestionAcceptsOnlyOneBoundAnswerAndRejectsStaleOrDuplicateInput()
        {
            ScenarioStep step = ScenarioTestData.QuestionDefinition().steps[0];
            var question = new ScenarioQuestionState();
            Assert.IsTrue(question.Begin(step, "run-1", 7));
            Assert.IsFalse(question.Begin(step, "run-1", 7));

            QuestionAnswerResult stale = question.Answer("wrong-question", "playful", "", 7);
            Assert.IsFalse(stale.accepted);
            Assert.AreEqual("favorite-trait", question.QuestionId);
            Assert.IsFalse(question.Answer("favorite-trait", "playful", "", 8).accepted);

            QuestionAnswerResult answer = question.Answer("favorite-trait", "playful", "", 7);
            Assert.IsTrue(answer.accepted);
            Assert.AreEqual("close", answer.nextStepId);
            Assert.IsFalse(question.Answer("favorite-trait", "playful", "", 7).accepted);
        }

        [Test]
        public void FreeTextSkipAndCancelFollowTheDeclaredSuccessorsWithoutTopicRerouting()
        {
            ScenarioStep step = ScenarioTestData.QuestionDefinition().steps[0];
            var question = new ScenarioQuestionState();
            question.Begin(step, "run-1", 8);
            QuestionAnswerResult freeText = question.Answer("favorite-trait", "", "I love their purrs", 8);
            Assert.IsTrue(freeText.accepted);
            Assert.AreEqual("close", freeText.nextStepId);
            Assert.AreEqual("I love their purrs", freeText.freeText);
            Assert.AreNotEqual("topic_route", freeText.kind);

            question.Begin(step, "run-1", 9);
            Assert.AreEqual("close", question.Skip("favorite-trait", 9).nextStepId);
            question.Begin(step, "run-1", 10);
            Assert.AreEqual("close", question.Cancel("favorite-trait", 10).nextStepId);
        }

        [Test]
        public void SessionTreatsPlayerAnswerAsThePendingQuestionAnswer()
        {
            var session = new ScenarioSession();
            ScenarioRuntimeSnapshot context = ScenarioTestData.RuntimeSnapshot();
            var effects = session.Start(ScenarioTestData.QuestionDefinition(), "run-1", context);
            Assert.AreEqual("ask_player", effects[0].type);
            effects = session.Advance(new ScenarioEvent { kind = "answer", runId = "run-1", stepId = "ask",
                questionId = "favorite-trait", answerId = "playful", contextRevision = context.contextRevision }, context);
            Assert.AreEqual("show_dialogue", effects[0].type);
            Assert.AreEqual("close", session.Running.currentStepId);
        }
    }
}
