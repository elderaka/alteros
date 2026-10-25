using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AlterOS.Scenarios
{
    [Serializable]
    public sealed class ScenarioPlaygroundFixture
    {
        public string runId = "scenario-playground-run";
        public string turnId = "scenario-playground-turn";
        public ScenarioDefinition definition;
        public List<ScenarioPlaygroundFixtureEvent> events = new List<ScenarioPlaygroundFixtureEvent>();
    }

    [Serializable]
    public sealed class ScenarioPlaygroundFixtureEvent
    {
        public double nowSeconds;
        public ScenarioEvent input;
    }

    public sealed class ScenarioPlaygroundReport
    {
        public readonly double nowSeconds;
        public readonly string currentStepId, status;
        public readonly bool hasSimulatedActivity;
        public readonly int paidStageRequestCount;
        public readonly ReadOnlyCollection<ScenarioEffect> effects;

        internal ScenarioPlaygroundReport(double time, ScenarioInstanceView running, ScenarioInstanceView suspended,
            bool simulatedActivity, IList<ScenarioEffect> currentEffects)
        {
            nowSeconds = time;
            currentStepId = running != null ? running.currentStepId : suspended != null ? suspended.currentStepId : string.Empty;
            status = running != null ? running.status : suspended != null ? suspended.status : "idle";
            hasSimulatedActivity = simulatedActivity;
            paidStageRequestCount = 0;
            effects = new ReadOnlyCollection<ScenarioEffect>(new List<ScenarioEffect>(currentEffects ?? new ScenarioEffect[0]));
        }
    }

    /// <summary>Headless Scenario runner facade for synthetic Playground events; never calls providers or game capabilities.</summary>
    public sealed class ScenarioPlaygroundSession
    {
        readonly ScenarioSession session = new ScenarioSession();
        readonly ScenarioRuntimeSnapshot context;
        readonly SyntheticScenarioActivityPort activityPort = new SyntheticScenarioActivityPort();
        double lastTimeSeconds;

        public ScenarioPlaygroundReport InitialReport { get; private set; }

        public ScenarioPlaygroundSession(ScenarioDefinition definition, string runId, ScenarioRuntimeSnapshot runtimeContext)
        {
            if (runtimeContext == null) throw new ArgumentNullException(nameof(runtimeContext));
            if (runtimeContext.runId != runId) throw new ArgumentException("Runtime context must be bound to the Playground run.", nameof(runtimeContext));
            context = runtimeContext;
            IReadOnlyList<ScenarioEffect> started = session.Start(definition, runId, runtimeContext);
            InitialReport = Process(0d, started);
        }

        public static ScenarioRuntimeSnapshot CreateSyntheticContext(ScenarioDefinition definition, string runId, string turnId = "scenario-playground-turn")
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var expressions = new HashSet<string>(StringComparer.Ordinal);
            var conditions = new HashSet<string>(StringComparer.Ordinal);
            var capabilityValues = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);
            var objects = new Dictionary<string, ScenarioObjectSnapshot>(StringComparer.Ordinal);
            foreach (ScenarioStep step in definition.steps ?? new List<ScenarioStep>())
            {
                foreach (ScenarioBeat beat in step.beats ?? new List<ScenarioBeat>())
                    if (beat != null && !string.IsNullOrWhiteSpace(beat.expression)) expressions.Add(beat.expression);
                if (step.question != null && !string.IsNullOrWhiteSpace(step.question.expression)) expressions.Add(step.question.expression);
                if (step.kind == "wait" && !string.IsNullOrWhiteSpace(step.conditionId)) conditions.Add(step.conditionId);
                if (step.kind != "action") continue;

                Dictionary<string, HashSet<string>> bindings;
                if (!capabilityValues.TryGetValue(step.capabilityId, out bindings))
                    capabilityValues[step.capabilityId] = bindings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (ScenarioActionArgument argument in step.arguments ?? new List<ScenarioActionArgument>())
                {
                    if (argument == null) continue;
                    HashSet<string> values;
                    if (!bindings.TryGetValue(argument.name, out values)) bindings[argument.name] = values = new HashSet<string>(StringComparer.Ordinal);
                    values.Add(argument.value);
                    if (argument.name == "targetObjectId" && !objects.ContainsKey(argument.value))
                        objects.Add(argument.value, new ScenarioObjectSnapshot(argument.value, "synthetic_use_point"));
                }
            }

            var capabilities = new List<ScenarioCapabilitySnapshot>();
            foreach (KeyValuePair<string, Dictionary<string, HashSet<string>>> capability in capabilityValues)
            {
                var bindings = new List<ScenarioArgumentBinding>();
                foreach (KeyValuePair<string, HashSet<string>> binding in capability.Value)
                    bindings.Add(new ScenarioArgumentBinding(binding.Key, binding.Value));
                capabilities.Add(new ScenarioCapabilitySnapshot(capability.Key, bindings));
            }

            return new ScenarioRuntimeSnapshot(definition.scenarioId, runId, turnId, 1, "",
                string.IsNullOrWhiteSpace(definition.topicId) ? new string[0] : new[] { definition.topicId },
                expressions, capabilities, new List<ScenarioObjectSnapshot>(objects.Values), conditions);
        }

        public ScenarioPlaygroundReport Apply(ScenarioEvent input, double nowSeconds)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds) || nowSeconds < lastTimeSeconds)
                throw new InvalidOperationException("Playground event time must be finite and monotonic.");
            if (input.runId != context.runId)
                throw new InvalidOperationException("Playground event must be bound to the current Scenario run.");
            lastTimeSeconds = nowSeconds;
            return Process(nowSeconds, session.Advance(input, context));
        }

        ScenarioPlaygroundReport Process(double nowSeconds, IReadOnlyList<ScenarioEffect> initial)
        {
            var all = new List<ScenarioEffect>();
            var pending = new Queue<ScenarioEffect>(initial ?? new ScenarioEffect[0]);
            int syntheticTransitions = 0;
            while (pending.Count > 0 && syntheticTransitions++ < ScenarioDefinitionValidator.MaximumSteps)
            {
                ScenarioEffect effect = pending.Dequeue();
                all.Add(effect);
                if (effect.type != "request_activity") continue;

                ActivityRequestResult result = activityPort.TryRequest(effect.activityRequest, context);
                bool accepted = result.status == "accepted";
                all.Add(new ScenarioEffect("simulated_activity_result", effect.runId, effect.stepId,
                    accepted ? result.activityId : result.reason));
                ScenarioEvent simulated = new ScenarioEvent
                {
                    kind = "activity_result",
                    runId = effect.runId,
                    stepId = effect.stepId,
                    activityId = result.activityId,
                    success = accepted,
                };
                IReadOnlyList<ScenarioEffect> followup = session.Advance(simulated, context);
                foreach (ScenarioEffect next in followup) pending.Enqueue(next);
            }

            return new ScenarioPlaygroundReport(nowSeconds, session.Running, session.Suspended,
                activityPort.HasSimulatedActivity, all);
        }

        sealed class SyntheticScenarioActivityPort : IScenarioActivityPort
        {
            public bool HasSimulatedActivity { get; private set; }

            public ActivityRequestResult TryRequest(ActivityRequest request, ScenarioRuntimeSnapshot runtime)
            {
                if (request == null || runtime == null || runtime.runId != request.runId ||
                    !ContainsCapability(runtime, request.capabilityId))
                    return ActivityRequestResult.Unavailable("synthetic_capability_unavailable");

                HasSimulatedActivity = true;
                return ActivityRequestResult.Accepted("simulated-" + request.runId);
            }

            static bool ContainsCapability(ScenarioRuntimeSnapshot runtime, string id)
            {
                foreach (ScenarioCapabilitySnapshot capability in runtime.capabilities)
                    if (capability != null && capability.capabilityId == id) return true;
                return false;
            }
        }
    }
}
