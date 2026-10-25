using UnityEngine;
using UniVRM10;

public class Blinker : MonoBehaviour
{
    [Tooltip("Assign the Vrm10Instance attached to the model.")]
    public Vrm10Instance vrmInstance;
    
    [Tooltip("Assign the AlteraEmotionController to detect when Joy is active.")]
    public AlteraEmotionController emotionController;

    [Header("Settings")]
    public float minBlinkInterval = 2f;
    public float maxBlinkInterval = 6f;
    public float blinkSpeed = 15f;

    private float timer;
    private float blinkStatus; // 0 = open, 1 = closed
    private bool isBlinking;

    /// <summary>
    /// Whether the blinker is currently performing a blink.
    /// Used by emotion system to suppress eye expressions during blink.
    /// </summary>
    public bool IsBlinking => isBlinking;

    private void Start()
    {
        if (vrmInstance == null) vrmInstance = GetComponentInChildren<Vrm10Instance>();
        if (emotionController == null) emotionController = GetComponent<AlteraEmotionController>();
        timer = Random.Range(minBlinkInterval, maxBlinkInterval);
    }

    private void Update()
    {
        if (vrmInstance == null || vrmInstance.Runtime == null) return;

        // Interference check: only Joy disables blink per user request
        var activePreset = emotionController != null ? emotionController.GetActivePreset() : null;
        if (activePreset != null && activePreset.expressionName != null && activePreset.expressionName.ToLower().Contains("happy"))
        {
            // Reset to open if we were actively blinking
            if (blinkStatus > 0)
            {
                blinkStatus = 0;
                isBlinking = false;
                vrmInstance.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.blink), 0f);
            }
            return;
        }

        timer -= Time.deltaTime;

        if (timer <= 0)
        {
            if (!isBlinking)
            {
                isBlinking = true;
                blinkStatus = 0f;
            }

            // Simple blink animation (0 -> 1 -> 0)
            blinkStatus += Time.deltaTime * blinkSpeed;
            if (blinkStatus >= 2f) 
            {
                isBlinking = false;
                blinkStatus = 0f;
                timer = Random.Range(minBlinkInterval, maxBlinkInterval);
            }
        }

        if (isBlinking)
        {
            float blinkWeight = blinkStatus > 1f ? 2f - blinkStatus : blinkStatus;
            float actualWeight = Mathf.Clamp(blinkWeight, 0f, 1f);

            vrmInstance.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.blink), actualWeight);
        }
        else
        {
            // Ensure the eyes completely open once the blink is finished
            vrmInstance.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.blink), 0f);
        }
    }
}
