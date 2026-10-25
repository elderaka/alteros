using System;
using AlterOS.Scenarios;

namespace AlterOS.Activities
{
    // Host-authored presentation metadata, not provider output or permission.
    [Serializable]
    public sealed class AuthoredActivityScenario
    {
        public string capabilityId;
        public ScenarioDefinition scenario;
        public ScenarioBeat waiting;
        public ScenarioBeat returning;
        public ScenarioBeat readyBark;
        public string continueLabel;
        public string stopLabel;
        public string engagement = "bark_then_e";
    }

    [Serializable]
    public sealed class AuthoredActivityScenarioLibrary
    {
        public AuthoredActivityScenario[] entries = new AuthoredActivityScenario[0];
    }

    public static class ScenarioEngagementPolicy
    {
        // A future initiative scheduler supplies authorization; content cannot grant it.
        public static string Resolve(string requested, bool independentlyAuthorized, bool interactionBusy)
        {
            if (interactionBusy) return "defer";
            if (requested == "independent" && independentlyAuthorized) return "independent";
            return "bark_then_e";
        }
    }
}
