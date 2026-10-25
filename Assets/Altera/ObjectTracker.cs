using UnityEngine;
using Pleiades.Player;

public class ObjectTracker : MonoBehaviour
{
    private enum AttentionZone
    {
        None,
        Far,
        Medium,
        Near,
        Intimate
    }

    [Header("Target")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private Transform targetEyeTransform;
    [SerializeField] private bool autoTargetMainCamera = true;
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.55f, 0f);
    [SerializeField] private float targetEyeForwardOffset = 0.35f;
    [SerializeField] private float targetEyeVerticalOffset = 0.03f;

    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform eyeReference;
    [SerializeField] private Transform leftEyeBone;
    [SerializeField] private Transform rightEyeBone;

    [Header("Distance Zones")]
    [SerializeField] private float maxTrackingDistance = 8f;
    [SerializeField] private float farAttentionDistance = 4.5f;
    [SerializeField] private float mediumAttentionDistance = 2.5f;
    [SerializeField] private float nearAttentionDistance = 1.4f;
    [SerializeField] private float intimateAttentionDistance = 0.8f;

    [Header("Look Limits")]
    [SerializeField] private float gazeDeadzoneYaw = 3f;
    [SerializeField] private float eyesOnlyYaw = 18f;
    [SerializeField] private float headAssistYaw = 30f;
    [SerializeField] private float strongHeadYaw = 45f;
    [SerializeField] private float maxHeadYaw = 65f;
    [SerializeField] private bool lockVerticalHeadMovement = false;
    [SerializeField] private float verticalHeadDeadzone = 8f;
    [SerializeField] private float maxHeadPitchUp = 18f;
    [SerializeField] private float maxHeadPitchDown = 16f;

    [Header("Look Weights")]
    [SerializeField] private float farLookWeight = 0.15f;
    [SerializeField] private float mediumLookWeight = 0.4f;
    [SerializeField] private float nearLookWeight = 0.7f;
    [SerializeField] private float intimateLookWeight = 0.85f;
    [SerializeField] private float farEyesWeight = 0.85f;
    [SerializeField] private float mediumEyesWeight = 0.95f;
    [SerializeField] private float nearEyesWeight = 1f;
    [SerializeField] private float intimateEyesWeight = 1f;
    [SerializeField] private float intimateHeadWeightScale = 0.45f;
    [SerializeField] private float intimateBodyWeightScale = 0.08f;
    [SerializeField] private float clampWeight = 0.45f;

    [Header("Responsiveness")]
    [SerializeField] private float reactionDelay = 0.12f;
    [SerializeField] private float lookAtBlendSpeed = 6f;
    [SerializeField] private float targetSmoothing = 5f;
    [SerializeField] private float targetMovementDeadzone = 0.05f;

    [Header("Mutual Attention")]
    [SerializeField] private bool requireMutualAttention = true;
    [SerializeField] private float selfVisionConeHalfAngle = 70f;
    [SerializeField] private float playerVisionConeHalfAngle = 10f;
    [SerializeField] private float stareToEngageSeconds = 2f;
    [SerializeField] private float attentionRetentionSeconds = 2.5f;
    [SerializeField] private float interactionFaceLockSeconds = 4f;
    [SerializeField] private float closeRangeVerticalBias = 0.06f;
    [SerializeField] private float closeRangeForwardOffset = 0.02f;

    [Header("Idle Relaxation")]
    [SerializeField] private float sustainedAttentionBeforeRelax = 2.5f;
    [SerializeField] private Vector2 relaxDurationRange = new Vector2(0.7f, 1.2f);
    [SerializeField] private Vector2 relaxCooldownRange = new Vector2(2.5f, 4f);
    [SerializeField] private float relaxYawOffset = 8f;
    [SerializeField] private float relaxBreakMovement = 0.2f;

    [Header("Root Turning")]
    [SerializeField] private bool rotateRootWhenNeeded = true;
    [SerializeField] private float rootTurnThreshold = 60f;
    [SerializeField] private float outOfVisionTurnThreshold = 80f;
    [SerializeField] private float rootTurnSpeed = 220f;
    [SerializeField] private float outOfVisionTurnSpeed = 420f;
    [SerializeField] private float rootTurnCompleteThreshold = 2f;
    [SerializeField] private float rootTurnBurstDuration = 0.12f;
    [SerializeField] private float rootTurnBurstPause = 0.08f;

    private bool _hasHead;
    private Vector3 _sampledTargetPosition;
    private Vector3 _currentLookTarget;
    private Vector3 _lastRawTargetPosition;
    private AttentionZone _currentZone;
    private float _reactionTimer;
    private float _currentLookWeight;
    private float _currentBodyWeight;
    private float _currentHeadWeight;
    private float _currentEyesWeight;
    private float _currentDistance;
    private float _playerStareTimer;
    private float _attentionRetentionTimer;
    private bool _hasAttentionLock;
    private float _forcedAttentionTimer;
    private bool _pendingFaceCommit;

    private bool _isRelaxing;
    private float _relaxTimer;
    private float _relaxCooldownTimer;
    private float _sustainedAttentionTimer;
    private int _relaxDirection = 1;

    private bool _isRootTurning;
    private Quaternion _rootTurnTargetRotation;
    private float _rootTurnBurstTimer;
    private float _rootTurnPauseTimer;

    private void Awake()
    {
        ResolveReferences();
        Vector3 referencePosition = _hasHead ? GetReferencePosition() : transform.position;
        _sampledTargetPosition = referencePosition + transform.forward * 2f;
        _currentLookTarget = _sampledTargetPosition;
        _lastRawTargetPosition = _sampledTargetPosition;
    }

    private void Reset()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolvePlayerTarget();
        ResolveTargetEyeTransform();

        if (_forcedAttentionTimer > 0f)
        {
            _forcedAttentionTimer -= Time.deltaTime;
        }

        if (!_hasHead || playerTarget == null)
        {
            FadeOutLookAt();
            return;
        }

        Vector3 rawTargetPosition = GetRawTargetPosition();
        Vector3 referencePosition = GetReferencePosition();
        _currentDistance = Vector3.Distance(referencePosition, rawTargetPosition);
        if (_currentDistance > maxTrackingDistance)
        {
            FadeOutLookAt();
            return;
        }

        _currentZone = ResolveZone(_currentDistance);
        float playerMovement = Vector3.Distance(rawTargetPosition, _lastRawTargetPosition);
        _lastRawTargetPosition = rawTargetPosition;

        Vector3 flatDirection = rawTargetPosition - transform.position;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude < 0.001f)
        {
            FadeOutLookAt();
            return;
        }

        float rootYaw = Vector3.SignedAngle(transform.forward, flatDirection.normalized, Vector3.up);
        bool canTrackPlayer = UpdateAttentionLock(referencePosition, rawTargetPosition, Mathf.Abs(rootYaw));
        if (!canTrackPlayer)
        {
            FadeOutLookAt();
            return;
        }

        bool shouldFullyFacePlayer = _pendingFaceCommit || _forcedAttentionTimer > 0f;
        RotateRootTowards(flatDirection.normalized, Mathf.Abs(rootYaw), shouldFullyFacePlayer);
        UpdateRelaxation(playerMovement, Mathf.Abs(rootYaw));

        Vector3 desiredTarget = GetClampedLookTarget(rawTargetPosition);
        if (_isRelaxing)
        {
            desiredTarget = ApplyRelaxation(desiredTarget, _currentDistance);
        }

        UpdateSampledTarget(desiredTarget);
        _currentLookTarget = Vector3.Lerp(_currentLookTarget, _sampledTargetPosition, Time.deltaTime * targetSmoothing);

        float localYaw = GetSignedYawToTarget(referencePosition, _currentLookTarget);
        ApplyAttentionWeights(Mathf.Abs(localYaw));
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || !_hasHead)
        {
            return;
        }

        animator.SetLookAtWeight(_currentLookWeight, _currentBodyWeight, _currentHeadWeight, _currentEyesWeight, clampWeight);
        animator.SetLookAtPosition(_currentLookTarget);
    }

    private void ResolveReferences()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null && animator.isHuman)
        {
            if (headBone == null)
            {
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (leftEyeBone == null)
            {
                leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            }

            if (rightEyeBone == null)
            {
                rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);
            }

            if (eyeReference == null)
            {
                eyeReference = headBone;
            }
        }

        _hasHead = headBone != null;
    }

    private void ResolvePlayerTarget()
    {
        if (playerTarget == null && autoTargetMainCamera && Camera.main != null)
        {
            playerTarget = Camera.main.transform;
        }
    }

    private void ResolveTargetEyeTransform()
    {
        if (playerTarget == null || targetEyeTransform != null)
        {
            return;
        }

        PlayerController controller = playerTarget.GetComponent<PlayerController>();
        if (controller == null)
        {
            controller = playerTarget.GetComponentInParent<PlayerController>();
        }

        if (controller != null && controller.CameraTransform != null)
        {
            targetEyeTransform = controller.CameraTransform;
            return;
        }

        Camera childCamera = playerTarget.GetComponentInChildren<Camera>();
        if (childCamera != null)
        {
            targetEyeTransform = childCamera.transform;
        }
    }

    private AttentionZone ResolveZone(float distance)
    {
        if (distance <= intimateAttentionDistance) return AttentionZone.Intimate;
        if (distance <= nearAttentionDistance) return AttentionZone.Near;
        if (distance <= mediumAttentionDistance) return AttentionZone.Medium;
        if (distance <= farAttentionDistance) return AttentionZone.Far;
        return AttentionZone.None;
    }

    private void RotateRootTowards(Vector3 flatDirection, float absoluteYaw, bool shouldFullyFacePlayer)
    {
        if (!rotateRootWhenNeeded)
        {
            _isRootTurning = false;
            return;
        }

        bool shouldTurn = shouldFullyFacePlayer
            ? (_isRootTurning || absoluteYaw > rootTurnCompleteThreshold)
            : (_isRootTurning || absoluteYaw >= rootTurnThreshold);
        if (!shouldTurn)
        {
            _isRootTurning = false;
            _rootTurnBurstTimer = 0f;
            _rootTurnPauseTimer = 0f;
            return;
        }

        _isRootTurning = true;
        _rootTurnTargetRotation = Quaternion.LookRotation(flatDirection, Vector3.up);

        if (_rootTurnPauseTimer > 0f)
        {
            _rootTurnPauseTimer -= Time.deltaTime;
            return;
        }

        if (_rootTurnBurstTimer <= 0f)
        {
            _rootTurnBurstTimer = rootTurnBurstDuration;
        }

        _rootTurnBurstTimer -= Time.deltaTime;
        float turnSpeed = absoluteYaw >= outOfVisionTurnThreshold ? outOfVisionTurnSpeed : rootTurnSpeed;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, _rootTurnTargetRotation, turnSpeed * Time.deltaTime);

        if (_rootTurnBurstTimer <= 0f)
        {
            _rootTurnPauseTimer = rootTurnBurstPause;
        }

        if (Quaternion.Angle(transform.rotation, _rootTurnTargetRotation) <= rootTurnCompleteThreshold)
        {
            transform.rotation = _rootTurnTargetRotation;
            _isRootTurning = false;
            _pendingFaceCommit = false;
            _rootTurnBurstTimer = 0f;
            _rootTurnPauseTimer = 0f;
        }
    }

    private void UpdateRelaxation(float playerMovement, float absoluteYaw)
    {
        if (_relaxCooldownTimer > 0f)
        {
            _relaxCooldownTimer -= Time.deltaTime;
        }

        if (_isRelaxing)
        {
            if (playerMovement > relaxBreakMovement || absoluteYaw > headAssistYaw)
            {
                StopRelaxation();
                return;
            }

            _relaxTimer -= Time.deltaTime;
            if (_relaxTimer <= 0f)
            {
                StopRelaxation();
            }
            return;
        }

        if (_relaxCooldownTimer > 0f)
        {
            _sustainedAttentionTimer = 0f;
            return;
        }

        bool canRelax = _currentZone == AttentionZone.Near || _currentZone == AttentionZone.Intimate;
        if (!canRelax || playerMovement > relaxBreakMovement || absoluteYaw > headAssistYaw)
        {
            _sustainedAttentionTimer = 0f;
            return;
        }

        _sustainedAttentionTimer += Time.deltaTime;
        if (_sustainedAttentionTimer >= sustainedAttentionBeforeRelax)
        {
            _isRelaxing = true;
            _sustainedAttentionTimer = 0f;
            _relaxTimer = Random.Range(relaxDurationRange.x, relaxDurationRange.y);
            _relaxDirection = Random.value > 0.5f ? 1 : -1;
        }
    }

    private void StopRelaxation()
    {
        _isRelaxing = false;
        _relaxTimer = 0f;
        _relaxCooldownTimer = Random.Range(relaxCooldownRange.x, relaxCooldownRange.y);
    }

    private Vector3 ApplyRelaxation(Vector3 desiredTarget, float distance)
    {
        Vector3 referencePosition = GetReferencePosition();
        Vector3 flatDirection = desiredTarget - referencePosition;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude < 0.001f)
        {
            flatDirection = transform.forward;
        }
        else
        {
            flatDirection.Normalize();
        }

        Quaternion yawOffset = Quaternion.AngleAxis(relaxYawOffset * _relaxDirection, Vector3.up);
        Vector3 relaxedDirection = yawOffset * flatDirection;
        Vector3 relaxedTarget = referencePosition + relaxedDirection * Mathf.Max(1.5f, distance);
        relaxedTarget.y = desiredTarget.y;
        return relaxedTarget;
    }

    private void UpdateSampledTarget(Vector3 desiredTarget)
    {
        if ((_sampledTargetPosition - desiredTarget).sqrMagnitude <= targetMovementDeadzone * targetMovementDeadzone)
        {
            _reactionTimer = 0f;
            return;
        }

        _reactionTimer += Time.deltaTime;
        if (_reactionTimer >= reactionDelay)
        {
            _sampledTargetPosition = desiredTarget;
            _reactionTimer = 0f;
        }
    }

    private Vector3 GetClampedLookTarget(Vector3 targetPosition)
    {
        Vector3 referencePosition = GetReferencePosition();
        Vector3 localDirection = transform.InverseTransformDirection(targetPosition - referencePosition).normalized;

        float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(localDirection.y, new Vector2(localDirection.x, localDirection.z).magnitude) * Mathf.Rad2Deg;

        yaw = Mathf.Clamp(yaw, -maxHeadYaw, maxHeadYaw);
        pitch = lockVerticalHeadMovement ? 0f : GetVerticalContribution(pitch);

        Quaternion localRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 clampedDirection = transform.TransformDirection(localRotation * Vector3.forward);
        return referencePosition + clampedDirection * 3f;
    }

    private float GetVerticalContribution(float pitch)
    {
        if (Mathf.Abs(pitch) <= verticalHeadDeadzone)
        {
            return 0f;
        }

        if (pitch < 0f)
        {
            float t = Mathf.InverseLerp(verticalHeadDeadzone, 28f, Mathf.Abs(pitch));
            return -Mathf.Lerp(0f, maxHeadPitchUp, t);
        }

        float downT = Mathf.InverseLerp(verticalHeadDeadzone, 30f, pitch);
        return Mathf.Lerp(0f, maxHeadPitchDown, downT);
    }

    private float GetSignedYawToTarget(Vector3 fromPosition, Vector3 targetPosition)
    {
        Vector3 flatDirection = targetPosition - fromPosition;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude < 0.001f)
        {
            return 0f;
        }

        return Vector3.SignedAngle(transform.forward, flatDirection.normalized, Vector3.up);
    }

    private void ApplyAttentionWeights(float absoluteYaw)
    {
        float targetLookWeight = 0f;
        float targetEyesWeight = 0f;

        switch (_currentZone)
        {
            case AttentionZone.Far:
                targetLookWeight = farLookWeight;
                targetEyesWeight = farEyesWeight;
                break;
            case AttentionZone.Medium:
                targetLookWeight = mediumLookWeight;
                targetEyesWeight = mediumEyesWeight;
                break;
            case AttentionZone.Near:
                targetLookWeight = nearLookWeight;
                targetEyesWeight = nearEyesWeight;
                break;
            case AttentionZone.Intimate:
                targetLookWeight = intimateLookWeight;
                targetEyesWeight = intimateEyesWeight;
                break;
        }

        if (absoluteYaw <= gazeDeadzoneYaw)
        {
            targetLookWeight *= 0.35f;
        }

        float targetHeadWeight;
        float targetBodyWeight;

        if (absoluteYaw <= eyesOnlyYaw)
        {
            targetHeadWeight = 0f;
            targetBodyWeight = 0f;
        }
        else if (absoluteYaw <= headAssistYaw)
        {
            float t = Mathf.InverseLerp(eyesOnlyYaw, headAssistYaw, absoluteYaw);
            targetHeadWeight = Mathf.Lerp(0.08f, 0.22f, t);
            targetBodyWeight = 0f;
        }
        else if (absoluteYaw <= strongHeadYaw)
        {
            float t = Mathf.InverseLerp(headAssistYaw, strongHeadYaw, absoluteYaw);
            targetHeadWeight = Mathf.Lerp(0.25f, 0.48f, t);
            targetBodyWeight = Mathf.Lerp(0.02f, 0.08f, t);
        }
        else
        {
            float t = Mathf.InverseLerp(strongHeadYaw, maxHeadYaw, Mathf.Min(absoluteYaw, maxHeadYaw));
            targetHeadWeight = Mathf.Lerp(0.5f, 0.72f, t);
            targetBodyWeight = Mathf.Lerp(0.08f, 0.16f, t);
        }

        if (_currentZone == AttentionZone.Far)
        {
            targetHeadWeight *= 0.3f;
            targetBodyWeight = 0f;
        }
        else if (_currentZone == AttentionZone.Medium)
        {
            targetHeadWeight *= 0.7f;
            targetBodyWeight *= 0.2f;
        }
        else if (_currentZone == AttentionZone.Intimate)
        {
            targetHeadWeight *= intimateHeadWeightScale;
            targetBodyWeight *= intimateBodyWeightScale;
        }

        if (_isRelaxing)
        {
            targetLookWeight *= 0.6f;
            targetHeadWeight *= 0.4f;
            targetBodyWeight *= 0.2f;
            targetEyesWeight *= 0.8f;
        }

        _currentLookWeight = Mathf.MoveTowards(_currentLookWeight, targetLookWeight, Time.deltaTime * lookAtBlendSpeed);
        _currentBodyWeight = Mathf.MoveTowards(_currentBodyWeight, targetBodyWeight, Time.deltaTime * lookAtBlendSpeed);
        _currentHeadWeight = Mathf.MoveTowards(_currentHeadWeight, targetHeadWeight, Time.deltaTime * lookAtBlendSpeed);
        _currentEyesWeight = Mathf.MoveTowards(_currentEyesWeight, targetEyesWeight, Time.deltaTime * lookAtBlendSpeed);
    }

    private void FadeOutLookAt()
    {
        _currentZone = AttentionZone.None;
        _reactionTimer = 0f;
        _sustainedAttentionTimer = 0f;
        _isRelaxing = false;
        _relaxTimer = 0f;
        _currentLookWeight = Mathf.MoveTowards(_currentLookWeight, 0f, Time.deltaTime * lookAtBlendSpeed);
        _currentBodyWeight = Mathf.MoveTowards(_currentBodyWeight, 0f, Time.deltaTime * lookAtBlendSpeed);
        _currentHeadWeight = Mathf.MoveTowards(_currentHeadWeight, 0f, Time.deltaTime * lookAtBlendSpeed);
        _currentEyesWeight = Mathf.MoveTowards(_currentEyesWeight, 0f, Time.deltaTime * lookAtBlendSpeed);
    }

    private Vector3 GetRawTargetPosition()
    {
        if (targetEyeTransform != null)
        {
            float closeT = Mathf.InverseLerp(mediumAttentionDistance, intimateAttentionDistance, _currentDistance > 0f ? _currentDistance : Vector3.Distance(GetReferencePosition(), targetEyeTransform.position));
            float forwardOffset = Mathf.Lerp(targetEyeForwardOffset, closeRangeForwardOffset, closeT);
            float verticalOffset = Mathf.Lerp(targetEyeVerticalOffset, targetEyeVerticalOffset + closeRangeVerticalBias, closeT);
            return targetEyeTransform.position
                + targetEyeTransform.forward * forwardOffset
                + Vector3.up * verticalOffset;
        }

        return playerTarget.position + targetOffset;
    }

    private bool UpdateAttentionLock(Vector3 referencePosition, Vector3 rawTargetPosition, float absoluteYaw)
    {
        if (_forcedAttentionTimer > 0f)
        {
            _hasAttentionLock = true;
            _pendingFaceCommit = true;
            _attentionRetentionTimer = Mathf.Max(_attentionRetentionTimer, attentionRetentionSeconds);
            return true;
        }

        if (!requireMutualAttention)
        {
            _hasAttentionLock = true;
            _attentionRetentionTimer = attentionRetentionSeconds;
            return true;
        }

        bool playerInSelfCone = absoluteYaw <= selfVisionConeHalfAngle;
        bool playerLookingAtAltera = IsPlayerLookingAtAltera(referencePosition);
        bool canTriggerAttention = playerLookingAtAltera;
        bool mutualAttentionNow = playerInSelfCone && playerLookingAtAltera;

        if (canTriggerAttention)
        {
            _playerStareTimer += Time.deltaTime;
            if (_playerStareTimer >= stareToEngageSeconds && !_hasAttentionLock)
            {
                _hasAttentionLock = true;
                _pendingFaceCommit = true;
                _attentionRetentionTimer = attentionRetentionSeconds;
            }
        }
        else
        {
            _playerStareTimer = 0f;
        }

        if (_hasAttentionLock && !mutualAttentionNow)
        {
            _attentionRetentionTimer -= Time.deltaTime;
            if (_attentionRetentionTimer <= 0f)
            {
                _hasAttentionLock = false;
                _pendingFaceCommit = false;
                _attentionRetentionTimer = 0f;
                _isRootTurning = false;
            }
        }

        if (_hasAttentionLock && mutualAttentionNow)
        {
            _attentionRetentionTimer = attentionRetentionSeconds;
        }

        return _hasAttentionLock;
    }

    private bool IsPlayerLookingAtAltera(Vector3 referencePosition)
    {
        if (targetEyeTransform == null)
        {
            return false;
        }

        Vector3 toAltera = referencePosition - targetEyeTransform.position;
        float distance = toAltera.magnitude;
        if (distance <= 0.001f)
        {
            return true;
        }

        float facingDot = Vector3.Dot(targetEyeTransform.forward.normalized, toAltera / distance);
        float requiredDot = Mathf.Cos(playerVisionConeHalfAngle * Mathf.Deg2Rad);
        return facingDot >= requiredDot;
    }

    public void ForceFacePlayer(float duration = -1f)
    {
        float lockDuration = duration > 0f ? duration : interactionFaceLockSeconds;
        _forcedAttentionTimer = Mathf.Max(_forcedAttentionTimer, lockDuration);
        _hasAttentionLock = true;
        _pendingFaceCommit = true;
        _playerStareTimer = stareToEngageSeconds;
        _attentionRetentionTimer = Mathf.Max(_attentionRetentionTimer, attentionRetentionSeconds);
        _isRelaxing = false;
        _relaxTimer = 0f;
        _relaxCooldownTimer = 0f;
    }

    private Vector3 GetReferencePosition()
    {
        if (leftEyeBone != null && rightEyeBone != null)
        {
            return (leftEyeBone.position + rightEyeBone.position) * 0.5f;
        }

        return eyeReference != null ? eyeReference.position : headBone.position;
    }
}
