using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureInteractionTests
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        [Test]
        public void ExistingDispenserInstanceAndUsePointAliasesResolveToStableCataloguePoint()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);

            string definitionId;
            string pointId;
            var resolved = FurnitureInteractionPolicy.TryResolvePoint(catalogue,
                "testbox.water_dispenser", "testbox.water_dispenser.drink", "", "", out definitionId, out pointId);

            Assert.IsTrue(resolved);
            Assert.AreEqual("furniture.water_dispenser", definitionId);
            Assert.AreEqual("drink", pointId);
        }

        [Test]
        public void DeclaredFurnitureActionWithoutAnInstalledExecutorCannotBeSelected()
        {
            var option = new FurnitureInteractionOption
            {
                state = "available", capabilityId = "social.sit_together", definitionId = "furniture.seat.basic",
                instanceId = "seat.home", pointId = "seat", pointRevision = "1", catalogueHash = "catalogue-hash"
            };

            Assert.IsFalse(FurnitureInteractionPolicy.CanExecute(option, new[] { "needs.drink_water" }, null));
        }

        [Test]
        public void FurnitureBindingPinsActorInstancePointCatalogueAndRevision()
        {
            var option = AvailableDrinkOption("1", "click-1");
            var binding = FurnitureInteractionPolicy.TryCreateBinding(option, "altera");

            Assert.IsNotNull(binding);
            Assert.AreEqual("altera", binding.actorId);
            Assert.IsTrue(FurnitureInteractionPolicy.IsCurrent(binding, option, "altera"));
            Assert.IsFalse(FurnitureInteractionPolicy.IsCurrent(binding, option, "player"));
            option.instanceId = "testbox.other_dispenser";
            Assert.IsFalse(FurnitureInteractionPolicy.IsCurrent(binding, option, "altera"));
        }

        [Test]
        public void FurnitureRequestIdentityIsPinnedToTheClickBinding()
        {
            var option = AvailableDrinkOption("1", "click-1");
            var binding = FurnitureInteractionPolicy.TryCreateBinding(option, "altera");

            Assert.AreEqual("click-1", binding.requestId);
            option.requestId = "click-2";
            Assert.IsTrue(FurnitureInteractionPolicy.IsCurrent(binding, option, "altera"));
            Assert.AreNotEqual(binding.bindingToken,
                FurnitureInteractionPolicy.TryCreateBinding(option, "altera").bindingToken);
        }

        [Test]
        public void DrinkWaterFurnitureOptionMeansAskAlteraRatherThanPlayerDrinks()
        {
            var option = AvailableDrinkOption("1", "click-1");
            var binding = FurnitureInteractionPolicy.TryCreateBinding(option, "altera");

            Assert.AreEqual("altera", binding.actorId);
            Assert.AreEqual("needs.drink_water", binding.capabilityId);
            Assert.AreEqual("Ask Alterra to get water", FurnitureInteractionPolicy.PromptFor(binding));
        }

        [Test]
        public void LockedFurnitureChoiceCannotExecuteOrAdvance()
        {
            var option = AvailableDrinkOption("1", "click-1");
            var locked = new FurnitureChoiceEvaluation { state = "locked", reasonKey = "choice.needs_usable_table" };

            Assert.IsFalse(FurnitureInteractionPolicy.CanExecute(option, new[] { "needs.drink_water" }, locked));
        }

        [Test]
        public void StaleAvailableBindingNeedsANewClickAfterRevisionRefresh()
        {
            var shown = AvailableDrinkOption("1", "click-1");
            var binding = FurnitureInteractionPolicy.TryCreateBinding(shown, "altera");
            var refreshed = AvailableDrinkOption("2", "click-2");

            Assert.IsFalse(FurnitureInteractionPolicy.IsCurrent(binding, refreshed, "altera"));
            var nextClickBinding = FurnitureInteractionPolicy.TryCreateBinding(refreshed, "altera");
            Assert.IsNotNull(nextClickBinding);
            Assert.AreNotEqual(binding.bindingToken, nextClickBinding.bindingToken);
        }

        [Test]
        public void HiddenFurnitureChoiceHasNoPlayerVisibleReasonOrRow()
        {
            var hidden = new FurnitureChoiceEvaluation { state = "hidden", reasonKey = null };
            var presentation = FurnitureChoicePresentation.Create(hidden, "Sit together", null);

            Assert.IsFalse(presentation.visible);
            Assert.IsFalse(presentation.interactable);
            Assert.IsNull(presentation.reasonText);
            Assert.IsNull(presentation.reasonKey);
        }

        private static FurnitureInteractionOption AvailableDrinkOption(string revision, string requestId)
        {
            return new FurnitureInteractionOption
            {
                state = "available", capabilityId = "needs.drink_water", definitionId = "furniture.water_dispenser",
                instanceId = "testbox.water_dispenser", pointId = "drink", pointRevision = revision,
                catalogueHash = "catalogue-hash", usePointId = "testbox.water_dispenser.drink", requestId = requestId
            };
        }
    }
}
