using System;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Furniture;
using AlterOS.Scenarios;
using Pleiades.Player;
using UnityEngine;

namespace Pleiades.Furniture
{
    /// <summary>Explicit Player E target. The only currently executable furniture option asks Alterra to act.</summary>
    public sealed class FurnitureInteractable : MonoBehaviour, IPlayerInteractable, IRevisionBoundPlayerInteractable
    {
        [SerializeField] private FurnitureSceneAdapter adapter;
        [SerializeField] private FurnitureUsePoint usePoint;
        [SerializeField] private Transform promptAnchor;
        [SerializeField] private string definitionId = "";
        [SerializeField] private string pointId = "";

        private FurnitureInteractionBinding lastBinding;
        private string promptText = string.Empty;

        public string PromptText { get { return promptText; } }

        private void Awake()
        {
            if (adapter == null) adapter = GetComponentInParent<FurnitureSceneAdapter>();
            if (usePoint == null) usePoint = GetComponent<FurnitureUsePoint>();
            if (usePoint == null) usePoint = GetComponentInParent<FurnitureUsePoint>();
        }

        public Vector3 GetPromptWorldPosition()
        { return promptAnchor == null ? transform.position : promptAnchor.position; }

        public bool CanInteract(PlayerController player)
        {
            if (player == null || adapter == null || usePoint == null) return false;
            FurnitureInteractionOption option = adapter.ResolveOptions(usePoint, definitionId, pointId)
                .FirstOrDefault(item => item.capabilityId == ActivityCapabilityIds.DrinkWater && item.state == "available");
            if (option == null) return false;
            var binding = FurnitureInteractionPolicy.TryCreateBinding(option, "altera", "prompt-check");
            promptText = FurnitureInteractionPolicy.PromptFor(binding);
            return !string.IsNullOrEmpty(promptText);
        }

        public string CaptureInteractionRevision(PlayerController player)
        {
            if (player == null || adapter == null || usePoint == null) return string.Empty;
            if (lastBinding != null && adapter.IsCurrent(usePoint, lastBinding, "altera"))
                return lastBinding.bindingToken;

            lastBinding = null;
            FurnitureInteractionOption option = adapter.ResolveOptions(usePoint, definitionId, pointId)
                .FirstOrDefault(item => item.capabilityId == ActivityCapabilityIds.DrinkWater && item.state == "available");
            if (option == null) return string.Empty;
            option.requestId = Guid.NewGuid().ToString("N");
            lastBinding = FurnitureInteractionPolicy.TryCreateBinding(option, "altera");
            promptText = FurnitureInteractionPolicy.PromptFor(lastBinding);
            return lastBinding == null ? string.Empty : lastBinding.bindingToken;
        }

        public bool TryInteract(PlayerController player, string expectedRevision)
        {
            if (player == null || string.IsNullOrWhiteSpace(expectedRevision) || lastBinding == null ||
                lastBinding.bindingToken != expectedRevision || adapter == null || usePoint == null) return false;
            if (!adapter.IsCurrent(usePoint, lastBinding, "altera"))
            { lastBinding = null; return false; }

            ActivityRequestResult result = adapter.TryExecute(usePoint, lastBinding);
            lastBinding = null;
            return result.status == "accepted";
        }

        public void Interact(PlayerController player)
        {
            if (player == null || lastBinding == null) return;
            TryInteract(player, lastBinding.bindingToken);
        }
    }
}
