using AlterOS.Furniture;
using UnityEngine;

namespace Pleiades.Furniture
{
    /// <summary>Applies committed per-instance power state without changing shared material assets.</summary>
    public sealed class FurniturePowerStateView : MonoBehaviour
    {
        [SerializeField] private Renderer[] stateRenderers = new Renderer[0];
        [SerializeField] private string stateProperty = "_FurniturePowerState";

        private MaterialPropertyBlock propertyBlock;
        private int propertyId;
        private string cachedPropertyName;

        public FurniturePowerVisualState CurrentState { get; private set; } = FurniturePowerVisualState.Unknown;

        public void ApplyCommittedState(HomeFurnitureLayoutInstance instance)
        {
            Apply(FurniturePowerStateProjection.FromResources(instance == null ? null : instance.resourceState));
        }

        public void Apply(FurniturePowerVisualState state)
        {
            if (!System.Enum.IsDefined(typeof(FurniturePowerVisualState), state))
                state = FurniturePowerVisualState.Invalid;
            CurrentState = state;
            if (stateRenderers == null || stateRenderers.Length == 0 || string.IsNullOrWhiteSpace(stateProperty)) return;

            EnsurePropertyBlock();
            foreach (Renderer target in stateRenderers)
            {
                if (target == null) continue;
                target.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(propertyId, (float)state);
                target.SetPropertyBlock(propertyBlock);
            }
        }

        private void EnsurePropertyBlock()
        {
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            if (cachedPropertyName == stateProperty) return;
            cachedPropertyName = stateProperty;
            propertyId = Shader.PropertyToID(stateProperty);
        }
    }
}
