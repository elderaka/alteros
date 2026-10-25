using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterOS.Tests
{
    public class ExpressionAnimatorTests
    {
        GameObject host;
        AnimatorController controller;
        Component emotion;
        Type emotionType;
        Animator animator;

        [SetUp]
        public void SetUp()
        {
            emotionType = Type.GetType("AlteraEmotionController, Assembly-CSharp", true);
            host = new GameObject("Expression animator test");
            animator = host.AddComponent<Animator>();
            controller = new AnimatorController();
            controller.AddLayer("Base Layer");
            controller.layers[0].stateMachine.AddState("Idle");
            controller.AddParameter("Default", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Joy", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Surprised", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Angry", AnimatorControllerParameterType.Trigger);
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Update(0f);
            emotion = host.AddComponent(emotionType);
            emotionType.GetField("animator").SetValue(emotion, animator);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
            foreach (var layer in controller.layers)
            {
                foreach (var state in layer.stateMachine.states)
                    UnityEngine.Object.DestroyImmediate(state.state);
                UnityEngine.Object.DestroyImmediate(layer.stateMachine);
            }
            UnityEngine.Object.DestroyImmediate(controller);
        }

        [TestCase("neutral", "Default")]
        [TestCase("happy", "Joy")]
        [TestCase("surprised", "Surprised")]
        [TestCase("angry", "Angry")]
        public void ExpressionUsesExistingBodyTrigger(string expression, string expectedTrigger)
        {
            SetExpression(expression);
            Assert.IsTrue(animator.GetBool(expectedTrigger),
                "The expression should activate the controller's actual trigger.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void FacialOnlyExpressionDoesNotInventBodyTrigger()
        {
            SetExpression("gentle");
            LogAssert.NoUnexpectedReceived();
            Assert.IsFalse(animator.GetBool("Default"));
            Assert.IsFalse(animator.GetBool("Joy"));
        }

        [Test]
        public void ReturnToNeutralUsesDefaultBodyTrigger()
        {
            emotionType.GetMethod("ReturnToNeutral").Invoke(emotion, new object[] { 0.5f });
            Assert.IsTrue(animator.GetBool("Default"));
            LogAssert.NoUnexpectedReceived();
        }

        void SetExpression(string expression)
        {
            emotionType.GetMethod("SetExpression", new[] { typeof(string), typeof(float), typeof(float) })
                .Invoke(emotion, new object[] { expression, 1f, 1f });
        }
    }
}
