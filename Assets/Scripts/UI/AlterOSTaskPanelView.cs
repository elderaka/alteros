using System;
using System.Collections.Generic;
using AlterOS.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterOS.UI
{
    public class AlterOSTaskPanelView : MonoBehaviour
    {
        [Serializable]
        public class HudTask
        {
            public string label;
            public bool completed;
        }

        [SerializeField] private AlteraProgressionStore progressionStore;
        [SerializeField] private bool useProgressionTasks = true;
        [SerializeField] private TextMeshProUGUI progressText;
        [SerializeField] private Transform taskContainer;
        [SerializeField] private Toggle taskTogglePrefab;
        [SerializeField] private List<HudTask> tasks = new List<HudTask>
        {
            new HudTask { label = "Finish sketch", completed = false },
            new HudTask { label = "Check messages", completed = true },
            new HudTask { label = "Buy coffee beans", completed = true },
            new HudTask { label = "Evening review", completed = false }
        };

        private readonly List<Toggle> spawnedToggles = new List<Toggle>();

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
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDestroy()
        {
            if (progressionStore != null)
            {
                progressionStore.Changed -= HandleProgressionChanged;
            }
        }

        public void SetTasks(List<HudTask> nextTasks)
        {
            tasks = nextTasks ?? new List<HudTask>();
            Rebuild();
        }

        public void AddTask(string label, bool completed = false)
        {
            if (useProgressionTasks && progressionStore != null)
            {
                PlayerTaskState created = progressionStore.AddTask(label, null, "hud");
                if (created != null && completed)
                {
                    progressionStore.SetTaskCompleted(created.id, true);
                }

                return;
            }

            if (tasks == null)
            {
                tasks = new List<HudTask>();
            }

            tasks.Add(new HudTask
            {
                label = string.IsNullOrWhiteSpace(label) ? $"New task {tasks.Count + 1}" : label,
                completed = completed
            });

            Rebuild();
        }

        public void Rebuild()
        {
            Clear();

            List<PlayerTaskState> progressionTasks = useProgressionTasks && progressionStore != null && progressionStore.State != null
                ? progressionStore.State.tasks
                : null;
            if (progressionTasks != null && progressionTasks.Count > 0)
            {
                RebuildProgressionTasks(progressionTasks);
                return;
            }

            RebuildFallbackTasks();
        }

        private void RebuildProgressionTasks(List<PlayerTaskState> progressionTasks)
        {
            int completedCount = 0;
            for (int i = 0; i < progressionTasks.Count; i++)
            {
                PlayerTaskState task = progressionTasks[i];
                if (task != null && task.completed)
                {
                    completedCount++;
                }

                if (taskContainer == null || taskTogglePrefab == null || task == null)
                {
                    continue;
                }

                string taskId = task.id;
                Toggle toggle = Instantiate(taskTogglePrefab, taskContainer);
                spawnedToggles.Add(toggle);
                toggle.SetIsOnWithoutNotify(task.completed);
                toggle.onValueChanged.AddListener(value =>
                {
                    progressionStore?.SetTaskCompleted(taskId, value);
                });

                TextMeshProUGUI label = toggle.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = task.label;
                }
            }

            if (progressText != null)
            {
                progressText.text = $"{completedCount}/{progressionTasks.Count}";
            }
        }

        private void RebuildFallbackTasks()
        {
            if (tasks == null)
            {
                tasks = new List<HudTask>();
            }

            int completedCount = 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                HudTask task = tasks[i];
                if (task != null && task.completed)
                {
                    completedCount++;
                }

                if (taskContainer == null || taskTogglePrefab == null || task == null)
                {
                    continue;
                }

                int taskIndex = i;
                Toggle toggle = Instantiate(taskTogglePrefab, taskContainer);
                spawnedToggles.Add(toggle);
                toggle.SetIsOnWithoutNotify(task.completed);
                toggle.onValueChanged.AddListener(value =>
                {
                    if (taskIndex >= 0 && taskIndex < tasks.Count && tasks[taskIndex] != null)
                    {
                        tasks[taskIndex].completed = value;
                        RefreshProgress();
                    }
                });

                TextMeshProUGUI label = toggle.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = task.label;
                }
            }

            if (progressText != null)
            {
                progressText.text = $"{completedCount}/{tasks.Count}";
            }
        }

        private void RefreshProgress()
        {
            if (tasks == null)
            {
                tasks = new List<HudTask>();
            }

            List<PlayerTaskState> progressionTasks = useProgressionTasks && progressionStore != null && progressionStore.State != null
                ? progressionStore.State.tasks
                : null;
            if (progressionTasks != null && progressionTasks.Count > 0)
            {
                int progressionCompletedCount = 0;
                foreach (PlayerTaskState task in progressionTasks)
                {
                    if (task != null && task.completed)
                    {
                        progressionCompletedCount++;
                    }
                }

                if (progressText != null)
                {
                    progressText.text = $"{progressionCompletedCount}/{progressionTasks.Count}";
                }

                return;
            }

            int completedCount = 0;
            foreach (HudTask task in tasks)
            {
                if (task != null && task.completed)
                {
                    completedCount++;
                }
            }

            if (progressText != null)
            {
                progressText.text = $"{completedCount}/{tasks.Count}";
            }
        }

        private void HandleProgressionChanged(AlterOSProgressionState state)
        {
            if (isActiveAndEnabled)
            {
                Rebuild();
            }
        }

        private void Clear()
        {
            foreach (Toggle toggle in spawnedToggles)
            {
                if (toggle != null)
                {
                    Destroy(toggle.gameObject);
                }
            }

            spawnedToggles.Clear();
        }
    }
}
