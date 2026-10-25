using UnityEngine;
using UnityEditor;

public class AlteraExpressionTester : EditorWindow
{
    private AlteraExpressionPreset[] presets = new AlteraExpressionPreset[0];
    private string[] presetNames = new string[0];
    private int selectedIndex = 0;
    private GameObject target;

    [MenuItem("Altera/Expression Tester")]
    public static void ShowWindow()
    {
        GetWindow<AlteraExpressionTester>("Expression Tester");
    }

    private void OnEnable()
    {
        RefreshPresets();
        Selection.selectionChanged += Repaint;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= Repaint;
    }

    private void RefreshPresets()
    {
        string[] guids = AssetDatabase.FindAssets("t:AlteraExpressionPreset");
        presets = new AlteraExpressionPreset[guids.Length];
        presetNames = new string[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            presets[i] = AssetDatabase.LoadAssetAtPath<AlteraExpressionPreset>(path);
            presetNames[i] = presets[i] != null ? presets[i].name : path;
        }
        if (presetNames.Length == 0)
        {
            presetNames = new string[] { "(no presets found)" };
        }
        selectedIndex = Mathf.Clamp(selectedIndex, 0, presetNames.Length - 1);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        if (GUILayout.Button("Refresh Presets"))
        {
            RefreshPresets();
        }

        EditorGUILayout.Space();
        target = EditorGUILayout.ObjectField("Target (GameObject)", target, typeof(GameObject), true) as GameObject;

        if (Selection.activeGameObject != null && target == null)
        {
            EditorGUILayout.HelpBox("Tip: select a GameObject in the Hierarchy or assign one above.", MessageType.Info);
        }

        EditorGUILayout.Space();
        selectedIndex = EditorGUILayout.Popup("Preset", selectedIndex, presetNames);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply (Play Mode)"))
        {
            ApplyPresetAtRuntime();
        }

        if (GUILayout.Button("Return To Neutral"))
        {
            ReturnToNeutralAtRuntime();
        }

        if (GUILayout.Button("Trigger Animator (Editor)"))
        {
            TriggerAnimatorInEditMode();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Apply will call SetExpression(preset) on the selected GameObject's AlteraEmotionController while in Play Mode.\nTrigger Animator attempts to set a trigger on the controller's Animator in Edit mode.", MessageType.None);
    }

    private void ApplyPresetAtRuntime()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Enter Play Mode", "Please enter Play Mode to apply presets to a runtime component.", "OK");
            return;
        }

        if (presets == null || presets.Length == 0 || presets[selectedIndex] == null)
        {
            EditorUtility.DisplayDialog("No Preset", "No AlteraExpressionPreset assets found. Run the preset generator or create presets under Assets/Altera/Presets.", "OK");
            return;
        }

        GameObject go = target != null ? target : Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("No Target", "Please assign or select a GameObject that has an AlteraEmotionController.", "OK");
            return;
        }

        var controller = go.GetComponentInChildren<AlteraEmotionController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Component Missing", "Selected GameObject doesn't contain an AlteraEmotionController in its children.", "OK");
            return;
        }

        controller.SetExpression(presets[selectedIndex]);
    }

    private void ReturnToNeutralAtRuntime()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Enter Play Mode", "Please enter Play Mode to call runtime methods.", "OK");
            return;
        }

        GameObject go = target != null ? target : Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("No Target", "Please assign or select a GameObject that has an AlteraEmotionController.", "OK");
            return;
        }

        var controller = go.GetComponentInChildren<AlteraEmotionController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Component Missing", "Selected GameObject doesn't contain an AlteraEmotionController in its children.", "OK");
            return;
        }

        controller.ReturnToNeutral(0.5f);
    }

    private void TriggerAnimatorInEditMode()
    {
        GameObject go = target != null ? target : Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("No Target", "Please assign or select a GameObject.", "OK");
            return;
        }

        var controller = go.GetComponentInChildren<AlteraEmotionController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Component Missing", "Selected GameObject doesn't contain an AlteraEmotionController in its children.", "OK");
            return;
        }

        var animator = go.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            EditorUtility.DisplayDialog("Animator Missing", "No Animator found on the selected GameObject.", "OK");
            return;
        }

        string triggerName = presets != null && presets.Length > 0 && presets[selectedIndex] != null ? presets[selectedIndex].name : null;
        if (string.IsNullOrEmpty(triggerName))
        {
            EditorUtility.DisplayDialog("Invalid Trigger", "Preset name is empty; cannot trigger Animator.", "OK");
            return;
        }

        animator.SetTrigger(triggerName);
        EditorUtility.SetDirty(animator);
    }
}
