using System;
using System.Collections;
using System.Reflection;
using AlterOS.Semantics;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests
{
    public class SplashLoadingTests
    {
        [TestCase(GteSemanticState.NotStarted)]
        [TestCase(GteSemanticState.Loading)]
        [TestCase(GteSemanticState.Failed)]
        public void SplashWaitsUntilRuntimeIsReady(GteSemanticState initialState)
        {
            var host = new GameObject("Splash loading test");
            host.SetActive(false);
            try
            {
                var runtime = host.AddComponent<GteSemanticRuntime>();
                SetState(runtime, initialState);
                var type = Type.GetType("PleiadesOS.UI.SplashController, Assembly-CSharp", true);
                var splash = host.AddComponent(type);
                var method = type.GetMethod("WaitForSemanticRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(method, "Splash must gate its transition on semantic readiness.");
                var wait = (IEnumerator)method.Invoke(splash, new object[] { runtime });
                Assert.IsTrue(wait.MoveNext(), "Splash must not enter gameplay before readiness.");
                Assert.IsTrue(wait.MoveNext(), "A loading or failed runtime must keep the gate closed.");
                SetState(runtime, GteSemanticState.Ready);
                Assert.IsFalse(wait.MoveNext(), "Readiness must release the gate.");
                (wait as IDisposable)?.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [TestCase(GteSemanticState.NotStarted)]
        [TestCase(GteSemanticState.Loading)]
        [TestCase(GteSemanticState.Ready)]
        [TestCase(GteSemanticState.Failed)]
        public void RetryDoesNotStartInactiveRuntime(GteSemanticState state)
        {
            var host = new GameObject("GTE retry guard test");
            host.SetActive(false);
            try
            {
                var runtime = host.AddComponent<GteSemanticRuntime>();
                SetState(runtime, state);
                var retry = typeof(GteSemanticRuntime).GetMethod("TryRetryInitialization");
                Assert.IsNotNull(retry, "Failed startup needs an explicit retry operation.");
                Assert.IsFalse((bool)retry.Invoke(runtime, null));
                Assert.AreEqual(state, runtime.State);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        static void SetState(GteSemanticRuntime runtime, GteSemanticState state)
        {
            typeof(GteSemanticRuntime).GetProperty("State").SetValue(runtime, state);
        }
    }
}
