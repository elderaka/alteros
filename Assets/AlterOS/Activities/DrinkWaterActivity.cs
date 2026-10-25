using System;
using System.Collections.Generic;
using AlterOS.Scenarios;
using UnityEngine;

namespace AlterOS.Activities
{
    /// <summary>Single dispenser use with exact reservation, bounded timed sip checkpoints and safe cleanup.</summary>
    public sealed class DrinkWaterActivity : IActivityExecution
    {
        private readonly ActivityExecutionContext context;
        private readonly FurnitureRegistry furniture;
        private readonly IActivityMotion motion;
        private readonly IHydrationCheckpointSink hydration;
        private readonly List<ActivityEvent> events = new List<ActivityEvent>();
        private ActivityView view;
        private FurnitureBinding binding;
        private bool terminal, suspended, stopPending, released;
        private string exchangeId = "", phase = "", phaseBeforeExchange = "";
        private string requestedTarget = "";
        private int checkpointIndex;
        private double phaseEndsAt;
        private double exchangeStartedAt;

        public ActivityView View { get { return view; } }

        public DrinkWaterActivity(ActivityExecutionContext activityContext, FurnitureRegistry furnitureRegistry,
            IActivityMotion activityMotion, IHydrationCheckpointSink hydrationSink)
        {
            if (activityContext == null) throw new ArgumentNullException("activityContext");
            if (furnitureRegistry == null) throw new ArgumentNullException("furnitureRegistry");
            if (activityMotion == null) throw new ArgumentNullException("activityMotion");
            if (hydrationSink == null) throw new ArgumentNullException("hydrationSink");
            if (activityContext.settings.executorKind != ActivityExecutorKind.DrinkWater ||
                activityContext.settings.capabilityId != ActivityCapabilityIds.DrinkWater)
                throw new ArgumentException("Drink execution requires a needs.drink_water definition.", "activityContext");
            context = activityContext;
            furniture = furnitureRegistry;
            motion = activityMotion;
            hydration = hydrationSink;
        }

        public void Start(double nowSeconds)
        {
            if (view != null) throw new InvalidOperationException("Drink execution has already started.");
            if (!motion.PlayerAvailable) { Finish("player_missing", ActivityTerminalReason.Failed); return; }
            requestedTarget = ReadTarget(context.request.arguments);
            if (requestedTarget.Length == 0 || !furniture.TryReserve(ActivityCapabilityIds.DrinkWater,
                    requestedTarget, context.activityId, out binding))
            { Finish("dispenser_unavailable", ActivityTerminalReason.Failed); return; }
            if (!hydration.Register(context.activityId, context.settings.hydrationCheckpointIds,
                    context.settings.hydrationPerCheckpoint))
            { Finish("hydration_unavailable", ActivityTerminalReason.Failed); return; }
            if (!MoveTo(binding.point.Approach.position))
            { Finish("approach_unavailable", ActivityTerminalReason.Failed); return; }
            SetPhase("approaching", true, true);
        }

        public void Tick(double nowSeconds)
        {
            if (terminal || suspended) return;
            if (!motion.PlayerAvailable) { Finish("player_missing", ActivityTerminalReason.Failed); return; }
            if (!furniture.IsValid(binding)) { Finish("dispenser_invalidated", ActivityTerminalReason.Failed); return; }
            if (motion.IsBlocked) { Finish("route_blocked", ActivityTerminalReason.Failed); return; }

            if (phase == "approaching" && motion.HasArrived)
            {
                if (!MoveTo(binding.point.Pose.position)) { Finish("pose_unreachable", ActivityTerminalReason.Failed); return; }
                motion.Face(binding.point.Use.position);
                SetPhase("aligning", true, true);
            }
            else if (phase == "aligning" && motion.HasArrived)
            {
                SetPhase("preparing", true, true);
                phaseEndsAt = nowSeconds + context.settings.drinkPhaseDurationSeconds;
            }
            else if (phase == "preparing" && nowSeconds >= phaseEndsAt)
            {
                SetPhase("sipping", false, false);
                phaseEndsAt = nowSeconds + context.settings.drinkPhaseDurationSeconds;
            }
            else if (phase == "sipping" && nowSeconds >= phaseEndsAt)
            {
                string checkpoint = context.settings.hydrationCheckpointIds[checkpointIndex];
                if (!furniture.IsValid(binding) || !hydration.TryApply(context.activityId, checkpoint))
                { Finish("hydration_checkpoint_failed", ActivityTerminalReason.Failed); return; }
                events.Add(new ActivityEvent(ActivityEventKind.Checkpoint, context.activityId,
                    context.request.runId, context.request.stepId, phase, ActivityTerminalReason.None, checkpoint));
                checkpointIndex++;
                if (stopPending || checkpointIndex >= context.settings.hydrationCheckpointIds.Count)
                {
                    if (!MoveTo(binding.point.Exit.position)) { Finish("exit_unavailable", ActivityTerminalReason.Failed); return; }
                    SetPhase("exiting", true, true);
                }
                else phaseEndsAt = nowSeconds + context.settings.drinkPhaseDurationSeconds;
            }
            else if (phase == "exiting" && motion.HasArrived)
            {
                Finish(stopPending ? "player_stopped" : "completed",
                    stopPending ? ActivityTerminalReason.PlayerStopped : ActivityTerminalReason.Completed);
            }
        }

        public bool Stop(string reason, double nowSeconds)
        {
            if (terminal) return false;
            if (phase == "sipping")
            {
                stopPending = true;
                return true;
            }
            Finish(string.IsNullOrWhiteSpace(reason) ? "player_stopped" : reason, ActivityTerminalReason.PlayerStopped);
            return true;
        }

        public void BeginExchange(string id, double nowSeconds)
        {
            if (terminal || view == null || !view.canTalk || string.IsNullOrWhiteSpace(id)) return;
            if (exchangeId == id) return;
            if (exchangeId.Length != 0) return;
            exchangeId = id;
            phaseBeforeExchange = phase;
            exchangeStartedAt = nowSeconds;
            if (phase == "approaching" || phase == "aligning") motion.Suspend();
            suspended = true;
            SetPhase("dialogue_suspended", true, true);
        }

        public void EndExchange(string id, double nowSeconds)
        {
            if (terminal || !suspended || string.IsNullOrWhiteSpace(id) || exchangeId != id) return;
            suspended = false;
            exchangeId = "";
            motion.Resume();
            phaseEndsAt += Math.Max(0d, nowSeconds - exchangeStartedAt);
            bool canTalk = phaseBeforeExchange != "sipping";
            SetPhase(phaseBeforeExchange, canTalk, canTalk);
        }

        public void Shutdown(string reason, double nowSeconds)
        {
            if (!terminal) Finish("interrupted_by_quit", ActivityTerminalReason.InterruptedByQuit);
        }

        public IReadOnlyList<ActivityEvent> DrainEvents()
        {
            ActivityEvent[] result = events.ToArray();
            events.Clear();
            return Array.AsReadOnly(result);
        }

        private bool MoveTo(Vector3 destination)
        {
            return motion.TryMoveTo(destination);
        }

        private void SetPhase(string value, bool canTalk, bool canPause)
        {
            phase = value;
            view = new ActivityView(context.activityId, context.request.runId, context.request.stepId,
                context.settings.capabilityId, value, ActivityTerminalReason.None, canTalk, canPause);
            events.Add(new ActivityEvent(ActivityEventKind.PhaseChanged, context.activityId,
                context.request.runId, context.request.stepId, value, ActivityTerminalReason.None, ""));
        }

        private void Finish(string value, ActivityTerminalReason reason)
        {
            if (terminal) return;
            terminal = true;
            motion.Clear();
            if (!released)
            {
                furniture.Release(context.activityId);
                hydration.Release(context.activityId);
                released = true;
            }
            phase = value;
            view = new ActivityView(context.activityId, context.request.runId, context.request.stepId,
                context.settings.capabilityId, value, reason, true, true);
            events.Add(new ActivityEvent(ActivityEventKind.Terminal, context.activityId,
                context.request.runId, context.request.stepId, value, reason, ""));
        }

        private static string ReadTarget(IReadOnlyList<ScenarioActionArgument> arguments)
        {
            if (arguments == null) return "";
            foreach (ScenarioActionArgument argument in arguments)
                if (argument != null && argument.name == "target") return argument.value ?? "";
            return "";
        }
    }
}
