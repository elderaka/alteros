using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Furniture
{
    public sealed class FurnitureReservationResult
    {
        public bool success;
        public string reasonCode;
        public List<string> claimIds = new List<string>();
    }

    public sealed class FurnitureReservationSet
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, string> ownerByPoint = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> pointsByOwner = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public FurnitureReservationResult TryAcquire(string ownerId, ArrangementEvaluation arrangement, FurnitureWorldSnapshot current)
        {
            if (string.IsNullOrWhiteSpace(ownerId) || arrangement == null || current == null)
                return Failure("invalid_request");
            if (arrangement.state != "available")
                return Failure(arrangement.state == "unknown" ? "evidence_unknown" : "arrangement_unavailable");
            if (!RevisionsMatch(arrangement, current))
                return Failure("stale_revision");

            var requested = (arrangement.reservationPointRefs ?? new List<FurniturePointReference>())
                .Where(reference => reference != null && !string.IsNullOrWhiteSpace(reference.instanceId) && !string.IsNullOrWhiteSpace(reference.pointId))
                .Select(reference => reference.StableId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(pointId => pointId, StringComparer.Ordinal)
                .ToList();
            if (requested.Count != (arrangement.reservationPointRefs ?? new List<FurniturePointReference>()).Count)
                return Failure("invalid_point_reference");

            var pointSnapshots = new HashSet<string>((current.points ?? new List<FurnitureWorldPointSnapshot>())
                .Where(point => point != null && point.enabled)
                .Select(point => (point.instanceId ?? string.Empty) + "/" + (point.pointId ?? string.Empty)), StringComparer.Ordinal);
            if (requested.Any(pointId => !pointSnapshots.Contains(pointId)))
                return Failure("stale_revision");

            var externallyOccupied = new HashSet<string>((current.occupiedPointRefs ?? new List<FurniturePointReference>())
                .Where(reference => reference != null)
                .Select(reference => reference.StableId), StringComparer.Ordinal);
            lock (gate)
            {
                HashSet<string> existingOwnerPoints;
                if (pointsByOwner.TryGetValue(ownerId, out existingOwnerPoints))
                {
                    if (existingOwnerPoints.SetEquals(requested))
                        return Success(requested);
                    return Failure("owner_already_reserved");
                }

                foreach (var pointId in requested)
                {
                    if (externallyOccupied.Contains(pointId)) return Failure("point_occupied");
                    string existingOwner;
                    if (ownerByPoint.TryGetValue(pointId, out existingOwner) && existingOwner != ownerId)
                        return Failure("point_occupied");
                }

                if (requested.Count > 0)
                {
                    pointsByOwner.Add(ownerId, new HashSet<string>(requested, StringComparer.Ordinal));
                    foreach (var pointId in requested) ownerByPoint.Add(pointId, ownerId);
                }
                return Success(requested);
            }
        }

        public void Release(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) return;
            lock (gate)
            {
                HashSet<string> claimed;
                if (!pointsByOwner.TryGetValue(ownerId, out claimed)) return;
                foreach (var pointId in claimed)
                {
                    string currentOwner;
                    if (ownerByPoint.TryGetValue(pointId, out currentOwner) && currentOwner == ownerId)
                        ownerByPoint.Remove(pointId);
                }
                pointsByOwner.Remove(ownerId);
            }
        }

        private static bool RevisionsMatch(ArrangementEvaluation arrangement, FurnitureWorldSnapshot current)
        {
            foreach (var stamp in arrangement.revisionVector ?? new List<FurnitureRevisionStamp>())
            {
                if (stamp == null || string.IsNullOrEmpty(stamp.entityId)) return false;
                if (stamp.entityId == "catalogue")
                {
                    if (stamp.revision != current.catalogueHash) return false;
                    continue;
                }
                if (stamp.entityId.StartsWith("instance:", StringComparison.Ordinal))
                {
                    var id = stamp.entityId.Substring("instance:".Length);
                    var instance = (current.instances ?? new List<FurnitureInstanceSnapshot>()).FirstOrDefault(item => item != null && item.instanceId == id);
                    if (instance == null || instance.instanceRevision != stamp.revision) return false;
                    continue;
                }
                if (stamp.entityId.StartsWith("support:", StringComparison.Ordinal))
                {
                    var id = stamp.entityId.Substring("support:".Length);
                    var support = (current.supports ?? new List<FurnitureSupportSnapshot>()).FirstOrDefault(item => item != null && item.supportId == id);
                    if (support == null || support.revision != stamp.revision) return false;
                    continue;
                }
                if (stamp.entityId.StartsWith("point:", StringComparison.Ordinal))
                {
                    var id = stamp.entityId.Substring("point:".Length);
                    var point = (current.points ?? new List<FurnitureWorldPointSnapshot>()).FirstOrDefault(item => item != null && item.instanceId + "/" + item.pointId == id);
                    if (point == null || point.revision != stamp.revision || !point.enabled) return false;
                    continue;
                }
                if (stamp.entityId.StartsWith("rule:", StringComparison.Ordinal))
                {
                    if (stamp.revision != current.catalogueHash) return false;
                    continue;
                }
                return false;
            }
            return true;
        }

        private static FurnitureReservationResult Success(IEnumerable<string> claims)
        {
            return new FurnitureReservationResult { success = true, reasonCode = string.Empty, claimIds = claims.ToList() };
        }

        private static FurnitureReservationResult Failure(string reason)
        {
            return new FurnitureReservationResult { success = false, reasonCode = reason ?? string.Empty, claimIds = new List<string>() };
        }
    }
}
