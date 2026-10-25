using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AlterOS.Tests
{
    public class DialogueCanvasWiringTests
    {
        Scene scene;
        bool opened;

        [OneTimeSetUp]
        public void OpenSceneReadOnly()
        {
            scene = SceneManager.GetSceneByPath("Assets/Scenes/ModelTest.unity");
            opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene("Assets/Scenes/ModelTest.unity", OpenSceneMode.Additive);
        }

        [OneTimeTearDown]
        public void CloseSceneWithoutSaving()
        {
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
        }

        [Test]
        public void AuthoredTaskListSeparatesRows()
        {
            var root = Find("TaskList");
            var scroll = root.GetComponent<ScrollRect>();
            Assert.IsNotNull(scroll, "Additional tasks need an authored scroll viewport.");
            Assert.IsNotNull(scroll.content.GetComponent<VerticalLayoutGroup>(),
                "Dynamic task templates need a Canvas-authored layout, not stacked identical positions.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HUDTaskToggle.prefab");
            Assert.IsNotNull(prefab.GetComponent<LayoutElement>(), "Each task row must provide its authored height.");
        }

        [Test]
        public void WorldChoicesUseTheScrollableContentRoot()
        {
            var host = Find("WorldDialogueChoices");
            var presenter = host.GetComponent(System.Type.GetType(
                "AlterOS.Dialogue.UI.WorldDialogueChoicePresenter, Assembly-CSharp", true));
            var references = new SerializedObject(presenter);
            var content = references.FindProperty("choiceRoot").objectReferenceValue;
            var scroll = host.GetComponentInChildren<ScrollRect>(true);
            Assert.IsNotNull(content, "Choices must not fall back to spawning directly under the Canvas.");
            Assert.AreSame(scroll.content, content, "The scrolled content must be the same root receiving the choices.");
            Assert.IsNotNull(((RectTransform)content).GetComponent<VerticalLayoutGroup>());
        }

        GameObject Find(string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == name)
                        return child.gameObject;
            Assert.Fail("Missing scene object: " + name);
            return null;
        }
    }
}
