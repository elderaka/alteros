using AlterOS.Furniture;
using TMPro;
using UnityEngine;

namespace Pleiades.Furniture
{
    /// <summary>Binds placement evidence to the Editor-authored ghost and floor footprint.</summary>
    public sealed class FurniturePlacementPreview : MonoBehaviour
    {
        [SerializeField] private Transform furnitureShape;
        [SerializeField] private Transform footprint;
        [SerializeField] private Renderer[] previewRenderers;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private Material validMaterial;
        [SerializeField] private Material invalidMaterial;

        public void Show(FurnitureDefinition definition, Vector3 localPosition, Quaternion localRotation,
            bool valid, string reason, string supportKind)
        {
            if (definition == null || definition.localBounds == null || definition.localBounds.minimum == null || definition.localBounds.maximum == null)
            {
                Hide();
                return;
            }

            Vector3 minimum = ToVector(definition.localBounds.minimum);
            Vector3 maximum = ToVector(definition.localBounds.maximum);
            Vector3 size = maximum - minimum;
            Vector3 center = (minimum + maximum) * 0.5f;
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            if (furnitureShape != null)
            {
                furnitureShape.localPosition = center;
                furnitureShape.localRotation = Quaternion.identity;
                furnitureShape.localScale = size;
            }
            if (footprint != null)
            {
                bool floor = supportKind == "roomFloor";
                footprint.gameObject.SetActive(floor);
                footprint.localPosition = new Vector3(center.x, minimum.y + 0.005f, center.z);
                footprint.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                footprint.localScale = new Vector3(size.x, size.z, 1f);
            }

            Material material = valid ? validMaterial : invalidMaterial;
            if (material != null)
                foreach (Renderer renderer in previewRenderers ?? new Renderer[0])
                    if (renderer != null) renderer.sharedMaterial = material;

            if (statusLabel != null)
                statusLabel.text = valid ? "Valid placement  •  Q / click to place" : FriendlyReason(reason);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private static Vector3 ToVector(FurnitureVector3 value)
        {
            return new Vector3((float)value.x, (float)value.y, (float)value.z);
        }

        private static string FriendlyReason(string reason)
        {
            switch (reason)
            {
                case "outside_room": return "This spot is outside the room.";
                case "structural_overlap": return "This spot overlaps another piece.";
                case "world_collision": return "This spot is blocked.";
                case "unsupported_support": return "This furniture cannot use that surface.";
                case "stale_support": return "The surface changed. Aim again.";
                case "point_in_use": return "Alterra is using this furniture.";
                case "has_dependents": return "Move attached furniture first.";
                case "evidence_unknown": return "Placement cannot be checked here.";
                default: return "This spot cannot be used.";
            }
        }
    }
}
