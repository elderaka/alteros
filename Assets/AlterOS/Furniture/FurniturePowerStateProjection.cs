using System;
using System.Collections.Generic;

namespace AlterOS.Furniture
{
    public enum FurniturePowerVisualState
    {
        Unknown = 0,
        Off = 1,
        On = 2,
        Invalid = 3
    }

    /// <summary>Projects committed resource state into a safe tri-state view value.</summary>
    public static class FurniturePowerStateProjection
    {
        public static FurniturePowerVisualState FromResources(IList<FurnitureResourceState> resources)
        {
            if (resources == null) return FurniturePowerVisualState.Unknown;

            FurnitureResourceState power = null;
            foreach (FurnitureResourceState resource in resources)
            {
                if (resource == null || resource.resourceId != FurniturePowerResourceIds.DevicePower) continue;
                if (power != null) return FurniturePowerVisualState.Invalid;
                power = resource;
            }

            if (power == null || !power.known) return FurniturePowerVisualState.Unknown;
            if (double.IsNaN(power.value) || double.IsInfinity(power.value)) return FurniturePowerVisualState.Invalid;
            if (power.value == (double)FurniturePowerState.Off) return FurniturePowerVisualState.Off;
            if (power.value == (double)FurniturePowerState.On) return FurniturePowerVisualState.On;
            return FurniturePowerVisualState.Invalid;
        }
    }
}
