using System;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Dialogue;
using AlterOS.Scenarios;
using UnityEngine;

namespace AlterOS.Conversation
{
    public sealed partial class DirectConversationBridge
    {
        [NonSerialized] private AuthoredActivityScenarioLibrary activityScenarios;
        [NonSerialized] private AuthoredActivityScenario authoredActivityScenario;
        [NonSerialized] private ScenarioEffect pendingAuthoredActivityFollowup;
        [NonSerialized] private bool authoredActivityBarkPending;

        private AuthoredActivityScenario FindActivityScenario(string capability)
        {
            if (activityScenarios == null)
            {
                ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenShippedRelease(Application.streamingAssetsPath);
                ContentResult<RuntimeContentDocument> document = opened.IsReady
                    ? opened.value.ReadDocument("topic/activity_scenarios.json") : null;
                try
                {
                    if (document == null || !document.IsReady)
                        throw new InvalidOperationException("The pinned SQL activity Scenario library is unavailable.");
                    activityScenarios = JsonUtility.FromJson<AuthoredActivityScenarioLibrary>(document.value.json);
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[DirectConversationBridge] Activity Scenarios unavailable: " + error.Message);
                }
                if (activityScenarios == null) activityScenarios = new AuthoredActivityScenarioLibrary();
            }
            return (activityScenarios.entries ?? new AuthoredActivityScenario[0]).FirstOrDefault(
                entry => entry != null && entry.capabilityId == capability);
        }

        private void StartAuthoredActivityScenario(string capability)
        {
            AuthoredActivityScenario entry = FindActivityScenario(capability);
            if (entry == null || entry.scenario == null) { SetStatus("activity_unavailable"); return; }
            var capabilities = CaptureActivityCapabilities(out ScenarioObjectSnapshot[] objects);
            string run = "authored-activity-" + Guid.NewGuid().ToString("N");
            var runtime = new ScenarioRuntimeSnapshot(entry.scenario.scenarioId, run, run,
                ScenarioContextRevision(new string[0], AllowedExpressions(), capabilities), entry.scenario.entryStepId,
                new string[0], AllowedExpressions(), capabilities, objects, new[] { ActivityConditionIds.Terminal });
            if (ScenarioDefinitionValidator.Validate(entry.scenario, runtime).Count != 0)
            {
                SetStatus("activity_unavailable");
                Debug.LogWarning("[DirectConversationBridge] Activity Scenario rejected by current host context.");
                return;
            }
            // Close the greeting before assigning the new owner; dismissal must not reset its checkpoint.
            dialogueManager?.CloseScenarioExchange();
            authoredActivityScenario = entry;
            pendingAuthoredActivityFollowup = null;
            authoredActivityBarkPending = false;
            scenarioPresentation = CreateScenarioPresentationModel();
            scenarioPresentationRuntime = runtime;
            scenarioTranscriptResponseId = run;
            PresentScenarioContinuation(scenarioPresentation.Start(entry.scenario, run, runtime), run);
        }

        private bool TryPresentActivityContext(ActivityView activity)
        {
            AuthoredActivityScenario entry = authoredActivityScenario ?? FindActivityScenario(activity.capabilityId);
            ScenarioBeat beat = activity.capabilityId == ActivityCapabilityIds.ComeHere && entry?.returning != null
                ? entry.returning : entry?.waiting;
            if (beat == null || dialogueManager == null) return false;
            var sequence = new DialogueSequence();
            var node = new DialogueNode { id = beat.id, speaker = "altera", text = beat.text,
                translatedText = beat.displayText, language = beat.language, emotion = beat.expression };
            node.choices.Add(new DialogueChoice { label = entry.continueLabel, action = "activity_continue", value = activity.activityId });
            if (activity.canPause)
                node.choices.Add(new DialogueChoice { label = entry.stopLabel, action = "activity_stop", value = activity.activityId });
            sequence.nodes.Add(node);
            dialogueManager.StartValidatedSequence(sequence, null);
            return true;
        }

        private void SayActivityBusy(ActivityView activity)
        {
            ScenarioBeat beat = (authoredActivityScenario ?? FindActivityScenario(activity.capabilityId))?.waiting;
            if (beat != null) dialogueManager?.ambientBubble?.Say(beat.text, beat.displayText, beat.language);
        }

        public void ContinueCurrentActivity(string expectedActivityId)
        {
            if (activityExchangeActivityId != expectedActivityId) return;
            EndActivityExchange();
            dialogueManager?.CloseScenarioExchange();
        }

        private bool TryPresentAuthoredActivityFollowup()
        {
            ScenarioEffect effect = pendingAuthoredActivityFollowup;
            if (effect == null || scenarioPresentation?.Running == null ||
                effect.runId != scenarioPresentation.Running.runId || effect.stepId != scenarioPresentation.Running.currentStepId)
                return false;
            pendingAuthoredActivityFollowup = null;
            authoredActivityBarkPending = false;
            PresentScenarioStep(effect.step, effect.runId, scenarioTranscriptResponseId, effect.type == "ask_player");
            return true;
        }

        private void NotifyAuthoredActivityFollowup()
        {
            if (!authoredActivityBarkPending || authoredActivityScenario == null || pendingAuthoredActivityFollowup == null ||
                dialogueManager == null || dialogueManager.ambientBubble == null || dialogueManager.ambientBubble.IsSpatting) return;
            // Future initiative scheduling may authorize another delivery mode. This slice never opens the camera autonomously.
            string delivery = ScenarioEngagementPolicy.Resolve(authoredActivityScenario.engagement, false,
                dialogueManager.BlocksPlayerControl || IsTurnInFlight);
            if (delivery == "defer") return;
            ScenarioBeat bark = authoredActivityScenario.readyBark;
            authoredActivityBarkPending = false;
            if (bark != null) dialogueManager.ambientBubble.Say(bark.text, bark.displayText, bark.language);
        }
    }
}
