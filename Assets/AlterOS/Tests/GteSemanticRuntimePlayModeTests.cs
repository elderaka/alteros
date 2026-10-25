using System.Collections;
using AlterOS.Semantics;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlterOS.Tests
{
    public class GteSemanticRuntimePlayModeTests
    {
        [Test]
        public void EmbeddingValidationRejectsZeroEmptyAndNonFiniteOutput()
        {
            foreach (var embedding in new[] { null, new float[0], new float[768], new[] { float.NaN }, new[] { float.PositiveInfinity } })
                Assert.Throws<GteSemanticQueryEncoder.InvalidEmbeddingException>(
                    () => GteSemanticQueryEncoder.ValidateEmbedding(embedding));
            Assert.DoesNotThrow(() => GteSemanticQueryEncoder.ValidateEmbedding(new[] { 0.6f, 0.8f }));
        }

        [UnityTest]
        public IEnumerator RetryFailedRuntimeDoesNotStartDuplicateWorkers()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var runtime = GteSemanticRuntime.Current;
            Assert.IsNotNull(runtime);
            while (runtime.State == GteSemanticState.Loading)
            {
                Assert.Less(timer.Elapsed.TotalSeconds, 180, "Initial warm-up timed out.");
                Assert.IsFalse(runtime.TryRetryInitialization(), "Loading must reject retries on an active host.");
                yield return null;
            }
            Assert.AreEqual(GteSemanticState.Ready, runtime.State, runtime.FailureReason);
            Assert.IsFalse(runtime.TryRetryInitialization(), "Ready must not restart inference.");

            // Synthetic failure without touching model assets; matches failed-worker cleanup.
            var worker = typeof(GteSemanticRuntime).GetField("m_Worker",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            ((System.IDisposable)worker.GetValue(runtime)).Dispose();
            worker.SetValue(runtime, null);
            typeof(GteSemanticRuntime).GetProperty("FailureReason").SetValue(runtime, "Synthetic retry failure.");
            typeof(GteSemanticRuntime).GetProperty("State").SetValue(runtime, GteSemanticState.Failed);

            Assert.IsTrue(runtime.TryRetryInitialization());
            Assert.AreEqual(GteSemanticState.Loading, runtime.State);
            Assert.IsNull(runtime.FailureReason);
            Assert.IsFalse(runtime.TryRetryInitialization(), "A second immediate retry must not start another worker.");
            timer.Restart();
            while (runtime.State == GteSemanticState.Loading)
            {
                Assert.Less(timer.Elapsed.TotalSeconds, 180, "Retry warm-up timed out.");
                yield return null;
            }
            Assert.AreEqual(GteSemanticState.Ready, runtime.State, runtime.FailureReason);
            Assert.IsTrue(runtime.TryRoute("ngl im kinda tired", out var matches));
            Assert.AreEqual("player_tired", matches[0].RouteId);
        }

        [UnityTest]
        public IEnumerator StartupPrewarmsAndRoutes()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int loadingFrames = 0;
            float maximumObservedFrameSeconds = 0f;
            while (GteSemanticRuntime.Current == null || GteSemanticRuntime.Current.State == GteSemanticState.Loading)
            {
                if (timer.Elapsed.TotalSeconds > 180)
                    Assert.Fail("GTE runtime did not finish startup warm-up.");
                loadingFrames++;
                maximumObservedFrameSeconds = UnityEngine.Mathf.Max(maximumObservedFrameSeconds, UnityEngine.Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.AreEqual(GteSemanticState.Ready, GteSemanticRuntime.Current.State, GteSemanticRuntime.Current.FailureReason);
            UnityEngine.Debug.Log($"GTE startup test: {loadingFrames} observed loading frames, {timer.Elapsed.TotalSeconds:F2}s wall time, max observed frame {maximumObservedFrameSeconds:F3}s.");
            Assert.IsTrue(GteSemanticRuntime.Current.TryRoute("ngl im kinda tired", out var matches));
            Assert.IsNotEmpty(matches);
            Assert.AreEqual("player_tired", matches[0].RouteId);
        }
    }
}
