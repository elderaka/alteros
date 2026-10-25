using System;
using System.Collections.Generic;
using AlterOS.Progression;
using TMPro;
using UnityEngine;

namespace AlterOS.UI
{
    public class AlterOSHudShell : MonoBehaviour
    {
        [Serializable]
        public class HudModeVisibility
        {
            public AlterOSHudMode mode;
            public bool showStatusCard = true;
            public bool showTopBar = true;
            public bool showCredits = true;
            public bool showTaskPanel = true;
            public bool showFocusPanel = true;
            public bool showMusicPanel = true;
            public bool showBottomNav = true;
            public bool showAssistantStatus = true;
        }

        [Header("Mode")]
        [SerializeField] private AlterOSHudMode initialMode = AlterOSHudMode.Room;
        [SerializeField] private List<HudModeVisibility> modeVisibility = new List<HudModeVisibility>();

        [Header("Panels")]
        [SerializeField] private AlterOSHudPanel statusCard;
        [SerializeField] private AlterOSHudPanel topBar;
        [SerializeField] private AlterOSHudPanel creditsPill;
        [SerializeField] private AlterOSHudPanel taskPanel;
        [SerializeField] private AlterOSHudPanel focusPanel;
        [SerializeField] private AlterOSHudPanel musicPanel;
        [SerializeField] private AlterOSHudPanel bottomNav;
        [SerializeField] private AlterOSHudPanel assistantStatus;

        [Header("Navigation")]
        [SerializeField] private AlterOSHudButton[] navButtons;
        [SerializeField] private AlterOSHudTab selectedTab = AlterOSHudTab.Assistant;

        [Header("Progression")]
        [SerializeField] private AlteraProgressionStore progressionStore;
        [SerializeField] private TextMeshProUGUI creditsText;
        [SerializeField] private TextMeshProUGUI alteraNameText;
        [SerializeField] private TextMeshProUGUI moodText;
        [SerializeField] private TextMeshProUGUI levelText;

        [Header("Widget Views")]
        [SerializeField] private AlterOSStatusCardView statusCardView;
        [SerializeField] private AlterOSCreditsPillView creditsPillView;
        [SerializeField] private AlterOSTopBarView topBarView;
        [SerializeField] private AlterOSTaskPanelView taskPanelView;
        [SerializeField] private AlterOSFocusSessionView focusSessionView;
        [SerializeField] private AlterOSAssistantStatusPill assistantStatusPill;

        public AlterOSHudMode CurrentMode { get; private set; }
        public AlterOSHudTab SelectedTab => selectedTab;

        private void Awake()
        {
            if (progressionStore == null)
            {
                progressionStore = FindObjectOfType<AlteraProgressionStore>();
            }

            if (progressionStore != null)
            {
                progressionStore.Changed += HandleProgressionChanged;
            }

            if (navButtons != null)
            {
                foreach (AlterOSHudButton button in navButtons)
                {
                    if (button != null)
                    {
                        button.Bind(this);
                    }
                }
            }

            SetMode(initialMode, true);
            Refresh();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnDestroy()
        {
            if (progressionStore != null)
            {
                progressionStore.Changed -= HandleProgressionChanged;
            }
        }

        public void SetMode(AlterOSHudMode mode)
        {
            SetMode(mode, false);
        }

        public void SetMode(AlterOSHudMode mode, bool immediate)
        {
            CurrentMode = mode;
            HudModeVisibility visibility = GetVisibility(mode);

            SetPanel(statusCard, visibility.showStatusCard, immediate);
            SetPanel(topBar, visibility.showTopBar, immediate);
            SetPanel(creditsPill, visibility.showCredits, immediate);
            SetPanel(taskPanel, visibility.showTaskPanel, immediate);
            SetPanel(focusPanel, visibility.showFocusPanel, immediate);
            SetPanel(musicPanel, visibility.showMusicPanel, immediate);
            SetPanel(bottomNav, visibility.showBottomNav, immediate);
            SetPanel(assistantStatus, visibility.showAssistantStatus, immediate);
        }

        public void SelectTab(AlterOSHudTab tab)
        {
            selectedTab = tab;
            RefreshNavigation();
        }

        public void Refresh()
        {
            RefreshProgression();
            RefreshWidgets();
            RefreshNavigation();
        }

        private HudModeVisibility GetVisibility(AlterOSHudMode mode)
        {
            if (modeVisibility != null)
            {
                foreach (HudModeVisibility entry in modeVisibility)
                {
                    if (entry != null && entry.mode == mode)
                    {
                        return entry;
                    }
                }
            }

            return DefaultVisibility(mode);
        }

        private static HudModeVisibility DefaultVisibility(AlterOSHudMode mode)
        {
            HudModeVisibility visibility = new HudModeVisibility { mode = mode };

            if (mode == AlterOSHudMode.Dialogue)
            {
                visibility.showStatusCard = false;
                visibility.showTopBar = false;
                visibility.showCredits = false;
                visibility.showTaskPanel = false;
                visibility.showFocusPanel = false;
                visibility.showMusicPanel = false;
                visibility.showBottomNav = false;
                visibility.showAssistantStatus = false;
            }
            else if (mode == AlterOSHudMode.Focus)
            {
                visibility.showMusicPanel = false;
                visibility.showTaskPanel = true;
                visibility.showFocusPanel = true;
            }
            else if (mode == AlterOSHudMode.Hidden)
            {
                visibility.showStatusCard = false;
                visibility.showTopBar = false;
                visibility.showCredits = false;
                visibility.showTaskPanel = false;
                visibility.showFocusPanel = false;
                visibility.showMusicPanel = false;
                visibility.showBottomNav = false;
                visibility.showAssistantStatus = false;
            }

            return visibility;
        }

        private static void SetPanel(AlterOSHudPanel panel, bool visible, bool immediate)
        {
            if (panel != null)
            {
                panel.SetVisible(visible, immediate);
            }
        }

        private void RefreshProgression()
        {
            if (progressionStore == null || progressionStore.State == null)
            {
                return;
            }

            if (creditsText != null) creditsText.text = progressionStore.State.player.credits.ToString("N0");
            if (alteraNameText != null) alteraNameText.text = "Altera";
            if (moodText != null) moodText.text = progressionStore.State.altera.mood;
            if (levelText != null) levelText.text = $"Lv. {progressionStore.State.altera.bondLevel}";
        }

        private void RefreshNavigation()
        {
            if (navButtons == null)
            {
                return;
            }

            foreach (AlterOSHudButton button in navButtons)
            {
                if (button != null)
                {
                    button.SetSelected(button.Tab == selectedTab);
                }
            }
        }

        private void RefreshWidgets()
        {
            if (statusCardView != null) statusCardView.Refresh();
            if (creditsPillView != null) creditsPillView.Refresh();
            if (topBarView != null) topBarView.Refresh();
            if (taskPanelView != null) taskPanelView.Rebuild();
            if (assistantStatusPill != null) assistantStatusPill.SetReady();
        }

        private void HandleProgressionChanged(AlterOSProgressionState state)
        {
            Refresh();
        }
    }
}
