using System;
using System.Collections.Generic;

namespace AlterOS.ContentWorkbench
{
    internal sealed class WorkbenchDiagnostic
    {
        public string code;
        public string severity;
        public string path;
        public string message;

        public WorkbenchDiagnostic(string diagnosticCode, string diagnosticPath, string text, string level = "error")
        {
            code = diagnosticCode ?? "invalid";
            severity = level ?? "error";
            path = diagnosticPath ?? "";
            message = text ?? "Invalid input.";
        }
    }

    internal sealed class WorkbenchResult
    {
        public string status;
        public object result;
        public List<WorkbenchDiagnostic> diagnostics = new List<WorkbenchDiagnostic>();

        public static WorkbenchResult Ok(object result)
        {
            return new WorkbenchResult { status = "ok", result = result };
        }

        public static WorkbenchResult Failure(string status, string code, string path, string message)
        {
            var response = new WorkbenchResult { status = status, result = null };
            response.diagnostics.Add(new WorkbenchDiagnostic(code, path, message));
            return response;
        }

        public static WorkbenchResult WithDiagnostics(string status, object result, IEnumerable<WorkbenchDiagnostic> diagnostics)
        {
            var response = new WorkbenchResult { status = status, result = result };
            if (diagnostics != null) response.diagnostics.AddRange(diagnostics);
            return response;
        }
    }

    internal static class WorkbenchCommands
    {
        public static object Handle(object rawRequest, WorkbenchEnvironment environment)
        {
            IDictionary<string, object> request = JsonWire.AsObject(rawRequest);
            if (request == null)
                return Invalid("", "request_invalid", "Request must be a JSON object.");

            string requestId = JsonWire.String(request, "requestId");
            int protocolVersion;
            if (!JsonWire.TryInt(request, "protocolVersion", out protocolVersion) || protocolVersion != 1)
                return Invalid(requestId, "unsupported_protocol_version", "protocolVersion must be 1.");
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
                return Invalid("", "request_id_invalid", "requestId must be 1..128 characters.");

            string operation = JsonWire.String(request, "operation");
            IDictionary<string, object> payload = JsonWire.AsObject(request.ContainsKey("payload") ? request["payload"] : null);
            if (payload == null)
                return Invalid(requestId, "payload_invalid", "payload must be a JSON object.");

            WorkbenchResult outcome;
            try
            {
                switch (operation)
                {
                    case "schema.describe":
                        outcome = WorkbenchSchemaCommands.Describe(payload);
                        break;
                    case "catalogue.read":
                        outcome = FurnitureUnavailable("catalogue.read");
                        break;
                    case "arrangement.evaluate":
                        outcome = FurnitureUnavailable("arrangement.evaluate");
                        break;
                    case "draft.list":
                        outcome = WorkbenchDraftStore.List(environment, payload);
                        break;
                    case "draft.read":
                        outcome = WorkbenchDraftStore.Read(environment, payload);
                        break;
                    case "draft.write":
                        outcome = WorkbenchDraftStore.Write(environment, payload, requestId);
                        break;
                    case "authoring.initialize":
                        outcome = WorkbenchAuthoringStore.Initialize(environment);
                        break;
                    case "authoring.info":
                        outcome = WorkbenchAuthoringStore.Info(environment);
                        break;
                    case "authoring.import":
                        outcome = WorkbenchAuthoringStore.Import(environment, payload);
                        break;
                    case "authoring.list":
                        outcome = WorkbenchAuthoringStore.List(environment, payload);
                        break;
                    case "authoring.read":
                        outcome = WorkbenchAuthoringStore.Read(environment, payload);
                        break;
                    case "references.list":
                        outcome = WorkbenchAuthoringStore.ListCompiledReferences(environment, payload);
                        break;
                    case "authoring.write":
                        outcome = WorkbenchAuthoringStore.Write(environment, payload);
                        break;
                    case "authoring.selection.read":
                        outcome = WorkbenchAuthoringStore.ReadSelection(environment, payload);
                        break;
                    case "authoring.selection.write":
                        outcome = WorkbenchAuthoringStore.WriteSelection(environment, payload);
                        break;
                    case "authoring.export":
                        outcome = WorkbenchAuthoringStore.Export(environment, payload);
                        break;
                    case "authoring.backup":
                        outcome = WorkbenchAuthoringStore.Backup(environment);
                        break;
                    case "content.validate":
                        outcome = WorkbenchPreview.Validate(environment, payload);
                        break;
                    case "preview.run":
                        outcome = WorkbenchPreview.Run(environment, payload);
                        break;
                    default:
                        outcome = WorkbenchResult.Failure("invalid", "unsupported_operation", "/operation", "Operation is not supported.");
                        break;
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Workbench operation " + operation + " failed: " + exception.GetType().Name);
                outcome = WorkbenchResult.Failure("invalid", "operation_failed", "", "The operation could not be completed safely.");
            }

            return Envelope(requestId, outcome);
        }

        public static object Invalid(string requestId, string code, string message)
        {
            return Envelope(requestId, WorkbenchResult.Failure("invalid", code, "", message));
        }

        private static WorkbenchResult FurnitureUnavailable(string operation)
        {
            return WorkbenchResult.Failure(
                "unavailable",
                "furniture_core_unavailable",
                "/operation",
                "Room F1/F2 furniture APIs are not integrated into this host build; " + operation + " did not run.");
        }

        private static object Envelope(string requestId, WorkbenchResult outcome)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "protocolVersion", 1 },
                { "requestId", requestId ?? "" },
                { "status", outcome == null ? "invalid" : outcome.status },
                { "result", outcome == null ? null : outcome.result },
                { "diagnostics", outcome == null ? new List<WorkbenchDiagnostic>() : outcome.diagnostics },
            };
        }
    }
}
