using System.Linq;
using AlterOS.Dialogue;
using NUnit.Framework;

namespace AlterOS.Tests
{
    public sealed class GreetingTopicSelectionTests
    {
        [Test]
        public void SuggestedTopicsTakePriorityWithinThreeSlots()
        {
            CollectionAssert.AreEqual(new[] { "suggested", "c", "b" },
                GreetingTopicSelection.Select(new[] { "suggested" }, new[] { "a", "b", "c" }, x => x, 3, count => count - 1));
        }

        [Test]
        public void RandomAuthoredTopicsHaveNoDuplicatesOrSuggestedOverlap()
        {
            CollectionAssert.AreEqual(new[] { "a", "b", "c" },
                GreetingTopicSelection.Select(new[] { "a", "a" }, new[] { "a", "b", "b", "c" }, x => x, 3, count => 0));
        }

        [Test]
        public void MoreThanThreeSuggestionsNeverOverflowsOrFallsBackToAuthored()
        {
            CollectionAssert.AreEqual(new[] { "a", "b", "c" },
                GreetingTopicSelection.Select(new[] { "a", "b", "c", "d" }, new[] { "other" }, x => x, 3, count => 0));
        }

        [Test]
        public void ShortEligibleCatalogueDoesNotInventExtraTopics()
        {
            Assert.AreEqual(1, GreetingTopicSelection.Select<string>(null, new[] { "only" }, x => x, 3, count => 0).Count);
        }
    }
}
