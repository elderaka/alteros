using System.Collections.Generic;
namespace AlterOS.Content
{
    public static class ContentHostEffectFunctionRegistry { public const string PlayerItemGrant = "player.item.grant"; }
    public sealed class ContentRef
    {
        public string store, id, revision, expectedHash;
        public ContentRef(string storeId, string contentId, string contentRevision, string hash = "")
        { store = storeId; id = contentId; revision = contentRevision; expectedHash = hash; }
    }
    public sealed class ContentDiagnostic { public string code; }
    public sealed class ContentResult<T>
    {
        public bool IsReady; public T value; public List<ContentDiagnostic> diagnostics = new List<ContentDiagnostic>();
    }
    public sealed class ActionRecord
    { public string implementationKind, functionId, argumentsJson, id, revision, semanticHash; }
    public sealed class ItemRecord { public string id, revision; public long stackLimit; }
    public sealed class FlowRecord { public string id, kind; public List<StepRecord> steps = new List<StepRecord>(); }
    public sealed class StepRecord { public string id, kind, dataJson; public ContentRef actionRef, dialogueRef; }
    public interface IContentReleaseReader
    {
        ContentResult<ActionRecord> ReadAction(ContentRef reference);
        ContentResult<ItemRecord> ReadItem(ContentRef reference);
        ContentResult<FlowRecord> ReadFlow(ContentRef reference);
        ContentResult<FlowRecord> ReadFlowById(string flowId);
    }
    public sealed class RuntimeContentStore : IContentReleaseReader
    {
        public ContentResult<ActionRecord> ReadAction(ContentRef reference) { return null; }
        public ContentResult<ItemRecord> ReadItem(ContentRef reference) { return null; }
        public ContentResult<FlowRecord> ReadFlow(ContentRef reference) { return null; }
        public ContentResult<FlowRecord> ReadFlowById(string flowId) { return null; }
    }
}
namespace AlterOS.Scenarios
{
    using AlterOS.Content;
    public sealed class ScenarioHostEffectRequest
    {
        public ContentRef actionRef;
        public string scenarioRunId, invocationId, stepExecutionId, requiresCompletedStepId, completedStepExecutionId;
    }
    public sealed class ScenarioHostEffectResult
    {
        public ScenarioHostEffectResult(string status, string runId, string invocationId, string stepExecutionId,
            string operationId, string receiptId, long revision, string reasonCode) { }
    }
    public static class ScenarioHostEffectStatuses
    {
        public const string Applied = "applied", Replayed = "replayed", Unavailable = "unavailable", Conflict = "conflict", Failed = "failed";
    }
    public interface IScenarioHostEffectPort { ScenarioHostEffectResult Apply(ScenarioHostEffectRequest request); }
}
