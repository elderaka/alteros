using System.Collections.Generic;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurniturePowerStateProjectionTests
    {
        [Test]
        public void MissingAndUnknownPowerRemainUnknown()
        {
            Assert.AreEqual(FurniturePowerVisualState.Unknown,
                FurniturePowerStateProjection.FromResources(null));
            Assert.AreEqual(FurniturePowerVisualState.Unknown,
                FurniturePowerStateProjection.FromResources(new List<FurnitureResourceState>()));
            Assert.AreEqual(FurniturePowerVisualState.Unknown,
                FurniturePowerStateProjection.FromResources(new List<FurnitureResourceState>
                {
                    new FurnitureResourceState { resourceId = FurniturePowerResourceIds.DevicePower, value = 1, known = false }
                }));
        }

        [Test]
        public void KnownNumericPowerMapsOnlyZeroAndOne()
        {
            Assert.AreEqual(FurniturePowerVisualState.Off,
                FurniturePowerStateProjection.FromResources(new List<FurnitureResourceState>
                {
                    new FurnitureResourceState { resourceId = FurniturePowerResourceIds.DevicePower, value = 0, known = true }
                }));
            Assert.AreEqual(FurniturePowerVisualState.On,
                FurniturePowerStateProjection.FromResources(new List<FurnitureResourceState>
                {
                    new FurnitureResourceState { resourceId = FurniturePowerResourceIds.DevicePower, value = 1, known = true }
                }));
        }

        [Test]
        public void InvalidAndDuplicateKnownPowerValuesFailClosed()
        {
            Assert.AreEqual(FurniturePowerVisualState.Invalid,
                FurniturePowerStateProjection.FromResources(new List<FurnitureResourceState>
                {
                    new FurnitureResourceState { resourceId = FurniturePowerResourceIds.DevicePower, value = 2, known = true }
                }));
            Assert.AreEqual(FurniturePowerVisualState.Invalid,
                FurniturePowerStateProjection.FromResources(new List<FurnitureResourceState>
                {
                    new FurnitureResourceState { resourceId = FurniturePowerResourceIds.DevicePower, value = 0, known = true },
                    new FurnitureResourceState { resourceId = FurniturePowerResourceIds.DevicePower, value = 1, known = true }
                }));
        }

        [Test]
        public void UnrelatedResourceStateDoesNotChangePowerProjection()
        {
            Assert.AreEqual(FurniturePowerVisualState.Unknown,
                FurniturePowerStateProjection.FromResources(new List<FurnitureResourceState>
                {
                    new FurnitureResourceState { resourceId = "device.volume", value = 1, known = true }
                }));
        }
    }
}
