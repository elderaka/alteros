using System;
using System.Collections.Generic;
using AlterOS.Scenarios;

namespace AlterOS.Activities
{
    /// <summary>Adapts one sequence runner invocation to ActivityHost's existing lifecycle.</summary>
    public sealed class ActivitySequenceExecution : IActivityExecution
    {
        private readonly ActivityExecutionContext context;
        private readonly ActivitySequenceRequest request;
        private readonly IActivitySequenceRunner runner;
        private readonly IActivitySequenceResourceOwner resources;
        private readonly Queue<ActivityEvent> events = new Queue<ActivityEvent>();
        private ActivityView view;
        private string phase = string.Empty;
        private bool runnerStarted;
        private bool resourcesReleased;

        public ActivityView View { get { return view; } }

        public ActivitySequenceExecution(ActivityExecutionContext executionContext, IActivitySequenceRunner sequenceRunner,
            IActivitySequenceResourceOwner resourceOwner = null)
        {
            context = executionContext ?? throw new ArgumentNullException("executionContext");
            request = context.sequenceRequest ?? throw new ArgumentException("A sequence invocation is required.", "executionContext");
            runner = sequenceRunner ?? throw new ArgumentNullException("sequenceRunner");
            resources = resourceOwner ?? runner as IActivitySequenceResourceOwner;
        }

        public void Start(double nowSeconds)
        {
            phase = "accepted";
            view = CreateView(phase, ActivityTerminalReason.None);
        }

        public void Tick(double nowSeconds)
        {
            if (!runnerStarted)
            {
                StartRunner();
                return;
            }
            Refresh(runner.Tick(nowSeconds));
        }

        private void StartRunner()
        {
            if (runnerStarted || view == null || view.isTerminal) return;
            runnerStarted = true;
            ActivitySequenceStartResult started = runner.Start(request);
            if (started == null || started.status != ActivitySequenceStatuses.Accepted ||
                started.scenarioRunId != request.scenarioRunId || started.flowInvocationId != request.flowInvocationId ||
                started.stepExecutionId != request.stepExecutionId || started.actionInvocationId != request.actionInvocationId)
            {
                view = CreateView("sequence_start_rejected", ActivityTerminalReason.Failed);
                return;
            }
            Refresh(new ActivitySequenceView(ActivitySequenceStatuses.Accepted, request.scenarioRunId,
                request.flowInvocationId, request.stepExecutionId, request.actionInvocationId, "", ""));
        }

        public bool Stop(string reason, double nowSeconds)
        {
            if (!runnerStarted)
            {
                Finalize(ActivityTerminalReason.Cancelled, reason ?? "cancelled");
                return true;
            }
            ActivitySequenceView result = runner.Cancel(new ActivitySequenceCancellation(request.scenarioRunId,
                request.flowInvocationId, request.stepExecutionId, request.actionInvocationId, reason));
            Refresh(result);
            return result != null && (result.status == ActivitySequenceStatuses.Cancelled ||
                result.status == ActivitySequenceStatuses.Completed || result.status == ActivitySequenceStatuses.Running);
        }

        public void BeginExchange(string exchangeId, double nowSeconds) { }
        public void EndExchange(string exchangeId, double nowSeconds) { }

        public void Shutdown(string reason, double nowSeconds)
        {
            if (!runnerStarted)
            {
                Finalize(ActivityTerminalReason.InterruptedByQuit, reason ?? "interrupted_by_quit");
                return;
            }
            ActivitySequenceView result = runner.Shutdown(reason ?? "interrupted_by_quit");
            if (result == null || result.status == ActivitySequenceStatuses.Failed ||
                result.status == ActivitySequenceStatuses.Cancelled || result.status == ActivitySequenceStatuses.Running)
                Finalize(ActivityTerminalReason.InterruptedByQuit, reason ?? "interrupted_by_quit");
            else Refresh(result);
        }

        public ActivitySequenceView CompletePrimitive(ActivityPrimitiveCompletion completion)
        {
            if (!runnerStarted)
                return new ActivitySequenceView(ActivitySequenceStatuses.Accepted, request.scenarioRunId,
                    request.flowInvocationId, request.stepExecutionId, request.actionInvocationId, "", "sequence_start_pending");
            ActivitySequenceView result = runner.CompletePrimitive(completion);
            Refresh(result);
            return result;
        }

        public IReadOnlyList<ActivityEvent> DrainEvents()
        {
            ActivityEvent[] result = events.ToArray();
            events.Clear();
            return Array.AsReadOnly(result);
        }

        private void Refresh(ActivitySequenceView sequence)
        {
            if (sequence == null || sequence.scenarioRunId != request.scenarioRunId ||
                sequence.flowInvocationId != request.flowInvocationId || sequence.stepExecutionId != request.stepExecutionId ||
                sequence.actionInvocationId != request.actionInvocationId || (view != null && view.isTerminal)) return;

            if (sequence.status == ActivitySequenceStatuses.Completed)
            { Finalize(ActivityTerminalReason.Completed, sequence.reasonCode); return; }
            if (sequence.status == ActivitySequenceStatuses.Cancelled)
            { Finalize(ActivityTerminalReason.Cancelled, sequence.reasonCode); return; }
            if (sequence.status == ActivitySequenceStatuses.Failed)
            { Finalize(ActivityTerminalReason.Failed, sequence.reasonCode); return; }

            string nextPhase = sequence.status == ActivitySequenceStatuses.Accepted ? "accepted" :
                string.IsNullOrWhiteSpace(sequence.sequenceStepId) ? "running" : sequence.sequenceStepId;
            view = CreateView(nextPhase, ActivityTerminalReason.None);
            if (phase != nextPhase)
            {
                phase = nextPhase;
                if (nextPhase != "accepted")
                    events.Enqueue(new ActivityEvent(ActivityEventKind.PhaseChanged, context.activityId,
                        request.scenarioRunId, request.scenarioStepId, nextPhase, ActivityTerminalReason.None, ""));
            }
        }

        private void Finalize(ActivityTerminalReason reason, string terminalPhase)
        {
            if (view != null && view.isTerminal) return;
            string finalPhase = string.IsNullOrWhiteSpace(terminalPhase) ? "terminal" : terminalPhase;
            view = CreateView(finalPhase, reason);
            if (!resourcesReleased)
            {
                resourcesReleased = true;
                try { resources?.ReleaseSequence(request.actionInvocationId); }
                catch (Exception) { }
            }
            events.Enqueue(new ActivityEvent(ActivityEventKind.Terminal, context.activityId,
                request.scenarioRunId, request.scenarioStepId, finalPhase, reason, ""));
        }

        private ActivityView CreateView(string currentPhase, ActivityTerminalReason terminalReason)
        { return new ActivityView(context.activityId, request.scenarioRunId, request.scenarioStepId,
            ActivityCapabilityIds.ActivitySequence, currentPhase, terminalReason, true, true); }
    }
}
