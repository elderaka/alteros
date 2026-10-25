using UnityEngine;

namespace AlterOS.Activities
{
    /// <summary>Unity-only motion observations/commands; path validity stays in the adapter.</summary>
    public interface IActivityMotion
    {
        bool TryResolveNearPlayer(float separation, out Vector3 destination);
        bool TryMoveTo(Vector3 destination);
        void Suspend();
        void Resume();
        void Clear();
        void FacePlayer();
        void Face(Vector3 worldPosition);
        bool PlayerAvailable { get; }
        float DistanceToPlayer { get; }
        float RemainingDistance { get; }
        bool HasArrived { get; }
        bool IsBlocked { get; }
    }
}
