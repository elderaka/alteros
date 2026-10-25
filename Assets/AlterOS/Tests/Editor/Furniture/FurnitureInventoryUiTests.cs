using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using AlterOS.Debugging;
using AlterOS.Furniture;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.Tests.Furniture
{
    public sealed class FurnitureInventoryUiTests
    {
        private const string PanelPrefabPath = "Assets/AlterOS/Testbox/Prefabs/FurnitureInventoryPanel.prefab";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject panelObject;
        private Component panel;
        private System.Type panelType;
        private System.Type rowType;
        private System.Type textType;
        private MemoryHomeStore store;

        [TearDown]
        public void TearDown()
        {
            if (panelObject != null) Object.DestroyImmediate(panelObject);
            panelObject = null;
        }

        [Test]
        public void PanelDiscoversRegisteredDefinitionsAndDisablesZeroStockRows()
        {
            FurnitureCatalogueSnapshot catalogue = CatalogueWithNewSeat();
            FurnitureDefinition extra = catalogue.definitions.Single(item => item.definitionId == "furniture.seat.debug_extra");
            store = CreateStore();
            store.document.inventory.Single(item => item.definitionId == "furniture.seat.basic").storedCount = 0;
            var session = new FurnitureHandlingSession(store, catalogue);
            Open(session, catalogue, new DebugModeState(), null, null);

            Component[] rows = GetRows();
            Assert.AreEqual(catalogue.definitions.Count, rows.Length);
            Component extraRow = rows.Single(row => RowText(row).Contains("Debug Extra"));
            Assert.IsTrue(RowText(extraRow).EndsWith("0"));
            Assert.IsFalse(extraRow.GetComponentInChildren<Button>(true).interactable);
            Component emptySeat = rows.Single(row => RowText(row).StartsWith("Seat Basic"));
            Assert.IsFalse(emptySeat.GetComponentInChildren<Button>(true).interactable);
        }

        [Test]
        public void SelectingOneRowClosesThePanelAndClosingWithoutSelectionDoesNotPlaceAnything()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureCatalogueRegistry.Load(Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json"));
            store = CreateStore();
            var session = new FurnitureHandlingSession(store, catalogue);
            string selected = null;
            int closed = 0;
            Open(session, catalogue, new DebugModeState(), value => selected = value, () => closed++);
            Component row = GetRows().Single(item => RowText(item).StartsWith("Seat Basic"));
            ((IList)panelType.GetField("rows", PrivateInstance).GetValue(panel)).Clear();

            row.GetComponentInChildren<Button>(true).onClick.Invoke();

            Assert.AreEqual("furniture.seat.basic", selected);
            Assert.IsFalse(IsOpen());
            Assert.AreEqual(1, closed);
        }

        [Test]
        public void ClosingWithoutSelectionDoesNotInvokeThePlacementCallback()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureCatalogueRegistry.Load(Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json"));
            store = CreateStore();
            var session = new FurnitureHandlingSession(store, catalogue);
            int selections = 0;
            int closed = 0;
            CreatePanel();
            SetField(panel, "rowPrefab", null);
            Invoke(panel, "Open", session, catalogue, new DebugModeState(), (System.Action<string>)(_ => selections++), (System.Action)(() => closed++));

            Invoke(panel, "Close");

            Assert.AreEqual(0, selections);
            Assert.AreEqual(1, closed);
        }

        [Test]
        public void FurnitureRowsRemainKeyboardNavigableAndTbaTabStaysInert()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureCatalogueRegistry.Load(Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json"));
            store = CreateStore();
            Open(new FurnitureHandlingSession(store, catalogue), catalogue, new DebugModeState(), null, null);
            Button tbaTab = (Button)panelType.GetField("tbaTab", PrivateInstance).GetValue(panel);
            Button row = GetRows().Single(item => RowText(item).StartsWith("Seat Basic"))
                .GetComponentInChildren<Button>(true);

            Assert.IsNotNull(tbaTab);
            Assert.IsFalse(tbaTab.interactable);
            Assert.AreNotEqual(Navigation.Mode.None, row.navigation.mode);
        }

        [Test]
        public void MissingRowPrefabLeavesTheInventoryOpenAndShowsConfigurationStatus()
        {
            FurnitureCatalogueSnapshot catalogue = FurnitureCatalogueRegistry.Load(Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json"));
            store = CreateStore();
            var session = new FurnitureHandlingSession(store, catalogue);
            CreatePanel();
            SetField(panel, "rowPrefab", null);
            Invoke(panel, "Open", session, catalogue, new DebugModeState(), null, null);

            Invoke(panel, "RefreshRows");

            Assert.IsTrue(IsOpen());
            Assert.IsEmpty(GetRows());
            object status = panelType.GetField("statusLabel", PrivateInstance).GetValue(panel);
            Assert.AreEqual("Furniture list is not configured.", textType.GetProperty("text").GetValue(status));
        }

        private void Open(FurnitureHandlingSession session, FurnitureCatalogueSnapshot catalogue,
            DebugModeState mode, System.Action<string> selected, System.Action closed)
        {
            CreatePanel();
            Invoke(panel, "Open", session, catalogue, mode, selected, closed);
        }

        private void CreatePanel()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            Assert.IsNotNull(prefab, "Expected the Editor-authored inventory panel prefab.");
            panelObject = Object.Instantiate(prefab);
            panelType = FindType("Pleiades.Furniture.FurnitureInventoryPanel");
            rowType = FindType("Pleiades.Furniture.FurnitureInventoryRow");
            textType = FindType("TMPro.TMP_Text");
            Assert.IsNotNull(panelType);
            Assert.IsNotNull(rowType);
            Assert.IsNotNull(textType);
            panel = panelObject.GetComponent(panelType);
            Assert.IsNotNull(panel);
        }

        private Component[] GetRows()
        { return panel.GetComponentsInChildren(rowType, true); }

        private string RowText(Component row)
        {
            Component text = row.GetComponentsInChildren(textType, true).FirstOrDefault();
            return text == null ? string.Empty : textType.GetProperty("text").GetValue(text) as string;
        }

        private bool IsOpen()
        { return (bool)panelType.GetProperty("IsOpen").GetValue(panel); }

        private static object Invoke(Component target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, PrivateInstance | BindingFlags.Public);
            Assert.IsNotNull(method, "Expected panel method " + methodName);
            return method.Invoke(target, args);
        }

        private static MemoryHomeStore CreateStore()
        {
            HomeFurnitureLayoutDocument document = FurnitureLayoutTestData.Seed("ui-test");
            document.schemaVersion = 2;
            document.inventory = FurnitureInventorySeed.CreateInitial();
            return new MemoryHomeStore(document);
        }

        private static FurnitureCatalogueSnapshot CatalogueWithNewSeat()
        {
            var serializer = new DataContractJsonSerializer(typeof(FurnitureCatalogueDocument));
            FurnitureCatalogueDocument document;
            using (var input = File.OpenRead(Path.GetFullPath("Assets/StreamingAssets/Furniture/catalogue.json")))
                document = (FurnitureCatalogueDocument)serializer.ReadObject(input);
            FurnitureDefinition seat = document.definitions.Single(item => item.definitionId == "furniture.seat.basic");
            document.definitions.Add(new FurnitureDefinition
            {
                definitionId = "furniture.seat.debug_extra",
                displayNameKey = "furniture.seat.debug_extra",
                category = seat.category,
                scalePolicy = seat.scalePolicy,
                defaultScale = seat.defaultScale,
                localBounds = seat.localBounds,
                supportKinds = new List<string>(seat.supportKinds),
                blocksNavigation = seat.blocksNavigation,
                points = seat.points
            });
            using (var output = new MemoryStream())
            {
                serializer.WriteObject(output, document);
                return FurnitureCatalogueRegistry.Parse(Encoding.UTF8.GetString(output.ToArray()));
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }

        private static System.Type FindType(string fullName)
        {
            return System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }

        private sealed class MemoryHomeStore : IFurnitureHomeStore
        {
            public HomeFurnitureLayoutDocument document;
            private long revision = 1;

            public MemoryHomeStore(HomeFurnitureLayoutDocument initial)
            { document = FurnitureLayoutTestData.Clone(initial); }

            public FurnitureHomeReadResult Read()
            {
                return new FurnitureHomeReadResult
                {
                    state = "loaded", document = FurnitureLayoutTestData.Clone(document),
                    gameRevision = revision, layoutRevision = document.layoutRevision
                };
            }

            public FurnitureHomeCommitResult Commit(string operationId, long expectedGameRevision,
                long expectedLayoutRevision, HomeFurnitureLayoutDocument candidate)
            {
                document = FurnitureLayoutTestData.Clone(candidate);
                revision++;
                return new FurnitureHomeCommitResult
                {
                    state = "committed", durable = true, document = FurnitureLayoutTestData.Clone(document),
                    gameRevision = revision, layoutRevision = document.layoutRevision
                };
            }
        }
    }
}
