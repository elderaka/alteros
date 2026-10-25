using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Debugging;
using AlterOS.Furniture;
using AlterOS.Persistence;
using AlterOS.Progression;
using Pleiades.Player;
using UnityEngine;

namespace Pleiades.Furniture
{
    /// <summary>Scene composition and Unity physics bridge for the portable furniture handling session.</summary>
    public sealed class FurnitureHandlingController : MonoBehaviour
    {
        [Header("Existing owners")]
        [SerializeField] private FurnitureSceneAdapter sceneAdapter;
        [SerializeField] private ActivitySceneHost activityHost;
        [SerializeField] private AlteraProgressionStore progressionStore;
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerLookInteraction playerInteraction;
        [SerializeField] private Camera playerCamera;

        [Header("Room")]
        [SerializeField] private Transform roomRoot;
        [SerializeField] private Transform roomGeometryRoot;
        [SerializeField] private BoxCollider floorCollider;
        [SerializeField] private FurnitureSupportSurface[] supportSurfaces = new FurnitureSupportSurface[0];
        [SerializeField] private LayerMask placementMask = ~0;
        [SerializeField] private string roomId = "room.testbox.modeltest";
        [SerializeField] private float placementRayDistance = 12f;

        [Header("Furniture presentation")]
        [SerializeField] private FurnitureInventoryPanel inventoryPanel;
        [SerializeField] private FurniturePlacementPreview previewPrefab;
        [SerializeField] private FurnitureDiagnosticsView diagnosticsView;
        [SerializeField] private AlterOS.UI.AlterraActivityDebugView activityDebugView;
        [SerializeField] private float gridSpacingMeters = 0.25f;
        [SerializeField] private float rotationStepDegrees = 15f;

        private readonly Dictionary<string, GameObject> runtimeVisuals = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly HashSet<string> sceneFurnitureIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<RendererState> hiddenSourceRenderers = new List<RendererState>();
        private readonly List<Collider> roomColliders = new List<Collider>();
        private readonly List<FurnitureSupportSnapshot> supportSnapshots = new List<FurnitureSupportSnapshot>();
        private IFurnitureHomeStore store;
        private FurnitureCatalogueSnapshot catalogue;
        private FurnitureHandlingSession session;
        private FurniturePlacementPreview preview;
        private FurnitureEditClaim heldEditClaim;
        private Transform heldSource;
        private string pendingOperationId;
        private DebugModeState debugMode;
        private bool ready;
        private bool snapping = true;
        private bool cursorWasExternallyOwned;
        private bool inventoryOpenedCursor;
        private float previewYawDegrees;
        private FurnitureSupportSurface currentSupport;
        private FurniturePlacementPose lastPose;
        private HomeFurnitureLayoutDocument currentHomeDocument;
        private string lastPlacementReason = "placement_pending";

        private sealed class RendererState
        {
            public Renderer renderer;
            public bool enabled;
        }

        public bool IsReady { get { return ready; } }
        public bool IsHoldingFurniture { get { return session != null && session.Current.state == "holding"; } }
        public bool IsInventoryOpen { get { return inventoryPanel != null && inventoryPanel.IsOpen; } }
        public DebugModeState DebugMode { get { return debugMode; } }
        public FurnitureHandlingView Current { get { return session == null ? null : session.Current; } }
        public FurnitureCatalogueSnapshot Catalogue { get { return catalogue; } }
        public Transform RoomRoot { get { return roomRoot; } }
        public HomeFurnitureLayoutDocument CurrentHomeDocument { get { return currentHomeDocument; } }
        public FurniturePlacementEvidence CreateDiagnosticEvidence() { return BuildBaseEvidence(); }

        public GameObject FindPlacedVisual(string instanceId)
        {
            GameObject visual;
            if (runtimeVisuals.TryGetValue(instanceId ?? string.Empty, out visual) && visual != null) return visual;
            FurnitureUsePoint point = roomRoot == null ? null : roomRoot.GetComponentsInChildren<FurnitureUsePoint>(true)
                .FirstOrDefault(item => item != null && item.InstanceId == instanceId);
            return point == null ? null : point.gameObject;
        }

        private void Awake()
        {
            if (sceneAdapter == null) sceneAdapter = GetComponent<FurnitureSceneAdapter>();
            if (activityHost == null) activityHost = GetComponent<ActivitySceneHost>();
            if (roomRoot == null) roomRoot = transform;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (playerInteraction == null && player != null) playerInteraction = player.GetComponent<PlayerLookInteraction>();
            if (playerCamera == null && player != null) playerCamera = player.PlayerCamera;
            if (roomGeometryRoot != null)
            {
                roomColliders.AddRange(roomGeometryRoot.GetComponentsInChildren<Collider>(true).Where(item => item != null));
                if (floorCollider == null) floorCollider = roomGeometryRoot.GetComponentsInChildren<BoxCollider>(true)
                    .FirstOrDefault(item => item != null && item.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            debugMode = new DebugModeState();
            if (activityDebugView != null) activityDebugView.BindDebugModeState(debugMode);
            if (diagnosticsView != null) diagnosticsView.BindDebugModeState(debugMode);
        }

        private void Start() { TryInitialize(); }

        private void Update()
        {
            if (!ready)
            {
                TryInitialize();
                return;
            }
            if (IsHoldingFurniture) UpdatePlacementPreview();
        }

        private void OnDisable()
        {
            if (session != null && IsHoldingFurniture) session.Cancel();
            ReleaseHeldSource();
            if (preview != null) preview.Hide();
            if (inventoryPanel != null) inventoryPanel.Close();
            if (inventoryOpenedCursor && player != null) player.SetExternalHudCursorMode(cursorWasExternallyOwned);
            inventoryOpenedCursor = false;
            if (playerInteraction != null) playerInteraction.SetFurnitureHandlingActive(false);
            ready = false;
        }

        public void ToggleDebugMode()
        {
            if (debugMode == null) debugMode = new DebugModeState();
            debugMode.Toggle();
            if (inventoryPanel != null) inventoryPanel.RefreshRows();
        }

        public void OpenInventory()
        {
            if (!ready || IsHoldingFurniture || inventoryPanel == null) return;
            cursorWasExternallyOwned = player != null && player.IsExternalHudCursorMode;
            inventoryOpenedCursor = player != null;
            if (player != null) player.SetExternalHudCursorMode(true);
            inventoryPanel.Open(session, catalogue, debugMode, SelectFromInventory, CloseInventoryCursor);
        }

        public void CloseInventory()
        {
            if (inventoryPanel != null) inventoryPanel.Close();
        }

        public void PickUpTargetedFurniture()
        {
            if (!ready || IsHoldingFurniture) return;
            if (activityHost != null && (activityHost.HasScenarioOwner || (activityHost.Host != null && activityHost.Host.Current != null)))
            {
                ShowStatus("Furniture is busy while Alterra is using it.");
                return;
            }
            RaycastHit hit;
            if (!TryGetTargetHit(out hit))
            {
                ShowStatus("Aim at a furniture piece to pick it up.");
                return;
            }
            string instanceId;
            if (!TryGetInstanceId(hit.collider, out instanceId))
            {
                ShowStatus("Aim at placed furniture to move it.");
                return;
            }
            FurnitureUsePoint targetUsePoint = hit.collider.GetComponentInParent<FurnitureUsePoint>();
            if (targetUsePoint != null && activityHost != null)
            {
                string ownerId = "furniture-edit-" + Guid.NewGuid().ToString("N");
                if (!activityHost.TryClaimFurnitureForEdit(targetUsePoint, ownerId, out heldEditClaim))
                {
                    ShowStatus(FriendlyReason("point_in_use"));
                    return;
                }
            }

            FurniturePlacementEvidence evidence = BuildBaseEvidence();
            FurnitureHandlingResult result = session.BeginMove(instanceId, evidence);
            if (!result.success)
            {
                ReleaseHeldSource();
                ShowStatus(FriendlyReason(result.reasonCode));
                return;
            }
            heldSource = hit.collider.GetComponentInParent<FurniturePlacedInstanceIdentity>() != null
                ? hit.collider.GetComponentInParent<FurniturePlacedInstanceIdentity>().transform
                : (targetUsePoint == null ? null : targetUsePoint.transform);
            HideSourceRenderers();
            previewYawDegrees = ExtractYaw(session.Current.pose == null ? null : session.Current.pose.rotation);
            BeginHolding();
        }

        public void ConfirmPlacement()
        {
            if (!ready || !IsHoldingFurniture) return;
            FurniturePlacementEvidence evidence;
            FurniturePlacementPose pose;
            string reason;
            if (!TryCreatePreview(out pose, out evidence, out reason))
            {
                lastPlacementReason = reason;
                UpdatePlacementPreview();
                return;
            }
            session.UpdatePreview(pose, evidence);
            if (!session.Current.valid)
            {
                ShowStatus(FriendlyReason(session.Current.reasonCode));
                return;
            }
            if (string.IsNullOrEmpty(pendingOperationId)) pendingOperationId = Guid.NewGuid().ToString("D");
            FurnitureHandlingResult result = session.Confirm(pendingOperationId, evidence);
            if (!result.success)
            {
                ShowStatus(FriendlyReason(result.reasonCode));
                return;
            }
            pendingOperationId = null;
            ReleaseHeldSource();
            EndHolding();
            SyncWorldFromStore();
        }

        public void ReturnHeldFurniture()
        {
            if (!ready || !IsHoldingFurniture) return;
            FurniturePlacementEvidence evidence = BuildBaseEvidence();
            if (string.IsNullOrEmpty(pendingOperationId)) pendingOperationId = Guid.NewGuid().ToString("D");
            FurnitureHandlingResult result = session.ReturnToInventory(pendingOperationId, evidence);
            if (!result.success)
            {
                ShowStatus(FriendlyReason(result.reasonCode));
                return;
            }
            pendingOperationId = null;
            ReleaseHeldSource();
            EndHolding();
            SyncWorldFromStore();
        }

        public void CancelFurnitureHandling()
        {
            if (session == null || !IsHoldingFurniture) return;
            FurnitureHandlingResult result = session.Cancel();
            if (!result.success)
            {
                ShowStatus(FriendlyReason(result.reasonCode));
                return;
            }
            pendingOperationId = null;
            ReleaseHeldSource();
            EndHolding();
            SyncWorldFromStore();
        }

        public void RotatePreview(bool clockwise)
        {
            if (!IsHoldingFurniture) return;
            if (currentSupport != null && currentSupport.SupportKind != "roomFloor")
            {
                ShowStatus("This mounting surface keeps its authored orientation.");
                return;
            }
            previewYawDegrees = Mathf.Repeat(previewYawDegrees + (clockwise ? rotationStepDegrees : -rotationStepDegrees), 360f);
        }

        public void ToggleGridSnap()
        {
            if (!IsHoldingFurniture) return;
            snapping = !snapping;
            ShowStatus(snapping ? "Grid snap on." : "Grid snap off.");
        }

        private void TryInitialize()
        {
            if (ready || progressionStore == null || !progressionStore.IsInitialized || sceneAdapter == null || roomRoot == null) return;
            catalogue = sceneAdapter.Catalogue;
            if (catalogue == null || catalogue.schemaVersion != 1 || catalogue.diagnostics.Any(item => item.severity == "error"))
            {
                ShowStatus("Furniture catalogue is unavailable.");
                return;
            }
            store = progressionStore.CreateFurnitureHomeStore(id => sceneAdapter.CreateHomeLayoutSeed(id, roomId, roomRoot));
            if (store == null)
            {
                ShowStatus("Game storage is not ready.");
                return;
            }
            CaptureSceneFurnitureIds();
            session = new FurnitureHandlingSession(store, catalogue);
            if (session.Current.state == "unavailable")
            {
                ShowStatus("Furniture inventory is unavailable.");
                return;
            }
            ready = true;
            SyncWorldFromStore();
            if (inventoryPanel != null) inventoryPanel.RefreshRows();
            if (diagnosticsView != null) diagnosticsView.Bind(this, session, catalogue, roomRoot, player);
        }

        private void SelectFromInventory(string definitionId)
        {
            if (!ready || IsHoldingFurniture) return;
            FurnitureHandlingResult result = session.BeginFromInventory(definitionId, debugMode.IsEnabled);
            if (!result.success)
            {
                ShowStatus(FriendlyReason(result.reasonCode));
                return;
            }
            pendingOperationId = null;
            lastPose = null;
            lastPlacementReason = "placement_pending";
            BeginHolding();
            EnsurePreview();
        }

        private void BeginHolding()
        {
            EnsurePreview();
            if (playerInteraction != null) playerInteraction.SetFurnitureHandlingActive(true);
            if (inventoryPanel != null) inventoryPanel.RefreshRows();
        }

        private void EndHolding()
        {
            if (preview != null) preview.Hide();
            lastPose = null;
            currentSupport = null;
            if (playerInteraction != null) playerInteraction.SetFurnitureHandlingActive(false);
            if (inventoryPanel != null) inventoryPanel.RefreshRows();
        }

        private void EnsurePreview()
        {
            if (preview != null || previewPrefab == null || roomRoot == null) return;
            preview = Instantiate(previewPrefab, roomRoot, false);
            preview.Hide();
        }

        private void UpdatePlacementPreview()
        {
            EnsurePreview();
            if (preview == null || session == null) return;
            FurniturePlacementEvidence evidence;
            FurniturePlacementPose pose;
            string reason;
            bool resolved = TryCreatePreview(out pose, out evidence, out reason);
            if (resolved)
            {
                session.UpdatePreview(pose, evidence);
                lastPose = pose;
                lastPlacementReason = session.Current.valid ? string.Empty : session.Current.reasonCode;
            }
            else
            {
                session.UpdatePreview(null, evidence);
                if (pose != null) lastPose = pose;
                lastPlacementReason = reason;
            }
            FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item.definitionId == session.Current.definitionId);
            FurniturePlacementPose displayPose = resolved ? pose : lastPose;
            if (displayPose == null) displayPose = FallbackPose();
            FurniturePlacementEvidence displayEvidence = evidence;
            string supportKind = currentSupport == null ? string.Empty : currentSupport.SupportKind;
            preview.Show(definition, ToVector(displayPose.positionMeters), ToQuaternion(displayPose.rotation),
                resolved && displayEvidence.evidenceComplete && session.Current.valid, lastPlacementReason, supportKind);
        }

        private bool TryCreatePreview(out FurniturePlacementPose pose, out FurniturePlacementEvidence evidence, out string reason)
        {
            pose = null;
            evidence = BuildBaseEvidence();
            reason = "evidence_unknown";
            if (!evidence.evidenceComplete)
            {
                reason = "evidence_unknown";
                return false;
            }
            if (activityHost != null && (activityHost.HasScenarioOwner || (activityHost.Host != null && activityHost.Host.Current != null)))
            {
                evidence.evidenceComplete = false;
                reason = "point_in_use";
                return false;
            }
            RaycastHit hit;
            FurnitureSupportSurface support;
            FurnitureSupportSnapshot supportSnapshot;
            if (!TryRaycastSupport(out hit, out support, out supportSnapshot))
            {
                evidence.evidenceComplete = false;
                reason = "unsupported_support";
                return false;
            }
            currentSupport = support;
            FurnitureHandlingView view = session.Current;
            FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item.definitionId == view.definitionId);
            if (definition == null)
            {
                evidence.evidenceComplete = false;
                reason = "missing_definition";
                return false;
            }

            Quaternion supportRotation = ToQuaternion(supportSnapshot.rotation);
            Vector3 roomHit = roomRoot.InverseTransformPoint(hit.point);
            Vector3 supportPosition = ToVector(supportSnapshot.positionMeters);
            Vector3 supportLocal = Quaternion.Inverse(supportRotation) * (roomHit - supportPosition);
            if (snapping)
            {
                if (support.SupportKind == "roomFloor")
                {
                    supportLocal.x = Snap(supportLocal.x);
                    supportLocal.z = Snap(supportLocal.z);
                }
                else
                {
                    supportLocal.x = Snap(supportLocal.x);
                    supportLocal.y = Snap(supportLocal.y);
                }
            }

            Quaternion rotation = support.SupportKind == "roomFloor"
                ? Quaternion.Euler(0f, previewYawDegrees, 0f)
                : supportRotation;
            Vector3 position = supportPosition + supportRotation * supportLocal;
            pose = new FurniturePlacementPose
            {
                positionMeters = ToDomain(position),
                rotation = ToDomain(rotation),
                support = new FurnitureLayoutSupportReference
                {
                    supportId = supportSnapshot.supportId,
                    supportKind = supportSnapshot.supportKind,
                    supportFrameRevision = supportSnapshot.revision,
                    attachmentPointId = support.SupportKind == "roomFloor" ? "origin" : "opening.center",
                    localPositionMeters = ToDomain(supportLocal),
                    localRotation = ToDomain(Quaternion.Inverse(supportRotation) * rotation)
                }
            };

            if (!IsPhysicsClear(definition, pose, support, out reason))
            {
                evidence.evidenceComplete = false;
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private bool IsPhysicsClear(FurnitureDefinition definition, FurniturePlacementPose pose,
            FurnitureSupportSurface support, out string reason)
        {
            reason = string.Empty;
            if (definition.localBounds == null || definition.localBounds.minimum == null || definition.localBounds.maximum == null)
            {
                reason = "evidence_unknown";
                return false;
            }
            Vector3 minimum = ToVector(definition.localBounds.minimum);
            Vector3 maximum = ToVector(definition.localBounds.maximum);
            Vector3 centerLocal = (minimum + maximum) * 0.5f;
            Vector3 halfExtents = (maximum - minimum) * 0.5f;
            Quaternion itemRotation = ToQuaternion(pose.rotation);
            Vector3 roomCenter = ToVector(pose.positionMeters) + itemRotation * centerLocal;
            Vector3 worldCenter = roomRoot.TransformPoint(roomCenter);
            Quaternion worldRotation = roomRoot.rotation * itemRotation;
            Physics.SyncTransforms();
            Collider[] overlaps = Physics.OverlapBox(worldCenter, halfExtents, worldRotation, placementMask, QueryTriggerInteraction.Ignore);
            Collider supportCollider = support == null ? null : support.SurfaceCollider;
            foreach (Collider collider in overlaps)
            {
                if (collider == null || collider == supportCollider || collider.isTrigger || IsUnder(collider.transform, preview == null ? null : preview.transform) ||
                    IsUnder(collider.transform, heldSource)) continue;
                if (CanOverlapPlacedFurniture(definition, collider)) continue;
                reason = "world_collision";
                return false;
            }
            return true;
        }

        private bool CanOverlapPlacedFurniture(FurnitureDefinition movingDefinition, Collider collider)
        {
            FurnitureDefinition placedDefinition;
            if (!TryGetPlacedFurnitureDefinition(collider, out placedDefinition)) return false;
            return movingDefinition.category == "carpet" || placedDefinition.category == "carpet";
        }

        private bool TryGetPlacedFurnitureDefinition(Collider collider, out FurnitureDefinition definition)
        {
            definition = null;
            if (collider == null || catalogue == null) return false;
            string definitionId = string.Empty;
            FurniturePlacedInstanceIdentity identity = collider.GetComponentInParent<FurniturePlacedInstanceIdentity>();
            if (identity != null) definitionId = identity.DefinitionId;
            else
            {
                FurnitureUsePoint point = collider.GetComponentInParent<FurnitureUsePoint>();
                string pointId;
                if (point == null || !FurnitureInteractionPolicy.TryResolvePoint(catalogue,
                    point.InstanceId, point.UsePointId, string.Empty, string.Empty, out definitionId, out pointId)) return false;
            }
            definition = catalogue.definitions.FirstOrDefault(item => item != null && item.definitionId == definitionId);
            return definition != null;
        }

        private FurniturePlacementEvidence BuildBaseEvidence()
        {
            var evidence = new FurniturePlacementEvidence
            {
                catalogue = catalogue,
                evidenceComplete = false,
                supports = new List<FurnitureSupportSnapshot>(),
                reservedPointRefs = new List<FurniturePointReference>()
            };
            if (roomRoot == null || floorCollider == null || supportSurfaces == null || supportSurfaces.Length == 0 ||
                !TryGetRoomBounds(out evidence.roomMinimum, out evidence.roomMaximum)) return evidence;
            foreach (FurnitureSupportSurface surface in supportSurfaces)
            {
                FurnitureSupportSnapshot snapshot;
                if (surface == null || !surface.TryCreateSnapshot(roomRoot, out snapshot)) return evidence;
                evidence.supports.Add(snapshot);
            }
            if (activityHost != null && (activityHost.HasScenarioOwner || (activityHost.Host != null && activityHost.Host.Current != null)))
            {
                foreach (FurnitureUsePoint point in roomRoot.GetComponentsInChildren<FurnitureUsePoint>(true))
                    if (point != null) evidence.reservedPointRefs.Add(new FurniturePointReference { instanceId = point.InstanceId, pointId = point.UsePointId });
            }
            evidence.evidenceComplete = true;
            return evidence;
        }

        private bool TryGetRoomBounds(out FurnitureVector3 minimum, out FurnitureVector3 maximum)
        {
            minimum = null;
            maximum = null;
            if (roomRoot == null || floorCollider == null) return false;
            var floorBox = floorCollider as BoxCollider;
            if (floorBox == null) return false;
            Vector3 floorMin;
            Vector3 floorMax;
            EncapsulateBox(floorBox, roomRoot, out floorMin, out floorMax);
            float ceiling = floorMax.y + 3f;
            foreach (Collider collider in roomColliders)
            {
                if (collider == null || collider == floorCollider) continue;
                Vector3 localBoundsMin;
                Vector3 localBoundsMax;
                BoundsInRoom(collider.bounds, roomRoot, out localBoundsMin, out localBoundsMax);
                ceiling = Mathf.Max(ceiling, localBoundsMax.y);
            }
            minimum = new FurnitureVector3 { x = floorMin.x, y = floorMax.y, z = floorMin.z };
            maximum = new FurnitureVector3 { x = floorMax.x, y = ceiling, z = floorMax.z };
            return maximum.x > minimum.x && maximum.y > minimum.y && maximum.z > minimum.z;
        }

        private static void EncapsulateBox(BoxCollider box, Transform room, out Vector3 minimum, out Vector3 maximum)
        {
            minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            Vector3 half = box.size * 0.5f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = room.InverseTransformPoint(box.transform.TransformPoint(box.center + Vector3.Scale(half, new Vector3(x, y, z))));
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
        }

        private static void BoundsInRoom(Bounds bounds, Transform room, out Vector3 minimum, out Vector3 maximum)
        {
            minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            Vector3 half = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = room.InverseTransformPoint(bounds.center + Vector3.Scale(half, new Vector3(x, y, z)));
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
        }

        private bool TryRaycastSupport(out RaycastHit selectedHit, out FurnitureSupportSurface selectedSurface,
            out FurnitureSupportSnapshot selectedSnapshot)
        {
            selectedHit = default(RaycastHit);
            selectedSurface = null;
            selectedSnapshot = null;
            if (playerCamera == null || roomRoot == null) return false;
            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray, placementRayDistance, placementMask, QueryTriggerInteraction.Collide)
                .OrderBy(hit => hit.distance).ToArray();
            foreach (RaycastHit hit in hits)
            {
                FurnitureSupportSurface surface = hit.collider.GetComponentInParent<FurnitureSupportSurface>();
                if (surface == null) continue;
                FurnitureSupportSnapshot snapshot;
                if (!surface.TryCreateSnapshot(roomRoot, out snapshot)) continue;
                selectedHit = hit;
                selectedSurface = surface;
                selectedSnapshot = snapshot;
                return true;
            }
            return false;
        }

        private bool TryGetTargetHit(out RaycastHit selectedHit)
        {
            selectedHit = default(RaycastHit);
            if (playerCamera == null) return false;
            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray, placementRayDistance, placementMask, QueryTriggerInteraction.Ignore)
                .OrderBy(hit => hit.distance).ToArray();
            foreach (RaycastHit hit in hits)
                if (TryGetInstanceId(hit.collider, out _)) { selectedHit = hit; return true; }
            return false;
        }

        private static bool TryGetInstanceId(Collider collider, out string instanceId)
        {
            instanceId = string.Empty;
            if (collider == null) return false;
            FurniturePlacedInstanceIdentity identity = collider.GetComponentInParent<FurniturePlacedInstanceIdentity>();
            if (identity != null && !string.IsNullOrWhiteSpace(identity.InstanceId))
            { instanceId = identity.InstanceId; return true; }
            FurnitureUsePoint point = collider.GetComponentInParent<FurnitureUsePoint>();
            if (point != null && !string.IsNullOrWhiteSpace(point.InstanceId))
            { instanceId = point.InstanceId; return true; }
            return false;
        }

        private void SyncWorldFromStore()
        {
            if (store == null || roomRoot == null) return;
            FurnitureHomeReadResult read;
            try { read = store.Read(); }
            catch (Exception exception) { ShowStatus("Furniture save could not be read: " + exception.Message); return; }
            if (read == null || read.state != "loaded" || read.document == null) { ShowStatus("Furniture save is unavailable."); return; }
            currentHomeDocument = read.document;
            HashSet<string> savedIds = new HashSet<string>(read.document.instances.Select(item => item.instanceId), StringComparer.Ordinal);
            FurnitureUsePoint[] points = roomRoot.GetComponentsInChildren<FurnitureUsePoint>(true);
            foreach (FurnitureUsePoint point in points)
            {
                if (point == null || !sceneFurnitureIds.Contains(point.InstanceId)) continue;
                HomeFurnitureLayoutInstance saved = read.document.instances.FirstOrDefault(item => item.instanceId == point.InstanceId);
                if (saved == null)
                {
                    point.gameObject.SetActive(false);
                    continue;
                }
                point.gameObject.SetActive(true);
                ApplyPose(point.transform, saved);
            }

            foreach (HomeFurnitureLayoutInstance instance in read.document.instances)
            {
                if (points.Any(point => point != null && point.InstanceId == instance.instanceId)) continue;
                GameObject visual;
                if (!runtimeVisuals.TryGetValue(instance.instanceId, out visual) || visual == null)
                {
                    FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item.definitionId == instance.definitionId);
                    visual = CreateRuntimeVisual(instance, definition);
                    runtimeVisuals[instance.instanceId] = visual;
                }
                visual.SetActive(true);
                ApplyPose(visual.transform, instance);
            }
            foreach (string staleId in runtimeVisuals.Keys.Where(id => !savedIds.Contains(id)).ToArray())
            {
                GameObject stale = runtimeVisuals[staleId];
                runtimeVisuals.Remove(staleId);
                if (stale != null) Destroy(stale);
            }
        }

        private GameObject CreateRuntimeVisual(HomeFurnitureLayoutInstance instance, FurnitureDefinition definition)
        {
            GameObject root = new GameObject(string.IsNullOrWhiteSpace(definition?.displayNameKey) ? instance.definitionId : definition.displayNameKey);
            root.transform.SetParent(roomRoot, false);
            var identity = root.AddComponent<FurniturePlacedInstanceIdentity>();
            identity.Configure(instance.instanceId, instance.definitionId);
            Vector3 min = definition == null || definition.localBounds == null ? new Vector3(-0.25f, 0f, -0.25f) : ToVector(definition.localBounds.minimum);
            Vector3 max = definition == null || definition.localBounds == null ? new Vector3(0.25f, 1f, 0.25f) : ToVector(definition.localBounds.maximum);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            GameObject shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shape.name = "Debug furniture shape";
            Collider generatedCollider = shape.GetComponent<Collider>();
            if (generatedCollider != null) Destroy(generatedCollider);
            shape.transform.SetParent(root.transform, false);
            shape.transform.localPosition = center;
            shape.transform.localScale = size;
            BoxCollider boundsCollider = root.AddComponent<BoxCollider>();
            boundsCollider.center = center;
            boundsCollider.size = size;
            return root;
        }

        private void CaptureSceneFurnitureIds()
        {
            sceneFurnitureIds.Clear();
            if (roomRoot == null) return;
            foreach (FurnitureUsePoint point in roomRoot.GetComponentsInChildren<FurnitureUsePoint>(true))
                if (point != null && !string.IsNullOrWhiteSpace(point.InstanceId)) sceneFurnitureIds.Add(point.InstanceId);
        }

        private void ApplyPose(Transform target, HomeFurnitureLayoutInstance instance)
        {
            target.localPosition = ToVector(instance.positionMeters);
            target.localRotation = ToQuaternion(instance.rotation);
            target.localScale = ToVector(instance.scale);
        }

        private void HideSourceRenderers()
        {
            hiddenSourceRenderers.Clear();
            if (heldSource == null) return;
            foreach (Renderer renderer in heldSource.GetComponentsInChildren<Renderer>(true))
            {
                hiddenSourceRenderers.Add(new RendererState { renderer = renderer, enabled = renderer.enabled });
                renderer.enabled = false;
            }
        }

        private void ReleaseHeldSource()
        {
            foreach (RendererState state in hiddenSourceRenderers)
                if (state.renderer != null) state.renderer.enabled = state.enabled;
            hiddenSourceRenderers.Clear();
            if (heldEditClaim != null && activityHost != null) activityHost.ReleaseFurnitureEditClaim(heldEditClaim);
            heldEditClaim = null;
            heldSource = null;
        }

        private void CloseInventoryCursor()
        {
            if (!inventoryOpenedCursor) return;
            if (player != null) player.SetExternalHudCursorMode(cursorWasExternallyOwned);
            inventoryOpenedCursor = false;
        }

        private void ShowStatus(string message)
        {
            if (inventoryPanel != null) inventoryPanel.ShowStatus(message);
            if (preview != null && IsHoldingFurniture && lastPose != null)
            {
                FurnitureDefinition definition = catalogue.definitions.FirstOrDefault(item => item.definitionId == session.Current.definitionId);
                preview.Show(definition, ToVector(lastPose.positionMeters), ToQuaternion(lastPose.rotation), false,
                    session.Current.reasonCode, currentSupport == null ? string.Empty : currentSupport.SupportKind);
            }
        }

        private FurniturePlacementPose FallbackPose()
        {
            Vector3 world = player == null ? roomRoot.position + roomRoot.forward * 2f : player.transform.position + player.transform.forward * 2f;
            Vector3 local = roomRoot.InverseTransformPoint(world);
            return new FurniturePlacementPose
            {
                positionMeters = ToDomain(local),
                rotation = ToDomain(Quaternion.identity),
                support = new FurnitureLayoutSupportReference
                {
                    supportId = "room.floor", supportKind = "roomFloor", supportFrameRevision = "1",
                    attachmentPointId = "origin", localPositionMeters = ToDomain(local), localRotation = new FurnitureQuaternion { w = 1 }
                }
            };
        }

        private float Snap(float value)
        {
            return gridSpacingMeters <= 0.001f ? value : Mathf.Round(value / gridSpacingMeters) * gridSpacingMeters;
        }

        private static float ExtractYaw(FurnitureQuaternion rotation)
        {
            return rotation == null ? 0f : ToQuaternion(rotation).eulerAngles.y;
        }

        private static string FriendlyReason(string reason)
        {
            switch (reason)
            {
                case "insufficient_stock": return "No furniture of this type is available.";
                case "point_in_use": return "This furniture is in use or being moved.";
                case "has_dependents": return "Move attached furniture first.";
                case "outside_room": return "This spot is outside the room.";
                case "structural_overlap": return "This spot overlaps another piece.";
                case "world_collision": return "This spot is blocked.";
                case "unsupported_support": return "This furniture cannot use that surface.";
                case "stale_support": return "The surface changed. Aim again.";
                case "home_commit_failed": return "Furniture could not be saved. Try again.";
                default: return string.IsNullOrWhiteSpace(reason) ? "Furniture action is unavailable." : "Furniture action needs attention.";
            }
        }

        private static bool IsUnder(Transform candidate, Transform root)
        {
            return root != null && candidate != null && (candidate == root || candidate.IsChildOf(root));
        }

        private static Vector3 ToVector(FurnitureVector3 value)
        {
            return value == null ? Vector3.zero : new Vector3((float)value.x, (float)value.y, (float)value.z);
        }

        private static Quaternion ToQuaternion(FurnitureQuaternion value)
        {
            return value == null ? Quaternion.identity : new Quaternion((float)value.x, (float)value.y, (float)value.z, (float)value.w);
        }

        private static FurnitureVector3 ToDomain(Vector3 value)
        {
            return new FurnitureVector3 { x = value.x, y = value.y, z = value.z };
        }

        private static FurnitureQuaternion ToDomain(Quaternion value)
        {
            return new FurnitureQuaternion { x = value.x, y = value.y, z = value.z, w = value.w };
        }
    }
}
