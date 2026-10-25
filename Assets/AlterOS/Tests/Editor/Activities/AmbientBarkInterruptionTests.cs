using System;
using System.Collections;
using System.Reflection;
using AlterOS.Activities;
using AlterOS.Content;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterOS.Tests.Activities
{
    public sealed class AmbientBarkInterruptionTests
    {
        [UnityTest]
        public IEnumerator StopBeforeVisibilityAcknowledgesFailureExactlyOnce()
        {
            var fixture = new AmbientBubbleFixture();
            try
            {
                int calls = 0;
                bool delivered = true;
                Assert.IsTrue(fixture.TrySay(value => { calls++; delivered = value; }));

                fixture.Stop();
                yield return null;

                Assert.AreEqual(1, calls);
                Assert.IsFalse(delivered);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void StopAfterVisibilityAcknowledgementDoesNotReplaceSuccess()
        {
            var fixture = new AmbientBubbleFixture();
            try
            {
                int calls = 0;
                bool delivered = false;
                Assert.IsTrue(fixture.TrySay(value => { calls++; delivered = value; }));

                fixture.AcknowledgeVisiblePresentation();

                Assert.AreEqual(1, calls);
                Assert.IsTrue(delivered);

                fixture.Stop();

                Assert.AreEqual(1, calls);
                Assert.IsTrue(delivered);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void OnDisableHandlerAcknowledgesPendingFailureExactlyOnce()
        {
            var fixture = new AmbientBubbleFixture();
            try
            {
                int calls = 0;
                bool delivered = true;
                Assert.IsTrue(fixture.TrySay(value => { calls++; delivered = value; }));

                fixture.InvokeDisableHandler();
                fixture.InvokeDisableHandler();

                Assert.AreEqual(1, calls);
                Assert.IsFalse(delivered);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void SequenceCancellationIgnoresReentrantFailureFromRealBubbleStop()
        {
            var fixture = new AmbientBubbleFixture();
            try
            {
                var presenter = new ReflectionAmbientPresenter(fixture);
                var operation = new ActivitySequenceBarkOperation(presenter);
                var identity = new ActivityPrimitiveIdentity("run", "flow", "step-execution", "action", "bark", "action:bark");
                var request = new ActivityPrimitiveRequest(identity,
                    new ContentRef("store", "action.locate", "revision", "hash"), ActivityPrimitiveIds.Bark,
                    new[] { ActivitySequenceArgument.FromLiteral("dialogueId", FlowValue.Text("dialogue.test")) });

                Assert.AreEqual(ActivitySequenceStatuses.Running, operation.Start(request).status);
                ActivityPrimitiveCompletion cancelled = operation.Cancel(new ActivityPrimitiveCancellation(identity, "stop"));
                ActivityPrimitiveCompletion late = operation.Poll(identity, 1d);

                Assert.AreEqual(ActivitySequenceStatuses.Cancelled, cancelled.status);
                Assert.IsNull(late);
                Assert.AreEqual(1, presenter.callbackCount);
                Assert.IsFalse(presenter.lastVisible);
            }
            finally { fixture.Dispose(); }
        }

        private sealed class ReflectionAmbientPresenter : IActivitySequenceBarkPresenter
        {
            private readonly AmbientBubbleFixture fixture;
            public int callbackCount;
            public bool lastVisible;

            public ReflectionAmbientPresenter(AmbientBubbleFixture bubbleFixture) { fixture = bubbleFixture; }

            public bool TryPresent(ActivityPrimitiveRequest request, Action<ActivityPrimitiveIdentity, bool> acknowledgement)
            {
                return fixture.TrySay(visible =>
                {
                    callbackCount++;
                    lastVisible = visible;
                    acknowledgement(request.identity, visible);
                }, request.identity.invocationId);
            }

            public bool Cancel(ActivityPrimitiveIdentity identity)
            { return fixture.Cancel(identity.invocationId); }
        }

        private sealed class AmbientBubbleFixture : IDisposable
        {
            private readonly GameObject bubbleObject;
            private readonly Canvas canvas;
            private readonly Component text;
            private readonly Component bubble;
            private readonly MethodInfo say;
            private readonly MethodInfo stop;
            private readonly MethodInfo cancel;
            private readonly MethodInfo acknowledgeVisibility;
            private readonly MethodInfo disable;
            private readonly Type bubbleType;

            public AmbientBubbleFixture()
            {
                bubbleType = Type.GetType("AlterOS.AlteraAmbientBubble, Assembly-CSharp");
                Type tmpType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                Assert.IsNotNull(bubbleType, "The test requires the production Assembly-CSharp ambient presenter.");
                Assert.IsNotNull(tmpType, "The test requires TextMeshPro from the installed Unity package.");

                bubbleObject = new GameObject("ambient-bark-interruption-test");
                bubble = bubbleObject.AddComponent(bubbleType);
                var canvasObject = new GameObject("BubbleCanvas", typeof(RectTransform), typeof(Canvas));
                canvasObject.transform.SetParent(bubbleObject.transform, false);
                canvas = canvasObject.GetComponent<Canvas>();
                var textObject = new GameObject("BubbleText", typeof(RectTransform), typeof(CanvasRenderer));
                textObject.transform.SetParent(canvasObject.transform, false);
                text = textObject.AddComponent(tmpType);
                Set("bubbleCanvas", canvas);
                Set("bubbleText", text);
                Set("fadeSpeed", 20f);

                say = bubbleType.GetMethod("TrySayWithAcknowledgement", BindingFlags.Instance | BindingFlags.Public);
                stop = bubbleType.GetMethod("StopSpat", BindingFlags.Instance | BindingFlags.Public);
                cancel = bubbleType.GetMethod("CancelAcknowledgedSay", BindingFlags.Instance | BindingFlags.Public);
                acknowledgeVisibility = bubbleType.GetMethod("AcknowledgePresentationVisibility",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                disable = bubbleType.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(say);
                Assert.IsNotNull(stop);
                Assert.IsNotNull(cancel);
                Assert.IsNotNull(acknowledgeVisibility);
                Assert.IsNotNull(disable);
            }

            public bool TrySay(Action<bool> acknowledgement, string presentationId = "test-presentation")
            {
                return (bool)say.Invoke(bubble, new object[]
                { presentationId, "hello", "hello", "en", acknowledgement });
            }

            public bool Cancel(string presentationId)
            { return (bool)cancel.Invoke(bubble, new object[] { presentationId }); }

            public void AcknowledgeVisiblePresentation()
            {
                canvas.gameObject.SetActive(true);
                text.gameObject.SetActive(true);
                acknowledgeVisibility.Invoke(bubble, new object[] { "test-presentation" });
            }

            public void Stop() { stop.Invoke(bubble, null); }
            public void InvokeDisableHandler() { disable.Invoke(bubble, null); }

            public void Dispose()
            {
                if (bubbleObject != null) UnityEngine.Object.DestroyImmediate(bubbleObject);
            }

            private void Set(string fieldName, object value)
            {
                FieldInfo field = bubbleType.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.IsNotNull(field, "Expected ambient presentation field " + fieldName);
                field.SetValue(bubble, value);
            }
        }
    }
}
