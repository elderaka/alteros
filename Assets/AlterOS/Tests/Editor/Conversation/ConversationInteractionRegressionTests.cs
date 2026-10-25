using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Conversation;
using AlterOS.Scenarios;
using AlterOS.Topics;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests
{
    public sealed class ConversationInteractionRegressionTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void CompletedScenarioClearsBindingAndEquivalentVocabularyReusesCacheKey()
        {
            var root = new GameObject("Synthetic conversation regression");
            root.SetActive(false); // Never run Bridge.Awake or open player/provider stores.
            try
            {
                Type type = Type.GetType("AlterOS.Conversation.DirectConversationBridge, Assembly-CSharp", true);
                var bridge = root.AddComponent(type);
                type.GetField("activeTopicId", PrivateInstance).SetValue(bridge, "topic.turn_on_tv");
                type.GetField("activeResponseId", PrivateInstance).SetValue(bridge, "synthetic-response");
                ConstructorInfo constructor = typeof(ScenarioEffect).GetConstructors(PrivateInstance).Single();
                object[] args = constructor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                args[0] = "end_scenario"; args[1] = "synthetic-run"; args[2] = "end"; args[3] = "completed";
                var effect = (ScenarioEffect)constructor.Invoke(args);
                type.GetMethod("PresentScenarioContinuation", PrivateInstance).Invoke(bridge,
                    new object[] { new[] { effect }, "synthetic-response" });
                var request = (ConversationRequest)type.GetMethod("NewRequest", PrivateInstance).Invoke(bridge,
                    new object[] { "Where is the tv?", "synthetic-request" });
                Assert.IsEmpty(request.activeTopicId);
                Assert.IsEmpty(request.activeResponseId);

                var create = type.GetMethod("CreateRuntimeContentVocabulary", PrivateInstance);
                var context = new TopicContext();
                var inputs = new object[] { context, new ScenarioCapabilitySnapshot[0], new[] { "neutral" } };
                var first = (ContentHostVocabulary)create.Invoke(bridge, inputs);
                Assert.AreSame(first, create.Invoke(bridge, inputs), "Equivalent requests must not invalidate the release cache.");
                context.facts.Add("synthetic.changed", true);
                Assert.AreNotSame(first, create.Invoke(bridge, inputs), "Changed registration must invalidate the cache.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void InstalledProofEntriesProjectPinnedActionsThatStartTheRealExecutor()
        {
            var store = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath).value;
            Assert.IsNotNull(store);
            var catalogue = ConversationTopicCatalog.Load(store);
            var furniture = store.ReadFurnitureCatalogue();
            Assert.IsTrue(furniture.IsReady);
            string[] useIds = furniture.value.document.definitions.SelectMany(d => d.points)
                .SelectMany(p => p.useIds).Distinct(StringComparer.Ordinal).ToArray();
            var sequences = ActivitySequenceCatalogFactory.CreateDefault();
            string[] expressions = { "neutral", "happy", "gentle", "thinking", "warm", "focused", "surprised", "angry", "shy", "playful", "sad", "soft" };
            var enums = sequences.Primitives.All.SelectMany(p => p.arguments).SelectMany(a => a.allowedValues)
                .Concat(sequences.Primitives.All.SelectMany(p => p.results).SelectMany(a => a.allowedValues))
                .Concat(Enum.GetNames(typeof(AlterOS.Furniture.FurniturePowerState)))
                .Concat(new[] { "applied", "no_change", "replayed", "unavailable", "conflict", "failed", "completed", "cancelled" });
            var vocabulary = new ContentHostVocabulary(sequences.Primitives.All.Select(p => p.requiredCapabilityId)
                .Concat(ContentHostEffectFunctionRegistry.RegisteredCapabilityIds).Concat(new[] { ActivityCapabilityIds.ActivitySequence }),
                expressions, enums, new string[0], catalogue.Catalogue.Keys, sequences.CreateBuilder());
            var timer = Stopwatch.StartNew();
            var warmed = store.WarmExecutionRelease(vocabulary);
            Assert.IsTrue(warmed.IsReady, string.Join(",", warmed.diagnostics.Select(d => d.code)));
            long coldMs = timer.ElapsedMilliseconds;
            timer.Restart();
            Assert.AreSame(warmed.value, store.WarmExecutionRelease(vocabulary).value);
            TestContext.WriteLine("Release preparation cold=" + coldMs + "ms cached=" + timer.ElapsedMilliseconds + "ms");
            foreach (string id in new[] { "topic.turn_on_tv", "topic.locate_tv", "topic.fetch_water" })
            {
                var dependency = store.Release.dependencies.Single(d => d.kind == "topic" && d.id == id);
                var reference = new ContentRef(dependency.store, id, dependency.revision, dependency.expectedHash);
                string run = "synthetic-" + id;
                var snapshot = new ScenarioRuntimeSnapshot(id, run, "synthetic-turn", 1, "", catalogue.Catalogue.Keys,
                    expressions, new[]
                    {
                        new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ActivitySequence, new ScenarioArgumentBinding[0]),
                        new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.ReserveNearest,
                            new[] { new ScenarioArgumentBinding("useId", useIds) })
                    },
                    new ScenarioObjectSnapshot[0], new string[0]);
                var prepared = store.PrepareTopicEntry(reference, catalogue.Catalogue[id].scenarios[0].scenarioId,
                    new Dictionary<string, FlowValue>(), snapshot, vocabulary);
                Assert.IsTrue(prepared.IsReady, id + ": " + string.Join(",", prepared.diagnostics.Select(d => d.code)));
                var model = new ScenarioConversationPresentationModel();
                IReadOnlyList<ScenarioEffect> effects = model.Start(prepared.value, run, snapshot);
                for (int i = 0; i < 8 && effects.Any(e => e.type == "show_dialogue"); i++)
                {
                    ScenarioStep step = effects.First(e => e.type == "show_dialogue").step;
                    foreach (ScenarioBeat beat in step.beats) Assert.IsTrue(model.AcknowledgeDialogueBeat(step.id, beat.id));
                    effects = model.CompleteDialogue(run, step.id);
                }
                var action = effects.Single(e => e.type == "request_activity").activitySequenceRequest;
                Assert.IsNotNull(action, id);
                Assert.IsNotEmpty(action.actionDefinitionRef.expectedHash, id + " projected an unresolved Action hash.");
                var port = new PendingPrimitivePort();
                var executor = new ActivitySequenceExecutor(sequences.Primitives, port);
                var started = executor.Start(action);
                Assert.AreEqual(ActivitySequenceStatuses.Accepted, started.status, id + ": " + started.reasonCode);
                Assert.AreEqual(1, port.startCount, id + " accepted the sequence but blocked its first primitive.");

                var denied = new ScenarioRuntimeSnapshot(id, run, "synthetic-turn", 1, "", catalogue.Catalogue.Keys,
                    expressions, new[] { new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ActivitySequence, new ScenarioArgumentBinding[0]) },
                    new ScenarioObjectSnapshot[0], new string[0]);
                var deniedPort = new PendingPrimitivePort();
                new ActivitySequenceExecutor(sequences.Primitives, deniedPort).Start(new ActivitySequenceRequest(
                    action.definition, action.actionDefinitionRef, action.scenarioRunId, action.flowInvocationId,
                    action.stepExecutionId, action.actionInvocationId, denied, action.arguments, action.scenarioStepId));
                Assert.AreEqual(0, deniedPort.startCount, id + " bypassed the missing furniture permission.");
            }
        }

        private sealed class PendingPrimitivePort : IActivityPrimitivePort
        {
            public int startCount;
            public ActivityPrimitiveStartResult Start(ActivityPrimitiveRequest request)
            { startCount++; return new ActivityPrimitiveStartResult(request.identity, "pending", null, ""); }
            public ActivityPrimitiveCompletion Cancel(ActivityPrimitiveCancellation request)
            { return new ActivityPrimitiveCompletion(request.identity, "cancelled", null, ""); }
        }
    }
}
