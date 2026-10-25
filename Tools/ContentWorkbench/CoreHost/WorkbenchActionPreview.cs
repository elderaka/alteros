using System.Collections.Generic;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Scenarios;

namespace AlterOS.ContentWorkbench
{
    /// <summary>Runs only fixture-safe sequence primitives through the production executor; host effects remain inspection-only.</summary>
    internal static class WorkbenchActionPreview
    {
        public static WorkbenchResult Run(WorkbenchPreviewSnapshot snapshot, TrustedFixtureContext fixture)
        {
            ActionRecord[] actions = (snapshot.Package.actions ?? new List<ActionRecord>())
                .Where(item => item != null && item.id == snapshot.RootSourceId).ToArray();
            if (actions.Length != 1)
                return WorkbenchResult.Failure("invalid", "preview_action_projection_missing", "/document/id",
                    "The detached package does not contain exactly one imported Action definition for this source.");

            ActionRecord action = actions[0];
            var details = new Dictionary<string, object>
            {
                { "id", action.id }, { "revision", action.revision }, { "expectedHash", action.semanticHash },
                { "implementationKind", action.implementationKind }, { "capabilityId", action.capabilityId }
            };
            if (action.implementationKind == "activity_sequence")
            {
                ActivitySequenceBuildResult built = ActivitySequenceReleaseAdapter.Build(action,
                    ActivitySequenceCatalogFactory.CreateDefault().CreateBuilder());
                if (!built.isValid)
                {
                    ContentDiagnostic first = built.diagnostics.FirstOrDefault();
                    return WorkbenchResult.Failure("invalid", first == null ? "preview_action_sequence_invalid" : first.code,
                        first == null ? "/document/data" : first.path,
                        first == null ? "The production Action sequence normalizer rejected this source." : first.message);
                }
                details.Add("sequence", new Dictionary<string, object>
                {
                    { "id", built.definition.id }, { "revision", built.definition.revision },
                    { "steps", built.definition.steps.Select(step => (object)new Dictionary<string, object>
                        { { "stepId", step.stepId }, { "primitiveId", step.primitiveId },
                          { "successNextStepId", step.successNextStepId }, { "failureNextStepId", step.failureNextStepId },
                          { "cancelNextStepId", step.cancelNextStepId }, { "checkpointPolicy", step.checkpointPolicy.ToString() } }).ToList() }
                });
                ScenarioRuntimeSnapshot context = new ScenarioRuntimeSnapshot("action.preview", fixture.RunId,
                    fixture.TurnId, fixture.Revision, "", new string[0], new string[0],
                    ReadCapabilities(fixture), new List<ScenarioObjectSnapshot>(), new string[0]);
                var actionReference = new ContentRef(snapshot.Package.storeId, action.id, action.revision, action.semanticHash);
                var request = new ActivitySequenceRequest(built.definition, actionReference, fixture.RunId,
                    "preview.flow", "preview.step", "preview.action", context);
                if (!WorkbenchFixtureActivitySequenceRunner.TryRun(request, snapshot.Package, snapshot.Release, fixture,
                    out ActivitySequenceView view, out string diagnostic, out string[] executed, out IList<object> trace))
                    return WorkbenchResult.Failure("unavailable", "preview_sequence_fixture_run_failed", "/fixtureId",
                        "The production sequence runner did not complete in the trusted safe-operation fixture: " + diagnostic);
                details["executionStatus"] = "synthetic_completed";
                details["sequenceStatus"] = view.status;
                details["executedPrimitives"] = executed;
                details["syntheticTrace"] = trace;
            }
            else if (action.implementationKind == "host_effect")
            {
                if (!ContentHostEffectFunctionRegistry.TryGetRequiredCapability(action.functionId, out string required) ||
                    required != action.capabilityId)
                    return WorkbenchResult.Failure("invalid", "preview_action_host_effect_unregistered", "/document/data/functionId",
                        "The selected host effect is not registered with this exact capability.");
                details.Add("functionId", action.functionId);
                details.Add("executionStatus", "not_executed_host_effect");
            }
            else
                return WorkbenchResult.Failure("invalid", "preview_action_kind_unsupported", "/document/implementationKind",
                    "Only registered activity_sequence and host_effect Actions are supported.");
            return WorkbenchResult.Ok(new Dictionary<string, object>
            {
                { "kindId", "action" }, { "fixtureId", fixture.FixtureId }, { "fixtureOnly", true },
                { "rootIsSynthetic", snapshot.RootIsSynthetic }, { "sourceSnapshot", snapshot.SourceSnapshot },
                { "ephemeralReleaseId", snapshot.Release.releaseId }, { "action", details },
                { "providerCalls", 0 }, { "ttsCalls", 0 },
            });
        }

        private static IEnumerable<ScenarioCapabilitySnapshot> ReadCapabilities(TrustedFixtureContext fixture)
        { return fixture.PreviewCapabilities; }

    }
}
