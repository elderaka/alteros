using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlterOS.Activities
{
    /// <summary>Per-request policy for a fixed Come here destination or a live Follow target.</summary>
    public sealed class MovementActivity : IActivityExecution
    {
        private readonly ActivityExecutionContext context;
        private readonly IActivityMotion motion;
        private readonly bool followPlayer;
        private readonly List<ActivityEvent> events = new List<ActivityEvent>();
        private ActivityView view;
        private Vector3 destination;
        private bool hasDestination, suspended, isMoving, terminal, motionNeedsClear = true;
        private string exchangeId = "";
        private double startedAt, lastRepathAt = double.NegativeInfinity, lastProgressAt;
        private double routeAcquisitionStartedAt = -1d;
        private float lastDistance;
        private int recoveryAttempts;

        public ActivityView View { get { return view; } }

        public MovementActivity(ActivityExecutionContext activityContext, IActivityMotion activityMotion, bool follow)
        {
            if (activityContext == null) throw new ArgumentNullException("activityContext");
            if (activityMotion == null) throw new ArgumentNullException("activityMotion");
            if (activityContext.settings.executorKind != ActivityExecutorKind.Movement)
                throw new ArgumentException("Movement execution requires a movement definition.", "activityContext");
            context = activityContext;
            motion = activityMotion;
            followPlayer = follow;
        }

        public void Start(double nowSeconds)
        {
            if (view != null) throw new InvalidOperationException("Movement execution has already started.");
            startedAt = lastProgressAt = nowSeconds;
            if (!motion.PlayerAvailable) { Finish("player_missing", ActivityTerminalReason.Failed); return; }
            if (!followPlayer)
            {
                if (!motion.TryResolveNearPlayer(context.settings.stopDistance, out destination))
                { Finish("destination_unavailable", ActivityTerminalReason.Failed); return; }
                hasDestination = true;
            }
            lastRepathAt = nowSeconds;
            RefreshView(followPlayer ? "following" : "approaching", true, true);
            UpdateMovement(nowSeconds, true);
            if (motion.HasArrived && !followPlayer) Complete();
        }

        public void Tick(double nowSeconds)
        {
            if (terminal) return;
            if (!motion.PlayerAvailable) { Finish("player_missing", ActivityTerminalReason.Failed); return; }
            if (suspended) return;
            if (motion.IsBlocked)
            {
                if (!TryRecover(nowSeconds)) Finish("route_blocked", ActivityTerminalReason.Failed);
                return;
            }
            if (followPlayer && !isMoving && routeAcquisitionStartedAt >= 0d &&
                motion.DistanceToPlayer >= context.settings.followResumeDistance &&
                nowSeconds - routeAcquisitionStartedAt >= context.settings.navigationAcquisitionTimeout)
            { Finish("navigation_unavailable", ActivityTerminalReason.Failed); return; }
            if (followPlayer)
            {
                if (motion.DistanceToPlayer <= context.settings.stopDistance)
                {
                    if (isMoving) ClearMotion();
                    isMoving = false;
                    routeAcquisitionStartedAt = -1d;
                    RefreshView("waiting_near_player", true, true);
                    return;
                }
                if (motion.DistanceToPlayer >= context.settings.followResumeDistance &&
                    nowSeconds - lastRepathAt >= context.settings.minimumRepathInterval)
                {
                    if (!ResolveAndMove(nowSeconds))
                    {
                        isMoving = false;
                        lastRepathAt = nowSeconds;
                        if (routeAcquisitionStartedAt < 0d) routeAcquisitionStartedAt = nowSeconds;
                        if (nowSeconds - routeAcquisitionStartedAt >= context.settings.navigationAcquisitionTimeout)
                            Finish("navigation_unavailable", ActivityTerminalReason.Failed);
                        return;
                    }
                }
            }
            else if (motion.HasArrived)
            {
                Complete();
                return;
            }

            if (isMoving)
            {
                float remainingDistance = motion.RemainingDistance;
                if (!float.IsNaN(remainingDistance) && !float.IsInfinity(remainingDistance) &&
                    remainingDistance + 0.05f < lastDistance)
                { lastDistance = remainingDistance; lastProgressAt = nowSeconds; }
                if (nowSeconds - lastProgressAt >= context.settings.stalledProgressTimeout)
                { Finish("movement_stalled", ActivityTerminalReason.Failed); return; }
            }
            else if (!followPlayer && nowSeconds - startedAt >= context.settings.navigationAcquisitionTimeout)
            { Finish("navigation_unavailable", ActivityTerminalReason.Failed); }
        }

        public bool Stop(string reason, double nowSeconds)
        {
            if (terminal) return false;
            Finish(string.IsNullOrWhiteSpace(reason) ? "player_stopped" : reason, ActivityTerminalReason.PlayerStopped);
            return true;
        }

        public void BeginExchange(string id, double nowSeconds)
        {
            if (terminal || string.IsNullOrWhiteSpace(id)) return;
            if (exchangeId == id) return;
            if (exchangeId.Length != 0) return;
            exchangeId = id;
            if (isMoving) motion.Suspend();
            isMoving = false;
            suspended = true;
            motion.FacePlayer();
            RefreshView("dialogue_suspended", true, true);
        }

        public void EndExchange(string id, double nowSeconds)
        {
            if (terminal || !suspended || string.IsNullOrWhiteSpace(id) || exchangeId != id) return;
            suspended = false;
            exchangeId = "";
            motion.Resume();
            lastProgressAt = nowSeconds;
            lastDistance = motion.RemainingDistance;
            if (followPlayer)
            {
                routeAcquisitionStartedAt = -1d;
                RefreshView("following", true, true);
            }
            else
            {
                // Come here retains its accepted destination across a dialogue exchange.
                if (hasDestination && !motion.TryMoveTo(destination))
                { Finish("resume_route_unavailable", ActivityTerminalReason.Failed); return; }
                isMoving = hasDestination;
                motionNeedsClear = hasDestination;
                RefreshView("approaching", true, true);
            }
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

        private bool ResolveAndMove(double nowSeconds)
        {
            Vector3 point;
            if (!motion.TryResolveNearPlayer(context.settings.stopDistance, out point)) return false;
            if (!followPlayer && hasDestination) point = destination;
            else { destination = point; hasDestination = true; }
            if (!motion.TryMoveTo(point)) return false;
            motionNeedsClear = true;
            lastRepathAt = nowSeconds;
            lastDistance = motion.RemainingDistance;
            lastProgressAt = nowSeconds;
            routeAcquisitionStartedAt = -1d;
            isMoving = true;
            RefreshView(followPlayer ? "following" : "approaching", true, true);
            return true;
        }

        private void UpdateMovement(double nowSeconds, bool initial)
        {
            if (followPlayer)
            {
                if (motion.DistanceToPlayer >= context.settings.followResumeDistance)
                {
                    if (!ResolveAndMove(nowSeconds)) routeAcquisitionStartedAt = nowSeconds;
                }
                else
                {
                    routeAcquisitionStartedAt = -1d;
                    RefreshView("waiting_near_player", true, true);
                }
            }
            else if (hasDestination)
            {
                if (motion.DistanceToPlayer <= context.settings.stopDistance)
                { Complete(); return; }
                if (motion.TryMoveTo(destination))
                {
                    motionNeedsClear = true;
                    isMoving = true;
                    lastDistance = motion.RemainingDistance;
                    lastProgressAt = nowSeconds;
                    lastRepathAt = nowSeconds;
                }
                else if (!initial) Finish("route_unavailable", ActivityTerminalReason.Failed);
            }
        }

        private bool TryRecover(double nowSeconds)
        {
            if (recoveryAttempts >= 1 || suspended) return false;
            recoveryAttempts++;
            ClearMotion();
            bool success = followPlayer ? ResolveAndMove(nowSeconds) : (hasDestination && motion.TryMoveTo(destination));
            if (success)
            {
                isMoving = true;
                lastProgressAt = nowSeconds;
                lastDistance = motion.RemainingDistance;
            }
            return success;
        }

        private void Complete()
        {
            ClearMotion();
            motion.FacePlayer();
            Finish("completed", ActivityTerminalReason.Completed);
        }

        private void Finish(string phase, ActivityTerminalReason reason)
        {
            if (terminal) return;
            terminal = true;
            ClearMotion();
            view = new ActivityView(context.activityId, context.request.runId, context.request.stepId,
                context.settings.capabilityId, phase, reason, true, true);
            events.Add(new ActivityEvent(ActivityEventKind.Terminal, context.activityId,
                context.request.runId, context.request.stepId, phase, reason, ""));
        }

        private void RefreshView(string phase, bool canTalk, bool canPause)
        {
            if (terminal) return;
            bool changed = view == null || view.phase != phase || view.canTalk != canTalk || view.canPause != canPause;
            view = new ActivityView(context.activityId, context.request.runId, context.request.stepId,
                context.settings.capabilityId, phase, ActivityTerminalReason.None, canTalk, canPause);
            if (changed)
                events.Add(new ActivityEvent(ActivityEventKind.PhaseChanged, context.activityId,
                    context.request.runId, context.request.stepId, phase, ActivityTerminalReason.None, ""));
        }

        private void ClearMotion()
        {
            if (!motionNeedsClear) return;
            motion.Clear();
            motionNeedsClear = false;
        }
    }
}
