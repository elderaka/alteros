using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Content;
using AlterOS.Furniture;
using AlterOS.Scenarios;
using UnityEngine;
using UnityEngine.AI;

namespace AlterOS.Activities
{
    /// <summary>Main-thread Unity composition root; scene references are Inspector-authored.</summary>
    public sealed class ActivitySceneHost : MonoBehaviour
    {
        [SerializeField] private ActivityDefinition[] definitions = new ActivityDefinition[0];
        [SerializeField] private UnityActivityMotion motion;
        [SerializeField] private NavMeshData navigationData;
        [SerializeField] private FurnitureUsePoint[] furnitureUsePoints = new FurnitureUsePoint[0];
        [SerializeField] private MonoBehaviour hydrationSinkComponent;
        [SerializeField] private MonoBehaviour sequencePrimitivePortComponent;

        private ActivityHost host;
        private ScenarioActivityAdapter scenarioPort;
        private FurnitureRegistry furniture;
        private IHydrationCheckpointSink hydrationSink;
        private IActivityPrimitivePort sequencePrimitivePort;
        private NavMeshDataInstance navigationInstance;
        private bool hostBuildAttempted;
        private bool scenarioOwnerActive;

        public ActivityHost Host { get { return host; } }
        public ScenarioActivityAdapter ScenarioPort { get { return scenarioPort; } }

        /// <summary>Conversation owner reports running or suspended Scenario state to gate direct E actions.</summary>
        public void SetScenarioOwnerActive(bool active) { scenarioOwnerActive = active; }

        public bool IsFurnitureUsePointReachable(FurnitureUsePoint point)
        {
            return point != null && host != null && motion != null && point.Approach != null &&
                (furnitureUsePoints ?? new FurnitureUsePoint[0]).Contains(point) && motion.CanReach(point.Approach.position);
        }

        public bool IsFurnitureUsePointClaimed(FurnitureUsePoint point)
        { return furniture != null && furniture.IsClaimed(point); }

        public bool TryReserveSequenceFurniture(string useId, string requestedObjectId, string actionInvocationId,
            Func<FurnitureUsePoint, bool> additionalEligibility, out FurnitureBinding binding)
        {
            binding = null;
            return OwnsSequenceAction(actionInvocationId) && furniture != null && furniture.TryReserve(useId, requestedObjectId, actionInvocationId,
                additionalEligibility, out binding);
        }

        public bool IsCurrentSequenceFurnitureBinding(FurnitureBinding binding)
        { return binding != null && OwnsSequenceAction(binding.activityId) && furniture != null && furniture.IsValid(binding); }

        private bool OwnsSequenceAction(string actionInvocationId)
        {
            ActivityView current = host == null ? null : host.Current;
            return current != null && !current.isTerminal &&
                current.capabilityId == ActivityCapabilityIds.ActivitySequence &&
                !string.IsNullOrWhiteSpace(actionInvocationId) &&
                string.Equals(current.activityId, actionInvocationId, StringComparison.Ordinal);
        }

        public void ReleaseSequenceFurniture(string actionInvocationId)
        { if (furniture != null) furniture.Release(actionInvocationId); }

        public bool TryMoveSequenceTo(Vector3 destination)
        { return motion != null && motion.TryMoveTo(destination); }

        public bool TryResolveSequenceNearPlayer(float separation, out Vector3 destination)
        {
            destination = Vector3.zero;
            return motion != null && motion.TryResolveNearPlayer(separation, out destination) && motion.TryMoveTo(destination);
        }

        public bool SequenceMovementArrived { get { return motion != null && motion.HasArrived; } }
        public bool SequenceMovementBlocked { get { return motion == null || motion.IsBlocked; } }
        public void FaceSequencePosition(Vector3 position) { if (motion != null) motion.Face(position); }
        public void ClearSequenceMovement() { if (motion != null) motion.Clear(); }

        public bool HasSequencePrimitivePort { get { return sequencePrimitivePort != null; } }

        public bool TryClaimFurnitureForEdit(FurnitureUsePoint point, string ownerId, out FurnitureEditClaim claim)
        {
            claim = null;
            return furniture != null && furniture.TryClaimForEdit(point, ownerId, out claim);
        }

        public bool ReleaseFurnitureEditClaim(FurnitureEditClaim claim)
        { return furniture != null && furniture.ReleaseEditClaim(claim); }

        public bool HasScenarioOwner { get { return scenarioOwnerActive; } }

        /// <summary>Starts the installed Alterra activity for one exact E-selected point under the exclusive host.</summary>
        public ActivityRequestResult TryRequestFurnitureUse(FurnitureUsePoint point, string capabilityId,
            string requestIdentity, int expectedPointRevision)
        {
            if (string.IsNullOrWhiteSpace(requestIdentity)) return ActivityRequestResult.Unavailable("invalid_request_identity");
            if (scenarioOwnerActive) return ActivityRequestResult.Unavailable("scenario_owns_interaction");
            if (host == null) return ActivityRequestResult.Unavailable("activity_host_unavailable");
            if (point == null || !point.IsSameRevision(expectedPointRevision) || !point.Supports(capabilityId) ||
                !IsFurnitureUsePointReachable(point)) return ActivityRequestResult.Unavailable("stale_or_unavailable_target");
            if (IsFurnitureUsePointClaimed(point)) return ActivityRequestResult.Unavailable("point_in_use");
            if (!host.SupportedCapabilityIds.Contains(capabilityId, StringComparer.Ordinal))
                return ActivityRequestResult.Unavailable("executor_unavailable");

            var capabilities = new[]
            {
                new ScenarioCapabilitySnapshot(capabilityId, new[]
                { new ScenarioArgumentBinding("target", new[] { point.UsePointId }) })
            };
            var objects = new[]
            {
                new ScenarioObjectSnapshot(point.UsePointId, "use_point"),
                new ScenarioObjectSnapshot(point.InstanceId, "furniture")
            };
            string runId = "furniture-e-" + requestIdentity;
            const string stepId = "request";
            var context = new ScenarioRuntimeSnapshot("furniture-interaction", runId, runId, 0, stepId,
                new string[0], new string[0], capabilities, objects, new string[0]);
            var request = new ActivityRequest(runId, stepId, capabilityId,
                new[] { new ScenarioActionArgument { name = "target", value = point.UsePointId } });
            return host.RequestFurnitureInteraction(request, context);
        }

        public ActivityRequestResult TryRequestSequence(ActivitySequenceRequest request)
        { return host == null ? ActivityRequestResult.Unavailable("activity_host_unavailable") : host.RequestSequence(request); }

        public bool CompleteSequencePrimitive(ActivityPrimitiveCompletion completion)
        { return host != null && host.CompletePrimitive(completion); }

        public static ScenarioCapabilitySnapshot BuildSequenceFurnitureCapability(FurnitureCatalogueSnapshot catalogue)
        {
            if (catalogue == null || catalogue.diagnostics == null ||
                catalogue.diagnostics.Any(item => item != null && item.severity == "error")) return null;
            string[] useIds = (catalogue.definitions ?? new List<FurnitureDefinition>())
                .Where(item => item != null).SelectMany(item => item.points ?? new List<FurnitureFunctionalPoint>())
                .Where(item => item != null).SelectMany(item => item.useIds ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
            return useIds.Length == 0 ? null : new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.ReserveNearest,
                new[] { new ScenarioArgumentBinding("useId", useIds) });
        }


        public IReadOnlyList<ScenarioCapabilitySnapshot> CaptureScenarioCapabilities(out ScenarioObjectSnapshot[] knownObjects)
        {
            var capabilities = new List<ScenarioCapabilitySnapshot>();
            var objects = new List<ScenarioObjectSnapshot>();
            knownObjects = objects.ToArray();
            if (host == null || motion == null || !motion.NavigationAvailable || !motion.PlayerAvailable)
                return capabilities.AsReadOnly();

            if (sequencePrimitivePort != null)
            {
                capabilities.Add(new ScenarioCapabilitySnapshot(ActivityCapabilityIds.ActivitySequence,
                    new ScenarioArgumentBinding[0]));
                capabilities.Add(new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.Complete,
                    new ScenarioArgumentBinding[0]));
                if (motion.NavigationAvailable && motion.PlayerAvailable)
                    capabilities.Add(new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.MoveNearPlayer,
                        new ScenarioArgumentBinding[0]));

                IFurnitureCatalogueSnapshotSource[] catalogueSources = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                    .OfType<IFurnitureCatalogueSnapshotSource>().ToArray();
                FurnitureCatalogueSnapshot sequenceFurniture = catalogueSources.Length == 1
                    ? catalogueSources[0].Catalogue : null;
                ScenarioCapabilitySnapshot furnitureCapability = BuildSequenceFurnitureCapability(sequenceFurniture);
                if (furnitureCapability != null) capabilities.Add(furnitureCapability);

                RuntimeContentStore runtimeContent = RuntimeContentStore.Current;
                string[] dialogueIds = runtimeContent == null ? new string[0] :
                    runtimeContent.Release.dependencies.Where(item => item != null && item.kind == "dialogue")
                        .Select(item => item.id).Distinct(StringComparer.Ordinal).ToArray();
                if (dialogueIds.Length > 0)
                    capabilities.Add(new ScenarioCapabilitySnapshot(ActivitySequenceCapabilityIds.Bark,
                        new[] { new ScenarioArgumentBinding("dialogueId", dialogueIds) }));
            }

            foreach (string capabilityId in host.SupportedCapabilityIds)
            {
                if (capabilityId == ActivityCapabilityIds.ComeHere || capabilityId == ActivityCapabilityIds.FollowPlayer)
                {
                    capabilities.Add(new ScenarioCapabilitySnapshot(capabilityId, new[]
                    { new ScenarioArgumentBinding("target", new[] { "player" }) }));
                    continue;
                }

                if (capabilityId != ActivityCapabilityIds.DrinkWater) continue;
                bool reachableUsePoint = false;
                foreach (FurnitureUsePoint point in furnitureUsePoints ?? new FurnitureUsePoint[0])
                {
                    if (point == null || !point.Supports(ActivityCapabilityIds.DrinkWater) || point.Approach == null ||
                        !motion.CanReach(point.Approach.position) || IsFurnitureUsePointClaimed(point)) continue;
                    reachableUsePoint = true;
                    objects.Add(new ScenarioObjectSnapshot(point.UsePointId, "use_point"));
                    objects.Add(new ScenarioObjectSnapshot(point.InstanceId, "furniture"));
                }
                if (reachableUsePoint)
                    capabilities.Add(new ScenarioCapabilitySnapshot(capabilityId, new[]
                    { new ScenarioArgumentBinding("target", objects.Where(item => item.kind == "use_point")
                        .Select(item => item.objectId).Concat(new[] { "available_dispenser" })
                        .Distinct(StringComparer.Ordinal).ToArray()) }));
            }

            knownObjects = objects.Distinct().ToArray();
            return capabilities.AsReadOnly();
        }

        private void OnEnable()
        {
            scenarioPort = new ScenarioActivityAdapter(host);
            if (navigationData != null && !navigationInstance.valid)
                navigationInstance = NavMesh.AddNavMeshData(navigationData, transform.position, transform.rotation);
        }

        private void Update()
        {
            if (host == null && !hostBuildAttempted && motion != null &&
                motion.NavigationAvailable && motion.PlayerAvailable)
            {
                hostBuildAttempted = true;
                BuildHost();
            }
            double nowSeconds = Time.realtimeSinceStartupAsDouble;
            IActivitySequencePollingPort pollingPort = sequencePrimitivePort as IActivitySequencePollingPort;
            if (pollingPort != null && host != null)
                foreach (ActivityPrimitiveCompletion completion in pollingPort.Tick(nowSeconds))
                    host.CompletePrimitive(completion);
            if (host != null) host.Tick(nowSeconds);
        }

        private void OnDisable()
        {
            if (host != null)
            {
                host.Shutdown("interrupted_by_quit");
                host.LifecycleChanged -= LogActivityLifecycle;
            }
            host = null;
            furniture = null;
            hostBuildAttempted = false;
            if (navigationInstance.valid) navigationInstance.Remove();
            scenarioPort = new ScenarioActivityAdapter(null);
            scenarioOwnerActive = false;
        }

        private void OnDestroy()
        {
            if (host != null) host.Shutdown("interrupted_by_quit");
        }

        private void BuildHost()
        {
            if (motion == null) motion = GetComponent<UnityActivityMotion>();
            hydrationSink = hydrationSinkComponent as IHydrationCheckpointSink;
            if (motion == null)
            {
                Debug.LogError("ActivitySceneHost requires an authored UnityActivityMotion reference.", this);
                return;
            }

            FurnitureUsePoint[] points = furnitureUsePoints ?? new FurnitureUsePoint[0];
            try
            {
                furniture = new FurnitureRegistry(points, point => point != null && point.Approach != null && motion.CanReach(point.Approach.position),
                    point => point == null || point.Approach == null ? float.PositiveInfinity : motion.DistanceTo(point.Approach.position));
            }
            catch (ArgumentException exception)
            {
                Debug.LogError("Furniture use-point registry is invalid: " + exception.Message, this);
                return;
            }

            var settings = new List<ActivitySettings>();
            var factories = new Dictionary<ActivityExecutorKind, ActivityExecutionFactory>();
            if (motion.NavigationAvailable && motion.PlayerAvailable)
                factories.Add(ActivityExecutorKind.Movement, context => new MovementActivity(context, motion,
                    context.settings.capabilityId == ActivityCapabilityIds.FollowPlayer));
            if (motion.NavigationAvailable && motion.PlayerAvailable && hydrationSink != null && points.Length != 0)
                factories.Add(ActivityExecutorKind.DrinkWater, context => new DrinkWaterActivity(context, furniture, motion, hydrationSink));

            sequencePrimitivePort = sequencePrimitivePortComponent != null && sequencePrimitivePortComponent.isActiveAndEnabled
                ? sequencePrimitivePortComponent as IActivityPrimitivePort : null;
            if (sequencePrimitivePortComponent != null && sequencePrimitivePortComponent.isActiveAndEnabled && sequencePrimitivePort == null)
                Debug.LogError("ActivitySceneHost sequencePrimitivePortComponent must implement IActivityPrimitivePort.", sequencePrimitivePortComponent);
            if (sequencePrimitivePort != null)
            {
                ActivitySequenceCatalog sequenceCatalog = ActivitySequenceCatalogFactory.CreateDefault();
                factories.Add(ActivityExecutorKind.ActivitySequence, context =>
                    new ActivitySequenceExecution(context, new ActivitySequenceExecutor(sequenceCatalog.Primitives, sequencePrimitivePort),
                        sequencePrimitivePort as IActivitySequenceResourceOwner));
                settings.Add(new ActivitySettings(ActivityCapabilityIds.ActivitySequence, ActivityExecutorKind.ActivitySequence));
            }

            foreach (ActivityDefinition definition in definitions ?? new ActivityDefinition[0])
            {
                if (definition == null) continue;
                ActivitySettings copy;
                string error;
                if (!definition.TryCreateSettings(out copy, out error))
                {
                    Debug.LogError("Activity definition is invalid: " + error, definition);
                    continue;
                }
                if (!factories.ContainsKey(copy.executorKind)) continue;
                settings.Add(copy);
            }

            if (settings.Count == 0) return;
            try
            {
                host = new ActivityHost(settings, factories);
                host.LifecycleChanged += LogActivityLifecycle;
                scenarioPort = new ScenarioActivityAdapter(host);
            }
            catch (ArgumentException exception) { Debug.LogError("Activity host configuration is invalid: " + exception.Message, this); }
        }

        private void LogActivityLifecycle(ActivityEvent item)
        {
            ActivityView view;
            string capability = host != null && host.TryGetOutcome(item.activityId, out view) ? view.capabilityId : "unknown";
            string stage = item.kind == ActivityEventKind.Accepted ? "start" : "finish";
            Debug.Log("[Alterra Activity] " + stage + " action=" + capability + " id=" + item.activityId +
                " phase=" + item.phase + " outcome=" + item.terminalReason + " run=" + item.runId, this);
        }
    }

    /// <summary>Small conversation-facing facade over the scene-owned activity port.</summary>
    public sealed class ActivityInteractionBridge
    {
        private readonly ActivitySceneHost sceneHost;

        public ActivityInteractionBridge(ActivitySceneHost host) { sceneHost = host; }

        public IReadOnlyList<ScenarioCapabilitySnapshot> CaptureCapabilities(out ScenarioObjectSnapshot[] objects)
        {
            if (sceneHost == null)
            {
                objects = new ScenarioObjectSnapshot[0];
                return new ScenarioCapabilitySnapshot[0];
            }
            return sceneHost.CaptureScenarioCapabilities(out objects);
        }

        public void SetScenarioOwnerActive(bool active)
        { if (sceneHost != null) sceneHost.SetScenarioOwnerActive(active); }

        public ActivityRequestResult TryRequest(ActivityRequest request, ScenarioRuntimeSnapshot context)
        {
            return sceneHost == null || sceneHost.ScenarioPort == null
                ? ActivityRequestResult.Unavailable("activity_host_unavailable")
                : sceneHost.ScenarioPort.TryRequest(request, context);
        }

        public ActivityRequestResult TryRequestSequence(ActivitySequenceRequest request)
        { return sceneHost == null ? ActivityRequestResult.Unavailable("activity_host_unavailable") : sceneHost.TryRequestSequence(request); }

        public bool CompleteSequencePrimitive(ActivityPrimitiveCompletion completion)
        { return sceneHost != null && sceneHost.CompleteSequencePrimitive(completion); }


        public IReadOnlyList<ScenarioEvent> DrainScenarioEvents(ScenarioInstanceView running, ScenarioInstanceView suspended)
        {
            return sceneHost == null || sceneHost.ScenarioPort == null
                ? new ScenarioEvent[0]
                : sceneHost.ScenarioPort.DrainScenarioEvents(running, suspended);
        }

        public bool Stop(string activityId, string reason)
        { return sceneHost != null && sceneHost.ScenarioPort != null && sceneHost.ScenarioPort.Stop(activityId, reason); }

        public bool BeginExchange(string activityId, string exchangeId)
        { return sceneHost != null && sceneHost.ScenarioPort != null && sceneHost.ScenarioPort.BeginExchange(activityId, exchangeId); }

        public bool EndExchange(string activityId, string exchangeId)
        { return sceneHost != null && sceneHost.ScenarioPort != null && sceneHost.ScenarioPort.EndExchange(activityId, exchangeId); }

        public ActivityView Current { get { return sceneHost == null || sceneHost.ScenarioPort == null ? null : sceneHost.ScenarioPort.Current; } }
    }
}
