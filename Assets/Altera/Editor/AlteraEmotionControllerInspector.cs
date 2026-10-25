using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(AlteraEmotionController))]
public class AlteraEmotionControllerInspector : Editor
{
    private AlteraExpressionPreset[] presets = new AlteraExpressionPreset[0];
    private string[] presetNames = new string[0];
    private int selectedIndex = 0;

    private void OnEnable()
    {
        RefreshPresets();
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
        if (presetNames.Length == 0) presetNames = new string[] { "(no presets found)" };
        selectedIndex = Mathf.Clamp(selectedIndex, 0, presetNames.Length - 1);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Refresh Presets")) RefreshPresets();

        selectedIndex = EditorGUILayout.Popup("Preset Preview", selectedIndex, presetNames);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply (Play Mode)"))
        {
            var controller = target as AlteraEmotionController;
            if (controller == null) return;
            if (presets.Length == 0 || presets[selectedIndex] == null)
            {
                EditorUtility.DisplayDialog("No Preset", "No presets available.", "OK");
            }
            else
            {
                if (Application.isPlaying)
                {
                    controller.SetExpression(presets[selectedIndex]);
                }
                else
                {
                    EditorUtility.DisplayDialog("Enter Play Mode", "Switch to Play Mode to apply presets to runtime components, or use Preview.", "OK");
                }
            }
        }

        if (GUILayout.Button("Preview (Editor)"))
        {
            var controller = target as AlteraEmotionController;
            if (controller == null) return;
            if (presets.Length == 0 || presets[selectedIndex] == null)
            {
                EditorUtility.DisplayDialog("No Preset", "No presets available.", "OK");
            }
            else
            {
                controller.SetExpression(presets[selectedIndex]);
                controller.ApplyCurrentTargetsImmediateEditor();
                EditorUtility.SetDirty(controller);
            }
        }
        if (GUILayout.Button("Return To Neutral"))
        {
            var controller = target as AlteraEmotionController;
            if (controller == null) return;
            if (Application.isPlaying)
            {
                controller.ReturnToNeutral(0.5f);
            }
            else
            {
                // In editor preview, reset renderer weights immediately
                controller.ReturnToNeutral(0.1f);
                controller.ApplyCurrentTargetsImmediateEditor();
                EditorUtility.SetDirty(controller);
            }
        }
        EditorGUILayout.EndHorizontal();
    }
}
