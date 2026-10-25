using System;
using UnityEngine;

namespace AlterOS.Activities
{
    /// <summary>Shared authored configuration only. Per-attempt state belongs to ActivityHost.</summary>
    [CreateAssetMenu(fileName = "ActivityDefinition", menuName = "AlterOS/Activities/Activity Definition")]
    public sealed class ActivityDefinition : ScriptableObject
    {
        [SerializeField] private string capabilityId = "";
        [SerializeField] private ActivityExecutorKind executorKind = ActivityExecutorKind.Movement;
        [SerializeField] private float stopDistance = 1.5f;
        [SerializeField] private float followResumeDistance = 2.2f;
        [SerializeField] private float minimumRepathInterval = 0.3f;
        [SerializeField] private float stalledProgressTimeout = 10f;
        [SerializeField] private float navigationAcquisitionTimeout = 3f;
        [SerializeField] private string[] hydrationCheckpointIds = new string[0];
        [SerializeField] private float hydrationPerCheckpoint = 10f;
        [SerializeField] private float drinkPhaseDurationSeconds = 0.5f;

        public bool TryCreateSettings(out ActivitySettings settings, out string error)
        {
            settings = null;
            error = "invalid_activity_definition";
            try
            {
                settings = new ActivitySettings(capabilityId, executorKind, stopDistance,
                    followResumeDistance, minimumRepathInterval, stalledProgressTimeout,
                    navigationAcquisitionTimeout,
                    executorKind == ActivityExecutorKind.DrinkWater ? hydrationCheckpointIds : null,
                    hydrationPerCheckpoint, drinkPhaseDurationSeconds);
                error = "";
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
