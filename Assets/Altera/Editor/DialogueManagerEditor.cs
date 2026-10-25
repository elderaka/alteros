using UnityEngine;
using UnityEditor;
using System.IO;

[CustomEditor(typeof(DialogueManager))]
public class DialogueManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DialogueManager manager = (DialogueManager)target;

        // Draw the default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Testing Dropdown", EditorStyles.boldLabel);

        if (manager.availableTestFiles == null || manager.availableTestFiles.Length == 0)
        {
            if (GUILayout.Button("Scan Dialogue Folder"))
            {
                manager.RefreshDialogueList();
            }
        }
        else
        {
            // Dropdown for file selection
            int newIndex = EditorGUILayout.Popup("Select Dialogue", manager.selectedTestFileIndex, manager.availableTestFiles);
            
            if (newIndex != manager.selectedTestFileIndex || string.IsNullOrEmpty(manager.testDialogueFile))
            {
                manager.selectedTestFileIndex = newIndex;
                manager.testDialogueFile = manager.availableTestFiles[newIndex];
                EditorUtility.SetDirty(manager);
            }

            if (GUILayout.Button("Refresh List"))
            {
                manager.RefreshDialogueList();
            }

            GUI.color = Color.green;
            if (GUILayout.Button("▶ Play Selected Dialogue", GUILayout.Height(30)))
            {
                manager.PlayTestDialogueFile();
            }
            GUI.color = Color.white;
        }
    }
}
