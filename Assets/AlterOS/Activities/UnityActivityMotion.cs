using UnityEngine;
using UnityEngine.AI;

namespace AlterOS.Activities
{
    /// <summary>NavMesh-backed movement port. It never teleports or accepts partial routes.</summary>
    public sealed class UnityActivityMotion : MonoBehaviour, IActivityMotion
    {
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private Transform player;
        [SerializeField, Min(0.1f)] private float sampleRadius = 0.5f;
        [SerializeField, Min(0.01f)] private float arrivalVelocity = 0.1f;
        [SerializeField, Min(0.1f)] private float routePendingTimeout = 3f;
        private NavMeshPath candidatePath;
        private bool suspended;
        private bool awaitingPath;
        private float requestTime;

        public bool PlayerAvailable { get { return player != null && player.gameObject.activeInHierarchy; } }
        public bool NavigationAvailable { get { EnsureAgent(); return UsableAgent; } }
        public float DistanceToPlayer { get { EnsureAgent(); return PlayerAvailable && agent != null ? HorizontalDistance(agent.transform.position, player.position) : float.PositiveInfinity; } }
        public float RemainingDistance
        {
            get
            {
                EnsureAgent();
                if (!UsableAgent || !agent.hasPath || agent.pathPending) return float.PositiveInfinity;
                return agent.remainingDistance;
            }
        }
        public bool HasArrived
        {
            get
            {
                EnsureAgent();
                return UsableAgent && agent.hasPath && !agent.pathPending &&
                    agent.pathStatus == NavMeshPathStatus.PathComplete &&
                    agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, 0.05f) &&
                    agent.velocity.sqrMagnitude <= arrivalVelocity * arrivalVelocity;
            }
        }
        public bool IsBlocked
        {
            get
            {
                EnsureAgent();
                if (!UsableAgent) return true;
                if (agent.pathPending) return Time.realtimeSinceStartup - requestTime >= routePendingTimeout;
                if (!agent.hasPath) return awaitingPath && Time.realtimeSinceStartup - requestTime >= routePendingTimeout;
                return agent.pathStatus != NavMeshPathStatus.PathComplete;
            }
        }

        private bool UsableAgent { get { EnsureAgent(); return agent != null && agent.enabled && agent.gameObject.activeInHierarchy && agent.isOnNavMesh; } }

        private void Awake()
        {
            candidatePath = new NavMeshPath();
            EnsureAgent();
        }

        public bool TryResolveNearPlayer(float separation, out Vector3 destination)
        {
            destination = Vector3.zero;
            if (!PlayerAvailable || !UsableAgent || separation <= 0f) return false;
            EnsureCandidatePath();

            NavMeshHit playerHit;
            if (!NavMesh.SamplePosition(player.position, out playerHit, Mathf.Max(sampleRadius, agent.height), agent.areaMask))
                return false;

            // Sample a ring outside the player's collision volume. Complete path and
            // deterministic angle order select a reachable point without teleporting.
            const int sampleCount = 16;
            float bestPathLength = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < sampleCount; i++)
            {
                float angle = (Mathf.PI * 2f * i) / sampleCount;
                Vector3 candidate = playerHit.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * separation;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidate, out hit, sampleRadius, agent.areaMask)) continue;
                if (HorizontalDistance(hit.position, playerHit.position) < separation * 0.8f) continue;
                if (!agent.CalculatePath(hit.position, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete) continue;
                float length = PathLength(candidatePath);
                if (length >= bestPathLength) continue;
                bestPathLength = length;
                destination = hit.position;
                found = true;
            }
            return found;
        }

        public bool TryMoveTo(Vector3 destination)
        {
            if (!UsableAgent || !IsFinite(destination)) return false;
            EnsureCandidatePath();
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(destination, out hit, sampleRadius, agent.areaMask)) return false;
            if (!agent.CalculatePath(hit.position, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete) return false;
            if (!agent.SetPath(candidatePath)) return false;
            awaitingPath = true;
            requestTime = Time.realtimeSinceStartup;
            suspended = false;
            return true;
        }

        public bool CanReach(Vector3 destination)
        {
            if (!UsableAgent || !IsFinite(destination)) return false;
            EnsureCandidatePath();
            NavMeshHit hit;
            return NavMesh.SamplePosition(destination, out hit, sampleRadius, agent.areaMask) &&
                agent.CalculatePath(hit.position, candidatePath) && candidatePath.status == NavMeshPathStatus.PathComplete;
        }

        public float DistanceTo(Vector3 position)
        {
            EnsureAgent();
            return agent == null ? float.PositiveInfinity : Vector3.Distance(agent.transform.position, position);
        }

        public void Suspend()
        {
            if (!UsableAgent || suspended) return;
            suspended = true;
            agent.isStopped = true;
        }

        public void Resume()
        {
            if (!UsableAgent || !suspended) return;
            suspended = false;
            agent.isStopped = false;
        }

        public void Clear()
        {
            awaitingPath = false;
            suspended = false;
            if (!UsableAgent) return;
            agent.isStopped = false;
            agent.ResetPath();
        }

        public void FacePlayer()
        {
            EnsureAgent();
            if (!PlayerAvailable || agent == null) return;
            Face(player.position);
        }

        public void Face(Vector3 worldPosition)
        {
            EnsureAgent();
            if (agent == null) return;
            Vector3 direction = worldPosition - agent.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) return;
            agent.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private void Update()
        {
            if (UsableAgent && awaitingPath && !agent.pathPending && agent.hasPath &&
                agent.pathStatus == NavMeshPathStatus.PathComplete) awaitingPath = false;
        }

        private static float PathLength(NavMeshPath path)
        {
            Vector3[] corners = path.corners;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private void EnsureAgent()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
        }

        private void EnsureCandidatePath()
        {
            // Editor path queries can precede Awake, but serialization must never allocate it.
            if (candidatePath == null) candidatePath = new NavMeshPath();
        }

        private static bool IsFinite(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
                !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        }
    }
}
