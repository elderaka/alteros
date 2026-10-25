using System;
using System.Globalization;
using AlterOS.Furniture;
using UnityEngine;

namespace Pleiades.Furniture
{
    /// <summary>Editor-authored placement frame for one room surface.</summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureSupportSurface : MonoBehaviour
    {
        [SerializeField] private string supportId = string.Empty;
        [SerializeField] private string supportKind = string.Empty;
        [SerializeField] private string frameRevision = "1";
        [SerializeField] private bool useRoomOriginFrame;
        [SerializeField] private Transform supportFrame;
        [SerializeField] private Collider surfaceCollider;

        public string SupportId { get { return supportId; } }
        public string SupportKind { get { return supportKind; } }
        public Collider SurfaceCollider { get { return surfaceCollider != null ? surfaceCollider : GetComponent<Collider>(); } }

        public bool TryCreateSnapshot(Transform roomRoot, out FurnitureSupportSnapshot snapshot)
        {
            snapshot = null;
            if (roomRoot == null || string.IsNullOrWhiteSpace(supportId) || string.IsNullOrWhiteSpace(supportKind)) return false;
            Transform frame = useRoomOriginFrame ? roomRoot : supportFrame != null ? supportFrame : transform;
            Vector3 position = roomRoot.InverseTransformPoint(frame.position);
            Quaternion rotation = Quaternion.Inverse(roomRoot.rotation) * frame.rotation;
            string revision = frameRevision ?? string.Empty;
            if (!useRoomOriginFrame)
            {
                revision += ":" + position.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    position.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                    position.z.ToString("R", CultureInfo.InvariantCulture) + ";" +
                    rotation.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    rotation.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                    rotation.z.ToString("R", CultureInfo.InvariantCulture) + "," +
                    rotation.w.ToString("R", CultureInfo.InvariantCulture);
            }
            snapshot = new FurnitureSupportSnapshot
            {
                supportId = supportId,
                supportKind = supportKind,
                revision = revision,
                positionMeters = new FurnitureVector3 { x = position.x, y = position.y, z = position.z },
                rotation = new FurnitureQuaternion { x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w }
            };
            return true;
        }
    }

}
