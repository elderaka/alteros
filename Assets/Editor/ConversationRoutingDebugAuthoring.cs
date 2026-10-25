using System;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Conversation;
using AlterOS.UI;
using Pleiades.Furniture;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ConversationRoutingDebugAuthoring
{
    [MenuItem("AlterOS/Debug/Create Routing HUD")]
    public static void Create() { Debug.Log(CreateInModelTest()); }

    public static string CreateInModelTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring the HUD.");
        const string scenePath = "Assets/Scenes/ModelTest.unity";
        const string prefabPath = "Assets/AlterOS/Debugging/ConversationRoutingDebug.prefab";
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            if (scene.isDirty) throw new InvalidOperationException("ModelTest has unsaved work; leave it untouched until saved by its owner.");
            var components = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)).ToArray();
            Canvas hud = components.OfType<Canvas>().Single(c => c.name == "AlterOSHUDCanvas");
            if (hud.transform.Find("ConversationRoutingDebug") != null) return "Routing HUD already exists; unchanged.";
            var conversation = components.OfType<DirectConversationBridge>().Single();
            var owner = components.OfType<FurnitureHandlingController>().Single();
            var host = components.OfType<ActivitySceneHost>().Single();
            var primitive = components.OfType<ActivitySequenceScenePrimitivePort>().Single();
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("ConversationRoutingDebug", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(ConversationRoutingDebugView));
            Undo.RegisterCreatedObjectUndo(root, "Add routing debug HUD");
            root.transform.SetParent(hud.transform, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -12f);
            rect.sizeDelta = new Vector2(940f, 580f);
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 200;
            canvas.enabled = false;
            var image = root.GetComponent<Image>();
            image.color = new Color(0.015f, 0.02f, 0.025f, 0.85f);
            image.raycastTarget = false;
            var child = new GameObject("RoutingText", typeof(RectTransform), typeof(TextMeshProUGUI));
            child.transform.SetParent(root.transform, false);
            var label = child.GetComponent<TextMeshProUGUI>();
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(14f, 12f);
            label.rectTransform.offsetMax = new Vector2(-14f, -12f);
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 21f;
            label.color = new Color(0.9f, 1f, 0.9f, 1f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.richText = false;
            label.raycastTarget = false;
            label.text = "ROUTING DEBUG [F5]\nNo input yet.";
            var view = new SerializedObject(root.GetComponent<ConversationRoutingDebugView>());
            view.FindProperty("label").objectReferenceValue = label;
            view.ApplyModifiedPropertiesWithoutUndo();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                throw new InvalidOperationException("Routing prefab already exists without its scene instance; preserve it for owner inspection.");
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.AutomatedAction);
            view.Update();
            view.FindProperty("conversation").objectReferenceValue = conversation;
            view.FindProperty("debugModeOwner").objectReferenceValue = owner;
            view.FindProperty("activityHost").objectReferenceValue = host;
            view.FindProperty("primitivePort").objectReferenceValue = primitive;
            view.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.GetComponent<ConversationRoutingDebugView>());
            var bridge = new SerializedObject(conversation);
            bridge.FindProperty("routingDebugModeOwner").objectReferenceValue = owner;
            bridge.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(conversation);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("ModelTest save failed.");
            return "Saved ModelTest routing HUD and Canvas prefab. F5 owner reused; no new input or state owner.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (openedHere && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
