using System.Collections.Generic;
using AlterOS.Activities;
using AlterOS.Scenarios;
using NUnit.Framework;
using UnityEngine;

namespace AlterOS.Tests.Activities
{
    public sealed class MovementActivityTests
    {
        [Test]
        public void ComeHereKeepsAcceptedDestinationWhenPlayerMoves()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 4f };
            MovementActivity activity = Create(motion, false, "movement.come_here");
            activity.Start(0);
            Vector3 acceptedPoint = motion.LastDestination;
            motion.PlayerPosition = new Vector3(100, 0, 100);
            activity.Tick(1);
            Assert.AreEqual(1, motion.ResolveCalls);
            Assert.AreEqual(acceptedPoint.x, motion.LastDestination.x);
        }

        [Test]
        public void AlreadyNearCompletesWithoutMove()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 1f, HasArrived = false };
            MovementActivity activity = Create(motion, false, "movement.come_here");
            activity.Start(0);
            Assert.AreEqual(0, motion.MoveCalls);
            Assert.AreEqual(ActivityTerminalReason.Completed, activity.View.terminalReason);
        }

        [Test]
        public void FollowUsesStopAndResumeHysteresisAndNeverCompletesOnCatchup()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 3f };
            MovementActivity activity = Create(motion, true, "movement.follow_player");
            activity.Start(0);
            Assert.AreEqual(1, motion.MoveCalls);
            motion.DistanceToPlayer = 1.4f;
            activity.Tick(0.1);
            Assert.AreEqual("waiting_near_player", activity.View.phase);
            Assert.IsFalse(activity.View.isTerminal);
            activity.Tick(0.2);
            Assert.AreEqual(1, CountPhase(activity.DrainEvents(), "waiting_near_player"));
            motion.DistanceToPlayer = 2.3f;
            activity.Tick(0.4);
            Assert.AreEqual(2, motion.MoveCalls);
            Assert.IsFalse(activity.View.isTerminal);
        }

        [Test]
        public void FollowNeverRepatsFasterThanConfiguredInterval()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 3f };
            MovementActivity activity = Create(motion, true, "movement.follow_player");
            activity.Start(0);
            activity.Tick(0.1);
            activity.Tick(0.29);
            Assert.AreEqual(1, motion.MoveCalls);
            activity.Tick(0.31);
            Assert.AreEqual(2, motion.MoveCalls);
        }

        [Test]
        public void LateExchangeCloseCannotResumeStoppedFollow()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 3f };
            MovementActivity activity = Create(motion, true, "movement.follow_player");
            activity.Start(0);
            activity.BeginExchange("talk-1", 0.1);
            Assert.IsTrue(activity.Stop("player_stopped", 0.2));
            int resumeCount = motion.ResumeCalls;
            activity.EndExchange("talk-1", 0.3);
            Assert.AreEqual(resumeCount, motion.ResumeCalls);
            Assert.AreEqual(ActivityTerminalReason.PlayerStopped, activity.View.terminalReason);
        }

        [Test]
        public void ComeHereResumesItsOriginalDestinationAfterExchange()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 4f };
            MovementActivity activity = Create(motion, false, "movement.come_here");
            activity.Start(0);
            Vector3 initial = motion.LastDestination;
            activity.BeginExchange("talk-1", 0.1);
            activity.EndExchange("talk-1", 0.2);
            Assert.AreEqual(initial.x, motion.LastDestination.x);
            Assert.AreEqual(initial.y, motion.LastDestination.y);
            Assert.AreEqual(initial.z, motion.LastDestination.z);
            Assert.AreEqual("approaching", activity.View.phase);
        }

        [Test]
        public void RepeatedExchangeOpenDoesNotSuspendTwice()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 3f };
            MovementActivity activity = Create(motion, true, "movement.follow_player");
            activity.Start(0);
            activity.BeginExchange("talk-1", 0.1);
            activity.BeginExchange("talk-1", 0.2);
            Assert.AreEqual(1, motion.SuspendCalls);
        }

        [Test]
        public void BlockedRouteGetsOneBoundedRecoveryThenFails()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 4f, MoveSucceeds = false };
            MovementActivity activity = Create(motion, false, "movement.come_here");
            activity.Start(0);
            motion.IsBlocked = true;
            activity.Tick(0.1);
            Assert.AreEqual(2, motion.MoveCalls); // initial route plus one recovery attempt
            Assert.AreEqual(ActivityTerminalReason.Failed, activity.View.terminalReason);
            Assert.AreEqual(1, motion.ClearCalls);
        }

        [Test]
        public void MissingPlayerFailsWithoutStartingMovement()
        {
            FakeMotion motion = new FakeMotion { PlayerIsAvailable = false };
            MovementActivity activity = Create(motion, false, "movement.come_here");
            activity.Start(0);
            Assert.AreEqual(ActivityTerminalReason.Failed, activity.View.terminalReason);
            Assert.AreEqual(0, motion.MoveCalls);
        }

        [Test]
        public void MissingFollowRouteFailsAfterConfiguredAcquisitionWindow()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 3f, MoveSucceeds = false };
            MovementActivity activity = Create(motion, true, "movement.follow_player");
            activity.Start(0);
            activity.Tick(2.9);
            Assert.IsFalse(activity.View.isTerminal);
            activity.Tick(3.1);
            Assert.AreEqual(ActivityTerminalReason.Failed, activity.View.terminalReason);
        }

        [Test]
        public void FollowWaitingNearPlayerDoesNotExpire()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 1f };
            MovementActivity activity = Create(motion, true, "movement.follow_player");
            activity.Start(0);
            activity.Tick(100);
            Assert.IsFalse(activity.View.isTerminal);
            Assert.AreEqual("waiting_near_player", activity.View.phase);
        }

        [Test]
        public void FollowResetsMissingRouteAcquisitionWindowWhenExchangeCloses()
        {
            FakeMotion motion = new FakeMotion { DistanceToPlayer = 3f, MoveSucceeds = false };
            MovementActivity activity = Create(motion, true, "movement.follow_player");
            activity.Start(0);
            activity.BeginExchange("talk-1", 0.1);
            activity.EndExchange("talk-1", 2.9);
            activity.Tick(3.1);
            activity.Tick(5.9);
            Assert.IsFalse(activity.View.isTerminal);
            activity.Tick(6.2);
            Assert.AreEqual(ActivityTerminalReason.Failed, activity.View.terminalReason);
        }

        private static MovementActivity Create(FakeMotion motion, bool follow, string capability)
        {
            ActivitySettings settings = new ActivitySettings(capability, ActivityExecutorKind.Movement);
            ActivityRequest request = new ActivityRequest("run", "step", capability,
                new[] { new ScenarioActionArgument { name = "target", value = "player" } });
            ScenarioRuntimeSnapshot snapshot = new ScenarioRuntimeSnapshot("test", "run", "turn", 1, "step", null, null,
                new[] { new ScenarioCapabilitySnapshot(capability,
                    new[] { new ScenarioArgumentBinding("target", new[] { "player" }) }) }, null, null);
            return new MovementActivity(new ActivityExecutionContext("activity", request, snapshot, settings), motion, follow);
        }

        private static int CountPhase(IReadOnlyList<ActivityEvent> events, string phase)
        {
            int count = 0;
            foreach (ActivityEvent item in events)
                if (item.kind == ActivityEventKind.PhaseChanged && item.phase == phase) count++;
            return count;
        }

        private sealed class FakeMotion : IActivityMotion
        {
            public int ResolveCalls, MoveCalls, ResumeCalls, SuspendCalls;
            public float DistanceToPlayer;
            public bool HasArrived, IsBlocked;
            public float RemainingDistance = 5f;
            public bool MoveSucceeds = true, PlayerIsAvailable = true;
            public int ClearCalls;
            public Vector3 LastDestination, PlayerPosition;
            public bool PlayerAvailable { get { return PlayerIsAvailable; } }
            bool IActivityMotion.HasArrived { get { return HasArrived; } }
            bool IActivityMotion.IsBlocked { get { return IsBlocked; } }
            float IActivityMotion.DistanceToPlayer { get { return DistanceToPlayer; } }
            float IActivityMotion.RemainingDistance { get { return RemainingDistance; } }
            public bool TryResolveNearPlayer(float separation, out Vector3 destination)
            { ResolveCalls++; destination = new Vector3(PlayerPosition.x + separation, PlayerPosition.y, PlayerPosition.z); return true; }
            public bool TryMoveTo(Vector3 destination) { MoveCalls++; LastDestination = destination; return MoveSucceeds; }
            public void Suspend() { SuspendCalls++; }
            public void Resume() { ResumeCalls++; }
            public void Clear() { ClearCalls++; }
            public void FacePlayer() { }
            public void Face(Vector3 worldPosition) { }
        }
    }
}
