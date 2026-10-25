using System;
using System.IO;
using NUnit.Framework;
using AlterOS.Dialogue;

namespace AlterOS.Tests
{
    public class AuthoredDialogueRouteCatalogTests
    {
        [Test]
        public void ChoosesTheFirstStableVariantForARecognizedRoute()
        {
            var catalog = new AuthoredDialogueRouteCatalog(new[]
            {
                new AuthoredDialogueRouteEntry("player_tired", "Generic/player_tired/player_tired_002.json"),
                new AuthoredDialogueRouteEntry("player_tired", "Generic/player_tired/player_tired_001.json"),
            });

            Assert.IsTrue(catalog.TryGetSequencePath("player_tired", out var path));
            Assert.AreEqual("Generic/player_tired/player_tired_001.json", path);
        }

        [Test]
        public void RejectsRoutesThatHaveNoAuthoredSequence()
        {
            var catalog = new AuthoredDialogueRouteCatalog(new[]
            {
                new AuthoredDialogueRouteEntry("player_tired", "Generic/player_tired/player_tired_001.json"),
            });

            Assert.IsFalse(catalog.TryGetSequencePath("unknown_route", out _));
        }

        [Test]
        public void BuildsRuntimePathsFromGenericDialogueFolders()
        {
            string root = Path.Combine(Path.GetTempPath(), "alteros-route-catalog-" + Guid.NewGuid().ToString("N"));
            string routeFolder = Path.Combine(root, "player_tired");
            Directory.CreateDirectory(routeFolder);
            File.WriteAllText(Path.Combine(routeFolder, "player_tired_001.json"), "{}");

            try
            {
                var catalog = AuthoredDialogueRouteCatalog.FromDirectory(root);
                Assert.IsTrue(catalog.TryGetSequencePath("player_tired", out var path));
                Assert.AreEqual("Generic/player_tired/player_tired_001.json", path);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
