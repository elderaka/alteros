using System.Collections;
using UnityEngine;

namespace AlterOS.Dialogue.UI
{
    public class DialogueCameraDirector : MonoBehaviour
    {
        public enum DialogueShot
        {
            Center,
            ChoicesLeft,
            ChoicesRight,
            Close
        }

        [Header("References")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform centerPose;
        [SerializeField] private Transform choicesLeftPose;
        [SerializeField] private Transform choicesRightPose;
        [SerializeField] private Transform closePose;
        [SerializeField] private Transform lookAtTarget;
        [SerializeField] private DialogueShot defaultShot = DialogueShot.Center;

        [Header("Fallback Pose")]
        [SerializeField] private float fallbackDistance = 3.2f;
        [SerializeField] private float fallbackHeight = 1.25f;
        [SerializeField] private float fallbackSideOffset = 0f;
        [SerializeField] private float fallbackChoiceSideOffset = 0.65f;

        [Header("Blend")]
        [SerializeField] private float enterDuration = 0.45f;
        [SerializeField] private float exitDuration = 0.25f;
        [SerializeField] private AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private float dialogueFov = 42f;

        private Coroutine blendRoutine;
        private Transform cameraTransform;
        private Vector3 savedLocalPosition;
        private Quaternion savedLocalRotation;
        private float savedFov;
        private bool hasSavedPose;

        public float DialogueFov => dialogueFov;

        private void Awake()
        {
            CacheCamera();
        }

        public bool IsBlending { get; private set; }

        public void EnterDialogue(Transform subject, System.Action onComplete = null)
        {
            EnterDialogue(subject, defaultShot, onComplete);
        }

        public void EnterDialogue(Transform subject, DialogueShot shot, System.Action onComplete = null)
        {
            CacheCamera();
            if (targetCamera == null)
            {
                return;
            }

            if (!hasSavedPose)
            {
                savedLocalPosition = cameraTransform.localPosition;
                savedLocalRotation = cameraTransform.localRotation;
                savedFov = targetCamera.fieldOfView;
                hasSavedPose = true;
            }

            Transform targetLookAt = lookAtTarget != null ? lookAtTarget : subject;
            Vector3 targetPosition;
            Quaternion targetRotation;
            BuildTargetPose(subject, targetLookAt, shot, out targetPosition, out targetRotation);
            StartBlend(targetPosition, targetRotation, dialogueFov, enterDuration, onComplete);
        }

        public void ReframeDialogue(Transform subject, DialogueShot shot)
        {
            CacheCamera();
            if (targetCamera == null || !hasSavedPose)
            {
                return;
            }

            Transform targetLookAt = lookAtTarget != null ? lookAtTarget : subject;
            Vector3 targetPosition;
            Quaternion targetRotation;
            BuildTargetPose(subject, targetLookAt, shot, out targetPosition, out targetRotation);
            StartBlend(targetPosition, targetRotation, dialogueFov, enterDuration * 0.65f);
        }

        public void ExitDialogue(System.Action onComplete = null)
        {
            CacheCamera();
            if (targetCamera == null || !hasSavedPose)
            {
                onComplete?.Invoke();
                return;
            }

            Transform parent = cameraTransform.parent;
            Vector3 worldPosition = parent != null ? parent.TransformPoint(savedLocalPosition) : savedLocalPosition;
            Quaternion worldRotation = parent != null ? parent.rotation * savedLocalRotation : savedLocalRotation;
            StartBlend(worldPosition, worldRotation, savedFov, exitDuration, () =>
            {
                hasSavedPose = false;
                onComplete?.Invoke();
            });
        }

        private void BuildTargetPose(Transform subject, Transform targetLookAt, DialogueShot shot, out Vector3 targetPosition, out Quaternion targetRotation)
        {
            Transform authoredPose = GetPoseForShot(shot);
            if (authoredPose != null)
            {
                targetPosition = authoredPose.position;
                targetRotation = authoredPose.rotation;
                return;
            }

            Vector3 lookAt = targetLookAt != null ? targetLookAt.position : cameraTransform.position + cameraTransform.forward;
            if (subject != null)
            {
                lookAt.y = subject.position.y + fallbackHeight;
            }

            Vector3 fromSubjectToCamera = cameraTransform.position - lookAt;
            fromSubjectToCamera.y = 0f;
            if (fromSubjectToCamera.sqrMagnitude < 0.001f && subject != null)
            {
                fromSubjectToCamera = -subject.forward;
            }

            Vector3 viewDirection = fromSubjectToCamera.normalized;
            Vector3 side = subject != null ? subject.right : Vector3.Cross(Vector3.up, viewDirection).normalized;
            float sideOffset = fallbackSideOffset;
            if (shot == DialogueShot.ChoicesLeft)
            {
                sideOffset = fallbackChoiceSideOffset;
            }
            else if (shot == DialogueShot.ChoicesRight)
            {
                sideOffset = -fallbackChoiceSideOffset;
            }
            else if (shot == DialogueShot.Close)
            {
                sideOffset = fallbackSideOffset * 0.5f;
                viewDirection *= 0.72f;
            }

            targetPosition = lookAt + viewDirection * fallbackDistance + side * sideOffset;
            targetPosition.y = lookAt.y;
            targetRotation = Quaternion.LookRotation((lookAt - targetPosition).normalized, Vector3.up);
        }

        private Transform GetPoseForShot(DialogueShot shot)
        {
            switch (shot)
            {
                case DialogueShot.ChoicesLeft:
                    return choicesLeftPose != null ? choicesLeftPose : centerPose;
                case DialogueShot.ChoicesRight:
                    return choicesRightPose != null ? choicesRightPose : centerPose;
                case DialogueShot.Close:
                    return closePose != null ? closePose : centerPose;
                default:
                    return centerPose;
            }
        }

        private void StartBlend(Vector3 targetPosition, Quaternion targetRotation, float targetFov, float duration, System.Action onComplete = null)
        {
            if (blendRoutine != null)
            {
                StopCoroutine(blendRoutine);
            }

            IsBlending = true;
            blendRoutine = StartCoroutine(BlendRoutine(targetPosition, targetRotation, targetFov, Mathf.Max(0.01f, duration), onComplete));
        }

        private IEnumerator BlendRoutine(Vector3 targetPosition, Quaternion targetRotation, float targetFov, float duration, System.Action onComplete)
        {
            Vector3 startPosition = cameraTransform.position;
            Quaternion startRotation = cameraTransform.rotation;
            float startFov = targetCamera.fieldOfView;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = blendCurve != null ? blendCurve.Evaluate(Mathf.Clamp01(elapsed / duration)) : Mathf.Clamp01(elapsed / duration);
                cameraTransform.position = Vector3.Lerp(startPosition, targetPosition, t);
                cameraTransform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
                targetCamera.fieldOfView = Mathf.Lerp(startFov, targetFov, t);
                yield return null;
            }

            cameraTransform.position = targetPosition;
            cameraTransform.rotation = targetRotation;
            targetCamera.fieldOfView = targetFov;
            blendRoutine = null;
            IsBlending = false;
            onComplete?.Invoke();
        }

        private void CacheCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera != null)
            {
                cameraTransform = targetCamera.transform;
            }
        }
    }
}
