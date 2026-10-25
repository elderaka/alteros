using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Furniture
{
    public sealed class FurnitureInteractionOption
    {
        public string state;
        public string reasonCode;
        public string capabilityId;
        public string definitionId;
        public string instanceId;
        public string pointId;
        public string usePointId;
        public string pointRevision;
        public string catalogueHash;
        public string arrangementId;
        public string revisionToken;
        public string requestId;
        public string safeReasonKey;
    }

    public sealed class FurnitureInteractionBinding
    {
        public string actorId;
        public string capabilityId;
        public string definitionId;
        public string instanceId;
        public string pointId;
        public string usePointId;
        public string pointRevision;
        public string catalogueHash;
        public string arrangementId;
        public string revisionToken;
        public string requestId;
        public string bindingToken;
    }

    public sealed class FurnitureChoicePresentation
    {
        public bool visible;
        public bool interactable;
        public string label;
        public string reasonKey;
        public string reasonText;

        public static FurnitureChoicePresentation Create(FurnitureChoiceEvaluation evaluation, string label, string localizedSafeReason)
        {
            if (evaluation == null || evaluation.state == "hidden")
                return new FurnitureChoicePresentation { visible = false, interactable = false, label = string.Empty, reasonKey = null, reasonText = null };
            if (evaluation.state == "available")
                return new FurnitureChoicePresentation { visible = true, interactable = true, label = label ?? string.Empty, reasonKey = null, reasonText = null };
            if (evaluation.state == "locked")
                return new FurnitureChoicePresentation
                {
                    visible = true, interactable = false, label = label ?? string.Empty,
                    reasonKey = evaluation.reasonKey, reasonText = localizedSafeReason ?? "Availability cannot be confirmed yet."
                };
            return new FurnitureChoicePresentation
            {
                visible = true, interactable = false, label = label ?? string.Empty,
                reasonKey = "choice.availability_unconfirmed", reasonText = localizedSafeReason ?? "Availability cannot be confirmed yet."
            };
        }
    }

    public static class FurnitureInteractionPolicy
    {
        public static string PromptFor(FurnitureInteractionBinding binding)
        {
            return binding != null && binding.actorId == "altera" && binding.capabilityId == "needs.drink_water"
                ? "Ask Alterra to get water" : string.Empty;
        }

        public static bool TryResolvePoint(FurnitureCatalogueSnapshot catalogue, string legacyInstanceId, string legacyUsePointId,
            string explicitDefinitionId, string explicitPointId, out string definitionId, out string pointId)
        {
            definitionId = string.Empty;
            pointId = string.Empty;
            if (catalogue == null) return false;

            if (!string.IsNullOrWhiteSpace(explicitDefinitionId) && !string.IsNullOrWhiteSpace(explicitPointId))
            {
                definitionId = explicitDefinitionId;
                pointId = explicitPointId;
                return ContainsPoint(catalogue, definitionId, pointId);
            }

            var instanceAlias = catalogue.legacyAliases.FirstOrDefault(alias => alias.legacyId == legacyInstanceId && string.IsNullOrEmpty(alias.pointId));
            var pointAlias = catalogue.legacyAliases.FirstOrDefault(alias => alias.legacyId == legacyUsePointId && !string.IsNullOrEmpty(alias.pointId));
            if (instanceAlias == null || pointAlias == null || instanceAlias.definitionId != pointAlias.definitionId) return false;
            definitionId = pointAlias.definitionId;
            pointId = pointAlias.pointId;
            return ContainsPoint(catalogue, definitionId, pointId);
        }

        public static FurnitureInteractionBinding TryCreateBinding(FurnitureInteractionOption option, string actorId, string requestId = null)
        {
            if (option == null || option.state != "available" || string.IsNullOrWhiteSpace(actorId) ||
                string.IsNullOrWhiteSpace(option.capabilityId) || string.IsNullOrWhiteSpace(option.instanceId) ||
                string.IsNullOrWhiteSpace(option.pointId) || string.IsNullOrWhiteSpace(option.pointRevision) ||
                string.IsNullOrWhiteSpace(option.catalogueHash) || string.IsNullOrWhiteSpace(requestId ?? option.requestId)) return null;
            var binding = new FurnitureInteractionBinding
            {
                actorId = actorId,
                capabilityId = option.capabilityId,
                definitionId = option.definitionId,
                instanceId = option.instanceId,
                pointId = option.pointId,
                usePointId = option.usePointId,
                pointRevision = option.pointRevision,
                catalogueHash = option.catalogueHash,
                arrangementId = option.arrangementId,
                revisionToken = option.revisionToken,
                requestId = requestId ?? option.requestId
            };
            binding.bindingToken = CreateToken(binding);
            return binding;
        }

        public static bool IsCurrent(FurnitureInteractionBinding binding, FurnitureInteractionOption currentOption, string actorId)
        {
            var current = TryCreateBinding(currentOption, actorId, binding == null ? null : binding.requestId);
            return binding != null && current != null && binding.bindingToken == current.bindingToken;
        }

        public static bool CanExecute(FurnitureInteractionOption option, IEnumerable<string> installedCapabilities, FurnitureChoiceEvaluation choice)
        {
            if (option == null || option.state != "available" || installedCapabilities == null ||
                !installedCapabilities.Contains(option.capabilityId, StringComparer.Ordinal)) return false;
            if (choice == null) return true;
            if (choice.state != "available") return false;
            if (!string.IsNullOrEmpty(option.arrangementId) && option.arrangementId != choice.boundArrangementId) return false;
            if (!string.IsNullOrEmpty(option.revisionToken) && option.revisionToken != choice.boundRevisionToken) return false;
            return true;
        }

        private static bool ContainsPoint(FurnitureCatalogueSnapshot catalogue, string definitionId, string pointId)
        {
            var definition = catalogue.definitions.FirstOrDefault(item => item.definitionId == definitionId);
            return definition != null && definition.points != null && definition.points.Any(point => point != null && point.pointId == pointId);
        }

        private static string CreateToken(FurnitureInteractionBinding binding)
        {
            return string.Join("\u001f", new[]
            {
                binding.actorId ?? string.Empty,
                binding.capabilityId ?? string.Empty,
                binding.definitionId ?? string.Empty,
                binding.instanceId ?? string.Empty,
                binding.pointId ?? string.Empty,
                binding.usePointId ?? string.Empty,
                binding.pointRevision ?? string.Empty,
                binding.catalogueHash ?? string.Empty,
                binding.arrangementId ?? string.Empty,
                binding.revisionToken ?? string.Empty,
                binding.requestId ?? string.Empty
            });
        }
    }
}
