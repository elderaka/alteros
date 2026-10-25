using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Scenarios;

namespace AlterOS.Activities
{
    [Serializable]
    public sealed class ActivityGreetingOption
    {
        public string label;
        public string capabilityId;
        public string target;
    }

    [Serializable]
    public sealed class ActivityGreetingMenu
    {
        public ActivityGreetingOption[] options = new ActivityGreetingOption[0];

        public IReadOnlyList<ActivityGreetingOption> Available(
            IReadOnlyList<ScenarioCapabilitySnapshot> capabilities, bool interactionOccupied)
        {
            var available = new List<ActivityGreetingOption>();
            if (interactionOccupied || capabilities == null) return available.AsReadOnly();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ActivityGreetingOption option in options ?? new ActivityGreetingOption[0])
            {
                if (option == null || string.IsNullOrWhiteSpace(option.label) || !SupportedTarget(option)) continue;
                if (!capabilities.Any(capability => capability != null && capability.capabilityId == option.capabilityId &&
                    capability.argumentBindings.Any(binding => binding.name == "target" && binding.values.Contains(option.target)))) continue;
                if (seen.Add(option.capabilityId)) available.Add(option);
            }
            return available.AsReadOnly();
        }

        private static bool SupportedTarget(ActivityGreetingOption option)
        {
            return ((option.capabilityId == ActivityCapabilityIds.ComeHere || option.capabilityId == ActivityCapabilityIds.FollowPlayer) &&
                    option.target == "player") ||
                   (option.capabilityId == ActivityCapabilityIds.DrinkWater && option.target == "available_dispenser");
        }
    }
}
