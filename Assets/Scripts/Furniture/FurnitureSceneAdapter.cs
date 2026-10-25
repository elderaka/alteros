using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlterOS.Activities;
using AlterOS.Content;
using AlterOS.Furniture;
using AlterOS.Scenarios;
using UnityEngine;

namespace Pleiades.Furniture
{
    public enum FurnitureCatalogueSourceMode
    {
        LegacyJson = 0,
        CompiledSql = 1
    }

    /// <summary>Scene-owned bridge from authored catalogue data to installed activity points.</summary>
    public sealed class FurnitureSceneAdapter : MonoBehaviour, IFurnitureFunctionSceneTargetResolver,
        IFurnitureCatalogueSnapshotSource
    {
        private static readonly IReadOnlyList<ContentDiagnostic> EmptyCatalogueSourceDiagnostics =
            Array.AsReadOnly(new ContentDiagnostic[0]);

        [SerializeField] private ActivitySceneHost activityHost;
        [SerializeField] private FurnitureCatalogueSourceMode catalogueSourceMode = FurnitureCatalogueSourceMode.LegacyJson;
        [SerializeField] private TextAsset catalogueJson;
        [SerializeField] private string catalogueStreamingAssetPath = "Furniture/catalogue.json";

        private FurnitureCatalogueSnapshot catalogue;
        private string catalogueAssetText;
        private bool hasCachedCatalogue;
        private FurnitureCatalogueSourceMode cachedCatalogueSourceMode;
        private RuntimeContentStore cachedContentStore;
        private string cachedContentReleaseId = string.Empty;
        private IReadOnlyList<ContentDiagnostic> catalogueSourceDiagnostics = EmptyCatalogueSourceDiagnostics;

        public IReadOnlyList<ContentDiagnostic> CatalogueSourceDiagnostics { get { return catalogueSourceDiagnostics; } }

        public FurnitureCatalogueSnapshot Catalogue
        {
            get
            {
                if (catalogueSourceMode == FurnitureCatalogueSourceMode.LegacyJson)
                    return GetLegacyCatalogue();
                if (catalogueSourceMode == FurnitureCatalogueSourceMode.CompiledSql)
                    return GetCompiledSqlCatalogue();

                if (hasCachedCatalogue && cachedCatalogueSourceMode == catalogueSourceMode && cachedContentStore == null)
                    return catalogue;
                var invalidMode = ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                    "furniture_catalogue_source_mode_invalid", "furniture.catalogue", "The Furniture catalogue source mode is unsupported.");
                return CacheCatalogue(catalogueSourceMode, null, null, string.Empty, null, invalidMode.diagnostics);
            }
        }

        public void ConfigureForTests(ActivitySceneHost host, TextAsset asset)
        {
            activityHost = host;
            catalogueJson = asset;
            catalogueSourceMode = FurnitureCatalogueSourceMode.LegacyJson;
            ResetCatalogueCache();
        }

        private FurnitureCatalogueSnapshot GetLegacyCatalogue()
        {
            string currentText = catalogueJson != null ? catalogueJson.text : ReadStreamingCatalogue();
            if (hasCachedCatalogue && cachedCatalogueSourceMode == FurnitureCatalogueSourceMode.LegacyJson &&
                catalogueAssetText == currentText)
                return catalogue;

            FurnitureCatalogueSnapshot snapshot = string.IsNullOrEmpty(currentText)
                ? null : FurnitureCatalogueRegistry.Parse(currentText);
            return CacheCatalogue(FurnitureCatalogueSourceMode.LegacyJson, currentText, null, string.Empty,
                snapshot, EmptyCatalogueSourceDiagnostics);
        }

        private FurnitureCatalogueSnapshot GetCompiledSqlCatalogue()
        {
            RuntimeContentStore store = RuntimeContentStore.Current;
            string releaseId = store == null || store.Release == null ? string.Empty : store.Release.releaseId ?? string.Empty;
            if (hasCachedCatalogue && cachedCatalogueSourceMode == FurnitureCatalogueSourceMode.CompiledSql &&
                ReferenceEquals(store, cachedContentStore) && cachedContentReleaseId == releaseId)
                return catalogue;

            ContentResult<CompiledFurnitureCatalogue> result = store == null
                ? ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Unavailable,
                    "runtime_content_store_unavailable", "furniture.catalogue", "No pinned SQL content release is open.")
                : store.ReadFurnitureCatalogue();
            if (result.IsReady && result.value == null)
                result = ContentResult<CompiledFurnitureCatalogue>.Failure(ContentStatuses.Invalid,
                    "furniture_catalogue_result_invalid", "furniture.catalogue", "The compiled Furniture catalogue reader returned no value.");

            FurnitureCatalogueSnapshot snapshot = result.IsReady
                ? FurnitureCatalogueRegistry.CreateCompiledSnapshot(result.value.document, result.value.fingerprint)
                : null;
            return CacheCatalogue(FurnitureCatalogueSourceMode.CompiledSql, null, store, releaseId,
                snapshot, result.diagnostics);
        }

        private FurnitureCatalogueSnapshot CacheCatalogue(FurnitureCatalogueSourceMode mode, string sourceText,
            RuntimeContentStore store, string releaseId, FurnitureCatalogueSnapshot snapshot,
            IReadOnlyList<ContentDiagnostic> sourceDiagnostics)
        {
            catalogue = snapshot;
            catalogueAssetText = sourceText;
            cachedCatalogueSourceMode = mode;
            cachedContentStore = store;
            cachedContentReleaseId = releaseId ?? string.Empty;
            catalogueSourceDiagnostics = sourceDiagnostics ?? EmptyCatalogueSourceDiagnostics;
            hasCachedCatalogue = true;
            return catalogue;
        }

        private void ResetCatalogueCache()
        {
            catalogue = null;
            catalogueAssetText = null;
            cachedContentStore = null;
            cachedContentReleaseId = string.Empty;
            catalogueSourceDiagnostics = EmptyCatalogueSourceDiagnostics;
            hasCachedCatalogue = false;
        }

        private string ReadStreamingCatalogue()
        {
            if (string.IsNullOrWhiteSpace(catalogueStreamingAssetPath)) return string.Empty;
            string path = Path.Combine(Application.streamingAssetsPath,
                catalogueStreamingAssetPath.Replace('/', Path.DirectorySeparatorChar));
            try { return File.ReadAllText(path); }
            catch (IOException) { return string.Empty; }
            catch (UnauthorizedAccessException) { return string.Empty; }
        }

        public HomeFurnitureLayoutDocument CreateHomeLayoutSeed(string homeId, string roomId, Transform roomRoot)
        {
            FurnitureCatalogueSnapshot snapshot = Catalogue;
            if (roomRoot == null || snapshot == null) return null;
            Vector3 rootScale = roomRoot.lossyScale;
            if (Mathf.Abs(rootScale.x) < 0.0001f || Mathf.Abs(rootScale.y) < 0.0001f || Mathf.Abs(rootScale.z) < 0.0001f)
                return null;

            var items = new List<FurnitureHomeSeedItem>();
            foreach (FurnitureUsePoint point in GetComponentsInChildren<FurnitureUsePoint>(true))
            {
                if (point == null || !point.isActiveAndEnabled || !point.gameObject.activeInHierarchy) continue;
                Vector3 position = roomRoot.InverseTransformPoint(point.transform.position);
                Quaternion rotation = Quaternion.Inverse(roomRoot.rotation) * point.transform.rotation;
                Vector3 worldScale = point.transform.lossyScale;
                items.Add(new FurnitureHomeSeedItem
                {
                    legacyInstanceId = point.InstanceId,
                    legacyUsePointId = point.UsePointId,
                    positionMeters = new FurnitureVector3 { x = position.x, y = position.y, z = position.z },
                    rotation = new FurnitureQuaternion { x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w },
                    scale = new FurnitureVector3 { x = worldScale.x / rootScale.x, y = worldScale.y / rootScale.y, z = worldScale.z / rootScale.z }
                });
            }
            return FurnitureHomeSeedBuilder.Create(homeId, roomId, "testbox-scene-seed-v1", snapshot, items);
        }

        public IReadOnlyList<FurnitureInteractionOption> ResolveOptions(FurnitureUsePoint point,
            string explicitDefinitionId = "", string explicitPointId = "")
        {
            var options = ResolveCurrentOptions(point, explicitDefinitionId, explicitPointId);
            string blockedReason = activityHost == null || activityHost.HasScenarioOwner ? "scenario_owns_interaction" :
                activityHost.Host == null || activityHost.Host.Current != null ? "activity_busy" :
                activityHost.IsFurnitureUsePointClaimed(point) ? "point_in_use" : string.Empty;
            if (blockedReason.Length == 0) return options;
            return options.Select(option => new FurnitureInteractionOption
            {
                state = "unavailable", reasonCode = blockedReason, capabilityId = option.capabilityId,
                definitionId = option.definitionId, instanceId = option.instanceId, pointId = option.pointId,
                usePointId = option.usePointId, pointRevision = option.pointRevision, catalogueHash = option.catalogueHash,
                arrangementId = option.arrangementId, revisionToken = option.revisionToken,
                safeReasonKey = "furniture." + blockedReason
            }).ToArray();
        }

        private IReadOnlyList<FurnitureInteractionOption> ResolveCurrentOptions(FurnitureUsePoint point,
            string explicitDefinitionId, string explicitPointId)
        {
            return ResolveOptions(Catalogue,
                activityHost == null || activityHost.Host == null ? new string[0] : activityHost.Host.SupportedCapabilityIds,
                point, explicitDefinitionId, explicitPointId, IsPointReachable);
        }

        public static IReadOnlyList<FurnitureInteractionOption> ResolveOptions(FurnitureCatalogueSnapshot snapshot,
            IEnumerable<string> installedCapabilities, FurnitureUsePoint point, string explicitDefinitionId,
            string explicitPointId, Func<FurnitureUsePoint, bool> isReachable)
        {
            if (snapshot == null || point == null) return new FurnitureInteractionOption[0];
            string definitionId;
            string pointId;
            if (!FurnitureInteractionPolicy.TryResolvePoint(snapshot, point.InstanceId, point.UsePointId,
                explicitDefinitionId, explicitPointId, out definitionId, out pointId))
                return new FurnitureInteractionOption[0];

            FurnitureDefinition definition = snapshot.definitions.FirstOrDefault(item => item.definitionId == definitionId);
            FurnitureFunctionalPoint functionalPoint = definition == null || definition.points == null
                ? null : definition.points.FirstOrDefault(item => item != null && item.pointId == pointId);
            if (functionalPoint == null) return new FurnitureInteractionOption[0];
            var installed = new HashSet<string>(installedCapabilities ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            bool reachable = isReachable != null && isReachable(point);
            int revision = point.Revision;
            var options = new List<FurnitureInteractionOption>();
            foreach (string capability in functionalPoint.useIds ?? new List<string>())
            {
                bool physicallyDeclared = point.Supports(capability);
                bool executorInstalled = installed.Contains(capability);
                string reason = !physicallyDeclared ? "use_point_unavailable" :
                    !executorInstalled ? "executor_unavailable" : !reachable ? "approach_unavailable" : string.Empty;
                options.Add(new FurnitureInteractionOption
                {
                    state = reason.Length == 0 ? "available" : "unavailable",
                    reasonCode = reason,
                    capabilityId = capability,
                    definitionId = definitionId,
                    instanceId = point.InstanceId,
                    pointId = pointId,
                    usePointId = point.UsePointId,
                    pointRevision = revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    catalogueHash = snapshot.catalogueHash,
                    safeReasonKey = reason.Length == 0 ? string.Empty : "furniture." + reason
                });
            }
            return options.AsReadOnly();
        }

        private bool IsPointReachable(FurnitureUsePoint point)
        { return activityHost != null && activityHost.IsFurnitureUsePointReachable(point); }

        public bool IsCurrent(FurnitureUsePoint point, FurnitureInteractionBinding binding, string actorId)
        {
            if (point == null || binding == null || binding.actorId != actorId) return false;
            var option = ResolveCurrentOptions(point, binding.definitionId, binding.pointId)
                .FirstOrDefault(item => item.capabilityId == binding.capabilityId);
            return FurnitureInteractionPolicy.IsCurrent(binding, option, actorId) &&
                point.IsSameRevision(ParseRevision(binding.pointRevision));
        }

        public ActivityRequestResult TryExecute(FurnitureUsePoint point, FurnitureInteractionBinding binding)
        {
            if (binding == null || binding.actorId != "altera" || !IsCurrent(point, binding, "altera"))
                return ActivityRequestResult.Unavailable("stale_or_unavailable_choice");
            return activityHost == null
                ? ActivityRequestResult.Unavailable("activity_host_unavailable")
                : activityHost.TryRequestFurnitureUse(point, binding.capabilityId, binding.requestId,
                    ParseRevision(binding.pointRevision));
        }

        public FurnitureFunctionTargetValidation ValidateCurrent(FurnitureFunctionReservationRequest request,
            string cataloguePointId)
        {
            if (request == null || string.IsNullOrWhiteSpace(cataloguePointId) || activityHost == null)
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Unavailable, diagnosticCode = "furniture_scene_target_unavailable" };

            FurnitureCatalogueSnapshot snapshot = Catalogue;
            if (snapshot == null || snapshot.diagnostics.Any(item => item.severity == "error"))
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Unavailable, diagnosticCode = "furniture_catalogue_unavailable" };

            FurnitureUsePoint[] matches = GetComponentsInChildren<FurnitureUsePoint>(true)
                .Where(point => point != null && point.InstanceId == request.instanceId && point.UsePointId == request.usePointId)
                .ToArray();
            if (matches.Length != 1 || !matches[0].isActiveAndEnabled || !matches[0].gameObject.activeInHierarchy)
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Unavailable, diagnosticCode = "furniture_point_unavailable" };

            FurnitureUsePoint point = matches[0];
            if (!point.Supports(request.useId))
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Unavailable, diagnosticCode = "furniture_use_unavailable" };
            if (!point.IsSameRevision(request.usePointRevision))
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Conflict, diagnosticCode = "stale_furniture_binding" };
            if (!activityHost.IsFurnitureUsePointClaimed(point))
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Unavailable, diagnosticCode = "furniture_reservation_unavailable" };

            string resolvedDefinitionId;
            string resolvedPointId;
            if (!FurnitureInteractionPolicy.TryResolvePoint(snapshot, request.instanceId, request.usePointId,
                string.Empty, string.Empty, out resolvedDefinitionId, out resolvedPointId))
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Unavailable, diagnosticCode = "furniture_point_mapping_unavailable" };
            if (resolvedDefinitionId != request.definitionId || resolvedPointId != cataloguePointId)
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Conflict, diagnosticCode = "furniture_binding_mismatch" };

            FurnitureDefinition definition = snapshot.definitions.FirstOrDefault(item => item.definitionId == resolvedDefinitionId);
            FurnitureFunctionalPoint functionalPoint = definition == null || definition.points == null
                ? null : definition.points.FirstOrDefault(item => item != null && item.pointId == resolvedPointId);
            if (functionalPoint == null || functionalPoint.useIds == null || !functionalPoint.useIds.Contains(request.useId, StringComparer.Ordinal))
                return new FurnitureFunctionTargetValidation
                { status = FurnitureFunctionTargetStatuses.Unavailable, diagnosticCode = "furniture_function_binding_invalid" };

            return new FurnitureFunctionTargetValidation
            { status = FurnitureFunctionTargetStatuses.Available, diagnosticCode = string.Empty };
        }

        public bool ApplyCommittedPowerState(GameObject furnitureRoot, HomeFurnitureLayoutInstance committedInstance)
        {
            if (furnitureRoot == null) return false;
            FurniturePowerStateView view = furnitureRoot.GetComponentInChildren<FurniturePowerStateView>(true);
            if (view == null) return false;
            view.ApplyCommittedState(committedInstance);
            return true;
        }

        private static int ParseRevision(string value)
        {
            int parsed;
            return int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out parsed) ? parsed : -1;
        }
    }
}
