using UnityEngine;

namespace Pleiades.Player
{
    public interface IPlayerInteractable
    {
        string PromptText { get; }
        Vector3 GetPromptWorldPosition();
        bool CanInteract(PlayerController player);
        void Interact(PlayerController player);
    }

    /// <summary>Optional click-time binding for interactables whose target may change while focused.</summary>
    public interface IRevisionBoundPlayerInteractable
    {
        string CaptureInteractionRevision(PlayerController player);
        bool TryInteract(PlayerController player, string expectedRevision);
    }
}
