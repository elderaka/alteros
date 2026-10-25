using AlterOS.Furniture;
using System.Collections.Generic;
using Pleiades.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Pleiades.Furniture
{
    /// <summary>One Player-map listener translates contextual furniture actions through the portable input policy.</summary>
    public sealed class FurnitureInputController : MonoBehaviour
    {
        [SerializeField] private FurnitureHandlingController handling;
        [SerializeField] private PlayerLookInteraction worldInteraction;
        [SerializeField] private PlayerController player;

        private PlayerInput playerInput;
        private InputActionMap playerMap;
        private readonly Queue<FurnitureInputControl> pendingControls = new Queue<FurnitureInputControl>();

        private void Awake()
        {
            if (playerInput == null) playerInput = GetComponent<PlayerInput>();
            if (player == null) player = GetComponent<PlayerController>();
            if (worldInteraction == null) worldInteraction = GetComponent<PlayerLookInteraction>();
        }

        private void OnEnable()
        {
            if (playerInput == null) playerInput = GetComponent<PlayerInput>();
            playerMap = playerInput == null || playerInput.actions == null ? null : playerInput.actions.FindActionMap("Player", false);
            if (playerMap != null) playerMap.actionTriggered += OnActionTriggered;
        }

        private void OnDisable()
        {
            if (playerMap != null) playerMap.actionTriggered -= OnActionTriggered;
            playerMap = null;
            pendingControls.Clear();
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            if (context.phase != InputActionPhase.Performed || context.action == null) return;
            string action = context.action.name;
            if (action == "DebugMode")
            {
                if (handling != null) handling.ToggleDebugMode();
                return;
            }
            if (handling == null || !handling.IsReady) return;

            FurnitureInputControl control;
            if (action == "FurnitureRotateScroll")
            {
                float amount = context.ReadValue<Vector2>().y;
                if (Mathf.Abs(amount) < 0.001f) return;
                control = amount > 0f ? FurnitureInputControl.ScrollUp : FurnitureInputControl.ScrollDown;
            }
            else if (!TryMapControl(action, out control)) return;

            // Keep input owned by an open modal from leaking through when it closes this frame.
            if (handling.IsInventoryOpen || (player != null && player.InteractionLocked) || IsTextEntryActive()) return;
            pendingControls.Enqueue(control);
        }

        private void LateUpdate()
        {
            // EventSystem.Update has now processed this frame's pointer. Never query it in an InputAction callback.
            while (pendingControls.Count > 0)
            {
                FurnitureInputControl control = pendingControls.Dequeue();
                if (handling != null && handling.IsReady) ProcessControl(control);
            }
        }

        private void ProcessControl(FurnitureInputControl control)
        {

            var inputContext = new FurnitureInputContext
            {
                holdingFurniture = handling.IsHoldingFurniture,
                modalOwnsInput = handling.IsInventoryOpen || (player != null && player.InteractionLocked),
                textEntryActive = IsTextEntryActive(),
                pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()
            };
            FurnitureInputIntent intent = FurnitureInputPolicy.Resolve(control, inputContext);
            switch (intent)
            {
                case FurnitureInputIntent.PickUpPlacedFurniture:
                    handling.PickUpTargetedFurniture();
                    break;
                case FurnitureInputIntent.OpenInventory:
                    handling.OpenInventory();
                    break;
                case FurnitureInputIntent.ConfirmPlacement:
                    handling.ConfirmPlacement();
                    break;
                case FurnitureInputIntent.ReturnHeldItem:
                    handling.ReturnHeldFurniture();
                    break;
                case FurnitureInputIntent.RotateClockwise:
                    handling.RotatePreview(true);
                    break;
                case FurnitureInputIntent.RotateCounterClockwise:
                    handling.RotatePreview(false);
                    break;
                case FurnitureInputIntent.ToggleGridSnap:
                    handling.ToggleGridSnap();
                    break;
                case FurnitureInputIntent.CancelHandling:
                    handling.CancelFurnitureHandling();
                    break;
            }
        }

        private static bool TryMapControl(string action, out FurnitureInputControl control)
        {
            switch (action)
            {
                case "FurniturePrimary": control = FurnitureInputControl.Q; return true;
                case "FurnitureInventory": control = FurnitureInputControl.E; return true;
                case "FurnitureConfirmMouse": control = FurnitureInputControl.LeftMouseButton; return true;
                case "FurnitureReturnMouse": control = FurnitureInputControl.RightMouseButton; return true;
                case "FurnitureRotateClockwise": control = FurnitureInputControl.Z; return true;
                case "FurnitureRotateCounterClockwise": control = FurnitureInputControl.X; return true;
                case "FurnitureSnap": control = FurnitureInputControl.C; return true;
                case "FurnitureCancel": control = FurnitureInputControl.F; return true;
                default: control = default(FurnitureInputControl); return false;
            }
        }

        private static bool IsTextEntryActive()
        {
            GameObject selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            return selected != null && (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null);
        }
    }
}
