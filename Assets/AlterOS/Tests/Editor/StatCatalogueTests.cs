using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace AlterOS.Tests
{
    public class StatCatalogueTests
    {
        [Test]
        public void EveryReviewedIdResolvesWithItsOwnBounds()
        {
            Type catalogue = StatsTestAccess.Find("AlterOS.Stats.StatCatalogue");
            string[] needs = { "alterra.energy", "alterra.hydration", "alterra.satiety",
                "alterra.engagement", "alterra.connection", "alterra.physical_comfort" };
            string[] axes = { "alterra.emotion.valence", "alterra.emotion.activation", "alterra.emotion.control" };
            foreach (string id in needs)
                AssertDefinition(catalogue, id, 0f, 100f);
            foreach (string id in axes)
                AssertDefinition(catalogue, id, -1f, 1f);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (object definition in (IEnumerable)catalogue.GetProperty("Definitions").GetValue(null))
                Assert.IsTrue(ids.Add((string)definition.GetType().GetProperty("Id").GetValue(definition)));
            Assert.AreEqual(9, ids.Count);
        }

        [Test]
        public void UnknownOrNonCanonicalIdsAreNotAliases()
        {
            Type catalogue = StatsTestAccess.Find("AlterOS.Stats.StatCatalogue");
            foreach (string id in new[] { null, "", "altera.energy", "player.energy", "Alterra.energy", " alterra.energy", "alterra.anger" })
            {
                object[] arguments = { id, null };
                Assert.IsFalse((bool)catalogue.GetMethod("TryGet").Invoke(null, arguments), id);
                Assert.IsNull(arguments[1]);
            }
        }

        [Test]
        public void CallersCannotReplaceDefinitionsOrMutateTheirBounds()
        {
            Type catalogue = StatsTestAccess.Find("AlterOS.Stats.StatCatalogue");
            object definitions = catalogue.GetProperty("Definitions").GetValue(null);
            var list = definitions as IList;
            if (list != null)
                Assert.Throws<NotSupportedException>(() => list[0] = null);

            foreach (object definition in (IEnumerable)definitions)
            {
                foreach (string name in new[] { "Id", "Minimum", "Maximum" })
                    Assert.IsNull(definition.GetType().GetProperty(name).GetSetMethod(), name);
            }
        }

        private static void AssertDefinition(Type catalogue, string id, float minimum, float maximum)
        {
            object[] arguments = { id, null };
            Assert.IsTrue((bool)catalogue.GetMethod("TryGet").Invoke(null, arguments), id);
            object definition = arguments[1];
            Assert.AreEqual(id, definition.GetType().GetProperty("Id").GetValue(definition));
            Assert.AreEqual(minimum, definition.GetType().GetProperty("Minimum").GetValue(definition));
            Assert.AreEqual(maximum, definition.GetType().GetProperty("Maximum").GetValue(definition));
        }
    }
}
