using System;
using AlterOS.Activities;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterOS.Tests.Activities
{
    public sealed class UnityActivityMotionLifecycleTests
    {
        [Test]
        public void SerializedMotionPrefabReloadsWithoutConstructorException()
        {
            string path = "Assets/AlterOS/Tests/Editor/Activities/MotionLifecycle-" + Guid.NewGuid().ToString("N") + ".prefab";
            GameObject actor = null;
            GameObject contents = null;
            try
            {
                actor = new GameObject("motion-lifecycle-test");
                actor.AddComponent<UnityActivityMotion>();
                Assert.IsNotNull(PrefabUtility.SaveAsPrefabAsset(actor, path));
                UnityEngine.Object.DestroyImmediate(actor);
                actor = null;
                contents = PrefabUtility.LoadPrefabContents(path);
                var motion = contents.GetComponent<UnityActivityMotion>();
                Assert.IsNotNull(motion);
                Assert.IsFalse(motion.NavigationAvailable);
                Assert.IsFalse(motion.CanReach(Vector3.zero));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
                if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
