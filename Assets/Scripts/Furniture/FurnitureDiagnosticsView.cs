using System.Collections.Generic;
using System.Linq;
using AlterOS.Debugging;
using AlterOS.Furniture;
using Pleiades.Player;
using UnityEngine;
using UnityEngine.AI;

namespace Pleiades.Furniture
{
    /// <summary>Debug-only cones and pulsing outlines derived from the furniture arrangement resolver.</summary>
    public sealed class FurnitureDiagnosticsView : MonoBehaviour
    {
        [SerializeField] private Material lineMaterial;
        [SerializeField] private float refreshInterval = 0.35f;
        [SerializeField] private int coneSegments = 10;
        [SerializeField] private float lineWidth = 0.012f;

        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private FurnitureHandlingController owner;
        private FurnitureHandlingSession session;
        private FurnitureCatalogueSnapshot catalogue;
        private Transform roomRoot;
        private PlayerController player;
        private DebugModeState debugMode;
        private float nextRefresh;
        private float pulse;
        private string lastSignature = string.Empty;
        private readonly HashSet<LineRenderer> highlights = new HashSet<LineRenderer>();

        public void BindDebugModeState(DebugModeState sharedState)
        {
            if (debugMode == sharedState) return;
            if (debugMode != null) debugMode.Changed -= OnDebugModeChanged;
            debugMode = sharedState;
            if (isActiveAndEnabled && debugMode != null) debugMode.Changed += OnDebugModeChanged;
            lastSignature = string.Empty;
            if (debugMode == null || !debugMode.IsEnabled) ClearLines();
        }

        public void Bind(FurnitureHandlingController handling, FurnitureHandlingSession furnitureSession,
            FurnitureCatalogueSnapshot snapshot, Transform frame, PlayerController actor)
        {
            owner = handling;
            session = furnitureSession;
            catalogue = snapshot;
            roomRoot = frame;
            player = actor;
            lastSignature = string.Empty;
        }

        private void OnEnable()
        {
            if (debugMode != null) debugMode.Changed += OnDebugModeChanged;
        }

        private void OnDisable()
        {
            if (debugMode != null) debugMode.Changed -= OnDebugModeChanged;
            ClearLines();
        }

        private void LateUpdate()
        {
            if (debugMode == null || !debugMode.IsEnabled || owner == null || catalogue == null || roomRoot == null)
            {
                ClearLines();
                return;
            }
            pulse = (Mathf.Sin(Time.unscaledTime * 4f) + 1f) * 0.5f;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
                string signature = BuildSignature();
                if (signature != lastSignature)
                {
                    lastSignature = signature;
                    RebuildDiagnostics();
                }
            }
            UpdateHighlightPulse();
        }

        private void OnDebugModeChanged(bool enabled)
        {
            lastSignature = string.Empty;
            if (!enabled) ClearLines();
        }

        private string BuildSignature()
        {
            HomeFurnitureLayoutDocument document = owner.CurrentHomeDocument;
            FurnitureHandlingView view = session == null ? null : session.Current;
            string pose = view == null || view.pose == null ? string.Empty :
                view.pose.positionMeters.x + "," + view.pose.positionMeters.y + "," + view.pose.positionMeters.z + ";" +
                view.pose.rotation.x + "," + view.pose.rotation.y + "," + view.pose.rotation.z + "," + view.pose.rotation.w;
            return (document == null ? "missing" : document.layoutRevision.ToString()) + "|" +
                (view == null ? string.Empty : view.state + ":" + view.instanceId + ":" + view.definitionId) + "|" + pose + "|" +
                catalogue.catalogueHash;
        }

        private void RebuildDiagnostics()
        {
            ClearLines();
            HomeFurnitureLayoutDocument document = owner.CurrentHomeDocument;
            if (document == null || document.instances == null) return;
            FurniturePlacementEvidence placement = owner.CreateDiagnosticEvidence();
            FurnitureWorldSnapshot world = BuildWorld(document, placement);
            var participants = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (FurnitureRelationshipRule rule in catalogue.relationshipRules ?? new List<FurnitureRelationshipRule>())
            {
                if (rule == null || (rule.kind != "seatFacePair" && rule.kind != "seatPairTableContext")) continue;
                ArrangementEvaluation evaluation = FurnitureArrangementResolver.Evaluate(new FurnitureArrangementRequest
                {
                    catalogue = catalogue,
                    world = world,
                    relationshipRuleId = rule.relationshipRuleId
                });
                if (evaluation.state == "available")
                    foreach (FurnitureRoleBinding role in evaluation.roleBindings)
                        if (role != null && !string.IsNullOrWhiteSpace(role.instanceId)) participants.Add(role.instanceId);
            }

            DrawSeatCones(world);
            DrawHighlights(document, participants);
        }

        private FurnitureWorldSnapshot BuildWorld(HomeFurnitureLayoutDocument document, FurniturePlacementEvidence evidence)
        {
            var world = new FurnitureWorldSnapshot
            {
                homeId = document.homeId,
                roomId = document.roomId,
                catalogueHash = catalogue.catalogueHash,
                evidenceComplete = evidence != null && evidence.evidenceComplete,
                supports = evidence == null ? new List<FurnitureSupportSnapshot>() : evidence.supports,
                instances = new List<FurnitureInstanceSnapshot>(),
                points = new List<FurnitureWorldPointSnapshot>(),
                accessEvidence = new List<FurniturePointAccessEvidence>()
            };
            FurnitureHandlingView view = session == null ? null : session.Current;
            foreach (HomeFurnitureLayoutInstance placed in document.instances.Where(item => item != null))
            {
                string instanceId = placed.instanceId;
                FurnitureVector3 position = placed.positionMeters;
                FurnitureQuaternion rotation = placed.rotation;
                if (view != null && view.state == "holding" && view.instanceId == instanceId && view.pose != null)
                {
                    position = view.pose.positionMeters;
                    rotation = view.pose.rotation;
                }
                world.instances.Add(new FurnitureInstanceSnapshot
                {
                    instanceId = instanceId, definitionId = placed.definitionId,
                    positionMeters = position, rotation = rotation, scale = placed.scale,
                    supportId = placed.support == null ? string.Empty : placed.support.supportId,
                    instanceRevision = placed.instanceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    enabled = placed.enabled
                });
                AddWorldPoints(world, instanceId, placed.definitionId, position, rotation);
            }

            if (view != null && view.state == "holding" && view.sourceKind != "placed_furniture" && view.pose != null &&
                world.instances.All(item => item.instanceId != view.instanceId))
            {
                world.instances.Add(new FurnitureInstanceSnapshot
                {
                    instanceId = view.instanceId, definitionId = view.definitionId,
                    positionMeters = view.pose.positionMeters, rotation = view.pose.rotation,
                    scale = new FurnitureVector3 { x = 1, y = 1, z = 1 },
                    supportId = view.pose.support == null ? "room.floor" : view.pose.support.supportId,
                    instanceRevision = "preview", enabled = true
                });
                AddWorldPoints(world, view.instanceId, view.definitionId, view.pose.positionMeters, view.pose.rotation);
            }
            return world;
        }

        private void AddWorldPoints(FurnitureWorldSnapshot world, string instanceId, string definitionId,
            FurnitureVector3 position, FurnitureQuaternion rotation)
        {
            FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item.definitionId == definitionId);
            if (definition == null || definition.points == null) return;
            foreach (FurnitureFunctionalPoint point in definition.points.Where(item => item != null))
            {
                world.points.Add(new FurnitureWorldPointSnapshot
                {
                    instanceId = instanceId,
                    pointId = point.pointId,
                    pointTypeId = point.pointTypeId,
                    localPosition = point.localPosition,
                    localFacing = point.localFacing,
                    useIds = point.useIds == null ? new List<string>() : new List<string>(point.useIds),
                    revision = "1",
                    enabled = true
                });
                AddAccessEvidence(world, instanceId, point, position, rotation);
            }
        }

        private void AddAccessEvidence(FurnitureWorldSnapshot world, string instanceId, FurnitureFunctionalPoint point,
            FurnitureVector3 position, FurnitureQuaternion rotation)
        {
            Vector3 local = ToVector(position) + ToQuaternion(rotation) * ToVector(point.localPosition);
            Vector3 target = roomRoot.TransformPoint(local);
            var path = new NavMeshPath();
            bool found = player != null && NavMesh.CalculatePath(player.transform.position, target, NavMesh.AllAreas, path);
            bool reachable = found && path.status == NavMeshPathStatus.PathComplete;
            float cost = 0f;
            Vector3[] corners = path.corners;
            for (int i = 1; i < corners.Length; i++) cost += Vector3.Distance(corners[i - 1], corners[i]);
            world.accessEvidence.Add(new FurniturePointAccessEvidence
            {
                instanceId = instanceId,
                pointId = point.pointId,
                state = reachable ? "reachable" : found ? "blocked" : "unknown",
                pathClear = reachable,
                travelCostMeters = cost
            });
            if (!found) world.evidenceComplete = false;
        }

        private void DrawSeatCones(FurnitureWorldSnapshot world)
        {
            FurnitureRelationshipRule rule = catalogue.relationshipRules.FirstOrDefault(item => item != null && item.kind == "seatFacePair");
            if (rule == null) return;
            foreach (FurnitureWorldPointSnapshot point in world.points.Where(item => item.pointTypeId == "seat.sit"))
            {
                FurnitureInstanceSnapshot instance = world.instances.FirstOrDefault(item => item.instanceId == point.instanceId);
                if (instance == null) continue;
                Vector3 origin = roomRoot.TransformPoint(ToVector(instance.positionMeters) + ToQuaternion(instance.rotation) * ToVector(point.localPosition));
                Vector3 facing = roomRoot.rotation * ToQuaternion(instance.rotation) * ToVector(point.localFacing);
                facing = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
                if (facing.sqrMagnitude < 0.001f) continue;
                var positions = new List<Vector3>(coneSegments + 3) { origin };
                float halfAngle = Mathf.Clamp((float)rule.maximumFacingErrorDegrees, 1f, 89f);
                for (int segment = 0; segment <= coneSegments; segment++)
                {
                    float angle = Mathf.Lerp(-halfAngle, halfAngle, segment / (float)coneSegments);
                    Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * facing;
                    positions.Add(origin + direction * (float)rule.maximumDistanceMeters);
                }
                positions.Add(origin);
                CreateLine("Interaction Cone", positions, new Color(0.12f, 0.82f, 0.95f, 0.55f), false);
            }
        }

        private void DrawHighlights(HomeFurnitureLayoutDocument document, HashSet<string> instanceIds)
        {
            foreach (string instanceId in instanceIds)
            {
                HomeFurnitureLayoutInstance placed = document.instances.FirstOrDefault(item => item.instanceId == instanceId);
                if (placed == null) continue;
                FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item.definitionId == placed.definitionId);
                if (definition == null || definition.localBounds == null) continue;
                Vector3 min = ToVector(definition.localBounds.minimum);
                Vector3 max = ToVector(definition.localBounds.maximum);
                Vector3[] corners = new Vector3[8];
                int index = 0;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 local = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                    corners[index++] = roomRoot.TransformPoint(ToVector(placed.positionMeters) + ToQuaternion(placed.rotation) * local);
                }
                int[,] edges = { { 0, 1 }, { 0, 2 }, { 0, 4 }, { 1, 3 }, { 1, 5 }, { 2, 3 }, { 2, 6 }, { 3, 7 }, { 4, 5 }, { 4, 6 }, { 5, 7 }, { 6, 7 } };
                for (int edge = 0; edge < edges.GetLength(0); edge++)
                    highlights.Add(CreateLine("Furniture Interaction Shimmer",
                        new[] { corners[edges[edge, 0]], corners[edges[edge, 1]] }, new Color(0.4f, 1f, 0.45f, 0.7f), true));
            }
        }

        private LineRenderer CreateLine(string lineName, IList<Vector3> points, Color color, bool highlight)
        {
            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Count;
            for (int index = 0; index < points.Count; index++) line.SetPosition(index, points[index]);
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.startColor = color;
            line.endColor = color;
            line.sharedMaterial = lineMaterial;
            line.numCornerVertices = 1;
            line.numCapVertices = 1;
            lines.Add(line);
            if (highlight) highlights.Add(line);
            return line;
        }

        private void UpdateHighlightPulse()
        {
            float alpha = Mathf.Lerp(0.25f, 0.95f, pulse);
            foreach (LineRenderer line in highlights)
            {
                if (line == null) continue;
                Color color = new Color(0.35f, 1f, 0.4f, alpha);
                line.startColor = color;
                line.endColor = color;
                line.startWidth = lineWidth * Mathf.Lerp(0.8f, 1.6f, pulse);
                line.endWidth = line.startWidth;
            }
        }

        private void ClearLines()
        {
            foreach (LineRenderer line in lines)
                if (line != null) Destroy(line.gameObject);
            lines.Clear();
            highlights.Clear();
        }

        private static Vector3 ToVector(FurnitureVector3 value)
        {
            return value == null ? Vector3.zero : new Vector3((float)value.x, (float)value.y, (float)value.z);
        }

        private static Quaternion ToQuaternion(FurnitureQuaternion value)
        {
            return value == null ? Quaternion.identity : new Quaternion((float)value.x, (float)value.y, (float)value.z, (float)value.w);
        }
    }
}
