using System;
using System.Linq;

namespace AlterOS.Scenarios
{
    public sealed class QuestionAnswerResult
    {
        public readonly bool accepted;
        public readonly string kind, reason, questionId, answerId, freeText, nextStepId;
        internal QuestionAnswerResult(bool valid, string answerKind, string error, string question,
            string option, string text, string successor)
        { accepted = valid; kind = answerKind ?? ""; reason = error ?? ""; questionId = question ?? ""; answerId = option ?? ""; freeText = text ?? ""; nextStepId = successor ?? ""; }
    }

    /// <summary>One bound, single-use question. It validates an answer but never routes free text.</summary>
    public sealed class ScenarioQuestionState
    {
        private string runId = "", stepId = "", questionId = "", freeTextNext = "", skipNext = "", cancelNext = "";
        private long contextRevision;
        private bool allowFreeText;
        private ScenarioQuestionOption[] options = new ScenarioQuestionOption[0];

        public bool HasPending { get { return !string.IsNullOrEmpty(questionId); } }
        public string QuestionId { get { return questionId; } }
        public string RunId { get { return runId; } }
        public string StepId { get { return stepId; } }

        public bool Begin(ScenarioStep askStep, string owningRunId, long revision)
        {
            if (HasPending || askStep == null || askStep.kind != "ask" || string.IsNullOrWhiteSpace(owningRunId) ||
                string.IsNullOrWhiteSpace(askStep.questionId) || revision < 0) return false;
            runId = owningRunId; stepId = askStep.id; questionId = askStep.questionId;
            contextRevision = revision; allowFreeText = askStep.allowFreeText;
            freeTextNext = askStep.freeTextNextStepId ?? ""; skipNext = askStep.skipNextStepId ?? "";
            cancelNext = askStep.cancelNextStepId ?? "";
            options = askStep.options == null ? new ScenarioQuestionOption[0] : askStep.options.ToArray();
            return true;
        }

        public QuestionAnswerResult Answer(string expectedQuestionId, string answerId, string freeText, long revision)
        {
            string text = freeText ?? "", optionId = answerId ?? "";
            if (!HasPending || expectedQuestionId != questionId || revision != contextRevision)
                return Reject("stale_question");
            if (string.IsNullOrWhiteSpace(optionId) == string.IsNullOrWhiteSpace(text)) return Reject("answer_shape_invalid");
            if (!string.IsNullOrEmpty(optionId))
            {
                ScenarioQuestionOption option = options.FirstOrDefault(item => item != null && item.optionId == optionId);
                if (option == null) return Reject("answer_option_invalid");
                return Accept("option", optionId, "", option.nextStepId);
            }
            if (!allowFreeText || text.Length > 2048) return Reject("free_text_unavailable");
            return Accept("free_text", "", text, freeTextNext);
        }

        public QuestionAnswerResult Skip(string expectedQuestionId, long revision)
        {
            if (!HasPending || expectedQuestionId != questionId || revision != contextRevision) return Reject("stale_question");
            return Accept("skip", "", "", skipNext);
        }

        public QuestionAnswerResult Cancel(string expectedQuestionId, long revision)
        {
            if (!HasPending || expectedQuestionId != questionId || revision != contextRevision) return Reject("stale_question");
            return Accept("cancel", "", "", cancelNext);
        }

        public void Clear()
        {
            runId = stepId = questionId = freeTextNext = skipNext = cancelNext = "";
            contextRevision = 0; allowFreeText = false; options = new ScenarioQuestionOption[0];
        }

        private QuestionAnswerResult Accept(string kind, string option, string text, string next)
        {
            string acceptedQuestion = questionId;
            var result = new QuestionAnswerResult(true, kind, "", acceptedQuestion, option, text, next);
            Clear();
            return result;
        }

        private QuestionAnswerResult Reject(string reason)
        { return new QuestionAnswerResult(false, "invalid", reason, questionId, "", "", ""); }
    }
}
