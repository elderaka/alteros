using System;
using System.Collections.Generic;
using AlterOS.Stats;

namespace AlterOS.Dialogue
{
    [Serializable]
    public sealed class DialogueDefinition
    {
        public const int CurrentSchemaVersion = 1;
        public const string GenericSource = "generic";
        public const string PersonalizedSource = "personalized";

        public int schemaVersion = CurrentSchemaVersion;
        public string id = "";
        public string source = GenericSource;
        public DialogueRouting routing = new DialogueRouting();
        public List<CanonicalDialogueNode> nodes = new List<CanonicalDialogueNode>();
    }

    [Serializable]
    public sealed class DialogueRouting
    {
        public string routeId = "";
        public float priority = 1f;
        public int startHour = -1;
        public int endHour = -1;
        public List<string> examples = new List<string>();
        public List<string> requiredContext = new List<string>();
        public List<string> blockedContext = new List<string>();
        public CanonicalStatEligibility eligibility;
    }

    [Serializable]
    public sealed class CanonicalDialogueNode
    {
        public string id = "";
        public string speaker = "alterra";
        public string text = "";
        public string displayText = "";
        public string language = "";
        public string voice = "";
        public string audioKey = "";
        public string nextId = "";
        public DialoguePresentation presentation = new DialoguePresentation();
        // ponytail: game commands stay out until a concrete system defines a typed command contract.
        public List<CanonicalDialogueChoice> choices = new List<CanonicalDialogueChoice>();
    }

    [Serializable]
    public sealed class CanonicalDialogueChoice
    {
        public string label = "";
        public string nextId = "";
    }

    [Serializable]
    public sealed class DialoguePresentation
    {
        public string expression = "";
        public string animation = "";
    }
}
