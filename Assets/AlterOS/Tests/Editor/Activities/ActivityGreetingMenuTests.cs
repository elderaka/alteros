using System.IO;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Scenarios;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Activities
{
    public sealed class ActivityGreetingMenuTests
    {
        private static ActivityGreetingMenu LoadMenu()
        {
            return JsonUtility.FromJson<ActivityGreetingMenu>(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "Dialogue/topic/activity_options.json")));
        }

        private static ScenarioCapabilitySnapshot Capability(string id, string target)
        {
            return new ScenarioCapabilitySnapshot(id, new[] { new ScenarioArgumentBinding("target", new[] { target }) });
        }

        [Test]
        public void AuthoredMenuOffersThreeAvailableActionsInOrder()
        {
            var capabilities = new[] { Capability(ActivityCapabilityIds.ComeHere, "player"),
                Capability(ActivityCapabilityIds.FollowPlayer, "player"), Capability(ActivityCapabilityIds.DrinkWater, "available_dispenser") };
            CollectionAssert.AreEqual(new[] { "Come here", "Follow me", "Drink water" },
                LoadMenu().Available(capabilities, false).Select(option => option.label).ToArray());
        }

        [Test]
        public void MissingDispenserDoesNotOfferDrinking()
        {
            var capabilities = new[] { Capability(ActivityCapabilityIds.FollowPlayer, "player") };
            CollectionAssert.AreEqual(new[] { "Follow me" }, LoadMenu().Available(capabilities, false).Select(option => option.label).ToArray());
        }

        [Test]
        public void BusyActivityOrScenarioOffersNoReplacementActions()
        {
            Assert.IsEmpty(LoadMenu().Available(new[] { Capability(ActivityCapabilityIds.FollowPlayer, "player") }, true));
        }

        [Test]
        public void ChangedTargetPermissionRejectsPreviouslyOfferedChoice()
        {
            Assert.IsEmpty(LoadMenu().Available(new[] { Capability(ActivityCapabilityIds.FollowPlayer, "another_actor") }, false));
        }

        [Test]
        public void UnknownCapabilitiesAndDuplicateEntriesCannotAddActions()
        {
            var menu = LoadMenu();
            menu.options = new[] { menu.options[0], menu.options[0],
                new ActivityGreetingOption { label = "Other", capabilityId = "untrusted.action", target = "player" } };
            Assert.AreEqual(1, menu.Available(new[] { Capability(ActivityCapabilityIds.ComeHere, "player"),
                Capability("untrusted.action", "player") }, false).Count);
        }
    }
}
