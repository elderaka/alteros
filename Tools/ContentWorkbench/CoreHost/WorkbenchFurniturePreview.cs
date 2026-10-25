using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;
using AlterOS.Furniture;

namespace AlterOS.ContentWorkbench
{
    internal static class WorkbenchFurniturePreview
    {
        public static WorkbenchResult Run(WorkbenchPreviewSnapshot snapshot, TrustedFixtureContext fixture)
        {
            if (!fixture.HasPreviewState)
                return WorkbenchResult.Failure("unavailable", "preview_furniture_state_unavailable", "/fixtureId",
                    "This trusted fixture has no detached Furniture instance snapshot.");
            FurnitureCatalogueSnapshot catalogue;
            string catalogueError;
            if (!TryCreateCatalogue(snapshot.Package, out catalogue, out catalogueError))
                return WorkbenchResult.Failure("invalid", "preview_furniture_catalogue_invalid", "/document/definition", catalogueError);

            FurnitureDefinition[] definitions = catalogue.definitions.Where(item => item != null && item.definitionId == snapshot.RootSourceId).ToArray();
            if (definitions.Length != 1)
                return WorkbenchResult.Failure("invalid", "preview_furniture_definition_missing", "/document/id",
                    "The exact Furniture definition is absent or ambiguous in the validated catalogue snapshot.");

            FurnitureDefinition definition = definitions[0];
            var targetValidator = new FurnitureFunctionTargetValidator(catalogue,
                new FixtureSceneResolver(fixture), new FixtureReservationSource(fixture));
            var targets = new List<object>();
            foreach (WorkbenchPreviewFurnitureInstance instance in fixture.PreviewFurniture.Where(item => item.definitionId == definition.definitionId))
            {
                var functions = new List<object>();
                foreach (FurnitureFunctionBindingDescriptor binding in definition.functionBindings ?? new List<FurnitureFunctionBindingDescriptor>())
                {
                    FurnitureFunctionRequest request = TargetRequest(fixture, instance, binding);
                    FurnitureFunctionTargetValidation target = targetValidator.Validate(request);
                    functions.Add(new Dictionary<string, object>
                    {
                        { "functionId", binding.functionId }, { "pointId", binding.pointId }, { "useId", binding.useId },
                        { "status", target == null ? FurnitureFunctionTargetStatuses.Unavailable : target.status },
                        { "reason", target == null ? "furniture_target_validation_missing" : target.diagnosticCode }
                    });
                }
                targets.Add(new Dictionary<string, object>
                {
                    { "instanceId", instance.instanceId }, { "definitionId", instance.definitionId },
                    { "usePointId", instance.usePointId }, { "useId", instance.useId },
                    { "usePointRevision", instance.usePointRevision }, { "instanceRevision", instance.instanceRevision },
                    { "currentState", instance.powerKnown ? instance.powerState : "unknown" },
                    { "functions", functions }
                });
            }

            return WorkbenchResult.Ok(new Dictionary<string, object>
            {
                { "kindId", "furniture" }, { "fixtureId", fixture.FixtureId }, { "fixtureOnly", true },
                { "rootIsSynthetic", snapshot.RootIsSynthetic }, { "sourceSnapshot", snapshot.SourceSnapshot },
                { "ephemeralReleaseId", snapshot.Release.releaseId },
                { "catalogue", new Dictionary<string, object>
                    { { "catalogueVersion", catalogue.catalogueVersion }, { "catalogueHash", catalogue.catalogueHash },
                      { "definitionCount", catalogue.definitions.Count } } },
                { "definition", new Dictionary<string, object>
                    { { "id", definition.definitionId }, { "category", definition.category },
                      { "revision", snapshot.Package.furnitureDefinitions.First(item => item != null && item.id == definition.definitionId).revision },
                      { "functionBindings", (definition.functionBindings ?? new List<FurnitureFunctionBindingDescriptor>()).Select(binding => (object)new Dictionary<string, object>
                        { { "functionId", binding.functionId }, { "pointId", binding.pointId }, { "useId", binding.useId } }).ToList() } } },
                { "targets", targets }, { "functionResults", new List<object>() },
                { "providerCalls", 0 }, { "ttsCalls", 0 },
            });
        }

        internal static bool TryCreateCatalogue(ContentPackage package, out FurnitureCatalogueSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = "";
            FurnitureCatalogueDocument settings = package == null ? null : package.furnitureCatalogueSettings;
            if (settings == null)
            { error = "The selected source snapshot does not contain its Furniture catalogue settings document."; return false; }
            var catalogue = new FurnitureCatalogueDocument
            {
                schemaVersion = settings.schemaVersion, catalogueVersion = settings.catalogueVersion,
                lengthUnit = settings.lengthUnit, angleUnit = settings.angleUnit,
                coordinateConvention = settings.coordinateConvention,
                definitions = new List<FurnitureDefinition>(),
                relationshipRules = settings.relationshipRules ?? new List<FurnitureRelationshipRule>(),
                capabilities = settings.capabilities ?? new List<FurnitureCapabilityDescriptor>(),
                legacyAliases = settings.legacyAliases ?? new List<FurnitureLegacyAlias>()
            };
            try
            {
                foreach (FurnitureDefinitionRecord item in package.furnitureDefinitions ?? new List<FurnitureDefinitionRecord>())
                    if (item != null)
                        catalogue.definitions.Add((FurnitureDefinition)JsonWire.ConvertTo(JsonWire.Deserialize(item.definitionJson), typeof(FurnitureDefinition)));
            }
            catch (Exception exception)
            { error = "A pinned Furniture definition could not be projected: " + exception.GetType().Name; return false; }

            snapshot = FurnitureCatalogueRegistry.CreateSnapshot(catalogue);
            FurnitureDiagnostic issue = snapshot.diagnostics.FirstOrDefault(item => item.severity == "error");
            if (issue == null) return true;
            error = issue.code + " at " + issue.path + ": " + issue.message;
            return false;
        }

        private static FurnitureFunctionRequest TargetRequest(TrustedFixtureContext fixture,
            WorkbenchPreviewFurnitureInstance instance, FurnitureFunctionBindingDescriptor binding)
        {
            string owner = instance.reservationOwnerId ?? "";
            return new FurnitureFunctionRequest
            {
                functionId = binding.functionId, instanceId = instance.instanceId, definitionId = instance.definitionId,
                usePointId = instance.usePointId, useId = binding.useId, usePointRevision = instance.usePointRevision,
                reservationOwnerId = owner, expectedInstanceRevision = instance.instanceRevision,
                expectedLayoutRevision = fixture.PreviewLayoutRevision, expectedGameRevision = fixture.PreviewGameRevision,
                runId = fixture.RunId, invocationId = "preview:" + fixture.FixtureId,
                stepExecutionId = "preview:function-check", activityId = owner,
                sequenceStepId = "preview:function-check", operationId = "preview:function-check:" + instance.instanceId,
                arguments = null
            };
        }

        internal sealed class FixtureSceneResolver : IFurnitureFunctionSceneTargetResolver
        {
            private readonly TrustedFixtureContext fixture;
            public FixtureSceneResolver(TrustedFixtureContext trustedFixture) { fixture = trustedFixture; }

            public FurnitureFunctionTargetValidation ValidateCurrent(FurnitureFunctionReservationRequest request, string cataloguePointId)
            {
                WorkbenchPreviewFurnitureInstance instance = fixture.PreviewFurniture.FirstOrDefault(item => item.instanceId == request.instanceId);
                if (instance == null || !instance.available)
                    return FurnitureFunctionTargetValidator.Result(FurnitureFunctionTargetStatuses.Unavailable, "fixture_target_unavailable");
                if (instance.occupied || instance.definitionId != request.definitionId)
                    return FurnitureFunctionTargetValidator.Result(FurnitureFunctionTargetStatuses.Conflict, "fixture_target_conflict");
                if (instance.usePointId != cataloguePointId || instance.usePointId != request.usePointId ||
                    instance.useId != request.useId || instance.usePointRevision != request.usePointRevision)
                    return FurnitureFunctionTargetValidator.Result(FurnitureFunctionTargetStatuses.Conflict, "fixture_binding_mismatch");
                return FurnitureFunctionTargetValidator.Result(FurnitureFunctionTargetStatuses.Available, "");
            }
        }

        internal sealed class FixtureReservationSource : IFurnitureFunctionReservationContextSource
        {
            private readonly TrustedFixtureContext fixture;
            public FixtureReservationSource(TrustedFixtureContext trustedFixture) { fixture = trustedFixture; }

            public FurnitureFunctionReservationEvidence ResolveCurrent(FurnitureFunctionReservationRequest request)
            {
                WorkbenchPreviewFurnitureInstance instance = fixture.PreviewFurniture.FirstOrDefault(item => item.instanceId == request.instanceId);
                if (instance == null || string.IsNullOrWhiteSpace(instance.reservationOwnerId) ||
                    instance.reservationOwnerId != request.activityId || instance.usePointId != request.usePointId ||
                    instance.useId != request.useId || instance.usePointRevision != request.usePointRevision)
                    return null;
                return new FurnitureFunctionReservationEvidence(request.activityId, instance.instanceId,
                    instance.usePointId, instance.useId, instance.usePointRevision);
            }
        }
    }
}
