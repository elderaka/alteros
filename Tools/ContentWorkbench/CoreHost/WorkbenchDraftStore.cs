using System.Collections.Generic;

namespace AlterOS.ContentWorkbench
{
    /// <summary>Compatibility command facade; draft revisions live only in AuthoringRepository.</summary>
    internal static class WorkbenchDraftStore
    {
        public static WorkbenchResult List(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        { return WorkbenchAuthoringStore.DraftList(environment, payload); }

        public static WorkbenchResult Read(WorkbenchEnvironment environment, IDictionary<string, object> payload)
        { return WorkbenchAuthoringStore.DraftRead(environment, payload); }

        public static WorkbenchResult Write(WorkbenchEnvironment environment, IDictionary<string, object> payload, string requestId)
        { return WorkbenchAuthoringStore.DraftWrite(environment, payload, requestId); }
    }
}
