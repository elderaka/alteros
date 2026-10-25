using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlterOS.Furniture
{
    public sealed class FurnitureQuaternion
    {
        public double x;
        public double y;
        public double z;
        public double w = 1d;
    }

    public sealed class FurniturePointReference
    {
        public string instanceId;
        public string pointId;

        public string StableId { get { return (instanceId ?? string.Empty) + "/" + (pointId ?? string.Empty); } }
    }

    public sealed class FurnitureRoleBinding
    {
        public string roleId;
        public string instanceId;
        public string pointId;
    }

    public sealed class FurnitureRevisionStamp
    {
        public string entityId;
        public string revision;
    }

    public sealed class FurnitureSupportSnapshot
    {
        public string supportId;
        public string supportKind;
        public string revision;
        public FurnitureVector3 positionMeters;
        public FurnitureQuaternion rotation;
    }

    public sealed class FurnitureInstanceSnapshot
    {
        public string instanceId;
        public string definitionId;
        public FurnitureVector3 positionMeters;
        public FurnitureQuaternion rotation;
        public FurnitureVector3 scale;
        public string supportId;
        public string instanceRevision;
        public bool enabled;
    }

    public sealed class FurnitureWorldPointSnapshot
    {
        public string instanceId;
        public string pointId;
        public string pointTypeId;
        public FurnitureVector3 localPosition;
        public FurnitureVector3 localFacing;
        public List<string> useIds = new List<string>();
        public string revision = "1";
        public bool enabled = true;
    }

    public sealed class FurniturePointAccessEvidence
    {
        public string instanceId;
        public string pointId;
        public string state;
        public bool pathClear;
        public double travelCostMeters;
    }

    public sealed class FurnitureWorldSnapshot
    {
        public string homeId;
        public string roomId;
        public string catalogueHash;
        public bool evidenceComplete;
        public List<FurnitureInstanceSnapshot> instances = new List<FurnitureInstanceSnapshot>();
        public List<FurnitureSupportSnapshot> supports = new List<FurnitureSupportSnapshot>();
        public List<FurnitureWorldPointSnapshot> points = new List<FurnitureWorldPointSnapshot>();
        public List<FurniturePointAccessEvidence> accessEvidence = new List<FurniturePointAccessEvidence>();
        public List<FurniturePointReference> occupiedPointRefs = new List<FurniturePointReference>();
    }

    public sealed class FurnitureArrangementRequest
    {
        public FurnitureCatalogueSnapshot catalogue;
        public FurnitureWorldSnapshot world;
        public string relationshipRuleId;
        public List<FurnitureRoleBinding> intendedRoleBindings;
    }

    public sealed class ArrangementEvaluation
    {
        public string state;
        public string arrangementId;
        public string relationshipRuleId;
        public List<FurnitureRoleBinding> roleBindings = new List<FurnitureRoleBinding>();
        public List<FurnitureRevisionStamp> revisionVector = new List<FurnitureRevisionStamp>();
        public string revisionToken;
        public string primaryReasonCode;
        public List<FurnitureDiagnostic> developerDiagnostics = new List<FurnitureDiagnostic>();
        public List<FurniturePointReference> reservationPointRefs = new List<FurniturePointReference>();
    }

    public static class FurnitureArrangementResolver
    {
        private const string SeatPointType = "seat.sit";
        private const string GeneralTablePointType = "table.surface";
        private const string Available = "available";
        private const string Unavailable = "unavailable";
        private const string Unknown = "unknown";

        private sealed class EvaluatedPoint
        {
            public FurnitureDefinition definition;
            public FurnitureInstanceSnapshot instance;
            public FurnitureWorldPointSnapshot point;
            public FurnitureSupportSnapshot support;
            public FurnitureVector3 worldPosition;
            public FurnitureVector3 worldFacing;
            public FurniturePointAccessEvidence access;
        }

        private sealed class PairCandidate
        {
            public EvaluatedPoint player;
            public EvaluatedPoint alterra;
            public double maximumFacingError;
            public double combinedFacingError;
            public double travelCost;
            public string stableKey;
        }

        private sealed class CandidateFailure
        {
            public string reason;
            public bool uncertain;
            public string stableKey;
        }

        public static ArrangementEvaluation Evaluate(FurnitureArrangementRequest request)
        {
            if (request == null || request.catalogue == null || request.world == null)
                return CreateFailure(Unknown, "evidence_unknown", request == null ? string.Empty : request.relationshipRuleId,
                    "request", "Arrangement request is incomplete.");

            var rule = request.catalogue.relationshipRules.FirstOrDefault(item => item.relationshipRuleId == request.relationshipRuleId);
            if (rule == null)
                return CreateFailure(Unknown, "evidence_unknown", request.relationshipRuleId,
                    "rule", "Relationship rule is missing from the catalogue snapshot.");

            if (request.world.catalogueHash != request.catalogue.catalogueHash)
                return CreateFailure(Unknown, "stale_revision", request.relationshipRuleId,
                    "catalogue", "World snapshot and catalogue hashes do not match.");

            if (rule.kind == "seatFacePair")
                return EvaluateSeatPair(request, rule);
            if (rule.kind == "seatPairTableContext")
                return EvaluateSeatTableContext(request, rule);

            return CreateFailure(Unknown, "evidence_unknown", request.relationshipRuleId,
                "rule", "This relationship rule kind has no resolver implementation in the current slice.");
        }

        private static List<FurnitureRevisionStamp> CaptureRevisionVector(
            FurnitureCatalogueSnapshot catalogue,
            FurnitureWorldSnapshot world,
            IEnumerable<EvaluatedPoint> points,
            FurnitureRelationshipRule rule)
        {
            var revisions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["catalogue"] = catalogue.catalogueHash,
                ["rule:" + rule.relationshipRuleId] = catalogue.catalogueHash
            };
            foreach (var point in points.Where(item => item != null))
            {
                revisions["instance:" + point.instance.instanceId] = point.instance.instanceRevision ?? string.Empty;
                revisions["support:" + point.support.supportId] = point.support.revision ?? string.Empty;
                revisions["point:" + point.point.instanceId + "/" + point.point.pointId] = point.point.revision ?? string.Empty;
            }
            return revisions.Select(pair => new FurnitureRevisionStamp { entityId = pair.Key, revision = pair.Value })
                .OrderBy(stamp => stamp.entityId, StringComparer.Ordinal)
                .ToList();
        }

        internal static string MakeRevisionToken(IEnumerable<FurnitureRevisionStamp> stamps)
        {
            return string.Join(";", (stamps ?? Enumerable.Empty<FurnitureRevisionStamp>())
                .OrderBy(stamp => stamp.entityId, StringComparer.Ordinal)
                .Select(stamp => (stamp.entityId ?? string.Empty) + "=" + (stamp.revision ?? string.Empty)));
        }

        private static ArrangementEvaluation EvaluateSeatPair(FurnitureArrangementRequest request, FurnitureRelationshipRule rule)
        {
            var points = BuildPoints(request, rule.pointTypeIds.FirstOrDefault(typeId => typeId == SeatPointType) ?? SeatPointType);
            var failures = new List<CandidateFailure>();
            var candidates = new List<PairCandidate>();
            for (var leftIndex = 0; leftIndex < points.Count; leftIndex++)
            {
                for (var rightIndex = 0; rightIndex < points.Count; rightIndex++)
                {
                    if (leftIndex == rightIndex || SamePoint(points[leftIndex], points[rightIndex])) continue;
                    var left = points[leftIndex];
                    var right = points[rightIndex];
                    var candidate = new PairCandidate { player = left, alterra = right };
                    if (!MatchesIntendedBindings(request.intendedRoleBindings, candidate.player, candidate.alterra, null)) continue;
                    var failure = ValidatePair(request, rule, candidate);
                    if (failure != null)
                    {
                        failures.Add(failure);
                        continue;
                    }
                    candidates.Add(Score(candidate));
                }
            }

            var selected = candidates.OrderBy(candidate => candidate.maximumFacingError)
                .ThenBy(candidate => candidate.combinedFacingError)
                .ThenBy(candidate => candidate.travelCost)
                .ThenBy(candidate => candidate.stableKey, StringComparer.Ordinal)
                .FirstOrDefault();
            if (selected == null)
            {
                if (failures.Any(failure => failure.uncertain) || (failures.Count == 0 && !request.world.evidenceComplete))
                    return CreateFailure(Unknown, "evidence_unknown", rule.relationshipRuleId, "access", "Required relationship evidence is incomplete.");
                var reason = failures.OrderBy(failure => FailureRank(failure.reason)).ThenBy(failure => failure.stableKey, StringComparer.Ordinal)
                    .Select(failure => failure.reason).FirstOrDefault() ?? "missing_compatible_point";
                return CreateFailure(Unavailable, reason, rule.relationshipRuleId, "candidates", "No usable face-to-face seat pair was found.");
            }

            return CreateAvailable(request, rule, new[] { selected.player, selected.alterra },
                new[] { Binding("player", selected.player), Binding("alterra", selected.alterra) },
                new[] { Reference(selected.player), Reference(selected.alterra) });
        }

        private static ArrangementEvaluation EvaluateSeatTableContext(FurnitureArrangementRequest request, FurnitureRelationshipRule rule)
        {
            var seatRule = request.catalogue.relationshipRules.FirstOrDefault(item => item.kind == "seatFacePair");
            if (seatRule == null)
                return CreateFailure(Unknown, "evidence_unknown", rule.relationshipRuleId, "rule", "Seat-pair rule is not available.");
            var seatRequest = new FurnitureArrangementRequest
            {
                catalogue = request.catalogue,
                world = request.world,
                relationshipRuleId = seatRule.relationshipRuleId,
                intendedRoleBindings = request.intendedRoleBindings == null ? null : request.intendedRoleBindings.Where(binding => binding.roleId != "table").ToList()
            };
            var pair = EvaluateSeatPair(seatRequest, seatRule);
            if (pair.state != Available) return CopyWithRule(pair, rule.relationshipRuleId);

            var playerBinding = pair.roleBindings.Single(binding => binding.roleId == "player");
            var alterraBinding = pair.roleBindings.Single(binding => binding.roleId == "alterra");
            var player = FindEvaluatedPoint(request, playerBinding.instanceId, playerBinding.pointId);
            var alterra = FindEvaluatedPoint(request, alterraBinding.instanceId, alterraBinding.pointId);
            var surfaces = BuildPoints(request, GeneralTablePointType);
            var failures = new List<CandidateFailure>();
            var tables = new List<EvaluatedPoint>();
            foreach (var table in surfaces)
            {
                var failure = ValidateTableBetween(rule, player, alterra, table, request.world.evidenceComplete);
                if (failure == null && MatchesIntendedTable(request.intendedRoleBindings, table))
                    tables.Add(table);
                else if (failure != null)
                    failures.Add(failure);
            }

            var selected = tables.OrderBy(table => Distance(table.worldPosition, Midpoint(player.worldPosition, alterra.worldPosition)))
                .ThenBy(table => table.instance.instanceId, StringComparer.Ordinal)
                .ThenBy(table => table.point.pointId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (selected == null)
            {
                if (failures.Any(failure => failure.uncertain) || (failures.Count == 0 && !request.world.evidenceComplete))
                    return CreateFailure(Unknown, "evidence_unknown", rule.relationshipRuleId, "table", "Required table/access evidence is incomplete.");
                return CreateFailure(Unavailable, "missing_compatible_point", rule.relationshipRuleId, "table", "No usable table is associated with these seats.");
            }

            var used = new[] { player, alterra, selected };
            var bindings = new[] { Binding("player", player), Binding("alterra", alterra), Binding("table", selected) };
            return CreateAvailable(request, rule, used, bindings, new[] { Reference(player), Reference(alterra) });
        }

        private static List<EvaluatedPoint> BuildPoints(FurnitureArrangementRequest request, string pointTypeId)
        {
            var definitions = request.catalogue.definitions.ToDictionary(definition => definition.definitionId, StringComparer.Ordinal);
            var supports = request.world.supports.ToDictionary(support => support.supportId, StringComparer.Ordinal);
            var instances = request.world.instances.Where(instance => instance != null && instance.enabled)
                .ToDictionary(instance => instance.instanceId, StringComparer.Ordinal);
            var result = new List<EvaluatedPoint>();
            foreach (var point in request.world.points.Where(item => item != null && item.enabled && item.pointTypeId == pointTypeId)
                .OrderBy(item => item.instanceId, StringComparer.Ordinal).ThenBy(item => item.pointId, StringComparer.Ordinal))
            {
                FurnitureInstanceSnapshot instance;
                FurnitureDefinition definition;
                if (!instances.TryGetValue(point.instanceId ?? string.Empty, out instance) ||
                    !definitions.TryGetValue(instance.definitionId ?? string.Empty, out definition)) continue;
                var template = (definition.points ?? new List<FurnitureFunctionalPoint>())
                    .FirstOrDefault(item => item.pointId == point.pointId && item.pointTypeId == point.pointTypeId);
                if (template == null || point.localPosition == null || point.localFacing == null) continue;
                FurnitureSupportSnapshot support;
                if (!supports.TryGetValue(instance.supportId ?? string.Empty, out support))
                {
                    result.Add(new EvaluatedPoint { definition = definition, instance = instance, point = point });
                    continue;
                }
                var access = request.world.accessEvidence.FirstOrDefault(item => item.instanceId == point.instanceId && item.pointId == point.pointId);
                result.Add(new EvaluatedPoint
                {
                    definition = definition,
                    instance = instance,
                    point = point,
                    support = support,
                    worldPosition = TransformPoint(instance, point.localPosition),
                    worldFacing = Rotate(instance.rotation, point.localFacing),
                    access = access
                });
            }
            return result;
        }

        private static CandidateFailure ValidatePair(FurnitureArrangementRequest request, FurnitureRelationshipRule rule, PairCandidate pair)
        {
            var supportFailure = ValidateSupport(pair.player);
            if (supportFailure != null) return supportFailure;
            supportFailure = ValidateSupport(pair.alterra);
            if (supportFailure != null) return supportFailure;
            if (pair.player.support.supportId != pair.alterra.support.supportId)
                return Failure("support_mismatch", pair.player.point.instanceId + "/" + pair.alterra.point.instanceId);
            var distance = Distance(pair.player.worldPosition, pair.alterra.worldPosition);
            if (distance < rule.minimumDistanceMeters || distance > rule.maximumDistanceMeters || distance > rule.broadPhaseRadiusMeters)
                return Failure("missing_compatible_point", pair.player.point.instanceId + "/" + pair.alterra.point.instanceId);
            var towardAlterra = Normalize(Subtract(pair.alterra.worldPosition, pair.player.worldPosition));
            var towardPlayer = Normalize(Subtract(pair.player.worldPosition, pair.alterra.worldPosition));
            var errorPlayer = AngleDegrees(pair.player.worldFacing, towardAlterra);
            var errorAlterra = AngleDegrees(pair.alterra.worldFacing, towardPlayer);
            if (!IsFinite(errorPlayer) || !IsFinite(errorAlterra) ||
                errorPlayer > rule.maximumFacingErrorDegrees || errorAlterra > rule.maximumFacingErrorDegrees)
                return Failure("wrong_facing", pair.player.point.instanceId + "/" + pair.alterra.point.instanceId);
            if (Math.Abs(pair.player.worldPosition.y - pair.alterra.worldPosition.y) > rule.maximumHeightDifferenceMeters)
                return Failure("height_mismatch", pair.player.point.instanceId + "/" + pair.alterra.point.instanceId);
            var accessFailure = ValidateAccess(request.world, pair.player);
            return accessFailure ?? ValidateAccess(request.world, pair.alterra);
        }

        private static CandidateFailure ValidateTableBetween(FurnitureRelationshipRule rule, EvaluatedPoint player,
            EvaluatedPoint alterra, EvaluatedPoint table, bool evidenceComplete)
        {
            var supportFailure = ValidateSupport(table);
            if (supportFailure != null) return supportFailure;
            if (table.support.supportId != player.support.supportId || table.support.supportId != alterra.support.supportId)
                return Failure("support_mismatch", table.point.instanceId);
            var ab = new FurnitureVector3 { x = alterra.worldPosition.x - player.worldPosition.x, y = 0, z = alterra.worldPosition.z - player.worldPosition.z };
            var ap = new FurnitureVector3 { x = table.worldPosition.x - player.worldPosition.x, y = 0, z = table.worldPosition.z - player.worldPosition.z };
            var denominator = Dot(ab, ab);
            if (denominator <= 1e-12d) return Failure("missing_compatible_point", table.point.instanceId);
            var projection = Dot(ap, ab) / denominator;
            var closest = new FurnitureVector3 { x = player.worldPosition.x + ab.x * projection, y = table.worldPosition.y, z = player.worldPosition.z + ab.z * projection };
            var lateralError = Distance(new FurnitureVector3 { x = closest.x, y = 0, z = closest.z },
                new FurnitureVector3 { x = table.worldPosition.x, y = 0, z = table.worldPosition.z });
            if (projection < 0d || projection > 1d || lateralError > rule.lateralAllowanceMeters)
                return Failure("missing_compatible_point", table.point.instanceId);
            if (Distance(table.worldPosition, player.worldPosition) > rule.maximumDistanceMeters ||
                Distance(table.worldPosition, alterra.worldPosition) > rule.maximumDistanceMeters)
                return Failure("missing_compatible_point", table.point.instanceId);
            var seatHeight = (player.worldPosition.y + alterra.worldPosition.y) * 0.5d;
            if (Math.Abs(table.worldPosition.y - seatHeight) > rule.maximumHeightDifferenceMeters)
                return Failure("height_mismatch", table.point.instanceId);
            return ValidateAccess(new FurnitureWorldSnapshot { evidenceComplete = evidenceComplete, accessEvidence = table.access == null ? new List<FurniturePointAccessEvidence>() : new List<FurniturePointAccessEvidence> { table.access } }, table);
        }

        private static CandidateFailure ValidateSupport(EvaluatedPoint point)
        {
            if (point == null || point.support == null)
                return Failure("unsupported_support", point == null || point.point == null ? string.Empty : point.point.instanceId);
            if (!point.definition.supportKinds.Contains(point.support.supportKind))
                return Failure("unsupported_support", point.point.instanceId);
            return null;
        }

        private static CandidateFailure ValidateAccess(FurnitureWorldSnapshot world, EvaluatedPoint point)
        {
            if (point.access == null)
                return world.evidenceComplete ? Failure("blocked_access", point.point.instanceId + "/" + point.point.pointId)
                    : Uncertain("evidence_unknown", point.point.instanceId + "/" + point.point.pointId);
            if (point.access.state == "unknown")
                return Uncertain("evidence_unknown", point.point.instanceId + "/" + point.point.pointId);
            if (point.access.state != "reachable" || !point.access.pathClear)
                return Failure("blocked_access", point.point.instanceId + "/" + point.point.pointId);
            if (!IsFinite(point.access.travelCostMeters) || point.access.travelCostMeters < 0d)
                return Uncertain("evidence_unknown", point.point.instanceId + "/" + point.point.pointId);
            return null;
        }

        private static EvaluatedPoint FindEvaluatedPoint(FurnitureArrangementRequest request, string instanceId, string pointId)
        {
            var point = request.world.points.First(item => item.instanceId == instanceId && item.pointId == pointId);
            return BuildPoints(request, point.pointTypeId).First(item => item.point.instanceId == instanceId && item.point.pointId == pointId);
        }

        private static bool MatchesIntendedBindings(IList<FurnitureRoleBinding> intended, EvaluatedPoint player, EvaluatedPoint alterra, EvaluatedPoint table)
        {
            if (intended == null || intended.Count == 0) return true;
            return IntendedMatches(intended, "player", player) && IntendedMatches(intended, "alterra", alterra) &&
                (table == null || IntendedMatches(intended, "table", table));
        }

        private static bool MatchesIntendedTable(IList<FurnitureRoleBinding> intended, EvaluatedPoint table)
        {
            return intended == null || !intended.Any(binding => binding.roleId == "table") || IntendedMatches(intended, "table", table);
        }

        private static bool IntendedMatches(IList<FurnitureRoleBinding> intended, string roleId, EvaluatedPoint point)
        {
            var binding = intended.FirstOrDefault(item => item.roleId == roleId);
            return binding == null || (binding.instanceId == point.instance.instanceId && binding.pointId == point.point.pointId);
        }

        private static PairCandidate Score(PairCandidate candidate)
        {
            var directionToAlterra = Normalize(Subtract(candidate.alterra.worldPosition, candidate.player.worldPosition));
            var directionToPlayer = Normalize(Subtract(candidate.player.worldPosition, candidate.alterra.worldPosition));
            var playerError = AngleDegrees(candidate.player.worldFacing, directionToAlterra);
            var alterraError = AngleDegrees(candidate.alterra.worldFacing, directionToPlayer);
            candidate.maximumFacingError = Math.Max(playerError, alterraError);
            candidate.combinedFacingError = playerError + alterraError;
            candidate.travelCost = candidate.player.access.travelCostMeters + candidate.alterra.access.travelCostMeters;
            candidate.stableKey = candidate.player.point.instanceId + "/" + candidate.player.point.pointId + "|" +
                candidate.alterra.point.instanceId + "/" + candidate.alterra.point.pointId;
            return candidate;
        }

        private static ArrangementEvaluation CreateAvailable(FurnitureArrangementRequest request, FurnitureRelationshipRule rule,
            IEnumerable<EvaluatedPoint> usedPoints, IEnumerable<FurnitureRoleBinding> roleBindings,
            IEnumerable<FurniturePointReference> reservationPoints)
        {
            var used = usedPoints.ToList();
            var bindings = roleBindings.OrderBy(binding => binding.roleId, StringComparer.Ordinal).ToList();
            var arrangementId = "arrangement:" + rule.relationshipRuleId + "|" +
                string.Join("|", bindings.Select(binding => binding.roleId + "=" + binding.instanceId + "/" + binding.pointId));
            var revisions = CaptureRevisionVector(request.catalogue, request.world, used, rule);
            return new ArrangementEvaluation
            {
                state = Available,
                arrangementId = arrangementId,
                relationshipRuleId = rule.relationshipRuleId,
                roleBindings = bindings,
                revisionVector = revisions,
                revisionToken = MakeRevisionToken(revisions),
                reservationPointRefs = reservationPoints.OrderBy(point => point.instanceId, StringComparer.Ordinal)
                    .ThenBy(point => point.pointId, StringComparer.Ordinal).ToList(),
                developerDiagnostics = new List<FurnitureDiagnostic>()
            };
        }

        private static ArrangementEvaluation CreateFailure(string state, string reason, string ruleId, string path, string message)
        {
            return new ArrangementEvaluation
            {
                state = state,
                relationshipRuleId = ruleId ?? string.Empty,
                primaryReasonCode = reason,
                developerDiagnostics = new List<FurnitureDiagnostic> { new FurnitureDiagnostic(reason, "info", path, message) },
                roleBindings = new List<FurnitureRoleBinding>(),
                revisionVector = new List<FurnitureRevisionStamp>(),
                reservationPointRefs = new List<FurniturePointReference>()
            };
        }

        private static ArrangementEvaluation CopyWithRule(ArrangementEvaluation source, string ruleId)
        {
            return new ArrangementEvaluation
            {
                state = source.state,
                arrangementId = source.arrangementId,
                relationshipRuleId = ruleId,
                roleBindings = source.roleBindings,
                revisionVector = source.revisionVector,
                revisionToken = source.revisionToken,
                primaryReasonCode = source.primaryReasonCode,
                developerDiagnostics = source.developerDiagnostics,
                reservationPointRefs = source.reservationPointRefs
            };
        }

        private static FurnitureRoleBinding Binding(string roleId, EvaluatedPoint point)
        {
            return new FurnitureRoleBinding { roleId = roleId, instanceId = point.instance.instanceId, pointId = point.point.pointId };
        }

        private static FurniturePointReference Reference(EvaluatedPoint point)
        {
            return new FurniturePointReference { instanceId = point.instance.instanceId, pointId = point.point.pointId };
        }

        private static bool SamePoint(EvaluatedPoint left, EvaluatedPoint right)
        {
            return left.point.instanceId == right.point.instanceId && left.point.pointId == right.point.pointId;
        }

        private static CandidateFailure Failure(string reason, string stableKey)
        {
            return new CandidateFailure { reason = reason, stableKey = stableKey ?? string.Empty };
        }

        private static CandidateFailure Uncertain(string reason, string stableKey)
        {
            return new CandidateFailure { reason = reason, stableKey = stableKey ?? string.Empty, uncertain = true };
        }

        private static int FailureRank(string reason)
        {
            switch (reason)
            {
                case "unsupported_support": return 0;
                case "support_mismatch": return 1;
                case "wrong_facing": return 2;
                case "height_mismatch": return 3;
                case "blocked_access": return 4;
                default: return 5;
            }
        }

        private static FurnitureVector3 TransformPoint(FurnitureInstanceSnapshot instance, FurnitureVector3 local)
        {
            var scale = instance.scale ?? new FurnitureVector3 { x = 1d, y = 1d, z = 1d };
            var scaled = new FurnitureVector3 { x = local.x * scale.x, y = local.y * scale.y, z = local.z * scale.z };
            var rotated = Rotate(instance.rotation, scaled);
            return new FurnitureVector3
            {
                x = instance.positionMeters.x + rotated.x,
                y = instance.positionMeters.y + rotated.y,
                z = instance.positionMeters.z + rotated.z
            };
        }

        private static FurnitureVector3 Rotate(FurnitureQuaternion quaternion, FurnitureVector3 value)
        {
            var q = Normalize(quaternion);
            var u = new FurnitureVector3 { x = q.x, y = q.y, z = q.z };
            var uv = Cross(u, value);
            var uuv = Cross(u, uv);
            return new FurnitureVector3
            {
                x = value.x + 2d * (q.w * uv.x + uuv.x),
                y = value.y + 2d * (q.w * uv.y + uuv.y),
                z = value.z + 2d * (q.w * uv.z + uuv.z)
            };
        }

        private static FurnitureQuaternion Normalize(FurnitureQuaternion quaternion)
        {
            if (quaternion == null) return new FurnitureQuaternion { x = double.NaN, y = double.NaN, z = double.NaN, w = double.NaN };
            var length = Math.Sqrt(quaternion.x * quaternion.x + quaternion.y * quaternion.y + quaternion.z * quaternion.z + quaternion.w * quaternion.w);
            if (!IsFinite(length) || length <= 1e-12d) return new FurnitureQuaternion { x = double.NaN, y = double.NaN, z = double.NaN, w = double.NaN };
            return new FurnitureQuaternion { x = quaternion.x / length, y = quaternion.y / length, z = quaternion.z / length, w = quaternion.w / length };
        }

        private static FurnitureVector3 Normalize(FurnitureVector3 vector)
        {
            var length = Math.Sqrt(Dot(vector, vector));
            if (!IsFinite(length) || length <= 1e-12d) return new FurnitureVector3 { x = double.NaN, y = double.NaN, z = double.NaN };
            return new FurnitureVector3 { x = vector.x / length, y = vector.y / length, z = vector.z / length };
        }

        private static double AngleDegrees(FurnitureVector3 left, FurnitureVector3 right)
        {
            var a = Normalize(left);
            var b = Normalize(right);
            var dot = Math.Max(-1d, Math.Min(1d, Dot(a, b)));
            return Math.Acos(dot) * 180d / Math.PI;
        }

        private static FurnitureVector3 Subtract(FurnitureVector3 left, FurnitureVector3 right)
        {
            return new FurnitureVector3 { x = left.x - right.x, y = left.y - right.y, z = left.z - right.z };
        }

        private static FurnitureVector3 Midpoint(FurnitureVector3 left, FurnitureVector3 right)
        {
            return new FurnitureVector3 { x = (left.x + right.x) * 0.5d, y = (left.y + right.y) * 0.5d, z = (left.z + right.z) * 0.5d };
        }

        private static FurnitureVector3 Cross(FurnitureVector3 left, FurnitureVector3 right)
        {
            return new FurnitureVector3
            {
                x = left.y * right.z - left.z * right.y,
                y = left.z * right.x - left.x * right.z,
                z = left.x * right.y - left.y * right.x
            };
        }

        private static double Dot(FurnitureVector3 left, FurnitureVector3 right)
        {
            return left.x * right.x + left.y * right.y + left.z * right.z;
        }

        private static double Distance(FurnitureVector3 left, FurnitureVector3 right)
        {
            var dx = left.x - right.x;
            var dy = left.y - right.y;
            var dz = left.z - right.z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
