using System;
using System.Collections.Generic;
using System.Linq;
using AlterOS.Debugging;
using AlterOS.Furniture;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pleiades.Furniture
{
    /// <summary>Canvas-authored furniture list; runtime only binds catalogue state and dynamic rows.</summary>
    public sealed class FurnitureInventoryPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button furnitureTab;
        [SerializeField] private Button tbaTab;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform rowsRoot;
        [SerializeField] private FurnitureInventoryRow rowPrefab;
        [SerializeField] private TMP_Text statusLabel;

        private readonly List<FurnitureInventoryRow> rows = new List<FurnitureInventoryRow>();
        private FurnitureHandlingSession session;
        private FurnitureCatalogueSnapshot catalogue;
        private DebugModeState debugMode;
        private Action<string> onSelected;
        private Action onClosed;
        private bool isOpen;

        public bool IsOpen { get { return isOpen; } }

        private void Awake()
        {
            if (tbaTab != null) tbaTab.interactable = false;
            if (furnitureTab != null) furnitureTab.interactable = true;
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Open(FurnitureHandlingSession handling, FurnitureCatalogueSnapshot snapshot,
            DebugModeState sharedDebugMode, Action<string> selectDefinition, Action closed)
        {
            session = handling;
            catalogue = snapshot;
            onSelected = selectDefinition;
            onClosed = closed;
            BindDebugMode(sharedDebugMode);
            isOpen = true;
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshRows();
        }

        public void ShowStatus(string message)
        {
            if (statusLabel != null) statusLabel.text = message ?? string.Empty;
        }

        public void Close()
        {
            if (!isOpen) return;
            isOpen = false;
            if (panelRoot != null) panelRoot.SetActive(false);
            ClearRows();
            Action callback = onClosed;
            onSelected = null;
            onClosed = null;
            callback?.Invoke();
        }

        public void RefreshRows()
        {
            ClearRows();
            if (catalogue == null || catalogue.schemaVersion != 1 || catalogue.diagnostics.Any(item => item.severity == "error"))
            {
                ShowStatus("Furniture catalogue is unavailable.");
                return;
            }
            if (session == null || session.Current.state == "unavailable")
            {
                ShowStatus("Furniture inventory is unavailable.");
                return;
            }
            if (rowPrefab == null || rowsRoot == null)
            {
                ShowStatus("Furniture list is not configured.");
                return;
            }

            FurnitureHandlingView view = session.Current;
            var counts = (view.availableInventory ?? new FurnitureInventoryEntry[0])
                .Where(item => item != null).ToDictionary(item => item.definitionId, item => item.storedCount, StringComparer.Ordinal);
            foreach (FurnitureDefinition definition in catalogue.definitions.Where(item => item != null)
                .OrderBy(item => item.displayNameKey, StringComparer.Ordinal).ThenBy(item => item.definitionId, StringComparer.Ordinal))
            {
                int storedCount;
                if (!counts.TryGetValue(definition.definitionId, out storedCount)) storedCount = 0;
                bool unlimited = debugMode != null && debugMode.IsEnabled;
                FurnitureInventoryRow row = Instantiate(rowPrefab, rowsRoot, false);
                row.Bind(definition.definitionId,
                    DisplayName(definition),
                    unlimited ? "∞" : storedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    unlimited || storedCount > 0, Select);
                rows.Add(row);
            }
            ShowStatus("Choose furniture to place.");
        }

        private void BindDebugMode(DebugModeState state)
        {
            if (debugMode == state) return;
            if (debugMode != null) debugMode.Changed -= OnDebugModeChanged;
            debugMode = state;
            if (debugMode != null) debugMode.Changed += OnDebugModeChanged;
        }

        private void OnDebugModeChanged(bool enabled)
        {
            if (isOpen) RefreshRows();
        }

        private void Select(string definitionId)
        {
            Action<string> callback = onSelected;
            Close();
            callback?.Invoke(definitionId);
        }

        private static string DisplayName(FurnitureDefinition definition)
        {
            string value = string.IsNullOrWhiteSpace(definition.displayNameKey)
                ? definition.definitionId : definition.displayNameKey;
            const string keyPrefix = "furniture.";
            if (value.StartsWith(keyPrefix, StringComparison.Ordinal)) value = value.Substring(keyPrefix.Length);
            value = value.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
            value = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
            if (value == "Tv") return "TV";
            return value.Replace("Tv ", "TV ");
        }

        private void ClearRows()
        {
            foreach (FurnitureInventoryRow row in rows)
                if (row != null) Destroy(row.gameObject);
            rows.Clear();
        }

        private void OnDestroy()
        {
            if (debugMode != null) debugMode.Changed -= OnDebugModeChanged;
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }
    }
}
