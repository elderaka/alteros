using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using AlterOS.Semantics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterOS.Tests
{
    public class StartupResponsivenessTests
    {
        [UnityTest]
        public IEnumerator FailedWarmupReleasesWorker()
        {
            var host = new GameObject("GTE failed warm-up test");
            host.SetActive(false);
            IEnumerator initialization = null;
            var workerField = typeof(GteSemanticRuntime).GetField("m_Worker", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                var runtime = host.AddComponent<GteSemanticRuntime>();
                initialization = (IEnumerator)typeof(GteSemanticRuntime)
                    .GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(runtime, null);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (workerField.GetValue(runtime) == null)
                {
                    // Match the startup test's budget; cold Editor asset loads can exceed a minute.
                    Assert.Less(timer.Elapsed.TotalSeconds, 180, "Worker creation timed out.");
                    Assert.IsTrue(initialization.MoveNext(), runtime.FailureReason);
                    if (initialization.Current is AsyncOperation operation)
                    {
                        while (!operation.isDone)
                            yield return null;
                    }
                    yield return null;
                }

                // A disposed backend makes the next real scheduling operation fail.
                ((IDisposable)workerField.GetValue(runtime)).Dispose();
                LogAssert.Expect(LogType.Error, new Regex("AlterOS GTE semantic runtime failed to initialize:"));
                while (initialization.MoveNext())
                    yield return null;

                Assert.AreEqual(GteSemanticState.Failed, runtime.State);
                Assert.IsNotEmpty(runtime.FailureReason);
                Assert.IsNull(workerField.GetValue(runtime), "A failed persistent runtime must not retain its worker.");
            }
            finally
            {
                (initialization as IDisposable)?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void InitializationYieldsWhileStillLoading()
        {
            var host = new GameObject("GTE initialization test");
            host.SetActive(false);
            IEnumerator initialization = null;
            try
            {
                var runtime = host.AddComponent<GteSemanticRuntime>();
                initialization = (IEnumerator)typeof(GteSemanticRuntime)
                    .GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(runtime, null);

                Assert.IsTrue(initialization.MoveNext());
                Assert.IsTrue(initialization.MoveNext(),
                    "Initialization must yield during loading, not load and warm the whole model in one frame.");
                Assert.AreEqual(GteSemanticState.Loading, runtime.State);
            }
            finally
            {
                (initialization as IDisposable)?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
