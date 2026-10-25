using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents a single blend shape weight assignment.
/// </summary>
[System.Serializable]
public class BlendShapeWeight
{
    public string blendShapeName;
    [Range(0f, 1f)]
    public float weight = 1f;
}

/// <summary>
/// Head pose offset for an expression.
/// </summary>
[System.Serializable]
public class HeadPoseOffset
{
    [Tooltip("Head yaw rotation (negative = left, positive = right)")]
    public float yaw = 0f;
    
    [Tooltip("Head pitch rotation (negative = down, positive = up)")]
    public float pitch = 0f;
    
    [Tooltip("Head roll rotation (negative = tilt left, positive = tilt right)")]
    public float roll = 0f;
}

/// <summary>
/// Eye gaze offset for an expression.
/// </summary>
[System.Serializable]
public class EyeGazeOffset
{
    [Tooltip("Horizontal gaze offset (negative = left, positive = right)")]
    public float horizontal = 0f;
    
    [Tooltip("Vertical gaze offset (negative = down, positive = up)")]
    public float vertical = 0f;
}

/// <summary>
/// ScriptableObject defining a complete facial expression.
/// 
/// Contains all data needed to render an expression:
/// - Blend shape weights
/// - Head pose offset
/// - Eye gaze offset
/// - Animation parameters (speed, intensity)
/// </summary>
[CreateAssetMenu(fileName = "Expression_", menuName = "Altera/Expression Preset")]
public class AlteraExpressionPreset : ScriptableObject
{
    [Header("Expression Identity")]
    [Tooltip("Expression type: warm, gentle, playful, etc.")]
    public string expressionName = "neutral";

    [Header("Blend Shape Weights")]
    [Tooltip("List of blend shapes and their weights for this expression")]
    public List<BlendShapeWeight> blendShapes = new List<BlendShapeWeight>();

    [Header("Head Pose")]
    [Tooltip("Head rotation offset relative to neutral")]
    public HeadPoseOffset headPose = new HeadPoseOffset();

    [Header("Eye Gaze")]
    [Tooltip("Eye gaze direction offset")]
    public EyeGazeOffset eyeGaze = new EyeGazeOffset();

    [Header("Animation Settings")]
    [Range(0.1f, 20f)]
    [Tooltip("Speed of blend shape transitions")]
    public float transitionSpeed = 8f;

    [Range(0f, 1f)]
    [Tooltip("Base intensity multiplier for all blend shapes")]
    public float intensityMultiplier = 1f;

    /// <summary>
    /// Get the blend shape weight for a specific shape.
    /// </summary>
    public float GetBlendShapeWeight(string blendShapeName)
    {
        var blendShape = blendShapes.Find(b => b.blendShapeName == blendShapeName);
        return blendShape != null ? blendShape.weight * intensityMultiplier : 0f;
    }

    /// <summary>
    /// Get all blend shapes as a dictionary for efficient lookup.
    /// </summary>
    public Dictionary<string, float> GetBlendShapeDictionary(float intensityOverride = 1f)
    {
        var dict = new Dictionary<string, float>();
        foreach (var blendShape in blendShapes)
        {
            dict[blendShape.blendShapeName] = blendShape.weight * intensityMultiplier * intensityOverride;
        }
        return dict;
    }
}

/// <summary>
/// Enum for common gaze targets.
/// </summary>
public enum GazeTarget
{
    Forward,
    Player,
    Left,
    Right,
    Up,
    Down,
    Away
}
