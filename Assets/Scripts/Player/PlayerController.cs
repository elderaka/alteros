using UnityEngine;
using UnityEngine.InputSystem;

namespace Pleiades.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 5.0f;
        [SerializeField] private float sprintSpeed = 8.0f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -9.81f;

        [Header("Look Settings")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float lookSensitivity = 0.1f;
        [SerializeField] private float topClamp = 85.0f;
        [SerializeField] private float bottomClamp = -85.0f;
        
        [Header("Zoom Settings (Face Focus)")]
        [SerializeField] private float normalFOV = 60f;
        [SerializeField] private float zoomFOV = 20f;
        [SerializeField] private float zoomSpeed = 10f;

        [Header("HUD Cursor Mode")]
        [SerializeField] private bool holdAltForHudCursor = true;
        [SerializeField] private bool allowMovementWhileHudCursor = true;

        [Header("Control Locks")]
        [SerializeField] private DialogueManager dialogueManager;

        private CharacterController _controller;
        private Camera _playerCamera;
        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private Vector3 _velocity;
        private float _verticalRotation;
        private bool _isSprinting;

        // --- Dialogue State ---
        private bool _inDialogueMode = false;
        private float _dialogueFOV;
        private Transform _dialogueTarget;
        private float _dialogueSmoothness;
        private float _suppressActionInputUntil;
        private bool _externalCameraControl;
        private bool _isHudCursorMode;
        private bool _externalHudCursorMode;

        public Transform CameraTransform => cameraTransform;
        public Camera PlayerCamera => _playerCamera;
        public bool IsInDialogueMode => _inDialogueMode;
        public bool IsHudCursorMode => _isHudCursorMode;
        public bool IsExternalHudCursorMode => _externalHudCursorMode;
        public bool IsExternalCameraControl => _externalCameraControl;
        public bool ControlsLocked => _inDialogueMode || _externalCameraControl || _externalHudCursorMode || (dialogueManager != null && dialogueManager.BlocksPlayerControl);
        public bool InteractionLocked => ControlsLocked || _isHudCursorMode || _externalHudCursorMode;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (dialogueManager == null)
            {
                dialogueManager = FindObjectOfType<DialogueManager>();
            }
            
            if (cameraTransform != null)
            {
                _playerCamera = cameraTransform.GetComponent<Camera>();
                if (_playerCamera != null)
                {
                    // FIX: Set Near Clip Plane very low to prevent clipping through meshes when close
                    _playerCamera.nearClipPlane = 0.01f;
                    _playerCamera.fieldOfView = normalFOV;
                }
            }
            
            // Lock cursor for first person experience
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            UpdateHudCursorMode();

            if (ControlsLocked)
            {
                if (_inDialogueMode && !_externalCameraControl)
                {
                    HandleDialogueRotation();
                    HandleZoom(); // Dialogue zoom override
                }

                return;       // Skip normal movement and look
            }

            if (_externalCameraControl)
            {
                return;
            }

            if (_isHudCursorMode)
            {
                if (allowMovementWhileHudCursor)
                {
                    HandleMovement();
                }

                HandleZoom();
                return;
            }

            HandleMovement();
            HandleLook();
            HandleZoom();
        }

        // --- Dialogue API ---
        public void SetDialogueMode(bool active, float fov = 60f, Transform target = null, float smoothness = 8f)
        {
            _inDialogueMode = active;
            _dialogueFOV = fov;
            _dialogueTarget = target;
            _dialogueSmoothness = smoothness;

            if (active)
            {
                SetHudCursorMode(false);

                // Unlock cursor for UI interaction
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                
                // Clear any lingering inputs
                _moveInput = Vector2.zero;
                _lookInput = Vector2.zero;
            }
            else
            {
                _suppressActionInputUntil = Time.time + 0.15f;

                if (!IsAltHeld())
                {
                    SetHudCursorMode(false);
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        public void SetExternalCameraControl(bool active)
        {
            _externalCameraControl = active;
            if (active)
            {
                _moveInput = Vector2.zero;
                _lookInput = Vector2.zero;
            }
        }

        public void SetExternalHudCursorMode(bool active)
        {
            _externalHudCursorMode = active;
            UpdateHudCursorMode();
        }

        private void UpdateHudCursorMode()
        {
            if (_inDialogueMode || _externalCameraControl)
            {
                if (_isHudCursorMode && !_inDialogueMode && !_externalHudCursorMode)
                {
                    SetHudCursorMode(false);
                }

                return;
            }

            bool cursorRequested = _externalHudCursorMode || (holdAltForHudCursor && IsAltHeld());
            SetHudCursorMode(cursorRequested);
        }

        private void SetHudCursorMode(bool active)
        {
            if (_isHudCursorMode == active)
            {
                return;
            }

            _isHudCursorMode = active;
            _lookInput = Vector2.zero;

            if (active)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (!_inDialogueMode)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static bool IsAltHeld()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   ((keyboard.leftAltKey != null && keyboard.leftAltKey.isPressed) ||
                    (keyboard.rightAltKey != null && keyboard.rightAltKey.isPressed));
        }

        private void HandleDialogueRotation()
        {
            if (_dialogueTarget == null) return;

            // Smoothly rotate the player's body to face the target on the Y axis
            Vector3 direction = _dialogueTarget.position - transform.position;
            direction.y = 0; // Keep it horizontal
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * _dialogueSmoothness);
            }

            // Smoothly reset the vertical camera pitch to look level at the target's face
            if (cameraTransform != null)
            {
                _verticalRotation = Mathf.Lerp(_verticalRotation, 0f, Time.deltaTime * _dialogueSmoothness);
                cameraTransform.localRotation = Quaternion.Euler(_verticalRotation, 0f, 0f);
            }
        }

        // --- Input System Callbacks ---
        // These are called by the PlayerInput component via "Send Messages" or "Broadcast Messages"
        
        public void OnMove(InputValue value)
        {
            if (ControlsLocked)
            {
                _moveInput = Vector2.zero;
                return;
            }

            _moveInput = value.Get<Vector2>();
        }

        public void OnLook(InputValue value)
        {
            if (ControlsLocked)
            {
                _lookInput = Vector2.zero;
                return;
            }

            _lookInput = value.Get<Vector2>();
        }

        public void OnSprint(InputValue value)
        {
            if (ControlsLocked)
            {
                _isSprinting = false;
                return;
            }

            _isSprinting = value.isPressed;
        }

        public void OnJump(InputValue value)
        {
            if (ControlsLocked || Time.time < _suppressActionInputUntil)
            {
                return;
            }

            if (value.isPressed && _controller.isGrounded)
            {
                _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }

        // --- Logic ---

        private void HandleMovement()
        {
            // Ground check
            if (_controller.isGrounded && _velocity.y < 0)
            {
                _velocity.y = -2f; // Slight downward force to keep grounded
            }

            // Calculate movement direction relative to camera
            float currentSpeed = _isSprinting ? sprintSpeed : moveSpeed;
            Vector3 move = transform.right * _moveInput.x + transform.forward * _moveInput.y;
            
            _controller.Move(move * currentSpeed * Time.deltaTime);

            // Gravity
            _velocity.y += gravity * Time.deltaTime;
            _controller.Move(_velocity * Time.deltaTime);
        }

        private void HandleLook()
        {
            if (cameraTransform == null) return;

            // Horizontal rotation (Yaw) - rotates the player body
            transform.Rotate(Vector3.up * (_lookInput.x * lookSensitivity));

            // Vertical rotation (Pitch) - rotates the camera only
            _verticalRotation -= _lookInput.y * lookSensitivity;
            _verticalRotation = Mathf.Clamp(_verticalRotation, bottomClamp, topClamp);
            
            cameraTransform.localRotation = Quaternion.Euler(_verticalRotation, 0f, 0f);
        }
        
        private void HandleZoom()
        {
            if (_playerCamera == null) return;
            
            float targetFOV;
            if (_inDialogueMode)
            {
                targetFOV = _dialogueFOV;
            }
            else
            {
                // Check if Right Mouse Button is held down
                bool isZooming = Mouse.current != null && Mouse.current.rightButton.isPressed;
                targetFOV = isZooming ? zoomFOV : normalFOV;
            }
            
            // Smoothly interpolate FOV
            _playerCamera.fieldOfView = Mathf.Lerp(_playerCamera.fieldOfView, targetFOV, Time.deltaTime * zoomSpeed);
        }
    }
}
