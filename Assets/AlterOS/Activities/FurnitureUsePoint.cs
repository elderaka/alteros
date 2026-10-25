using System;
using UnityEngine;

namespace AlterOS.Activities
{
    /// <summary>Authored functional point on one furniture instance; occupancy is registry-owned.</summary>
    public sealed class FurnitureUsePoint : MonoBehaviour
    {
        [SerializeField] private string instanceId = "";
        [SerializeField] private string usePointId = "";
        [SerializeField] private string[] supportedUseIds = new string[0];
        [SerializeField] private Transform approach;
        [SerializeField] private Transform pose;
        [SerializeField] private Transform use;
        [SerializeField] private Transform exit;
        private int revision;
        private string lastSignature = "";

        public string InstanceId { get { return instanceId; } }
        public string UsePointId { get { return usePointId; } }
        public Transform Approach { get { return approach; } }
        public Transform Pose { get { return pose; } }
        public Transform Use { get { return use; } }
        public Transform Exit { get { return exit; } }
        public int Revision { get { RefreshRevision(); return revision; } }

        public bool Supports(string capabilityId)
        {
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(usePointId) ||
                string.IsNullOrWhiteSpace(capabilityId) || !IsUsable(approach) || !IsUsable(pose) ||
                !IsUsable(use) || !IsUsable(exit) ||
                supportedUseIds == null) return false;
            foreach (string item in supportedUseIds)
                if (string.Equals(item, capabilityId, StringComparison.Ordinal)) return true;
            return false;
        }

        public bool IsSameRevision(int expected)
        {
            RefreshRevision();
            return isActiveAndEnabled && revision == expected;
        }

        private void OnEnable() { RefreshRevision(); }
        private void OnValidate() { RefreshRevision(); }

        private void RefreshRevision()
        {
            string signature = BuildSignature();
            if (signature == lastSignature) return;
            lastSignature = signature;
            unchecked { revision++; }
        }

        private static bool IsUsable(Transform point)
        {
            return point != null && point.gameObject.activeInHierarchy;
        }

        private string BuildSignature()
        {
            return string.Concat(instanceId, "|", usePointId, "|", isActiveAndEnabled, "|",
                TransformSignature(transform), "|",
                TransformSignature(approach), "|", TransformSignature(pose), "|", TransformSignature(use), "|",
                TransformSignature(exit), "|", supportedUseIds == null ? "null" : string.Join(",", supportedUseIds));
        }

        private static string TransformSignature(Transform point)
        {
            if (point == null) return "null";
            Vector3 p = point.position;
            Quaternion r = point.rotation;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:R},{1:R},{2:R};{3:R},{4:R},{5:R},{6:R}", p.x, p.y, p.z, r.x, r.y, r.z, r.w);
        }
    }
}
