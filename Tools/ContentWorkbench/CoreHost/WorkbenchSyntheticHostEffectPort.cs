using System.Collections.Generic;
using System.Linq;
using System.Text;
using AlterOS.Content;
using AlterOS.Scenarios;

namespace AlterOS.ContentWorkbench
{
    /// <summary>Detached host-effect port for explicit Workbench fixtures; never touches durable player state.</summary>
    internal sealed class WorkbenchSyntheticHostEffectPort : IScenarioHostEffectPort
    {
        private readonly ContentPackage package;
        private readonly TrustedFixtureContext fixture;
        private readonly Dictionary<string, string> operations = new Dictionary<string, string>(System.StringComparer.Ordinal);
        public readonly List<object> Changes = new List<object>();

        public WorkbenchSyntheticHostEffectPort(ContentPackage selectedPackage, TrustedFixtureContext trustedFixture)
        { package = selectedPackage; fixture = trustedFixture; }

        public ScenarioHostEffectResult Apply(ScenarioHostEffectRequest request)
        {
            if (request == null) return null;
            ActionRecord action = ContentFlowAdapter.FindAction(package, request.actionRef);
            if (action == null || action.implementationKind != "host_effect" ||
                action.functionId != ContentHostEffectFunctionRegistry.PlayerItemGrant)
                return Result(request, ScenarioHostEffectStatuses.Unavailable, "preview_host_effect_not_supported");
            IDictionary<string, object> arguments;
            try { arguments = JsonWire.AsObject(JsonWire.Deserialize(Encoding.UTF8.GetBytes(action.argumentsJson ?? "{}"))); }
            catch { return Result(request, ScenarioHostEffectStatuses.Failed, "preview_host_effect_arguments_invalid"); }
            IDictionary<string, object> rawRef = JsonWire.AsObject(arguments != null && arguments.ContainsKey("itemRef") ? arguments["itemRef"] : null);
            if (rawRef == null || !JsonWire.TryLong(arguments, "quantity", out long quantity) || quantity <= 0)
                return Result(request, ScenarioHostEffectStatuses.Failed, "preview_host_effect_arguments_invalid");
            var itemRef = new ContentRef(JsonWire.String(rawRef, "store"), JsonWire.String(rawRef, "id"),
                JsonWire.String(rawRef, "revision"), JsonWire.String(rawRef, "expectedHash"));
            ItemRecord item = (package.items ?? new List<ItemRecord>()).FirstOrDefault(candidate => candidate != null &&
                itemRef.store == package.storeId && candidate.id == itemRef.id && candidate.revision == itemRef.revision &&
                (string.IsNullOrEmpty(itemRef.expectedHash) || candidate.semanticHash == itemRef.expectedHash) &&
                (package.releases ?? new List<ContentRelease>()).Any(release => release != null && release.dependencies != null &&
                    release.dependencies.Any(dependency => dependency != null && dependency.kind == "item" &&
                        dependency.store == package.storeId && dependency.id == candidate.id &&
                        dependency.revision == candidate.revision && dependency.expectedHash == candidate.semanticHash)));
            if (item == null || !fixture.PreviewItemQuantities.TryGetValue(item.id, out long current))
                return Result(request, ScenarioHostEffectStatuses.Unavailable, "preview_fixture_item_missing");
            string key = request.scenarioRunId + "\0" + request.invocationId + "\0" + request.stepExecutionId;
            string fingerprint = action.id + "\0" + action.revision + "\0" + item.id + "\0" + quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (operations.TryGetValue(key, out string prior))
                return prior == fingerprint
                    ? Result(request, ScenarioHostEffectStatuses.Replayed, "preview-receipt-" + request.stepExecutionId, "synthetic-replay")
                    : Result(request, ScenarioHostEffectStatuses.Conflict, "preview_host_effect_identity_conflict");
            if (current > item.stackLimit - quantity)
                return Result(request, ScenarioHostEffectStatuses.Unavailable, "preview_fixture_stack_limit");

            fixture.PreviewItemQuantities[item.id] = current + quantity;
            fixture.PreviewGameRevision++;
            operations.Add(key, fingerprint);
            string operationId = "preview-operation-" + request.stepExecutionId;
            string receiptId = "preview-receipt-" + request.stepExecutionId;
            Changes.Add(new Dictionary<string, object>
            {
                { "functionId", action.functionId }, { "itemId", item.id },
                { "quantity", quantity }, { "resultingQuantity", current + quantity },
                { "operationId", operationId }, { "receiptId", receiptId },
                { "syntheticOnly", true }, { "durable", false }
            });
            return new ScenarioHostEffectResult(ScenarioHostEffectStatuses.Applied, request.scenarioRunId,
                request.invocationId, request.stepExecutionId, operationId, receiptId,
                fixture.PreviewGameRevision, "synthetic_fixture_grant");
        }

        private static ScenarioHostEffectResult Result(ScenarioHostEffectRequest request, string status, string reason)
        { return new ScenarioHostEffectResult(status, request.scenarioRunId, request.invocationId,
            request.stepExecutionId, string.Empty, string.Empty, 0L, reason); }

        private static ScenarioHostEffectResult Result(ScenarioHostEffectRequest request, string status,
            string receipt, string reason)
        { return new ScenarioHostEffectResult(status, request.scenarioRunId, request.invocationId,
            request.stepExecutionId, "preview-replayed-" + request.stepExecutionId, receipt, 0L, reason); }
    }
}
