using System.IO;
using NUnit.Framework;
using AlterOS.Conversation;

namespace AlterOS.Tests
{
    public sealed class ConversationTopicCatalogTests
    {
        [Test]
        public void PromotedCatalogueLoadsOnlyAfterMigrationAndCoverageValidation()
        {
            string root = Directory.GetCurrentDirectory();
            string production = Path.Combine(root, "Assets/StreamingAssets/Dialogue/Topics");
            string coverage = Path.Combine(root, "Assets/StreamingAssets/Dialogue/ConversationCoverage.json");

            ConversationTopicCatalogLoadResult result = ConversationTopicCatalog.Load(production, coverage);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(57, result.Catalogue.Count);
            Assert.AreEqual(57, result.AuthoredInstances.Count);
            Assert.AreEqual("authored", result.AuthoredInstances["player_tired"].source);
            Assert.IsTrue(ConversationCoverage.AllowsUncoveredDiscussion(result.CoveragePolicy, "player_tired"));
            Assert.IsFalse(ConversationCoverage.AllowsUncoveredDiscussion(result.CoveragePolicy, "greet"));
        }

        [Test]
        public void InstalledTopicEntryReferencesAcceptOmittedHashButRejectWrongHash()
        {
            var opened = AlterOS.Content.RuntimeContentStore.OpenShippedRelease(UnityEngine.Application.streamingAssetsPath);
            Assert.IsTrue(opened.IsReady);
            var catalogue = ConversationTopicCatalog.Load(opened.value);
            var bridge = System.Type.GetType("AlterOS.Conversation.DirectConversationBridge, Assembly-CSharp", true);
            var compare = bridge.GetMethod("SameContentReference", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            foreach (string id in new[] { "topic.turn_on_tv", "topic.locate_tv", "topic.fetch_water" })
            {
                var reference = catalogue.Catalogue[id].scenarios[0].scenarioRef;
                var omitted = new AlterOS.Content.ContentRef(reference.store, reference.id, reference.revision, "");
                Assert.IsTrue((bool)compare.Invoke(null, new object[] { opened.value, omitted, reference }), id);
                var wrong = new AlterOS.Content.ContentRef(reference.store, reference.id, reference.revision, new string('0', 64));
                Assert.IsFalse((bool)compare.Invoke(null, new object[] { opened.value, omitted, wrong }), id);
            }
        }

        [Test]
        public void MissingPromotionOrCoverageFailsClosedWithoutPartialCatalogue()
        {
            string root = Directory.GetCurrentDirectory();
            ConversationTopicCatalogLoadResult result = ConversationTopicCatalog.Load(
                Path.Combine(root, "Assets/StreamingAssets/Dialogue/Topics/missing"),
                Path.Combine(root, "Assets/StreamingAssets/Dialogue/ConversationCoverage.json"));
            Assert.IsFalse(result.Success);
            Assert.AreEqual("catalogue_unavailable", result.Error);
            Assert.AreEqual(0, result.Catalogue.Count);
            Assert.AreEqual(0, result.AuthoredInstances.Count);
        }
    }
}
