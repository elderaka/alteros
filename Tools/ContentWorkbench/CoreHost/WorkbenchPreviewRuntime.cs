using System.Collections.Generic;
using AlterOS.Content;

namespace AlterOS.ContentWorkbench
{
    internal static class WorkbenchPreviewRuntime
    {
        public static WorkbenchResult Run(WorkbenchEnvironment environment, IDictionary<string, object> payload,
            string kindId, TrustedFixtureContext fixture)
        {
            WorkbenchPreviewSnapshot snapshot = new WorkbenchPreviewSnapshot();
            WorkbenchResult prepared = WorkbenchPreviewSourceBuilder.Prepare(environment, payload, kindId,
                DefaultSourceKind(kindId), snapshot);
            if (prepared.status != "ok") return prepared;

            switch (kindId)
            {
                case "dialogue": return WorkbenchDialoguePreview.Run(snapshot, payload, fixture.FixtureId);
                case "item": return WorkbenchItemPreview.Run(snapshot, fixture.FixtureId);
                case "furniture": return WorkbenchFurniturePreview.Run(snapshot, fixture);
                case "action": return WorkbenchActionPreview.Run(snapshot, fixture);
                case "topic": return WorkbenchTopicPreview.Run(snapshot, payload, fixture);
                case "scenario": return WorkbenchScenarioPreview.Run(snapshot, payload, fixture);
                default:
                    return WorkbenchResult.Failure("unavailable", "preview_adapter_unavailable", "/kindId",
                        "This preview kind has no complete detached runtime adapter yet.");
            }
        }

        private static string DefaultSourceKind(string kindId)
        {
            switch (kindId)
            {
                case "dialogue": return ContentSourceKinds.DialogueDefinition;
                case "scenario": return ContentSourceKinds.ScenarioDefinition;
                case "topic": return ContentSourceKinds.TopicDefinition;
                case "furniture": return ContentSourceKinds.FurnitureDefinition;
                case "item": return ContentSourceKinds.ItemDefinition;
                case "action": return ContentSourceKinds.ActionDefinition;
                default: return string.Empty;
            }
        }
    }
}
