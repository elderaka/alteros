using System;
using System.Collections.Generic;
using System.Reflection;
using AlterOS.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests
{
    public class DialogueRouteRecoveryTests
    {
        [Test]
        public void CancelButtonEndsTheConversationBehindTopicInput()
        {
            var host = new GameObject("Topic cancel test");
            host.SetActive(false);
            try
            {
                var managerType = Type.GetType("DialogueManager, Assembly-CSharp", true);
                var manager = host.AddComponent(managerType);
                var buttonObject = new GameObject("Cancel", typeof(RectTransform), typeof(UnityEngine.UI.Button));
                buttonObject.transform.SetParent(host.transform);
                var button = buttonObject.GetComponent<UnityEngine.UI.Button>();
                managerType.GetField("topicCancelButton").SetValue(manager, button);
                var sequenceType = Type.GetType("AlterOS.Dialogue.DialogueSequence, Assembly-CSharp", true);
                managerType.GetField("_currentSequence", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, Activator.CreateInstance(sequenceType));
                managerType.GetField("_currentIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, 0);
                managerType.GetField("_isTopicInputOpen", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, true);
                managerType.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);

                button.onClick.Invoke();

                Assert.IsFalse((bool)managerType.GetProperty("IsInputOpen").GetValue(manager));
                Assert.IsFalse((bool)managerType.GetProperty("BlocksPlayerControl").GetValue(manager),
                    "Cancel must end the old greeting too, not hide input over a still-active sequence.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void IneligibleAuthoredRouteDoesNotReportPlaybackSuccess()
        {
            var host = new GameObject("Rejected route test");
            host.SetActive(false);
            try
            {
                var managerType = Type.GetType("DialogueManager, Assembly-CSharp", true);
                var routerType = Type.GetType("AlterOS.DialogueRouter, Assembly-CSharp", true);
                var manager = host.AddComponent(managerType);
                var router = host.AddComponent(routerType);
                routerType.GetField("dialogueManager").SetValue(router, manager);
                var catalog = new AuthoredDialogueRouteCatalog(new[]
                {
                    new AuthoredDialogueRouteEntry("test_unavailable", "test_unavailable.json")
                });
                routerType.GetField("_authoredRouteCatalog", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(router, catalog);
                var cache = (Dictionary<string, string>)routerType
                    .GetField("_jsonCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(router);
                int blockedHour = (DateTime.Now.Hour + 12) % 24;
                var definition = new DialogueDefinition
                {
                    id = "test_unavailable_001",
                    routing = new DialogueRouting
                    {
                        routeId = "test_unavailable", startHour = blockedHour, endHour = (blockedHour + 1) % 24,
                        examples = new List<string> { "unavailable topic" }
                    },
                    nodes = new List<CanonicalDialogueNode>
                    {
                        new CanonicalDialogueNode { id = "start", text = "This must not play now." }
                    }
                };
                cache.Add("test_unavailable.json", JsonUtility.ToJson(definition));

                bool started = (bool)routerType.GetMethod("TryPlayAuthoredRoute")
                    .Invoke(router, new object[] { "test_unavailable" });

                Assert.IsFalse(started, "Unavailable dialogue must let its caller run fallback, not leave input busy.");
                Assert.IsFalse((bool)managerType.GetProperty("IsActive").GetValue(manager));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PublicRouterEntryPointsTreatSequencesAsUntrusted()
        {
            var host = new GameObject("Untrusted router sequence test");
            host.SetActive(false);
            try
            {
                var managerType = Type.GetType("DialogueManager, Assembly-CSharp", true);
                var routerType = Type.GetType("AlterOS.DialogueRouter, Assembly-CSharp", true);
                var manager = host.AddComponent(managerType);
                var router = host.AddComponent(routerType);
                routerType.GetField("dialogueManager").SetValue(router, manager);
                managerType.GetField("skipDialogueTts").SetValue(manager, true);

                object sequence = CreateSequence("Generated text");
                var sequenceType = sequence.GetType();

                routerType.GetMethod("PlayCinematic").Invoke(router, new object[] { sequence });
                Assert.IsFalse((bool)managerType.GetField("_activeSequenceAllowsGameEffects",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager),
                    "Caller-supplied sequences must not be treated as authored content.");

                string json = (string)sequenceType.GetMethod("ToJson").Invoke(sequence, null);
                routerType.GetMethod("PlayFromJson").Invoke(router, new object[] { json });
                Assert.IsFalse((bool)managerType.GetField("_activeSequenceAllowsGameEffects",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager),
                    "Dynamic JSON must not be treated as authored content.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CanonicalFileBackedAuthoredRouteRetainsItsEffectPermission()
        {
            var host = new GameObject("Validated authored route test");
            host.SetActive(false);
            try
            {
                var managerType = Type.GetType("DialogueManager, Assembly-CSharp", true);
                var routerType = Type.GetType("AlterOS.DialogueRouter, Assembly-CSharp", true);
                var manager = host.AddComponent(managerType);
                var router = host.AddComponent(routerType);
                routerType.GetField("dialogueManager").SetValue(router, manager);
                managerType.GetField("skipDialogueTts").SetValue(manager, true);

                const string routeId = "validated_authored_test";
                const string relativePath = "Generic/validated_authored_test/validated_authored_test.json";
                var definition = new DialogueDefinition
                {
                    id = "validated_authored_test_001",
                    routing = new DialogueRouting
                    {
                        routeId = routeId,
                        examples = new List<string> { "play the validated authored route" }
                    },
                    nodes = new List<CanonicalDialogueNode>
                    {
                        new CanonicalDialogueNode
                        {
                            id = "start",
                            speaker = "system",
                            text = "Validated authored dialogue."
                        }
                    }
                };

                routerType.GetField("_authoredRouteCatalog", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(router, new AuthoredDialogueRouteCatalog(new[]
                    {
                        new AuthoredDialogueRouteEntry(routeId, relativePath)
                    }));
                var cache = (Dictionary<string, string>)routerType
                    .GetField("_jsonCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(router);
                cache.Add(relativePath, JsonUtility.ToJson(definition));

                bool started = (bool)routerType.GetMethod("TryPlayAuthoredRoute")
                    .Invoke(router, new object[] { routeId });

                Assert.IsTrue(started, "A valid file-backed catalogue route should start.");
                Assert.IsTrue((bool)managerType.GetField("_activeSequenceAllowsGameEffects",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager),
                    "Only the validated authored route path should opt into authored effects.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GenericAndLocalSequenceFileStartsRemainUntrusted()
        {
            var host = new GameObject("Legacy file route trust test");
            host.SetActive(false);
            try
            {
                var managerType = Type.GetType("DialogueManager, Assembly-CSharp", true);
                var routerType = Type.GetType("AlterOS.DialogueRouter, Assembly-CSharp", true);
                var manager = host.AddComponent(managerType);
                var router = host.AddComponent(routerType);
                routerType.GetField("dialogueManager").SetValue(router, manager);
                managerType.GetField("dialogueRouter").SetValue(manager, router);
                managerType.GetField("skipDialogueTts").SetValue(manager, true);

                routerType.GetMethod("Play").Invoke(router, new object[] { "system/stat_change_test.json" });
                Assert.IsTrue((bool)managerType.GetProperty("IsActive").GetValue(manager),
                    "The generic file-backed fixture should start before its trust flag is checked.");
                Assert.IsFalse((bool)managerType.GetField("_activeSequenceAllowsGameEffects",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager),
                    "Generic file playback is not canonical catalogue validation.");

                bool localSequenceStarted = (bool)managerType.GetMethod("TryPlayLocalSequenceChoice",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager,
                    new object[] { "system/stat_change_test.json" });
                Assert.IsTrue(localSequenceStarted);
                Assert.IsFalse((bool)managerType.GetField("_activeSequenceAllowsGameEffects",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager),
                    "Legacy local sequence choices must not use the authored-effects path.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static object CreateSequence(string text)
        {
            var sequenceType = Type.GetType("AlterOS.Dialogue.DialogueSequence, Assembly-CSharp", true);
            var nodeType = Type.GetType("AlterOS.Dialogue.DialogueNode, Assembly-CSharp", true);
            object sequence = Activator.CreateInstance(sequenceType);
            object metadata = sequenceType.GetField("metadata").GetValue(sequence);
            metadata.GetType().GetField("mode").SetValue(metadata, "cinematic");

            object node = Activator.CreateInstance(nodeType);
            nodeType.GetField("id").SetValue(node, "start");
            nodeType.GetField("speaker").SetValue(node, "system");
            nodeType.GetField("text").SetValue(node, text);
            var nodes = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(nodeType));
            nodes.Add(node);
            sequenceType.GetField("nodes").SetValue(sequence, nodes);
            return sequence;
        }
    }
}
