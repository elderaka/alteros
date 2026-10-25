using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AlterOS.Scenarios;

namespace AlterOS.Activities
{
    public enum ActivityExecutorKind
    {
        Movement = 0,
        DrinkWater = 1,
        ActivitySequence = 2
    }

    public enum ActivityTerminalReason
    {
        None = 0,
        Completed = 1,
        PlayerStopped = 2,
        Cancelled = 3,
        Failed = 4,
        InterruptedByQuit = 5
    }

    public enum ActivityEventKind
    {
        Accepted = 0,
        PhaseChanged = 1,
        Checkpoint = 2,
        Terminal = 3
    }

    public static class ActivityCapabilityIds
    {
        public const string ComeHere = "movement.come_here";
        public const string FollowPlayer = "movement.follow_player";
        public const string DrinkWater = "needs.drink_water";
        public const string ActivitySequence = "scenario.activity_sequence";
    }

    public static class ActivityConditionIds
    {
        public const string Terminal = "m3.activity_terminal";
    }

    /// <summary>Immutable runtime copy of Inspector-authored activity configuration.</summary>
    public sealed class ActivitySettings
    {
        public readonly string capabilityId;
        public readonly ActivityExecutorKind executorKind;
        public readonly float stopDistance;
        public readonly float followResumeDistance;
        public readonly float minimumRepathInterval;
        public readonly float stalledProgressTimeout;
        public readonly float navigationAcquisitionTimeout;
        public readonly ReadOnlyCollection<string> hydrationCheckpointIds;
        public readonly float hydrationPerCheckpoint;
        public readonly float drinkPhaseDurationSeconds;

        public ActivitySettings(string capabilityId, ActivityExecutorKind executorKind,
            float stopDistance = 1.5f, float followResumeDistance = 2.2f,
            float minimumRepathInterval = 0.3f, float stalledProgressTimeout = 10f,
            float navigationAcquisitionTimeout = 3f, IEnumerable<string> hydrationCheckpointIds = null,
            float hydrationPerCheckpoint = 10f, float drinkPhaseDurationSeconds = 0.5f)
        {
            if (string.IsNullOrWhiteSpace(capabilityId)) throw new ArgumentException("An activity capability ID is required.", "capabilityId");
            if (!Enum.IsDefined(typeof(ActivityExecutorKind), executorKind)) throw new ArgumentOutOfRangeException("executorKind");
            if (!IsPositiveFinite(stopDistance)) throw new ArgumentOutOfRangeException("stopDistance");
            if (!IsPositiveFinite(followResumeDistance) || followResumeDistance <= stopDistance)
                throw new ArgumentOutOfRangeException("followResumeDistance");
            if (!IsPositiveFinite(minimumRepathInterval)) throw new ArgumentOutOfRangeException("minimumRepathInterval");
            if (!IsPositiveFinite(stalledProgressTimeout)) throw new ArgumentOutOfRangeException("stalledProgressTimeout");
            if (!IsPositiveFinite(navigationAcquisitionTimeout)) throw new ArgumentOutOfRangeException("navigationAcquisitionTimeout");
            if (!IsPositiveFinite(hydrationPerCheckpoint) || hydrationPerCheckpoint > 100f)
                throw new ArgumentOutOfRangeException("hydrationPerCheckpoint");
            if (!IsPositiveFinite(drinkPhaseDurationSeconds)) throw new ArgumentOutOfRangeException("drinkPhaseDurationSeconds");

            string[] checkpoints = hydrationCheckpointIds == null
                ? (executorKind == ActivityExecutorKind.DrinkWater ? new[] { "sip_1", "sip_2" } : new string[0])
                : hydrationCheckpointIds.ToArray();
            if (executorKind == ActivityExecutorKind.DrinkWater && checkpoints.Length == 0)
                throw new ArgumentException("Drink water requires at least one authored hydration checkpoint.", "hydrationCheckpointIds");
            if (checkpoints.Any(string.IsNullOrWhiteSpace) || checkpoints.Distinct(StringComparer.Ordinal).Count() != checkpoints.Length)
                throw new ArgumentException("Hydration checkpoint IDs must be non-empty and unique.", "hydrationCheckpointIds");

            this.capabilityId = capabilityId.Trim();
            this.executorKind = executorKind;
            this.stopDistance = stopDistance;
            this.followResumeDistance = followResumeDistance;
            this.minimumRepathInterval = minimumRepathInterval;
            this.stalledProgressTimeout = stalledProgressTimeout;
            this.navigationAcquisitionTimeout = navigationAcquisitionTimeout;
            this.hydrationCheckpointIds = Array.AsReadOnly(checkpoints);
            this.hydrationPerCheckpoint = hydrationPerCheckpoint;
            this.drinkPhaseDurationSeconds = drinkPhaseDurationSeconds;
        }

        public ActivitySettings Copy()
        {
            return new ActivitySettings(capabilityId, executorKind, stopDistance, followResumeDistance,
                minimumRepathInterval, stalledProgressTimeout, navigationAcquisitionTimeout,
                hydrationCheckpointIds, hydrationPerCheckpoint, drinkPhaseDurationSeconds);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class ActivityExecutionContext
    {
        public readonly string activityId;
        public readonly ActivityRequest request;
        public readonly ScenarioRuntimeSnapshot scenarioContext;
        public readonly ActivitySettings settings;
        public readonly ActivitySequenceRequest sequenceRequest;

        public ActivityExecutionContext(string id, ActivityRequest activityRequest,
            ScenarioRuntimeSnapshot context, ActivitySettings activitySettings,
            ActivitySequenceRequest sequence = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An activity ID is required.", "id");
            if (activityRequest == null) throw new ArgumentNullException("activityRequest");
            if (context == null) throw new ArgumentNullException("context");
            if (activitySettings == null) throw new ArgumentNullException("activitySettings");
            activityId = id;
            request = activityRequest;
            scenarioContext = context;
            settings = activitySettings.Copy();
            sequenceRequest = sequence;
        }
    }

    public sealed class ActivityView
    {
        public readonly string activityId;
        public readonly string runId;
        public readonly string stepId;
        public readonly string capabilityId;
        public readonly string phase;
        public readonly ActivityTerminalReason terminalReason;
        public readonly bool canTalk;
        public readonly bool canPause;

        public bool isTerminal { get { return terminalReason != ActivityTerminalReason.None; } }

        public ActivityView(string id, string run, string step, string capability, string currentPhase,
            ActivityTerminalReason reason, bool talkAllowed, bool pauseAllowed)
        {
            activityId = id ?? "";
            runId = run ?? "";
            stepId = step ?? "";
            capabilityId = capability ?? "";
            phase = currentPhase ?? "";
            terminalReason = reason;
            canTalk = talkAllowed;
            canPause = pauseAllowed;
        }
    }

    public sealed class ActivityEvent
    {
        public readonly ActivityEventKind kind;
        public readonly string activityId;
        public readonly string runId;
        public readonly string stepId;
        public readonly string phase;
        public readonly string checkpointId;
        public readonly ActivityTerminalReason terminalReason;

        public ActivityEvent(ActivityEventKind eventKind, string id, string run, string step,
            string eventPhase, ActivityTerminalReason reason, string checkpoint)
        {
            kind = eventKind;
            activityId = id ?? "";
            runId = run ?? "";
            stepId = step ?? "";
            phase = eventPhase ?? "";
            terminalReason = reason;
            checkpointId = checkpoint ?? "";
        }
    }

    public interface IActivityExecution
    {
        ActivityView View { get; }
        void Start(double nowSeconds);
        void Tick(double nowSeconds);
        bool Stop(string reason, double nowSeconds);
        void BeginExchange(string exchangeId, double nowSeconds);
        void EndExchange(string exchangeId, double nowSeconds);
        void Shutdown(string reason, double nowSeconds);
        IReadOnlyList<ActivityEvent> DrainEvents();
    }

    public delegate IActivityExecution ActivityExecutionFactory(ActivityExecutionContext context);

    public interface IHydrationCheckpointSink
    {
        bool Register(string activityId, IEnumerable<string> allowedCheckpointIds, float amountPerCheckpoint);
        bool TryApply(string activityId, string checkpointId);
        void Release(string activityId);
    }
}
