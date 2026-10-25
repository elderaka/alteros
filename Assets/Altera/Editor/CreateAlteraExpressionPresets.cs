using UnityEditor;
using UnityEngine;
using System.IO;

// Editor utility to generate default AlteraExpressionPreset assets
public static class CreateAlteraExpressionPresets
{
    private const string OutputFolder = "Assets/Altera/Presets";

    [MenuItem("Altera/Expressions/Create Default Presets")]
    public static void CreatePresets()
    {
        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);

        CreatePreset("warm", new (string, float)[] {
            ("Brow_Joy", 0.15f), ("Eye_Joy", 0.10f), ("mouthSmile", 0.20f), ("Mouth_Joy", 0.10f)
        }, yaw: 5f, pitch: 0f, roll: 3f, transition: 8f, intensity: 1f);

        CreatePreset("gentle", new (string, float)[] {
            ("Brow_Joy", 0.10f), ("Eye_Natural", 0.05f), ("eyeSquintLeft", 0.05f), ("Mouth_Small", 0.10f), ("mouthSmile", 0.12f)
        }, yaw: 3f, pitch: 0f, roll: -2f, transition: 8f, intensity: 0.9f);

        CreatePreset("soft", new (string, float)[] {
            ("browInnerUp", 0.08f), ("Eye_Natural", 0.05f), ("eyeSquintLeft", 0.08f), ("Mouth_Small", 0.12f), ("mouthSmile", 0.05f)
        }, yaw: 0f, pitch: -2f, roll: -4f, transition: 8f, intensity: 0.7f);

        CreatePreset("focused", new (string, float)[] {
            ("Brow_Angry", 0.08f), ("Eye_Natural", 0.02f), ("Mouth_Neutral", 0.15f), ("mouthPressLeft", 0.08f)
        }, yaw: -4f, pitch: 0f, roll: 0f, transition: 6f, intensity: 0.85f);

        CreatePreset("thinking", new (string, float)[] {
            ("Brow_Tilt", 0.15f), ("browInnerUp", 0.15f), ("Eye_Natural", 0.05f), ("Mouth_Small", 0.10f), ("mouthPressLeft", 0.12f)
        }, yaw: -10f, pitch: -2f, roll: 0f, transition: 6f, intensity: 0.8f);

        CreatePreset("playful", new (string, float)[] {
            ("Expression_Fun", 0.25f), ("eyeBlinkLeft", 0.10f), ("Mouth_Fun", 0.20f), ("mouthSmile", 0.25f)
        }, yaw: 8f, pitch: 0f, roll: 6f, transition: 10f, intensity: 1f);

        CreatePreset("shy", new (string, float)[] {
            ("Brow_Sorrow", 0.08f), ("browInnerUp", 0.12f), ("Eye_Sorrow", 0.10f), ("Mouth_Small", 0.10f), ("mouthSmile", 0.05f)
        }, yaw: 12f, pitch: -6f, roll: 4f, transition: 8f, intensity: 0.65f);

        CreatePreset("happy", new (string, float)[] {
            ("Expression_Joy", 0.35f), ("Brow_Joy", 0.20f), ("Eye_Joy", 0.20f), ("mouthSmile", 0.35f), ("Mouth_Joy", 0.25f)
        }, yaw: 0f, pitch: 0f, roll: 4f, transition: 10f, intensity: 1f);

        CreatePreset("sad", new (string, float)[] {
            ("Expression_Sorrow", 0.30f), ("Brow_Sorrow", 0.25f), ("Eye_Sorrow", 0.20f), ("mouthFrown", 0.20f), ("Mouth_Sorrow", 0.18f)
        }, yaw: 0f, pitch: -7f, roll: 0f, transition: 6f, intensity: 0.9f);

        CreatePreset("angry", new (string, float)[] {
            ("Expression_Angry", 0.35f), ("Brow_Angry", 0.30f), ("Eye_Angry", 0.20f), ("Mouth_Angry", 0.20f), ("mouthPressLeft", 0.15f)
        }, yaw: 0f, pitch: -5f, roll: 0f, transition: 6f, intensity: 0.95f);

        CreatePreset("surprised", new (string, float)[] {
            ("Expression_Surprised", 0.40f), ("Eye_Surprised", 0.35f), ("Mouth_Surprised", 0.30f), ("jawOpen", 0.25f)
        }, yaw: 0f, pitch: 2f, roll: 0f, transition: 12f, intensity: 1f);

        CreatePreset("neutral", new (string, float)[] {
            ("Expression_Neutral", 0.10f), ("Eye_Natural", 0.10f), ("Mouth_Neutral", 0.12f)
        }, yaw: 0f, pitch: 0f, roll: 0f, transition: 6f, intensity: 1f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.FocusProjectWindow();
        Debug.Log("Altera expression presets created in Assets/Altera/Presets");
    }

    private static void CreatePreset(string name, (string, float)[] blends, float yaw, float pitch, float roll, float transition, float intensity)
    {
        var preset = ScriptableObject.CreateInstance<AlteraExpressionPreset>();
        preset.expressionName = name;
        preset.transitionSpeed = transition;
        preset.intensityMultiplier = intensity;
        preset.headPose.yaw = yaw;
        preset.headPose.pitch = pitch;
        preset.headPose.roll = roll;
        preset.eyeGaze.horizontal = 0f;
        preset.eyeGaze.vertical = 0f;

        preset.blendShapes = new System.Collections.Generic.List<BlendShapeWeight>();
        foreach (var b in blends)
        {
            var bw = new BlendShapeWeight { blendShapeName = b.Item1, weight = b.Item2 };
            preset.blendShapes.Add(bw);
        }

        string path = Path.Combine(OutputFolder, name + ".asset");
        AssetDatabase.CreateAsset(preset, path);
    }
}
