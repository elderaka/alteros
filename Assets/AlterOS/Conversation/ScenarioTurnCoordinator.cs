using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlterOS.Scenarios;

namespace AlterOS.Conversation
{
    public sealed class ScenarioGroundedConstraint
    {
        public readonly string constraintId, rejectionCode;
        public readonly IReadOnlyList<string> prohibitedPhrases;

        public ScenarioGroundedConstraint(string id, string code, IEnumerable<string> phrases)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128) throw new ArgumentException("Grounded constraint identity is invalid.", nameof(id));
            if (code != "contradicts_grounded_fact" && code != "unsupported_action_claim")
                throw new ArgumentException("Grounded constraint rejection code is unsupported.", nameof(code));
            var values = (phrases ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 256).ToList();
            if (values.Count == 0 || values.Count > 16) throw new ArgumentException("Grounded constraint phrases are invalid.", nameof(phrases));
            constraintId = id; rejectionCode = code; prohibitedPhrases = values.AsReadOnly();
        }
    }

    public sealed class ScenarioTurnInput
    {
        public readonly string requestId, turnId;
        public readonly int turnVersion;
        public readonly ScenarioTurnJobV4 job;
        public readonly ScenarioRuntimeSnapshot runtime;
        public readonly IReadOnlyList<ScenarioBeat> preparedOpening;
        public readonly IReadOnlyList<ScenarioGroundedConstraint> groundedConstraints;

        public ScenarioTurnInput(string id, ScenarioTurnJobV4 turn, IEnumerable<ScenarioBeat> localOpening,
            IEnumerable<ScenarioGroundedConstraint> constraints = null)
        {
            requestId = id ?? string.Empty;
            job = turn;
            turnId = turn == null ? string.Empty : turn.turnId;
            turnVersion = turn == null ? 0 : turn.turnVersion;
            runtime = turn == null ? null : turn.runtime;
            preparedOpening = (localOpening ?? Enumerable.Empty<ScenarioBeat>()).ToList().AsReadOnly();
            groundedConstraints = (constraints ?? Enumerable.Empty<ScenarioGroundedConstraint>()).ToList().AsReadOnly();
        }

        public ScenarioTurnInput(string id, string turnIdentity, int version, ScenarioRuntimeSnapshot snapshot,
            IEnumerable<ScenarioBeat> localOpening, IEnumerable<ScenarioGroundedConstraint> constraints = null)
        {
            requestId = id ?? string.Empty;
            turnId = turnIdentity ?? string.Empty;
            turnVersion = version;
            runtime = snapshot;
            job = null;
            preparedOpening = (localOpening ?? Enumerable.Empty<ScenarioBeat>()).ToList().AsReadOnly();
            groundedConstraints = (constraints ?? Enumerable.Empty<ScenarioGroundedConstraint>()).ToList().AsReadOnly();
        }
    }

    /// <summary>Chooses one ordered first-visible sequence for head, main, or a prepared local opening.</summary>
    public sealed class ScenarioTurnCoordinator
    {
        private const double FirstVisibleDeadlineSeconds = 5.0;
        private Turn current;

        private sealed class Turn
        {
            public ScenarioTurnInput input;
            public double deadline;
            public ScenarioStageResult main;
            public string pendingStage, pendingStageId, pendingStepId;
            public List<ScenarioBeat> pendingBeats;
            public ScenarioStageResult pendingScenario;
            public int pendingIndex;
            public bool pendingVisible, openingCommitted, fallbackChosen, headChosen, mainChosen, mainAnnounced;
            public bool headCompleted, fallbackCompleted, mainCompleted, receiptCompleted, dismissed, cancelled, recoverySent;
            public readonly List<string> visibleOpening = new List<string>();
        }

        public IReadOnlyList<ScenarioEffect> Begin(ScenarioTurnInput input, double nowSeconds)
        {
            if (!ValidTime(nowSeconds) || !ValidInput(input)) return One("turn_rejected", input == null ? "" : input.runtime?.runId, "invalid_input");
            var effects = new List<ScenarioEffect>();
            if (current != null && !current.dismissed && !current.cancelled && !current.mainCompleted)
                effects.Add(new ScenarioEffect("supersede_turn", current.input.runtime.runId, "", current.input.requestId));
            current = new Turn { input = input, deadline = nowSeconds + FirstVisibleDeadlineSeconds };
            return effects.AsReadOnly();
        }

        public IReadOnlyList<ScenarioEffect> Receive(ScenarioStageResult stage, ScenarioRuntimeSnapshot context)
        {
            if (current == null || stage == null ||
                stage.requestId != current.input.requestId || stage.turnId != current.input.turnId ||
                stage.turnVersion != current.input.turnVersion) return Empty();
            string invalidContext = ValidateCurrentContext(current, context);
            if (invalidContext != null) return One("stage_rejected", current.input.runtime.runId, invalidContext);
            if (current.cancelled) return Empty();
            if (current.dismissed)
            {
                if (stage.stageId == (current.main == null ? "" : current.main.stageId) ||
                    stage.stage != "main" || stage.status != "ready" ||
                    ScenarioDefinitionValidator.ValidateStage(stage, context).Count != 0 ||
                    !OffersStayInContext(stage, context) || FindGroundedContradiction(stage, current.input.groundedConstraints) != null)
                    return Empty();
                return new[] { new ScenarioEffect("late_result_saved", current.input.runtime.runId, stage.segmentStepId,
                    "unpresented", null, null, "main", stage.stageId, "", "", null, stage) };
            }
            if (stage.status != "ready") return RejectStage(stage.stage, "stage_not_ready");

            if (stage.stage == "head")
            {
                if (current.fallbackChosen || current.mainChosen || current.headChosen) return Empty();
                IReadOnlyList<string> errors = ScenarioDefinitionValidator.ValidateStage(stage, context);
                if (errors.Count != 0) return RejectStage("head", "invalid_head");
                if (stage.beats.Any(HasQuestionMark)) return RejectStage("head", "head_contains_question");
                current.headChosen = true;
                if (current.main == null && !current.fallbackChosen)
                    return StartBeats("head", stage.stageId, "head", stage.beats, stage);
                return Empty();
            }

            if (stage.stage != "main" || current.mainChosen || current.mainCompleted) return Empty();
            IReadOnlyList<string> mainErrors = ScenarioDefinitionValidator.ValidateStage(stage, context);
            if (mainErrors.Count != 0 || stage.scenario == null || !string.IsNullOrEmpty(stage.scenario.scenarioId) ||
                !OffersStayInContext(stage, context)) return RejectStage("main", "invalid_main");
            string constraintFailure = FindGroundedContradiction(stage, current.input.groundedConstraints);
            if (constraintFailure != null) return RejectStage("main", constraintFailure);
            if (DuplicatesVisibleOpening(stage, current)) return RejectStage("main", "duplicate_opening");

            current.main = stage;
            if (current.fallbackChosen)
            {
                if (current.fallbackCompleted) return StartMain();
                return Empty();
            }
            if (current.headChosen && current.openingCommitted && !current.headCompleted) return Empty();
            if (!current.openingCommitted)
            {
                current.headChosen = false;
                if (current.pendingStage == "head") ClearPending();
            }
            return StartMain();
        }

        public IReadOnlyList<ScenarioEffect> Receive(ScenarioTurnReceipt receipt, ScenarioRuntimeSnapshot context)
        {
            if (current == null || receipt == null || current.dismissed || current.cancelled ||
                receipt.requestId != current.input.requestId || receipt.turnId != current.input.turnId ||
                receipt.turnVersion != current.input.turnVersion) return Empty();
            string invalidContext = ValidateCurrentContext(current, context);
            if (invalidContext != null) return One("turn_receipt_rejected", current.input.runtime.runId, invalidContext);
            if (receipt.main.status == "failed" || receipt.terminalReason == "failed")
            {
                if ((current.openingCommitted || current.mainCompleted) && !current.recoverySent)
                {
                    current.recoverySent = true;
                    return One("turn_recovery", current.input.runtime.runId, "safe_retry_or_close");
                }
            }
            if (receipt.terminalReason == "cancelled" || receipt.terminalReason == "superseded")
            {
                current.cancelled = true;
                return Empty();
            }
            if (receipt.terminalReason != "completed" || receipt.main.status != "ready" || current.receiptCompleted)
                return Empty();
            current.receiptCompleted = true;
            if (current.mainCompleted)
                return One("turn_complete", current.input.runtime.runId,
                    current.input.runtime.checkpointStepId, "completed");
            return Empty();
        }

        public IReadOnlyList<ScenarioEffect> Tick(double nowSeconds)
        {
            if (current == null || current.dismissed || current.cancelled || current.openingCommitted ||
                current.fallbackChosen || !ValidTime(nowSeconds) || nowSeconds < current.deadline) return Empty();
            current.fallbackChosen = true;
            current.headChosen = false;
            if (current.pendingStage == "main") { current.mainChosen = false; current.mainAnnounced = false; }
            ClearPending();
            return StartBeats("fallback", current.input.requestId + "-fallback", "opening",
                current.input.preparedOpening, null);
        }

        public bool MarkBeatVisible(string stageId, string beatId)
        {
            if (current == null || !current.pendingVisible || current.pendingStageId != stageId ||
                current.pendingIndex >= current.pendingBeats.Count || current.pendingBeats[current.pendingIndex].id != beatId)
                return false;

            ScenarioBeat visible = current.pendingBeats[current.pendingIndex];
            if (current.pendingStage == "head" || current.pendingStage == "fallback")
            {
                string key = Normalize(visible.displayText);
                if (key.Length != 0 && !current.visibleOpening.Contains(key)) current.visibleOpening.Add(key);
                current.openingCommitted = true;
            }
            else if (current.pendingStage == "main") current.openingCommitted = true;
            return true;
        }

        public IReadOnlyList<ScenarioEffect> AcknowledgeBeat(string stageId, string beatId)
        {
            if (current == null || !current.pendingVisible || current.pendingStageId != stageId ||
                current.pendingIndex >= current.pendingBeats.Count || current.pendingBeats[current.pendingIndex].id != beatId) return Empty();
            if (!MarkBeatVisible(stageId, beatId)) return Empty();
            ScenarioBeat acknowledged = current.pendingBeats[current.pendingIndex];
            var effects = new List<ScenarioEffect>();
            if (current.pendingStage == "main" && !current.mainAnnounced)
            {
                current.mainAnnounced = true;
                effects.Add(new ScenarioEffect("scenario_main_ready", current.input.runtime.runId,
                    current.pendingStepId, "", null, null, "main", current.pendingStageId,
                    "", "", null, current.pendingScenario));
            }
            if (current.pendingStage == "head" || current.pendingStage == "fallback")
            {
                string key = Normalize(acknowledged.displayText);
                if (key.Length != 0 && !current.visibleOpening.Contains(key)) current.visibleOpening.Add(key);
                current.openingCommitted = true;
            }
            else if (current.pendingStage == "main") current.openingCommitted = true;
            current.pendingVisible = false;
            current.pendingIndex++;
            if (current.pendingIndex < current.pendingBeats.Count)
            {
                effects.AddRange(EmitPendingBeat());
                return effects.AsReadOnly();
            }

            string finishedStage = current.pendingStage;
            ScenarioStageResult finishedResult = current.pendingScenario;
            string finishedStep = current.pendingStepId;
            current.pendingStage = current.pendingStageId = current.pendingStepId = string.Empty;
            current.pendingBeats = null; current.pendingScenario = null; current.pendingIndex = 0;
            if (finishedStage == "head")
            {
                current.headCompleted = true;
                return current.main == null ? Empty() : StartMain();
            }
            if (finishedStage == "fallback")
            {
                current.fallbackCompleted = true;
                return current.main == null ? Empty() : StartMain();
            }
            if (finishedStage == "main")
            {
                current.mainCompleted = true;
                effects.Add(new ScenarioEffect("turn_segment_complete", current.input.runtime.runId, finishedStep,
                    "", null, null, "main", finishedResult.stageId, beatIdentity: "", stageResult: finishedResult));
                ScenarioStep segment = finishedResult.scenario.steps.Find(step => step != null && step.id == finishedResult.segmentStepId);
                if (segment != null && segment.closeExchange)
                    effects.Add(new ScenarioEffect("close_exchange", current.input.runtime.runId, finishedStep));
                if (current.receiptCompleted)
                    effects.Add(new ScenarioEffect("turn_complete", current.input.runtime.runId,
                        current.input.runtime.checkpointStepId, "completed"));
                return effects.AsReadOnly();
            }
            return effects.AsReadOnly();
        }

        public bool TryGetVisibleBeat(string stageId, string beatId, out string stage, out string stepId,
            out string eventId, out ScenarioBeat beat)
        {
            stage = stepId = eventId = string.Empty;
            beat = null;
            if (current == null || !current.pendingVisible || current.pendingStageId != stageId ||
                current.pendingIndex >= current.pendingBeats.Count || current.pendingBeats[current.pendingIndex].id != beatId)
                return false;
            stage = current.pendingStage;
            stepId = current.pendingStepId;
            eventId = EventId(current.input.requestId, current.input.turnVersion, stageId, beatId);
            beat = current.pendingBeats[current.pendingIndex];
            return true;
        }

        public IReadOnlyList<ScenarioEffect> Dismiss()
        {
            if (current == null || current.dismissed) return Empty();
            current.dismissed = true; current.pendingVisible = false;
            var effects = new List<ScenarioEffect>
            { new ScenarioEffect("close_exchange", current.input.runtime.runId, current.input.runtime.checkpointStepId) };
            if (current.main != null && !current.mainCompleted)
            {
                int unacknowledgedIndex = current.pendingStage == "main" && current.pendingStageId == current.main.stageId
                    ? current.pendingIndex : 0;
                effects.Add(new ScenarioEffect("late_result_saved", current.input.runtime.runId,
                    current.main.segmentStepId, "unpresented", null, null, "main", current.main.stageId,
                    "", "", null, current.main, unacknowledgedIndex));
            }
            return effects.AsReadOnly();
        }

        public IReadOnlyList<ScenarioEffect> Cancel()
        {
            if (current == null || current.cancelled) return Empty();
            current.cancelled = true; current.pendingVisible = false;
            return One("cancel_turn", current.input.runtime.runId, current.input.runtime.checkpointStepId, current.input.requestId);
        }

        public IReadOnlyList<ScenarioEffect> Quit()
        {
            if (current == null) return Empty();
            string runId = current.input.runtime.runId;
            current.cancelled = true; current.pendingVisible = false; current = null;
            return One("cancel_turn", runId, "", "quit");
        }

        private IReadOnlyList<ScenarioEffect> StartMain()
        {
            if (current == null || current.main == null || current.mainChosen) return Empty();
            if (DuplicatesVisibleOpening(current.main, current)) return RejectStage("main", "duplicate_opening");
            current.mainChosen = true; current.headChosen = current.headChosen && current.headCompleted;
            ScenarioStep segment = current.main.scenario.steps.Find(step => step != null && step.id == current.main.segmentStepId);
            if (segment == null || segment.kind != "dialogue") return RejectStage("main", "invalid_segment");
            return StartBeats("main", current.main.stageId, segment.id, segment.beats, current.main);
        }

        private IReadOnlyList<ScenarioEffect> StartBeats(string stage, string stageId, string stepId,
            IEnumerable<ScenarioBeat> beats, ScenarioStageResult scenario)
        {
            List<ScenarioBeat> sequence = (beats ?? Enumerable.Empty<ScenarioBeat>()).ToList();
            if (sequence.Count == 0) return RejectStage(stage, "empty_segment");
            current.pendingStage = stage; current.pendingStageId = stageId; current.pendingStepId = stepId;
            current.pendingBeats = sequence; current.pendingScenario = scenario; current.pendingIndex = 0;
            current.pendingVisible = false;
            return EmitPendingBeat();
        }

        private IReadOnlyList<ScenarioEffect> EmitPendingBeat()
        {
            ScenarioBeat beat = current.pendingBeats[current.pendingIndex];
            current.pendingVisible = true;
            string eventId = EventId(current.input.requestId, current.input.turnVersion, current.pendingStageId, beat.id);
            return new[] { new ScenarioEffect("show_dialogue", current.input.runtime.runId, current.pendingStepId,
                current.pendingStage, null, null, current.pendingStage, current.pendingStageId, beat.id, eventId, beat, current.pendingScenario) };
        }

        private static string FindGroundedContradiction(ScenarioStageResult stage, IReadOnlyList<ScenarioGroundedConstraint> constraints)
        {
            if (constraints == null || constraints.Count == 0) return null;
            IEnumerable<ScenarioBeat> beats = stage.scenario.steps.Where(step => step != null)
                .SelectMany(step => step.kind == "ask" ? (step.question == null ? Enumerable.Empty<ScenarioBeat>() : new[] { step.question }) :
                    step.kind == "dialogue" ? step.beats ?? new List<ScenarioBeat>() : Enumerable.Empty<ScenarioBeat>());
            foreach (ScenarioGroundedConstraint constraint in constraints)
                foreach (ScenarioBeat beat in beats)
                    foreach (string phrase in constraint.prohibitedPhrases)
                        if ((beat.displayText ?? "").IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (beat.text ?? "").IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                            return constraint.rejectionCode;
            return null;
        }

        private static bool DuplicatesVisibleOpening(ScenarioStageResult stage, Turn state)
        {
            ScenarioStep segment = stage.scenario.steps.Find(step => step != null && step.id == stage.segmentStepId);
            if (segment == null) return true;
            var prior = new HashSet<string>(state.visibleOpening, StringComparer.Ordinal);
            if (state.fallbackChosen)
                foreach (ScenarioBeat beat in state.input.preparedOpening) prior.Add(Normalize(beat.displayText));
            return segment.beats.Any(beat => prior.Contains(Normalize(beat.displayText)) || prior.Contains(Normalize(beat.text)));
        }

        private void ClearPending()
        {
            if (current == null) return;
            current.pendingVisible = false; current.pendingStage = current.pendingStageId = current.pendingStepId = string.Empty;
            current.pendingBeats = null; current.pendingScenario = null; current.pendingIndex = 0;
        }

        private static bool OffersStayInContext(ScenarioStageResult stage, ScenarioRuntimeSnapshot context)
        {
            return stage.topicOffers.All(offer =>
                (!string.IsNullOrEmpty(offer.topicId) && context.knownTopicIds.Contains(offer.topicId) && string.IsNullOrEmpty(offer.intentId)) ||
                (!string.IsNullOrEmpty(offer.intentId) && context.knownIntentIds.Contains(offer.intentId) && string.IsNullOrEmpty(offer.topicId)));
        }

        private IReadOnlyList<ScenarioEffect> RejectStage(string stage, string reason)
        {
            var effects = new List<ScenarioEffect>
            { new ScenarioEffect("scenario_rejected", current.input.runtime.runId, stage, reason) };
            if (stage == "main" && current.openingCommitted && !current.recoverySent)
            {
                current.recoverySent = true;
                effects.Add(new ScenarioEffect("turn_recovery", current.input.runtime.runId, stage, "safe_retry_or_close"));
            }
            return effects.AsReadOnly();
        }

        private static string ValidateCurrentContext(Turn turn, ScenarioRuntimeSnapshot context)
        {
            ScenarioRuntimeSnapshot expected = turn.input.runtime;
            if (context == null || context.runId != expected.runId || context.turnId != expected.turnId) return "stale_run";
            if (context.contextRevision != expected.contextRevision) return "context_changed";
            return null;
        }

        private static bool ValidInput(ScenarioTurnInput input)
        {
            if (input == null || input.runtime == null || input.requestId != input.turnId || input.turnVersion < 1 ||
                input.runtime.turnId != input.turnId || input.preparedOpening.Count == 0 || input.preparedOpening.Count > 4 ||
                input.preparedOpening.Any(beat => beat == null || string.IsNullOrWhiteSpace(beat.id) || string.IsNullOrWhiteSpace(beat.displayText) ||
                    beat.language != "japanese" || (beat.expression.Length != 0 && !input.runtime.allowedExpressions.Contains(beat.expression)))) return false;
            return true;
        }

        private static bool ValidTime(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
        private static bool HasQuestionMark(ScenarioBeat beat)
        { return (beat.text ?? "").Contains("?") || (beat.text ?? "").Contains("？") || (beat.displayText ?? "").Contains("?") || (beat.displayText ?? "").Contains("？"); }
        private static string Normalize(string value) { return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant(); }

        private static string EventId(string requestId, int version, string stageId, string beatId)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(requestId + "\n" + version + "\n" + stageId + "\n" + beatId);
            using (SHA256 hash = SHA256.Create())
                return "scenario-beat-" + BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static IReadOnlyList<ScenarioEffect> One(string type, string runId, string stepId, string outcome = "")
        { return new[] { new ScenarioEffect(type, runId, stepId, outcome) }; }

        private static IReadOnlyList<ScenarioEffect> Empty() { return new ScenarioEffect[0]; }
    }
}
