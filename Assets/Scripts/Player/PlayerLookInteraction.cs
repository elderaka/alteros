using UnityEngine;

namespace Pleiades.Player
{
    public class PlayerLookInteraction : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private DialogueManager dialogueManager;

        [Header("Interaction")]
        [SerializeField] private float interactDistance = 3f;
        [SerializeField] private LayerMask interactMask = ~0;
        [SerializeField] private Vector2 promptScreenOffset = new Vector2(0f, -24f);

        [Header("Crosshair")]
        [SerializeField] private bool showCrosshair = true;
        [SerializeField] private float crosshairSize = 6f;
        [SerializeField] private Color crosshairColor = Color.white;
        [SerializeField] private Color promptColor = Color.white;
        [SerializeField] private int promptFontSize = 20;

        private Camera _camera;
        private IPlayerInteractable _currentInteractable;
        private string _currentInteractionRevision;
        private Vector3 _currentPromptPosition;
        private GUIStyle _promptStyle;
        private Texture2D _crosshairTexture;
        private bool _furnitureHandlingActive;

        private void Awake()
        {
            if (playerController == null)
            {
                playerController = GetComponent<PlayerController>();
            }

            if (cameraTransform == null && playerController != null)
            {
                cameraTransform = playerController.CameraTransform;
            }

            if (cameraTransform != null)
            {
                _camera = cameraTransform.GetComponent<Camera>();
            }

            if (dialogueManager == null)
            {
                dialogueManager = FindObjectOfType<DialogueManager>();
            }
        }

        private void Update()
        {
            RefreshTarget();
        }

        public void OnInteract()
        {
            if (IsInteractionLocked())
            {
                return;
            }

            if (_currentInteractable == null || playerController == null)
            {
                return;
            }

            if (_currentInteractable is IRevisionBoundPlayerInteractable revisionBound)
            {
                string expectedRevision = _currentInteractionRevision;
                if (!string.IsNullOrWhiteSpace(expectedRevision) && !revisionBound.TryInteract(playerController, expectedRevision))
                {
                    RefreshTarget();
                }
                return;
            }

            if (_currentInteractable.CanInteract(playerController))
            {
                _currentInteractable.Interact(playerController);
            }
        }

        public void SetFurnitureHandlingActive(bool active)
        {
            _furnitureHandlingActive = active;
            if (active) RefreshTarget();
        }

        private void RefreshTarget()
        {
            IPlayerInteractable previousInteractable = _currentInteractable;
            string previousRevision = _currentInteractionRevision;
            _currentInteractable = null;
            _currentInteractionRevision = null;

            if (_camera == null && cameraTransform != null)
            {
                _camera = cameraTransform.GetComponent<Camera>();
            }

            if (_camera == null || playerController == null || IsInteractionLocked())
            {
                return;
            }

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Collide))
            {
                return;
            }

            if (!TryGetInteractable(hit.collider, out IPlayerInteractable interactable))
            {
                return;
            }

            if (!interactable.CanInteract(playerController))
            {
                return;
            }

            if (interactable is IRevisionBoundPlayerInteractable revisionBound)
            {
                string currentRevision = revisionBound.CaptureInteractionRevision(playerController);
                if (string.IsNullOrWhiteSpace(currentRevision)) return;
                if (ReferenceEquals(previousInteractable, interactable) && !string.IsNullOrEmpty(previousRevision) &&
                    !string.Equals(previousRevision, currentRevision, System.StringComparison.Ordinal))
                {
                    return;
                }
                _currentInteractionRevision = currentRevision;
            }

            _currentInteractable = interactable;
            _currentPromptPosition = interactable.GetPromptWorldPosition();
        }

        private static bool TryGetInteractable(Collider collider, out IPlayerInteractable interactable)
        {
            MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IPlayerInteractable candidate)
                {
                    interactable = candidate;
                    return true;
                }
            }

            interactable = null;
            return false;
        }

        private void OnGUI()
        {
            if (_camera == null || playerController == null || IsInteractionLocked())
            {
                return;
            }

            if (showCrosshair)
            {
                DrawCrosshair();
            }

            if (_currentInteractable != null)
            {
                DrawPrompt();
            }
        }

        private void DrawCrosshair()
        {
            EnsureCrosshairTexture();

            float x = (Screen.width * 0.5f) - (crosshairSize * 0.5f);
            float y = (Screen.height * 0.5f) - (crosshairSize * 0.5f);

            Color previousColor = GUI.color;
            GUI.color = crosshairColor;
            GUI.DrawTexture(new Rect(x, y, crosshairSize, crosshairSize), _crosshairTexture);
            GUI.color = previousColor;
        }

        private void DrawPrompt()
        {
            Vector3 screenPoint = _camera.WorldToScreenPoint(_currentPromptPosition);
            if (screenPoint.z <= 0f)
            {
                return;
            }

            EnsurePromptStyle();

            Vector2 size = _promptStyle.CalcSize(new GUIContent(_currentInteractable.PromptText));
            float x = screenPoint.x - (size.x * 0.5f) + promptScreenOffset.x;
            float y = Screen.height - screenPoint.y - size.y + promptScreenOffset.y;

            GUI.Label(new Rect(x, y, size.x, size.y), _currentInteractable.PromptText, _promptStyle);
        }

        private void EnsurePromptStyle()
        {
            if (_promptStyle != null)
            {
                return;
            }

            _promptStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = promptFontSize,
                fontStyle = FontStyle.Bold
            };
            _promptStyle.normal.textColor = promptColor;
        }

        private void EnsureCrosshairTexture()
        {
            if (_crosshairTexture != null)
            {
                return;
            }

            _crosshairTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _crosshairTexture.SetPixel(0, 0, Color.white);
            _crosshairTexture.Apply();
        }

        private bool IsInteractionLocked()
        {
            return (playerController != null && playerController.InteractionLocked) ||
                   (dialogueManager != null && dialogueManager.BlocksPlayerControl) || _furnitureHandlingActive;
        }
    }
}
