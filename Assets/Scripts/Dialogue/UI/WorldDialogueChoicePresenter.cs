using System;
using System.Collections.Generic;
using AlterOS.Dialogue;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.Dialogue.UI
{
    public class WorldDialogueChoicePresenter : MonoBehaviour
    {
        public enum ChoiceSide
        {
            None,
            Left,
            Right
        }

        [Header("Placement")]
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private RectTransform choiceRoot;
        [SerializeField] private Transform anchor;
        [SerializeField] private Transform leftAnchor;
        [SerializeField] private Transform rightAnchor;
        [SerializeField] private Vector3 anchorOffset = new Vector3(-0.75f, 1.35f, 0.15f);
        [SerializeField] private bool faceCamera = true;
        [SerializeField] private Camera eventCamera;
        [SerializeField] private float baseWorldScale = 0.002f;
        [SerializeField] private float referenceDistance = 3f;
        [SerializeField] private float minScale = 0.75f;
        [SerializeField] private float maxScale = 1.15f;

        [Header("Side Selection")]
        [SerializeField] private bool autoChooseSide = true;
        [SerializeField] private LayerMask obstructionMask = ~0;
        [SerializeField] private Vector3 collisionProbeHalfExtents = new Vector3(0.55f, 0.28f, 0.06f);
        [SerializeField] private bool checkCameraLineOfSight = true;

        [Header("Choices")]
        [SerializeField] private WorldDialogueChoiceButton choiceButtonPrefab;
        [SerializeField] private bool hideWhenEmpty = true;

        private readonly List<GameObject> spawnedChoices = new List<GameObject>();
        private GraphicRaycaster graphicRaycaster;
        private Transform activeAnchor;

        public ChoiceSide ActiveSide { get; private set; } = ChoiceSide.None;

        private void Awake()
        {
            CacheComponents();
            ClearChoices();
        }

        private void LateUpdate()
        {
            UpdatePlacement();
        }

        public void ShowChoices(IList<DialogueChoice> choices, Action<DialogueChoice, string> onSelected)
        {
            CacheComponents();
            ClearChoices();

            if (choices == null || choices.Count == 0 || choiceButtonPrefab == null || choiceRoot == null)
            {
                SetVisible(false);
                return;
            }

            for (int i = 0; i < choices.Count; i++)
            {
                DialogueChoice choice = choices[i];
                if (choice == null)
                {
                    continue;
                }

                WorldDialogueChoiceButton button = Instantiate(choiceButtonPrefab, choiceRoot);
                button.Bind(choice, onSelected);
                spawnedChoices.Add(button.gameObject);
            }

            VerticalLayoutGroup layout = choiceRoot.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(choiceRoot);
            }

            SetVisible(true);
            UpdatePlacement();
        }

        public void ClearChoices()
        {
            for (int i = spawnedChoices.Count - 1; i >= 0; i--)
            {
                if (spawnedChoices[i] != null)
                {
                    Destroy(spawnedChoices[i]);
                }
            }

            spawnedChoices.Clear();

            if (hideWhenEmpty)
            {
                SetVisible(false);
            }

            ActiveSide = ChoiceSide.None;
        }

        private void UpdatePlacement()
        {
            if (worldCanvas == null)
            {
                return;
            }

            Camera cameraToFace = eventCamera != null ? eventCamera : Camera.main;

            if (anchor != null || leftAnchor != null || rightAnchor != null)
            {
                activeAnchor = ResolveAnchor(cameraToFace);
                worldCanvas.transform.position = GetAnchorPosition(activeAnchor);
            }

            if (faceCamera && cameraToFace != null)
            {
                Vector3 toCamera = worldCanvas.transform.position - cameraToFace.transform.position;
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    worldCanvas.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }

                float distance = Vector3.Distance(worldCanvas.transform.position, cameraToFace.transform.position);
                float distanceScale = Mathf.Clamp(distance / Mathf.Max(0.01f, referenceDistance), minScale, maxScale);
                worldCanvas.transform.localScale = Vector3.one * baseWorldScale * distanceScale;
            }
        }

        private Transform ResolveAnchor(Camera cameraToFace)
        {
            if (!autoChooseSide)
            {
                return anchor != null ? anchor : leftAnchor;
            }

            Transform left = leftAnchor != null ? leftAnchor : anchor;
            Transform right = rightAnchor;

            if (left == null || right == null)
            {
                return left != null ? left : right;
            }

            bool leftBlocked = IsCandidateBlocked(left, cameraToFace);
            bool rightBlocked = IsCandidateBlocked(right, cameraToFace);

            if (leftBlocked && !rightBlocked)
            {
                ActiveSide = ChoiceSide.Right;
                return right;
            }

            if (rightBlocked && !leftBlocked)
            {
                ActiveSide = ChoiceSide.Left;
                return left;
            }

            if (cameraToFace == null)
            {
                ActiveSide = ChoiceSide.Left;
                return left;
            }

            float leftDistance = Vector3.Distance(cameraToFace.transform.position, GetAnchorPosition(left));
            float rightDistance = Vector3.Distance(cameraToFace.transform.position, GetAnchorPosition(right));
            bool chooseLeft = leftDistance <= rightDistance;
            ActiveSide = chooseLeft ? ChoiceSide.Left : ChoiceSide.Right;
            return chooseLeft ? left : right;
        }

        private bool IsCandidateBlocked(Transform candidate, Camera cameraToFace)
        {
            Vector3 position = GetAnchorPosition(candidate);
            Quaternion rotation = worldCanvas != null ? worldCanvas.transform.rotation : candidate.rotation;

            if (Physics.CheckBox(position, collisionProbeHalfExtents, rotation, obstructionMask, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            if (checkCameraLineOfSight && cameraToFace != null)
            {
                Vector3 origin = cameraToFace.transform.position;
                Vector3 direction = position - origin;
                if (Physics.Raycast(origin, direction.normalized, direction.magnitude, obstructionMask, QueryTriggerInteraction.Ignore))
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 GetAnchorPosition(Transform candidate)
        {
            return candidate != null ? candidate.TransformPoint(anchorOffset) : transform.position;
        }

        private void OnDrawGizmosSelected()
        {
            Transform gizmoAnchor = activeAnchor != null ? activeAnchor : (anchor != null ? anchor : leftAnchor);
            if (gizmoAnchor == null)
            {
                return;
            }

            Gizmos.color = new Color(0.7f, 0.75f, 1f, 0.9f);
            Vector3 worldPosition = GetAnchorPosition(gizmoAnchor);
            Gizmos.DrawWireSphere(worldPosition, 0.08f);
            Gizmos.DrawWireCube(worldPosition, collisionProbeHalfExtents * 2f);
            Gizmos.DrawLine(gizmoAnchor.position, worldPosition);
        }

        private void SetVisible(bool visible)
        {
            if (worldCanvas != null)
            {
                worldCanvas.enabled = visible;
            }

            if (graphicRaycaster != null)
            {
                graphicRaycaster.enabled = visible;
            }
        }

        private void CacheComponents()
        {
            if (worldCanvas == null) worldCanvas = GetComponentInChildren<Canvas>(true);
            if (choiceRoot == null && worldCanvas != null) choiceRoot = worldCanvas.GetComponent<RectTransform>();
            if (worldCanvas != null)
            {
                worldCanvas.renderMode = RenderMode.WorldSpace;
                worldCanvas.worldCamera = eventCamera != null ? eventCamera : Camera.main;
                if (graphicRaycaster == null) graphicRaycaster = worldCanvas.GetComponent<GraphicRaycaster>();
            }
        }
    }
}
