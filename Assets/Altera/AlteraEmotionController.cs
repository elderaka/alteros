using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UniVRM10;

public class AlteraEmotionController : MonoBehaviour
{
    [Header("Components")]
    public Vrm10Instance vrmInstance;
    public Animator animator;

    [Header("References")]
    public AlteraLipSync lipSync;
    public Blinker blinker;

    [Header("Expression Presets")]
    public AlteraExpressionPreset[] expressionPresets = new AlteraExpressionPreset[4];

    [Header("Blend Shape Binding")]
    public Transform blendShapeRoot;
    public SkinnedMeshRenderer[] manualBlendRenderers;

    [Header("Head / Eye Binding")]
    public Transform headBone;
    public Transform neckBone;
    public Transform leftEyeBone;
    public Transform rightEyeBone;

    private AlteraExpressionPreset activePreset;
    private float currentIntensityOverride = 1f;
    private Dictionary<string, float> blendShapeTargets = new Dictionary<string, float>();
    private Dictionary<string, float> blendShapeCurrent = new Dictionary<string, float>();
    private Dictionary<string, (SkinnedMeshRenderer renderer, int index)> blendShapeBindings = new Dictionary<string, (SkinnedMeshRenderer, int)>();

    private Vector3 headPoseTarget = Vector3.zero;
    private Vector3 headPoseCurrent = Vector3.zero;
    private float headPoseTransitionSpeed = 5f;
    private Quaternion initialHeadLocalRotation = Quaternion.identity;
    private Quaternion initialNeckLocalRotation = Quaternion.identity;
    private bool hasHeadBaselineRotation;
    private bool hasNeckBaselineRotation;
    private bool hasHeadPoseOverride;

    private Vector2 eyeGazeTarget = Vector2.zero;
    private Vector2 eyeGazeCurrent = Vector2.zero;
    private float eyeGazeTransitionSpeed = 10f;

    private void Start()
    {
        if (vrmInstance == null) vrmInstance = GetComponentInChildren<Vrm10Instance>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (lipSync == null) lipSync = GetComponentInChildren<AlteraLipSync>();
        if (blinker == null) blinker = GetComponentInChildren<Blinker>();

        ResolveHeadReferences();
        BuildBlendShapeBindings();

        if (expressionPresets != null && expressionPresets.Length > 0 && expressionPresets[0] != null)
        {
            SetExpression(expressionPresets[0]);
        }
    }

    private void Update()
    {
        HandleInput();
        UpdateBlendShapes();
        UpdateEyeGaze();
    }

    private void LateUpdate()
    {
        UpdateHeadPose();
    }

    private void HandleInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.digit1Key.wasPressedThisFrame && expressionPresets.Length > 0 && expressionPresets[0] != null) SetExpression(expressionPresets[0]);
        if (keyboard.digit2Key.wasPressedThisFrame && expressionPresets.Length > 1 && expressionPresets[1] != null) SetExpression(expressionPresets[1]);
        if (keyboard.digit3Key.wasPressedThisFrame && expressionPresets.Length > 2 && expressionPresets[2] != null) SetExpression(expressionPresets[2]);
        if (keyboard.digit4Key.wasPressedThisFrame && expressionPresets.Length > 3 && expressionPresets[3] != null) SetExpression(expressionPresets[3]);
    }

    private void BuildBlendShapeBindings()
    {
        blendShapeBindings.Clear();

        SkinnedMeshRenderer[] renderers = null;
        if (manualBlendRenderers != null && manualBlendRenderers.Length > 0)
        {
            renderers = manualBlendRenderers;
        }
        else if (blendShapeRoot != null)
        {
            renderers = blendShapeRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }
        else if (vrmInstance != null)
        {
            renderers = vrmInstance.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        if (renderers == null) return;

        foreach (var r in renderers)
        {
            if (r == null || r.sharedMesh == null) continue;

            var mesh = r.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(name)) continue;
                if (!blendShapeBindings.ContainsKey(name)) blendShapeBindings[name] = (r, i);
            }
        }
    }

    private void ResolveHeadReferences()
    {
        if (animator != null && animator.isHuman)
        {
            if (headBone == null) headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (neckBone == null) neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (leftEyeBone == null) leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            if (rightEyeBone == null) rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);
        }

        if (headBone != null && !hasHeadBaselineRotation)
        {
            initialHeadLocalRotation = headBone.localRotation;
            hasHeadBaselineRotation = true;
        }

        if (neckBone != null && !hasNeckBaselineRotation)
        {
            initialNeckLocalRotation = neckBone.localRotation;
            hasNeckBaselineRotation = true;
        }
    }

    public void ApplyCurrentTargetsImmediateEditor()
    {
        BuildBlendShapeBindings();
        foreach (var kvp in blendShapeTargets)
        {
            if (blendShapeBindings.TryGetValue(kvp.Key, out var binding))
            {
                binding.renderer.SetBlendShapeWeight(binding.index, kvp.Value * 100f);
                blendShapeCurrent[kvp.Key] = kvp.Value;
            }
        }
    }

    public void SetExpression(AlteraExpressionPreset preset, float intensityOverride = 1f, float blendWeight = 1f)
    {
        if (preset == null)
        {
            Debug.LogError("[AlteraEmotionController] Cannot set null expression preset");
            return;
        }

        if (IsExpressionBlocked(preset)) return;

        activePreset = preset;
        currentIntensityOverride = intensityOverride;
        blendShapeTargets = preset.GetBlendShapeDictionary(intensityOverride);

        hasHeadPoseOverride = preset.headPose != null &&
            (Mathf.Abs(preset.headPose.pitch) > 0.0001f || Mathf.Abs(preset.headPose.yaw) > 0.0001f || Mathf.Abs(preset.headPose.roll) > 0.0001f);

        if (hasHeadPoseOverride)
        {
            headPoseTarget = new Vector3(preset.headPose.pitch, preset.headPose.yaw, preset.headPose.roll);
            headPoseTransitionSpeed = preset.transitionSpeed;
        }
        else
        {
            headPoseTarget = Vector3.zero;
        }

        eyeGazeTarget = new Vector2(preset.eyeGaze.horizontal, preset.eyeGaze.vertical);
        eyeGazeTransitionSpeed = preset.transitionSpeed;

        string triggerName = string.IsNullOrWhiteSpace(preset.expressionName) ? "neutral" : preset.expressionName;
        TrySetBodyExpressionTrigger(triggerName);

        Debug.Log($"[AlteraEmotionController] Expression set to: {triggerName} (intensity: {intensityOverride})");
    }

    public void SetHeadOffset(float yaw = 0f, float pitch = 0f, float roll = 0f, float transitionSpeed = 5f)
    {
        headPoseTarget = new Vector3(pitch, yaw, roll);
        headPoseTransitionSpeed = transitionSpeed;
        hasHeadPoseOverride = true;
    }

    public void SetGaze(GazeTarget target)
    {
        switch (target)
        {
            case GazeTarget.Forward: eyeGazeTarget = Vector2.zero; break;
            case GazeTarget.Player: eyeGazeTarget = Vector2.zero; break;
            case GazeTarget.Left: eyeGazeTarget = new Vector2(-1f, 0f); break;
            case GazeTarget.Right: eyeGazeTarget = new Vector2(1f, 0f); break;
            case GazeTarget.Up: eyeGazeTarget = new Vector2(0f, 1f); break;
            case GazeTarget.Down: eyeGazeTarget = new Vector2(0f, -1f); break;
            case GazeTarget.Away: eyeGazeTarget = new Vector2(0.5f, 0.5f); break;
        }
    }

    public void SetGaze(float horizontal, float vertical, float transitionSpeed = 10f)
    {
        eyeGazeTarget = new Vector2(horizontal, vertical);
        eyeGazeTransitionSpeed = transitionSpeed;
    }

    private void UpdateBlendShapes()
    {
        if (vrmInstance == null || vrmInstance.Runtime == null) return;

        foreach (var kvp in blendShapeTargets)
        {
            string blendShapeName = kvp.Key;
            float target = kvp.Value;

            if (IsBlendShapeForMouth(blendShapeName) && lipSync != null && lipSync.IsTalking) target = 0f;
            if (IsBlendShapeForEyes(blendShapeName) && blinker != null && blinker.IsBlinking) target = 0f;

            if (!blendShapeCurrent.ContainsKey(blendShapeName))
            {
                blendShapeCurrent[blendShapeName] = 0f;
            }

            float current = blendShapeCurrent[blendShapeName];
            if (Mathf.Abs(current - target) > 0.001f)
            {
                current = Mathf.Lerp(current, target, Time.deltaTime * (activePreset != null ? activePreset.transitionSpeed : 8f));
                blendShapeCurrent[blendShapeName] = current;

                if (blendShapeBindings.TryGetValue(blendShapeName, out var binding))
                {
                    binding.renderer.SetBlendShapeWeight(binding.index, current * 100f);
                }
            }
        }
    }

    private void UpdateHeadPose()
    {
        bool shouldBlendHead = hasHeadPoseOverride || headPoseCurrent.sqrMagnitude > 0.0001f || headPoseTarget.sqrMagnitude > 0.0001f;
        if (!shouldBlendHead) return;

        if (headBone == null)
        {
            ResolveHeadReferences();
            if (headBone == null) return;
        }

        headPoseCurrent = Vector3.Lerp(headPoseCurrent, headPoseTarget, Time.deltaTime * headPoseTransitionSpeed);

        Quaternion targetRotation = Quaternion.Euler(headPoseCurrent.x, headPoseCurrent.y, headPoseCurrent.z);

        if (neckBone != null)
        {
            neckBone.localRotation = neckBone.localRotation * Quaternion.Slerp(Quaternion.identity, targetRotation, 0.45f);
            headBone.localRotation = headBone.localRotation * Quaternion.Slerp(Quaternion.identity, targetRotation, 0.75f);
        }
        else
        {
            headBone.localRotation = headBone.localRotation * targetRotation;
        }
    }

    private void UpdateEyeGaze()
    {
        eyeGazeCurrent = Vector2.Lerp(eyeGazeCurrent, eyeGazeTarget, Time.deltaTime * eyeGazeTransitionSpeed);
    }

    private bool IsExpressionBlocked(AlteraExpressionPreset preset)
    {
        foreach (var blendShape in preset.blendShapes)
        {
            if (IsBlendShapeForMouth(blendShape.blendShapeName) && lipSync != null && lipSync.IsTalking) return true;
            if (IsBlendShapeForEyes(blendShape.blendShapeName) && blinker != null && blinker.IsBlinking) return true;
        }
        return false;
    }

    private bool IsBlendShapeForMouth(string blendShapeName)
    {
        return blendShapeName.Contains("MTH_") || blendShapeName.Contains("Mouth_") || blendShapeName.Contains("mouth") || blendShapeName.Contains("jaw");
    }

    private bool IsBlendShapeForEyes(string blendShapeName)
    {
        return blendShapeName.Contains("EYE_") || blendShapeName.Contains("Eye_") || blendShapeName.Contains("eye") || blendShapeName.Contains("brow");
    }

    public AlteraExpressionPreset GetActivePreset() => activePreset;
    public float GetCurrentIntensity() => currentIntensityOverride;

    public void SetExpression(string expressionName, float intensityOverride = 1f, float blendWeight = 1f)
    {
        if (string.IsNullOrWhiteSpace(expressionName)) expressionName = "neutral";

        AlteraExpressionPreset found = null;
        if (expressionPresets != null)
        {
            foreach (var p in expressionPresets)
            {
                if (p == null) continue;

                string presetName = string.IsNullOrWhiteSpace(p.expressionName) ? "neutral" : p.expressionName;
                if (string.Equals(presetName, expressionName, System.StringComparison.OrdinalIgnoreCase))
                {
                    found = p;
                    break;
                }
            }
        }

        if (found != null)
        {
            SetExpression(found, intensityOverride, blendWeight);
            return;
        }

        activePreset = null;
        currentIntensityOverride = intensityOverride;
        headPoseTarget = Vector3.zero;
        hasHeadPoseOverride = false;

        var zeroTargets = new Dictionary<string, float>();
        foreach (var k in blendShapeCurrent.Keys)
        {
            zeroTargets[k] = 0f;
        }
        blendShapeTargets = zeroTargets;

        TrySetBodyExpressionTrigger(expressionName);
    }

    public void SetExpression_Default()
    {
        SetExpression("neutral");
    }

    public void ReturnToNeutral(float fadeDuration = 0.5f)
    {
        var zeroTargets = new Dictionary<string, float>();
        foreach (var k in blendShapeCurrent.Keys)
        {
            zeroTargets[k] = 0f;
        }

        if (zeroTargets.Count == 0)
        {
            foreach (var kv in blendShapeBindings)
            {
                zeroTargets[kv.Key] = 0f;
            }
        }

        blendShapeTargets = zeroTargets;
        activePreset = null;
        currentIntensityOverride = 1f;

        // Keep head pose active while it eases back to zero instead of snapping.
        headPoseTarget = Vector3.zero;
        hasHeadPoseOverride = false;
        eyeGazeTarget = Vector2.zero;

        float clamp = Mathf.Max(0.01f, fadeDuration);
        headPoseTransitionSpeed = 1f / clamp * 8f;
        eyeGazeTransitionSpeed = 1f / clamp * 8f;

        TrySetBodyExpressionTrigger("neutral");
    }

    private void TrySetBodyExpressionTrigger(string expressionName)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        // Facial expressions are richer than the optional body-animation vocabulary.
        string alias = expressionName;
        if (string.Equals(expressionName, "neutral", System.StringComparison.OrdinalIgnoreCase))
            alias = "Default";
        else if (string.Equals(expressionName, "happy", System.StringComparison.OrdinalIgnoreCase))
            alias = "Joy";

        var parameters = animator.parameters;
        foreach (string candidate in new[] { expressionName, alias })
        {
            foreach (var parameter in parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger &&
                    string.Equals(parameter.name, candidate, System.StringComparison.OrdinalIgnoreCase))
                {
                    animator.SetTrigger(parameter.nameHash);
                    return;
                }
            }
        }
    }
}
