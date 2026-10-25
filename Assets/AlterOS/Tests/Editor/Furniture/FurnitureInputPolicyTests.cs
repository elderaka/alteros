using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureInputPolicyTests
    {
        [Test]
        public void EmptyHandsMapTalkPickupAndInventoryToTheirApprovedControls()
        {
            var context = new FurnitureInputContext();

            Assert.AreEqual(FurnitureInputIntent.Interact, FurnitureInputPolicy.Resolve(FurnitureInputControl.T, context));
            Assert.AreEqual(FurnitureInputIntent.PickUpPlacedFurniture, FurnitureInputPolicy.Resolve(FurnitureInputControl.Q, context));
            Assert.AreEqual(FurnitureInputIntent.OpenInventory, FurnitureInputPolicy.Resolve(FurnitureInputControl.E, context));
            Assert.AreEqual(FurnitureInputIntent.None, FurnitureInputPolicy.Resolve(FurnitureInputControl.F, context));
        }

        [Test]
        public void HoldingFurnitureMapsConfirmReturnRotateSnapAndCancelWithoutWorldTalkOrInventory()
        {
            var context = new FurnitureInputContext { holdingFurniture = true };

            Assert.AreEqual(FurnitureInputIntent.ConfirmPlacement, FurnitureInputPolicy.Resolve(FurnitureInputControl.Q, context));
            Assert.AreEqual(FurnitureInputIntent.ConfirmPlacement, FurnitureInputPolicy.Resolve(FurnitureInputControl.LeftMouseButton, context));
            Assert.AreEqual(FurnitureInputIntent.ReturnHeldItem, FurnitureInputPolicy.Resolve(FurnitureInputControl.E, context));
            Assert.AreEqual(FurnitureInputIntent.ReturnHeldItem, FurnitureInputPolicy.Resolve(FurnitureInputControl.RightMouseButton, context));
            Assert.AreEqual(FurnitureInputIntent.RotateClockwise, FurnitureInputPolicy.Resolve(FurnitureInputControl.ScrollUp, context));
            Assert.AreEqual(FurnitureInputIntent.RotateCounterClockwise, FurnitureInputPolicy.Resolve(FurnitureInputControl.ScrollDown, context));
            Assert.AreEqual(FurnitureInputIntent.RotateClockwise, FurnitureInputPolicy.Resolve(FurnitureInputControl.Z, context));
            Assert.AreEqual(FurnitureInputIntent.RotateCounterClockwise, FurnitureInputPolicy.Resolve(FurnitureInputControl.X, context));
            Assert.AreEqual(FurnitureInputIntent.ToggleGridSnap, FurnitureInputPolicy.Resolve(FurnitureInputControl.C, context));
            Assert.AreEqual(FurnitureInputIntent.CancelHandling, FurnitureInputPolicy.Resolve(FurnitureInputControl.F, context));
            Assert.AreEqual(FurnitureInputIntent.None, FurnitureInputPolicy.Resolve(FurnitureInputControl.T, context));
        }

        [Test]
        public void TextAndModalOwnershipSuppressGameInputAndUiClicksCannotPlaceThroughThePanel()
        {
            var modal = new FurnitureInputContext { holdingFurniture = true, modalOwnsInput = true };
            var text = new FurnitureInputContext { textEntryActive = true };
            var pointer = new FurnitureInputContext { holdingFurniture = true, pointerOverUi = true };

            Assert.AreEqual(FurnitureInputIntent.None, FurnitureInputPolicy.Resolve(FurnitureInputControl.Q, modal));
            Assert.AreEqual(FurnitureInputIntent.None, FurnitureInputPolicy.Resolve(FurnitureInputControl.T, text));
            Assert.AreEqual(FurnitureInputIntent.None, FurnitureInputPolicy.Resolve(FurnitureInputControl.LeftMouseButton, pointer));
            Assert.AreEqual(FurnitureInputIntent.ReturnHeldItem, FurnitureInputPolicy.Resolve(FurnitureInputControl.E, pointer));
        }
    }
}
