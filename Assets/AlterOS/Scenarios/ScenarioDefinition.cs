using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Content;

namespace AlterOS.Scenarios
{
    [Serializable]
    public sealed class ScenarioDefinition
    {
        public string scenarioId = "";
        public string topicId = "";
        public string entryStepId = "";
        public List<ScenarioStep> steps = new List<ScenarioStep>();
    }

    [Serializable]
    public sealed class ScenarioStep
    {
        public string id = "";
        public string sourceStepId = "";
        public string kind = "";
        public List<ScenarioBeat> beats = new List<ScenarioBeat>();
        public string nextStepId = "";
        public bool closeExchange;
        public string questionId = "";
        public ScenarioBeat question;
        public List<ScenarioQuestionOption> options = new List<ScenarioQuestionOption>();
        public bool allowFreeText;
        public string freeTextNextStepId = "";
        public string skipNextStepId = "";
        public string cancelNextStepId = "";
        public string capabilityId = "";
        public List<ScenarioActionArgument> arguments = new List<ScenarioActionArgument>();
        public string successNextStepId = "";
        public string failureNextStepId = "";
        public string activityId = "";
        public string conditionId = "";
        public string objective = "";
        public string sourceId = "";
        public List<ScenarioBranchCase> cases = new List<ScenarioBranchCase>();
        public string defaultNextStepId = "";
        public string outcome = "";
        public ContentRef actionRef;
        public string requiresCompletedStepId = "";
        public ActivitySequenceDefinition activitySequence;
        public List<ActivitySequenceArgument> activitySequenceArguments = new List<ActivitySequenceArgument>();
    }

    [Serializable]
    public sealed class ScenarioBeat
    {
        public string id = "";
        public string speaker = "";
        public string text = "";
        public string displayText = "";
        public string language = "";
        public string expression = "";
        public string voice = "";
        public string audioKey = "";
        public string animation = "";
    }

    [Serializable]
    public sealed class ScenarioQuestionOption
    {
        public string optionId = "";
        public string label = "";
        public string value = "";
        public string nextStepId = "";
    }

    [Serializable]
    public sealed class ScenarioActionArgument
    {
        public string name = "";
        public string value = "";
    }

    [Serializable]
    public sealed class ScenarioBranchCase
    {
        public string value = "";
        public string nextStepId = "";
    }

    [Serializable]
    public sealed class ScenarioTopicOffer
    {
        public string offerId = "";
        public string label = "";
        public string topicId = "";
        public string intentId = "";
    }

    public sealed class ScenarioObjectSnapshot
    {
        public readonly string objectId, kind;
        public ScenarioObjectSnapshot(string id, string objectKind) { objectId = id ?? ""; kind = objectKind ?? ""; }
    }

    public sealed class ScenarioArgumentBinding
    {
        public readonly string name;
        public readonly ReadOnlyCollection<string> values;
        public ScenarioArgumentBinding(string bindingName, IEnumerable<string> allowedValues)
        { name = bindingName ?? ""; values = Array.AsReadOnly((allowedValues ?? new string[0]).ToArray()); }
    }

    public sealed class ScenarioCapabilitySnapshot
    {
        public readonly string capabilityId;
        public readonly ReadOnlyCollection<ScenarioArgumentBinding> argumentBindings;
        public ScenarioCapabilitySnapshot(string id, IEnumerable<ScenarioArgumentBinding> bindings)
        { capabilityId = id ?? ""; argumentBindings = Array.AsReadOnly((bindings ?? new ScenarioArgumentBinding[0]).ToArray()); }
    }

    /// <summary>Frozen, host-owned context; provider output cannot add capabilities or bindings.</summary>
    public sealed class ScenarioRuntimeSnapshot
    {
        public readonly string scenarioId, runId, turnId, checkpointStepId;
        public readonly long contextRevision;
        public readonly ReadOnlyCollection<string> knownTopicIds, knownIntentIds, allowedExpressions, waitConditions;
        public readonly ReadOnlyCollection<ScenarioCapabilitySnapshot> capabilities;
        public readonly ReadOnlyCollection<ScenarioObjectSnapshot> objects;
        public readonly ReadOnlyDictionary<string, bool> hostFacts;

        public ScenarioRuntimeSnapshot(string scenario, string run, string turn, long revision, string checkpoint,
            IEnumerable<string> topics, IEnumerable<string> expressions, IEnumerable<ScenarioCapabilitySnapshot> allowedCapabilities,
            IEnumerable<ScenarioObjectSnapshot> knownObjects, IEnumerable<string> conditions, IEnumerable<string> intents = null,
            IDictionary<string, bool> registeredHostFacts = null)
        {
            scenarioId = scenario ?? ""; runId = run ?? ""; turnId = turn ?? ""; contextRevision = revision;
            checkpointStepId = checkpoint ?? "";
            knownTopicIds = Array.AsReadOnly((topics ?? new string[0]).ToArray());
            knownIntentIds = Array.AsReadOnly((intents ?? new string[0]).ToArray());
            allowedExpressions = Array.AsReadOnly((expressions ?? new string[0]).ToArray());
            capabilities = Array.AsReadOnly((allowedCapabilities ?? new ScenarioCapabilitySnapshot[0]).ToArray());
            objects = Array.AsReadOnly((knownObjects ?? new ScenarioObjectSnapshot[0]).ToArray());
            waitConditions = Array.AsReadOnly((conditions ?? new string[0]).ToArray());
            hostFacts = new ReadOnlyDictionary<string, bool>(registeredHostFacts == null
                ? new Dictionary<string, bool>(StringComparer.Ordinal)
                : new Dictionary<string, bool>(registeredHostFacts, StringComparer.Ordinal));
        }
    }
}
