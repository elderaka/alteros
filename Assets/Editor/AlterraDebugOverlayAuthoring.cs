using AlterOS.Activities;
using AlterOS.Conversation;
using AlterOS.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class AlterraDebugOverlayAuthoring
{
    [MenuItem("AlterOS/Debug/Create Activity Overlay")]
    public static void Create()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/ModelTest.unity")
        {
            Debug.LogWarning("Open ModelTest outside Play Mode to author the activity overlay.");
            return;
        }
        var actor = GameObject.Find("Altera");
        var host = Object.FindAnyObjectByType<ActivitySceneHost>();
        var conversation = Object.FindAnyObjectByType<DirectConversationBridge>();
        var dialogue = Object.FindAnyObjectByType<DialogueManager>();
        if (actor == null || host == null || conversation == null || dialogue == null)
        {
            Debug.LogError("Activity overlay requires the existing Alterra, activity host and dialogue references.");
            return;
        }
        if (actor.transform.Find("ActivityDebugOverlay") != null)
        {
            Debug.Log("Alterra activity overlay already exists; no duplicate created.");
            return;
        }
        bool previouslyDirty = scene.isDirty;
        var root = new GameObject("ActivityDebugOverlay", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(AlterraActivityDebugView));
        Undo.RegisterCreatedObjectUndo(root, "Add Alterra activity debug overlay");
        root.transform.SetParent(actor.transform, false);
        root.transform.localPosition = new Vector3(0f, 2.15f, 0f);
        root.transform.localScale = Vector3.one * 0.0025f;
        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(720f, 170f);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var background = root.GetComponent<Image>();
        background.color = new Color(0.025f, 0.035f, 0.05f, 0.85f);
        background.raycastTarget = false;
        var textObject = new GameObject("StateLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(root.transform, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(14f, 10f);
        text.rectTransform.offsetMax = new Vector2(-14f, -10f);
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 27f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        text.text = "State: initializing\nAction: none\nScenario: none";

        const string prefabPath = "Assets/AlterOS/Testbox/ActivityDebugOverlay.prefab";
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath))
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.AutomatedAction);
        var serialized = new SerializedObject(root.GetComponent<AlterraActivityDebugView>());
        serialized.FindProperty("label").objectReferenceValue = text;
        serialized.FindProperty("activityHost").objectReferenceValue = host;
        serialized.FindProperty("conversation").objectReferenceValue = conversation;
        serialized.FindProperty("dialogue").objectReferenceValue = dialogue;
        serialized.FindProperty("viewingCamera").objectReferenceValue = Camera.main;
        serialized.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(root.GetComponent<AlterraActivityDebugView>());
        EditorSceneManager.MarkSceneDirty(scene);
        if (!previouslyDirty) EditorSceneManager.SaveScene(scene);
        Debug.Log(previouslyDirty ? "Activity overlay authored; scene left unsaved to preserve prior edits." : "Activity overlay authored and ModelTest saved.");
    }
}
