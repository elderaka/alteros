using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterOS.Tests
{
    public class InactivePanelVisibilityTests
    {
        [TestCase("AlterOS.UI.AlterOSHudPanel", "SetVisible")]
        [TestCase("AlterOS.Dialogue.UI.AlterOSDialoguePanelView", "Hide")]
        public void HidingAnAlreadyInactivePanelDoesNotStartAnAnimation(string typeName, string method)
        {
            var root = new GameObject("Already hidden panel", typeof(RectTransform), typeof(CanvasGroup));
            root.SetActive(false);
            try
            {
                var type = Type.GetType(typeName + ", Assembly-CSharp", true);
                var view = root.AddComponent(type);
                var arguments = method == "SetVisible" ? new object[] { false, false } : null;

                type.GetMethod(method).Invoke(view, arguments);

                Assert.AreEqual(0f, root.GetComponent<CanvasGroup>().alpha,
                    "An already hidden view must apply its hidden state without scheduling a coroutine.");
                Assert.IsFalse(root.activeSelf);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
