using System.IO;
using System.Linq;
using NUnit.Framework;
using AlterOS.Debugging;
using AlterOS.Furniture;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureHandlingTests
    {
        private static readonly string CataloguePath = Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json");

        [Test]
        public void FiniteHoldIsOnlyReservedInViewUntilConfirmAndCancelDoesNotWrite()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);

            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.AreEqual(9, session.Current.availableCount);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual(0, store.commitCount);

            Assert.IsTrue(session.Cancel().success);
            Assert.AreEqual(10, session.Current.availableCount);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual(0, store.commitCount);
        }

        [Test]
        public void FinitePlacementConsumesOneStoredUnitAndReturningPlacedPieceCreditsItOnce()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);

            Assert.IsTrue(session.Confirm("place-chair-1", FurnitureLayoutTestData.Evidence(catalogue)).success);

            Assert.AreEqual(9, Stock(store, "furniture.seat.basic"));
            var placedId = store.document.instances.Single(item => item.instanceId != "seat.a" && item.instanceId != "seat.b").instanceId;
            Assert.IsTrue(session.BeginMove(placedId, FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsTrue(session.ReturnToInventory("return-chair-1", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
            Assert.IsFalse(store.document.instances.Any(item => item.instanceId == placedId));
            Assert.AreEqual("no_hold", session.ReturnToInventory("return-chair-1", FurnitureLayoutTestData.Evidence(catalogue)).reasonCode);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
        }

        [Test]
        public void MovingAndCancellingRestoresCommittedPoseWithoutChangingStock()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            var original = store.document.instances.Single(item => item.instanceId == "seat.a").positionMeters.x;

            Assert.IsTrue(session.BeginMove("seat.a", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(original, store.document.instances.Single(item => item.instanceId == "seat.a").positionMeters.x);
            Assert.IsTrue(session.Cancel().success);

            Assert.AreEqual(original, store.document.instances.Single(item => item.instanceId == "seat.a").positionMeters.x);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual(0, store.commitCount);
        }

        [Test]
        public void CancelledUnlimitedPreviewCreatesNoInventoryAndPlacedDebugFurnitureReturnsAsOrdinaryStock()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            var mode = new DebugModeState();
            mode.SetEnabled(true);

            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", mode.IsEnabled).success);
            Assert.IsTrue(session.Current.unlimitedSupply);
            Assert.IsTrue(session.Cancel().success);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual(0, store.commitCount);

            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", mode.IsEnabled).success);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsTrue(session.Confirm("debug-place-1", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
            var placedId = store.document.instances.Single(item => item.instanceId != "seat.a" && item.instanceId != "seat.b").instanceId;

            mode.Toggle();
            Assert.IsTrue(session.BeginMove(placedId, FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsTrue(session.ReturnToInventory("debug-return-1", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(11, Stock(store, "furniture.seat.basic"));
            Assert.IsFalse(store.document.instances.Any(item => item.instanceId == placedId));
        }

        [Test]
        public void F5TransitionsDuringAHoldDoNotChangeItsCapturedStockOrigin()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var mode = new DebugModeState();
            var session = new FurnitureHandlingSession(store, catalogue);

            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", mode.IsEnabled).success);
            mode.Toggle();
            mode.Toggle();
            Assert.AreEqual("finite_inventory", session.Current.sourceKind);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsTrue(session.Confirm("finite-after-f5", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(9, Stock(store, "furniture.seat.basic"));

            mode.SetEnabled(true);
            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", mode.IsEnabled).success);
            mode.Toggle();
            Assert.AreEqual("debug_supply", session.Current.sourceKind);
            Assert.IsTrue(session.Current.unlimitedSupply);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, 2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsTrue(session.Confirm("debug-after-f5", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(9, Stock(store, "furniture.seat.basic"));
        }

        [Test]
        public void HandlingViewDoesNotExposeMutableInventoryOrPoseReferences()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            FurnitureHandlingView view = session.Current;
            view.availableInventory[0].storedCount = 999;
            view.pose.positionMeters.x = 999;

            Assert.AreEqual(9, session.Current.availableCount);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual(-2.7, session.Current.pose.positionMeters.x, 0.001);
        }

        [Test]
        public void ZeroStockUnknownDefinitionAndInvalidPlacementCannotCommit()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            store.document.inventory.Single(item => item.definitionId == "furniture.seat.basic").storedCount = 0;
            var session = new FurnitureHandlingSession(store, catalogue);

            Assert.AreEqual("insufficient_stock", session.BeginFromInventory("furniture.seat.basic", false).reasonCode);
            Assert.AreEqual("unknown_definition", session.BeginFromInventory("furniture.removed.chair", false).reasonCode);
            Assert.IsTrue(session.BeginFromInventory("furniture.table.general", false).success);
            Assert.IsTrue(session.UpdatePreview(Pose(100, 100), FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsFalse(session.Current.valid);
            Assert.AreEqual("outside_room", session.Current.reasonCode);
            Assert.AreEqual("invalid_placement", session.Confirm("invalid-place", FurnitureLayoutTestData.Evidence(catalogue)).reasonCode);
            Assert.AreEqual(0, store.commitCount);
            Assert.IsTrue(session.UpdatePreview(Pose(2.7, 2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsTrue(session.Confirm("valid-place", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(9, Stock(store, "furniture.table.general"));
            Assert.AreEqual(1, store.commitCount);
        }

        [Test]
        public void ChangedCatalogueCannotStartAStaleFurnitureMove()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);

            FurnitureHandlingResult result = session.BeginMove("seat.a", FurnitureLayoutTestData.Evidence(ChangedCatalogue()));

            Assert.IsFalse(result.success);
            Assert.AreEqual("catalogue_changed", result.reasonCode);
            Assert.AreEqual(0, store.commitCount);
        }

        [Test]
        public void MovedSourceOrNewlyOccupiedLayoutRevisionRejectsConfirmation()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var evidence = FurnitureLayoutTestData.Evidence(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(session.BeginMove("seat.a", evidence).success);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), evidence).success);
            store.document.instances.Single(item => item.instanceId == "seat.a").instanceRevision++;
            store.document.layoutRevision++;
            store.layoutRevision++;
            store.gameRevision++;

            FurnitureHandlingResult moved = session.Confirm("stale-move", evidence);

            Assert.AreEqual("stale_revision", moved.reasonCode);
            Assert.AreEqual(0, store.commitCount);
            Assert.AreEqual("holding", session.Current.state);
            Assert.IsFalse(session.Current.valid);
            Assert.AreEqual("stale_revision", session.Current.reasonCode);

            var secondStore = Store(catalogue);
            var secondSession = new FurnitureHandlingSession(secondStore, catalogue);
            Assert.IsTrue(secondSession.BeginFromInventory("furniture.table.general", false).success);
            Assert.IsTrue(secondSession.UpdatePreview(Pose(2.7, 2.7), evidence).success);
            secondStore.document.instances.Add(FurnitureLayoutTestData.Instance("chair.concurrent", "furniture.seat.basic",
                new FurnitureVector3 { x = -2.7, y = -2.7, z = 0 }, FurnitureLayoutTestData.Identity(), "room.floor"));
            secondStore.document.layoutRevision++;
            secondStore.layoutRevision++;
            secondStore.gameRevision++;

            FurnitureHandlingResult occupied = secondSession.Confirm("stale-place", evidence);

            Assert.AreEqual("stale_revision", occupied.reasonCode);
            Assert.AreEqual(10, Stock(secondStore, "furniture.table.general"));
            Assert.AreEqual(0, secondStore.commitCount);
        }

        [Test]
        public void ReturningPlacedFurnitureRejectsInventoryOverflowAndReturningNewPreviewDoesNotWrite()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            store.document.inventory.Single(item => item.definitionId == "furniture.seat.basic").storedCount = int.MaxValue;
            var session = new FurnitureHandlingSession(store, catalogue);

            Assert.IsTrue(session.BeginMove("seat.a", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual("stock_overflow", session.ReturnToInventory("overflow-return", FurnitureLayoutTestData.Evidence(catalogue)).reasonCode);
            Assert.IsTrue(store.document.instances.Any(item => item.instanceId == "seat.a"));
            Assert.AreEqual(0, store.commitCount);

            var freshStore = Store(catalogue);
            var freshSession = new FurnitureHandlingSession(freshStore, catalogue);
            Assert.IsTrue(freshSession.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.IsTrue(freshSession.ReturnToInventory("return-new-preview", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(10, Stock(freshStore, "furniture.seat.basic"));
            Assert.AreEqual(0, freshStore.commitCount);
        }

        [Test]
        public void ReservedFurnitureAndFurnitureWithAttachedDependentsCannotBeMoved()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            var reserved = FurnitureLayoutTestData.Evidence(catalogue);
            reserved.reservedPointRefs.Add(FurnitureArrangementTestData.Ref("seat.a", "seat"));
            Assert.AreEqual("point_in_use", session.BeginMove("seat.a", reserved).reasonCode);

            var dependent = FurnitureLayoutTestData.Instance("dependent.table", "furniture.table.general",
                new FurnitureVector3 { x = 2.7, y = 0, z = 2.7 }, FurnitureLayoutTestData.Identity(), "seat.a");
            store.document.instances.Add(dependent);
            session = new FurnitureHandlingSession(store, catalogue);
            Assert.AreEqual("has_dependents", session.BeginMove("seat.a", FurnitureLayoutTestData.Evidence(catalogue)).reasonCode);
        }

        [Test]
        public void FailedCommitKeepsDurableTruthAndRetainsTheSameHoldForRetry()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            store.failNextCommit = true;

            Assert.AreEqual("unavailable", session.Confirm("place-retry", FurnitureLayoutTestData.Evidence(catalogue)).state);
            Assert.AreEqual(10, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual("holding", session.Current.state);
            Assert.AreEqual("operation_identity_conflict", session.Confirm("different-operation", FurnitureLayoutTestData.Evidence(catalogue)).reasonCode);
            Assert.IsTrue(session.Confirm("place-retry", FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.AreEqual(9, Stock(store, "furniture.seat.basic"));
        }

        [Test]
        public void CommitAcknowledgementLossRetriesTheSameCandidateAndReloadsDuplicateTruth()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            store.commitThenLoseAck = true;

            FurnitureHandlingResult lostAck = session.Confirm("place-replay", FurnitureLayoutTestData.Evidence(catalogue));
            Assert.AreEqual("unavailable", lostAck.state);
            Assert.AreEqual(9, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual("holding", session.Current.state);
            Assert.AreEqual("operation_identity_conflict", session.Confirm("different-replay", FurnitureLayoutTestData.Evidence(catalogue)).reasonCode);

            FurnitureHandlingResult replay = session.Confirm("place-replay", FurnitureLayoutTestData.Evidence(catalogue));

            Assert.IsTrue(replay.success);
            Assert.AreEqual("duplicate", replay.state);
            Assert.AreEqual(9, session.Current.availableCount);
            Assert.AreEqual("idle", session.Current.state);
            Assert.AreEqual(1, store.commitCount);
        }

        [Test]
        public void CancellingAfterAnUncertainCommitReloadsTheLastCommittedTruth()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(session.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.IsTrue(session.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            store.commitThenLoseAck = true;
            Assert.AreEqual("unavailable", session.Confirm("uncertain-place", FurnitureLayoutTestData.Evidence(catalogue)).state);

            Assert.IsTrue(session.Cancel().success);

            Assert.AreEqual("idle", session.Current.state);
            Assert.AreEqual(9, session.Current.availableCount);
            Assert.AreEqual(9, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual(3, store.document.instances.Count);
        }

        [Test]
        public void UncertainReturnRetriesTheSameRemovalWithoutCreditingStockTwice()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var session = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(session.BeginMove("seat.a", FurnitureLayoutTestData.Evidence(catalogue)).success);
            store.commitThenLoseAck = true;

            Assert.AreEqual("unavailable", session.ReturnToInventory("return-replay", FurnitureLayoutTestData.Evidence(catalogue)).state);
            Assert.AreEqual(11, Stock(store, "furniture.seat.basic"));
            Assert.IsFalse(store.document.instances.Any(item => item.instanceId == "seat.a"));

            FurnitureHandlingResult replay = session.ReturnToInventory("return-replay", FurnitureLayoutTestData.Evidence(catalogue));

            Assert.IsTrue(replay.success);
            Assert.AreEqual("duplicate", replay.state);
            Assert.AreEqual(11, Stock(store, "furniture.seat.basic"));
            Assert.AreEqual(1, store.document.instances.Count);
            Assert.AreEqual(1, store.commitCount);
        }

        [Test]
        public void RecreatedSessionAndReceiptReplayKeepTheAlreadyCommittedStockAndLayout()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            long originalGameRevision = store.gameRevision;
            long originalLayoutRevision = store.layoutRevision;
            var firstSession = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(firstSession.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.IsTrue(firstSession.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);
            Assert.IsTrue(firstSession.Confirm("restart-replay", FurnitureLayoutTestData.Evidence(catalogue)).success);

            var recreatedSession = new FurnitureHandlingSession(store, catalogue);
            Assert.AreEqual("idle", recreatedSession.Current.state);
            Assert.AreEqual(9, recreatedSession.Current.availableInventory.Single(item => item.definitionId == "furniture.seat.basic").storedCount);
            Assert.AreEqual(3, store.document.instances.Count);
            FurnitureHomeCommitResult replay = store.Commit("restart-replay", originalGameRevision,
                originalLayoutRevision, store.GetReceipt("restart-replay"));
            Assert.AreEqual("duplicate", replay.state);
            Assert.IsTrue(replay.durable);

            var reopenedAgain = new FurnitureHandlingSession(store, catalogue);
            Assert.AreEqual(9, reopenedAgain.Current.availableInventory.Single(item => item.definitionId == "furniture.seat.basic").storedCount);
            Assert.AreEqual(3, store.document.instances.Count);
            Assert.AreEqual(1, store.commitCount);
        }

        [Test]
        public void RecreatedSessionDoesNotResumeAnUncommittedHold()
        {
            var catalogue = FurnitureCatalogueRegistry.Load(CataloguePath);
            var store = Store(catalogue);
            var interrupted = new FurnitureHandlingSession(store, catalogue);
            Assert.IsTrue(interrupted.BeginFromInventory("furniture.seat.basic", false).success);
            Assert.IsTrue(interrupted.UpdatePreview(Pose(-2.7, -2.7), FurnitureLayoutTestData.Evidence(catalogue)).success);

            var reopened = new FurnitureHandlingSession(store, catalogue);

            Assert.AreEqual("idle", reopened.Current.state);
            Assert.AreEqual(10, reopened.Current.availableInventory.Single(item => item.definitionId == "furniture.seat.basic").storedCount);
            Assert.AreEqual(2, store.document.instances.Count);
            Assert.AreEqual(0, store.commitCount);
        }

        private static FurniturePlacementPose Pose(double x, double z)
        {
            return new FurniturePlacementPose
            {
                positionMeters = new FurnitureVector3 { x = x, y = 0, z = z },
                rotation = FurnitureLayoutTestData.Identity(),
                support = new FurnitureLayoutSupportReference
                {
                    supportId = "room.floor", supportKind = "roomFloor", supportFrameRevision = "1",
                    attachmentPointId = "origin", localPositionMeters = new FurnitureVector3 { x = x, y = 0, z = z },
                    localRotation = FurnitureLayoutTestData.Identity()
                }
            };
        }

        private static FurnitureCatalogueSnapshot ChangedCatalogue()
        {
            string json = File.ReadAllText(CataloguePath).Replace("\"catalogueVersion\": \"1.0.0\"", "\"catalogueVersion\": \"1.0.1\"");
            return FurnitureCatalogueRegistry.Parse(json);
        }

        private static int Stock(InMemoryHomeStore store, string definitionId)
        {
            return store.document.inventory.Single(item => item.definitionId == definitionId).storedCount;
        }

        private static InMemoryHomeStore Store(FurnitureCatalogueSnapshot catalogue)
        {
            var document = FurnitureLayoutTestData.Seed("seed-1");
            document.schemaVersion = 2;
            document.inventory = FurnitureInventorySeed.CreateInitial(catalogue);
            return new InMemoryHomeStore(document);
        }

        private sealed class InMemoryHomeStore : IFurnitureHomeStore
        {
            private readonly System.Collections.Generic.Dictionary<string, HomeFurnitureLayoutDocument> receipts =
                new System.Collections.Generic.Dictionary<string, HomeFurnitureLayoutDocument>();
            public HomeFurnitureLayoutDocument document;
            public long gameRevision = 1;
            public long layoutRevision = 1;
            public int commitCount;
            public bool failNextCommit;
            public bool commitThenLoseAck;

            public InMemoryHomeStore(HomeFurnitureLayoutDocument initial) { document = FurnitureLayoutTestData.Clone(initial); }

            public FurnitureHomeReadResult Read()
            {
                return new FurnitureHomeReadResult
                {
                    state = "loaded", document = FurnitureLayoutTestData.Clone(document),
                    gameRevision = gameRevision, layoutRevision = layoutRevision
                };
            }

            public HomeFurnitureLayoutDocument GetReceipt(string operationId)
            {
                return FurnitureLayoutTestData.Clone(receipts[operationId]);
            }

            public FurnitureHomeCommitResult Commit(string operationId, long expectedGameRevision,
                long expectedLayoutRevision, HomeFurnitureLayoutDocument candidate)
            {
                if (failNextCommit)
                {
                    failNextCommit = false;
                    return new FurnitureHomeCommitResult { state = "unavailable", durable = false };
                }
                HomeFurnitureLayoutDocument prior;
                if (receipts.TryGetValue(operationId, out prior))
                {
                    if (!System.Linq.Enumerable.SequenceEqual(FurnitureLayoutTestData.Serialize(prior), FurnitureLayoutTestData.Serialize(candidate)))
                        return new FurnitureHomeCommitResult { state = "conflicting_operation", durable = false };
                    return new FurnitureHomeCommitResult
                    {
                        state = "duplicate", durable = true, document = FurnitureLayoutTestData.Clone(document),
                        gameRevision = gameRevision, layoutRevision = layoutRevision
                    };
                }
                if (expectedGameRevision != gameRevision || expectedLayoutRevision != layoutRevision)
                    return new FurnitureHomeCommitResult { state = "stale_revision", durable = false };

                document = FurnitureLayoutTestData.Clone(candidate);
                document.layoutRevision = checked(expectedLayoutRevision + 1);
                gameRevision++;
                layoutRevision++;
                receipts.Add(operationId, FurnitureLayoutTestData.Clone(document));
                commitCount++;
                if (commitThenLoseAck)
                {
                    commitThenLoseAck = false;
                    return new FurnitureHomeCommitResult { state = "unavailable", durable = false };
                }
                return new FurnitureHomeCommitResult
                {
                    state = "committed", durable = true, document = FurnitureLayoutTestData.Clone(document),
                    gameRevision = gameRevision, layoutRevision = layoutRevision
                };
            }
        }
    }
}
