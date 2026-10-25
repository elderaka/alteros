using System;
using System.Linq;

namespace AlterOS.Furniture
{
    /// <summary>Combines catalogue opt-in, live scene resolution and the current Activity-owned claim proof.</summary>
    public sealed class FurnitureFunctionTargetValidator : IFurnitureFunctionTargetValidator
    {
        private readonly FurnitureCatalogueSnapshot catalogue;
        private readonly IFurnitureFunctionSceneTargetResolver sceneTargetResolver;
        private readonly IFurnitureFunctionReservationContextSource reservationContextSource;

        public FurnitureFunctionTargetValidator(FurnitureCatalogueSnapshot snapshot,
            IFurnitureFunctionSceneTargetResolver sceneResolver,
            IFurnitureFunctionReservationContextSource reservationSource)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (sceneResolver == null) throw new ArgumentNullException("sceneResolver");
            if (reservationSource == null) throw new ArgumentNullException("reservationSource");
            catalogue = snapshot;
            sceneTargetResolver = sceneResolver;
            reservationContextSource = reservationSource;
        }

        public FurnitureFunctionTargetValidation Validate(FurnitureFunctionRequest request)
        {
            if (request == null) return Result(FurnitureFunctionTargetStatuses.Unavailable, "invalid_furniture_function_request");
            FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item != null && item.definitionId == request.definitionId);
            if (definition == null) return Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_definition_unavailable");

            FurnitureFunctionBindingDescriptor[] bindings = (definition.functionBindings ?? new System.Collections.Generic.List<FurnitureFunctionBindingDescriptor>())
                .Where(item => item != null && item.functionId == request.functionId && item.useId == request.useId).ToArray();
            if (bindings.Length == 0) return Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_function_not_opted_in");

            FurnitureFunctionBindingDescriptor binding = null;
            FurnitureFunctionTargetValidation sceneFailure = null;
            foreach (FurnitureFunctionBindingDescriptor candidate in bindings)
            {
                if (!HasCompatiblePoint(definition, candidate.pointId, candidate.useId))
                {
                    sceneFailure = Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_function_binding_invalid");
                    continue;
                }
                FurnitureFunctionReservationRequest candidateQuery = new FurnitureFunctionReservationRequest(request.activityId,
                    request.instanceId, request.definitionId, request.usePointId, candidate.useId, request.usePointRevision);
                FurnitureFunctionTargetValidation candidateResult = ValidateSceneTarget(candidateQuery,
                    candidate.pointId, sceneTargetResolver);
                if (candidateResult.status == FurnitureFunctionTargetStatuses.Available)
                {
                    binding = candidate;
                    break;
                }
                if (sceneFailure == null || candidateResult.status == FurnitureFunctionTargetStatuses.Conflict)
                    sceneFailure = candidateResult;
            }
            if (binding == null)
                return sceneFailure ?? Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_scene_target_unavailable");

            var query = new FurnitureFunctionReservationRequest(request.activityId, request.instanceId,
                request.definitionId, request.usePointId, binding.useId, request.usePointRevision);

            FurnitureFunctionReservationEvidence evidence;
            try { evidence = reservationContextSource.ResolveCurrent(query); }
            catch (Exception) { return Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_reservation_context_unavailable"); }
            if (evidence == null) return Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_reservation_unavailable");
            if (!Matches(evidence, query)) return Result(FurnitureFunctionTargetStatuses.Conflict, "furniture_reservation_evidence_mismatch");
            return Result(FurnitureFunctionTargetStatuses.Available, string.Empty);
        }

        internal static bool HasCompatiblePoint(FurnitureDefinition definition, string pointId, string useId)
        {
            if (definition == null || definition.points == null) return false;
            FurnitureFunctionalPoint point = definition.points.FirstOrDefault(item => item != null && item.pointId == pointId);
            return point != null && point.useIds != null && point.useIds.Contains(useId, StringComparer.Ordinal);
        }

        internal static FurnitureFunctionTargetValidation ValidateSceneTarget(
            FurnitureFunctionReservationRequest query, string cataloguePointId,
            IFurnitureFunctionSceneTargetResolver sceneTargetResolver)
        {
            if (sceneTargetResolver == null) return Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_scene_target_unavailable");
            FurnitureFunctionTargetValidation result;
            try { result = sceneTargetResolver.ValidateCurrent(query, cataloguePointId); }
            catch (Exception) { return Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_scene_target_unavailable"); }
            if (result == null) return Result(FurnitureFunctionTargetStatuses.Unavailable, "furniture_scene_target_unavailable");
            if (result.status == FurnitureFunctionTargetStatuses.Available) return Result(FurnitureFunctionTargetStatuses.Available, string.Empty);
            if (result.status == FurnitureFunctionTargetStatuses.Conflict)
                return Result(FurnitureFunctionTargetStatuses.Conflict, SafeCode(result.diagnosticCode, "furniture_binding_conflict"));
            if (result.status == FurnitureFunctionTargetStatuses.Unavailable)
                return Result(FurnitureFunctionTargetStatuses.Unavailable, SafeCode(result.diagnosticCode, "furniture_scene_target_unavailable"));
            return Result(FurnitureFunctionTargetStatuses.Unavailable, "invalid_furniture_scene_validation");
        }

        internal static bool Matches(FurnitureFunctionReservationEvidence evidence,
            FurnitureFunctionReservationRequest request)
        {
            return evidence != null && request != null &&
                evidence.activityId == request.activityId && evidence.instanceId == request.instanceId &&
                evidence.usePointId == request.usePointId && evidence.useId == request.useId &&
                evidence.usePointRevision == request.usePointRevision;
        }

        internal static FurnitureFunctionTargetValidation Result(string status, string diagnosticCode)
        { return new FurnitureFunctionTargetValidation { status = status, diagnosticCode = diagnosticCode ?? string.Empty }; }

        internal static string SafeCode(string value, string fallback)
        { return string.IsNullOrWhiteSpace(value) ? fallback : value; }
    }

    /// <summary>Applies the same opt-in and exact binding checks to transient presentation operations.</summary>
    public sealed class FurniturePresentationTargetValidator : IFurniturePresentationContextValidator
    {
        private readonly FurnitureCatalogueSnapshot catalogue;
        private readonly IFurnitureFunctionSceneTargetResolver sceneTargetResolver;
        private readonly IFurnitureFunctionReservationContextSource reservationContextSource;

        public FurniturePresentationTargetValidator(FurnitureCatalogueSnapshot snapshot,
            IFurnitureFunctionSceneTargetResolver sceneResolver,
            IFurnitureFunctionReservationContextSource reservationSource)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (sceneResolver == null) throw new ArgumentNullException("sceneResolver");
            if (reservationSource == null) throw new ArgumentNullException("reservationSource");
            catalogue = snapshot;
            sceneTargetResolver = sceneResolver;
            reservationContextSource = reservationSource;
        }

        public FurniturePresentationValidationResult Validate(FurniturePresentationHandle handle,
            FurniturePresentationValidationPurpose purpose)
        {
            if (!IsValid(handle) || !Enum.IsDefined(typeof(FurniturePresentationValidationPurpose), purpose))
                return Denied("invalid_presentation_request");
            FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item != null && item.definitionId == handle.definitionId);
            if (definition == null) return Denied("furniture_definition_unavailable");

            FurniturePresentationBindingDescriptor[] bindings = (definition.presentationBindings ?? new System.Collections.Generic.List<FurniturePresentationBindingDescriptor>())
                .Where(item => item != null && item.descriptorId == handle.presentationDescriptorId).ToArray();
            if (bindings.Length == 0) return Denied("presentation_descriptor_not_opted_in");

            FurnitureFunctionReservationRequest query = null;
            FurnitureFunctionTargetValidation sceneFailure = null;
            foreach (FurniturePresentationBindingDescriptor candidate in bindings)
            {
                if (!FurnitureFunctionTargetValidator.HasCompatiblePoint(definition, candidate.pointId, candidate.useId))
                {
                    sceneFailure = FurnitureFunctionTargetValidator.Result(FurnitureFunctionTargetStatuses.Unavailable,
                        "presentation_binding_invalid");
                    continue;
                }
                var candidateQuery = new FurnitureFunctionReservationRequest(handle.activityId, handle.instanceId,
                    handle.definitionId, handle.usePointId, candidate.useId, handle.usePointRevision);
                FurnitureFunctionTargetValidation candidateResult = FurnitureFunctionTargetValidator.ValidateSceneTarget(
                    candidateQuery, candidate.pointId, sceneTargetResolver);
                if (candidateResult.status == FurnitureFunctionTargetStatuses.Available)
                {
                    query = candidateQuery;
                    break;
                }
                if (sceneFailure == null || candidateResult.status == FurnitureFunctionTargetStatuses.Conflict)
                    sceneFailure = candidateResult;
            }
            if (query == null)
                return Denied(FurnitureFunctionTargetValidator.SafeCode(
                    sceneFailure == null ? string.Empty : sceneFailure.diagnosticCode,
                    "furniture_scene_target_unavailable"));

            FurnitureFunctionReservationEvidence evidence;
            try { evidence = reservationContextSource.ResolveCurrent(query); }
            catch (Exception) { return Denied("furniture_reservation_context_unavailable"); }
            if (evidence == null) return Denied("furniture_reservation_unavailable");
            if (!FurnitureFunctionTargetValidator.Matches(evidence, query))
                return Denied("furniture_reservation_evidence_mismatch");
            return new FurniturePresentationValidationResult { allowed = true, diagnosticCode = string.Empty };
        }

        private static bool IsValid(FurniturePresentationHandle handle)
        {
            return handle != null && !string.IsNullOrWhiteSpace(handle.activityId) &&
                !string.IsNullOrWhiteSpace(handle.instanceId) && !string.IsNullOrWhiteSpace(handle.definitionId) &&
                !string.IsNullOrWhiteSpace(handle.usePointId) && handle.usePointRevision >= 0;
        }

        private static FurniturePresentationValidationResult Denied(string diagnosticCode)
        { return new FurniturePresentationValidationResult { allowed = false, diagnosticCode = diagnosticCode ?? "furniture_binding_unavailable" }; }
    }
}
