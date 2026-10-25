using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests
{
    public sealed class GreetingMenuContractTests
    {
        [Serializable] public sealed class Menu { public Node[] nodes; }
        [Serializable] public sealed class Node { public string id; public bool includeTopicSuggestions; public int topicSuggestionLimit; public Choice[] choices; }
        [Serializable] public sealed class Choice { public string label; public string action; public string nextId; public string value; }
        private static Menu Load()
        {
            return JsonUtility.FromJson<Menu>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Dialogue/greetings.json")));
        }

        [Test]
        public void RootHasThreeFixedEntrancesThenThreeTopicSlotsAndChat()
        {
            var root = Load().nodes.Single(node => node.id == "greetings_menu");
            CollectionAssert.AreEqual(new[] { "What are you doing?", "I want to ask you a favor", "Let's do things together", "Let's chat" },
                root.choices.Select(choice => choice.label).ToArray());
            Assert.IsTrue(root.includeTopicSuggestions);
            Assert.AreEqual(3, root.topicSuggestionLimit);
        }

        [Test]
        public void FavorOffersFollowAndTogetherOffersDrinkAndUnsubmittedInput()
        {
            var menu = Load();
            var favor = menu.nodes.Single(node => node.id == "greetings_favor");
            CollectionAssert.AreEqual(new[] { "movement.follow_player" },
                favor.choices.Where(choice => choice.action == "activity_start").Select(choice => choice.value).ToArray());
            Assert.IsTrue(favor.choices.Any(choice => choice.nextId == "greetings_menu"));
            var together = menu.nodes.Single(node => node.id == "greetings_together");
            Assert.IsTrue(together.choices.Any(choice => choice.action == "open_topic_input"));
            var drink = together.choices.Single(choice => choice.action == "activity_start");
            Assert.AreEqual("Let's get a drink", drink.label);
            Assert.AreEqual("needs.drink_water", drink.value);
            Assert.IsTrue(together.choices.Any(choice => choice.nextId == "greetings_menu"));
        }
    }
}
