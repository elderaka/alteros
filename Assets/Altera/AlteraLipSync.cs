using UnityEngine;
using UniVRM10;
using uLipSync;

public class AlteraLipSync : MonoBehaviour
{
    [Tooltip("Drag the Vrm10Instance component here")]
    public Vrm10Instance vrmInstance;
    
    [Tooltip("Drag all uLipSync components here (e.g. one for main TTS, one for Ambient Bubble)")]
    public uLipSync.uLipSync[] lipSyncSources;

    [Header("Volume Settings")]
    public float minVolume = -2.5f;
    public float maxVolume = -1.5f;
    
    [Header("Smoothing")]
    [Range(0f, 0.3f)] public float smoothness = 0.05f;

    [Header("Blendshape Mapping")]
    [Tooltip("Map the phonemes detected by uLipSync to VRM Expression Keys")]
    public PhonemeMapping[] mappings = new PhonemeMapping[]
    {
        new PhonemeMapping() { phoneme = "A", vrmKey = ExpressionPreset.aa },
        new PhonemeMapping() { phoneme = "I", vrmKey = ExpressionPreset.ih },
        new PhonemeMapping() { phoneme = "U", vrmKey = ExpressionPreset.ou },
        new PhonemeMapping() { phoneme = "E", vrmKey = ExpressionPreset.ee },
        new PhonemeMapping() { phoneme = "O", vrmKey = ExpressionPreset.oh }
    };

    [System.Serializable]
    public class PhonemeMapping
    {
        public string phoneme;
        public ExpressionPreset vrmKey;
        [HideInInspector] public float weight;
        [HideInInspector] public float velocity;
    }

    private float _volume = 0f;
    private float _volumeVelocity = 0f;

    /// <summary>Returns true if Altera is currently outputting vocal volume.</summary>
    public bool IsTalking => _volume > 0.05f;

    private void OnEnable()
    {
        if (lipSyncSources == null) return;
        foreach (var source in lipSyncSources)
        {
            if (source != null) source.onLipSyncUpdate.AddListener(OnLipSyncUpdate);
        }
    }

    private void OnDisable()
    {
        if (lipSyncSources == null) return;
        foreach (var source in lipSyncSources)
        {
            if (source != null) source.onLipSyncUpdate.RemoveListener(OnLipSyncUpdate);
        }
    }

    private void OnLipSyncUpdate(LipSyncInfo info)
    {
        if (vrmInstance == null || vrmInstance.Runtime == null || vrmInstance.Runtime.Expression == null)
            return;

        // 1. Calculate Normalized Volume (Smoothing)
        float targetVol = 0f;
        if (info.rawVolume > 0f)
        {
            targetVol = Mathf.Log10(info.rawVolume);
            targetVol = (targetVol - minVolume) / Mathf.Max(maxVolume - minVolume, 1e-4f);
            targetVol = Mathf.Clamp(targetVol, 0f, 1f);
        }
        _volume = Mathf.SmoothDamp(_volume, targetVol, ref _volumeVelocity, smoothness);

        // 2. Calculate Vowel Weights (Smoothing)
        float sumWeight = 0f;
        foreach (var mapping in mappings)
        {
            float targetWeight = 0f;
            if (info.phonemeRatios != null)
            {
                info.phonemeRatios.TryGetValue(mapping.phoneme, out targetWeight);
            }

            mapping.weight = Mathf.SmoothDamp(mapping.weight, targetWeight, ref mapping.velocity, smoothness);
            sumWeight += mapping.weight;
        }

        // 3. Normalize and Apply to VRM Expressions
        foreach (var mapping in mappings)
        {
            float normalizedWeight = sumWeight > 0f ? mapping.weight / sumWeight : 0f;
            
            // Final mouth opening = phoneme importance * total volume
            float finalWeight = normalizedWeight * _volume;

            var key = ExpressionKey.CreateFromPreset(mapping.vrmKey);
            vrmInstance.Runtime.Expression.SetWeight(key, finalWeight);
        }
    }
}
