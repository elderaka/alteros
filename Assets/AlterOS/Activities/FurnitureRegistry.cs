using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Activities
{
    public sealed class FurnitureEditClaim
    {
        public readonly string usePointId;
        public readonly string ownerId;

        internal FurnitureEditClaim(string pointId, string owner)
        {
            usePointId = pointId;
            ownerId = owner;
        }
    }

    public sealed class FurnitureBinding
    {
        public readonly string instanceId;
        public readonly string usePointId;
        public readonly string activityId;
        public readonly string useId;
        public readonly FurnitureUsePoint point;
        internal readonly int revision;
        public int CapturedRevision { get { return revision; } }

        internal FurnitureBinding(FurnitureUsePoint source, string owner, string requestedUse)
        {
            point = source;
            instanceId = source.InstanceId;
            usePointId = source.UsePointId;
            activityId = owner;
            useId = requestedUse;
            revision = source.Revision;
        }
    }

    /// <summary>Deterministic point discovery and per-use-point claims for currently registered furniture.</summary>
    public sealed class FurnitureRegistry
    {
        private sealed class ResourceClaim
        {
            public string ownerId;
            public FurnitureBinding activityBinding;
            public FurnitureEditClaim editClaim;
        }

        private readonly Dictionary<string, FurnitureUsePoint> points = new Dictionary<string, FurnitureUsePoint>(StringComparer.Ordinal);
        private readonly Dictionary<string, ResourceClaim> claims = new Dictionary<string, ResourceClaim>(StringComparer.Ordinal);
        private readonly Func<FurnitureUsePoint, bool> reachable;
        private readonly Func<FurnitureUsePoint, float> distance;

        public FurnitureRegistry(IEnumerable<FurnitureUsePoint> registeredPoints,
            Func<FurnitureUsePoint, bool> isReachable, Func<FurnitureUsePoint, float> distanceToPoint)
        {
            if (registeredPoints == null) throw new ArgumentNullException("registeredPoints");
            reachable = isReachable ?? (_ => true);
            distance = distanceToPoint ?? (_ => 0f);
            foreach (FurnitureUsePoint point in registeredPoints)
            {
                if (point == null || string.IsNullOrWhiteSpace(point.UsePointId) || points.ContainsKey(point.UsePointId))
                    throw new ArgumentException("Furniture use-point IDs must be non-empty and unique.", "registeredPoints");
                points.Add(point.UsePointId, point);
            }
        }

        public bool TryReserve(string useId, string requestedObjectId, string activityId, out FurnitureBinding binding)
        { return TryReserve(useId, requestedObjectId, activityId, null, out binding); }

        public bool TryReserve(string useId, string requestedObjectId, string activityId,
            Func<FurnitureUsePoint, bool> additionalEligibility, out FurnitureBinding binding)
        {
            binding = null;
            if (string.IsNullOrWhiteSpace(useId) || string.IsNullOrWhiteSpace(activityId)) return false;
            IEnumerable<FurnitureUsePoint> candidates = points.Values.Where(point => point.Supports(useId) &&
                (string.IsNullOrWhiteSpace(requestedObjectId) || requestedObjectId == "available_dispenser" ||
                 requestedObjectId == point.InstanceId || requestedObjectId == point.UsePointId) &&
                !claims.ContainsKey(point.UsePointId) && reachable(point) &&
                (additionalEligibility == null || additionalEligibility(point)));

            FurnitureUsePoint selected = candidates
                .OrderBy(distance)
                .ThenBy(point => point.InstanceId, StringComparer.Ordinal)
                .ThenBy(point => point.UsePointId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (selected == null) return false;
            binding = new FurnitureBinding(selected, activityId, useId);
            claims.Add(selected.UsePointId, new ResourceClaim { ownerId = activityId, activityBinding = binding });
            return true;
        }

        public bool TryClaimForEdit(FurnitureUsePoint point, string ownerId, out FurnitureEditClaim claim)
        {
            claim = null;
            if (point == null || !point.isActiveAndEnabled || string.IsNullOrWhiteSpace(ownerId) ||
                string.IsNullOrWhiteSpace(point.UsePointId)) return false;
            FurnitureUsePoint registered;
            if (!points.TryGetValue(point.UsePointId, out registered) || !ReferenceEquals(registered, point) ||
                claims.ContainsKey(point.UsePointId)) return false;

            claim = new FurnitureEditClaim(point.UsePointId, ownerId);
            claims.Add(point.UsePointId, new ResourceClaim { ownerId = ownerId, editClaim = claim });
            return true;
        }

        public bool IsClaimed(FurnitureUsePoint point)
        {
            FurnitureUsePoint registered;
            return point != null && !string.IsNullOrWhiteSpace(point.UsePointId) &&
                points.TryGetValue(point.UsePointId, out registered) && ReferenceEquals(registered, point) &&
                claims.ContainsKey(point.UsePointId);
        }

        public bool ReleaseEditClaim(FurnitureEditClaim claim)
        {
            if (claim == null) return false;
            ResourceClaim current;
            if (!claims.TryGetValue(claim.usePointId, out current) || !ReferenceEquals(current.editClaim, claim))
                return false;
            claims.Remove(claim.usePointId);
            return true;
        }

        public bool IsValid(FurnitureBinding binding)
        {
            if (binding == null || binding.point == null) return false;
            ResourceClaim claim;
            return points.ContainsKey(binding.usePointId) && ReferenceEquals(points[binding.usePointId], binding.point) &&
                claims.TryGetValue(binding.usePointId, out claim) && ReferenceEquals(claim.activityBinding, binding) &&
                binding.point.InstanceId == binding.instanceId && binding.point.UsePointId == binding.usePointId &&
                binding.point.IsSameRevision(binding.revision) && binding.point.Supports(binding.useId);
        }

        public void Release(string activityId)
        {
            if (string.IsNullOrWhiteSpace(activityId)) return;
            foreach (string usePointId in claims.Where(item => item.Value.ownerId == activityId).Select(item => item.Key).ToArray())
                claims.Remove(usePointId);
        }
    }
}
