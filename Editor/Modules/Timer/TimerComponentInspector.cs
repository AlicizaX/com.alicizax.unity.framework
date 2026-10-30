using AlicizaX.Editor.UIToolkit;
using AlicizaX.Timer.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Timer.Editor
{
    [CustomEditor(typeof(TimerComponent))]
    internal sealed class TimerComponentInspector : AlicizaUIEditor
    {
        private const int DISPLAY_COUNT = 32;
        private const int MIN_INITIAL_CAPACITY = 256;
        private const int MAX_INITIAL_CAPACITY = 16384;
        private const int CAPACITY_STEP = 256;

        private readonly TimerDebugInfo[] _timerBuffer = new TimerDebugInfo[DISPLAY_COUNT];
        private readonly TimerDebugInfo[] _staleBuffer = new TimerDebugInfo[DISPLAY_COUNT];
        private SerializedProperty _initialCapacityProperty;
        private VisualElement _runtimeRoot;

        private void OnEnable()
        {
            _initialCapacityProperty = serializedObject.FindProperty("_initialCapacity");
        }

        protected override void BuildInspector(VisualElement root)
        {
            AlicizaPanel configPanel = AlicizaEditorUI.CreatePanel(null);
            root.Add(configPanel);
            BuildConfiguration(configPanel.Body);

            AlicizaPanel runtimePanel = AlicizaEditorUI.CreatePanel(null);
            runtimePanel.Body.style.paddingBottom = 0f;
            root.Add(runtimePanel);

            _runtimeRoot = new VisualElement();
            runtimePanel.Body.Add(_runtimeRoot);

            RefreshRuntime();
            AlicizaEditorUI.RefreshEvery(root, RefreshRuntime, 250);
        }

        private void BuildConfiguration(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Configuration");
            parent.Add(section);

            if (_initialCapacityProperty != null)
            {
                var slider = new AlicizaIntSliderField(
                    MIN_INITIAL_CAPACITY,
                    MAX_INITIAL_CAPACITY);
                slider.BindProperty(_initialCapacityProperty);
                slider.SetEnabled(!Application.isPlaying);
                slider.RegisterValueChangedCallback(evt =>
                {
                    int aligned = AlignCapacity(evt.newValue);
                    if (aligned != evt.newValue)
                    {
                        _initialCapacityProperty.intValue = aligned;
                        _initialCapacityProperty.serializedObject.ApplyModifiedProperties();
                    }
                });

                AlicizaEditorUI.AddRow(section.Body, "Initial Capacity", slider);
                section.Body.Add(AlicizaEditorUI.CreateHint(
                    $"Rounded to {CAPACITY_STEP} steps, from {MIN_INITIAL_CAPACITY} to {MAX_INITIAL_CAPACITY}."));
            }
        }

        private void RefreshRuntime()
        {
            if (_runtimeRoot == null)
            {
                return;
            }

            _runtimeRoot.Clear();

            if (!Application.isPlaying)
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                    "Available during runtime only."));
                return;
            }

            if (!AppServices.HasWorld || !AppServices.App.TryGet<ITimerService>(out ITimerService timerService))
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                    "Timer service is not initialized."));
                return;
            }

            if (!(timerService is ITimerDebugService timerDebugService))
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                    "Timer debug service is not available."));
                return;
            }

            timerDebugService.GetStatistics(
                out int activeCount,
                out int poolCapacity,
                out int peakActiveCount,
                out int freeCount);

            AlicizaSection stats = AlicizaEditorUI.CreateStaticSection("Runtime Debug");
            _runtimeRoot.Add(stats);

            AddStat(stats.Body, "Active Timers", activeCount, activeCount > 0);
            AddStat(stats.Body, "Pool Capacity", poolCapacity, poolCapacity > 0);
            AddStat(stats.Body, "Peak Active Count", peakActiveCount, peakActiveCount > 0);
            AddStat(stats.Body, "Free Slots", freeCount, freeCount > 0);

            AddUsage(stats.Body, "Active Usage", activeCount, poolCapacity);
            AddUsage(stats.Body, "Peak Usage", peakActiveCount, poolCapacity);

            BuildTimerList(timerDebugService, activeCount);
            BuildStaleTimerList(timerDebugService, activeCount);
        }

        private void BuildTimerList(ITimerDebugService timerDebugService, int activeCount)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Active Timer Sample");
            _runtimeRoot.Add(section);

            if (activeCount <= 0)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("No active timers."));
                return;
            }

            int timerCount = timerDebugService.GetAllTimers(_timerBuffer);
            if (activeCount > DISPLAY_COUNT)
            {
                section.Body.Add(AlicizaEditorUI.CreateHint(
                    $"Showing first {timerCount} timers of {activeCount}."));
            }

            for (int i = 0; i < timerCount; i++)
            {
                TimerDebugInfo info = _timerBuffer[i];
                byte flags = info.Flags;
                bool running = (flags & TimerDebugFlags.Running) != 0;
                string mode = (flags & TimerDebugFlags.Loop) != 0 ? "Loop" : "Once";
                string scale = (flags & TimerDebugFlags.Unscaled) != 0 ? "Unscaled" : "Scaled";
                string title = $"ID {info.TimerHandle}";
                string value =
                    $"{mode} | {scale} | {(running ? "Running" : "Paused")} | Left {info.LeftTime:F2}s | Duration {info.Duration:F2}s";

                AlicizaEditorUI.AddValueRow(
                    section.Body,
                    title,
                    value,
                    running ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            }
        }

        private void BuildStaleTimerList(ITimerDebugService timerDebugService, int activeCount)
        {
            if (activeCount <= 0 || !(timerDebugService is ITimerEditorDebugService editorDebugService))
            {
                return;
            }

            int staleCount = editorDebugService.GetStaleOneShotTimers(_staleBuffer);
            if (staleCount <= 0)
            {
                return;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Stale One-shot Timers");
            _runtimeRoot.Add(section);
            section.Body.Add(AlicizaEditorUI.CreateHint("Long-lived one-shot timers detected."));

            for (int i = 0; i < staleCount; i++)
            {
                TimerDebugInfo info = _staleBuffer[i];
                AlicizaEditorUI.AddValueRow(
                    section.Body,
                    $"ID {info.TimerHandle}",
                    $"Age {info.Age:F1}s | Left {info.LeftTime:F2}s",
                    AlicizaValueTone.Warning);
            }
        }

        private static void AddStat(VisualElement parent, string label, int value, bool active)
        {
            AlicizaEditorUI.AddValueRow(
                parent,
                label,
                value.ToString(),
                active ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
        }

        private static void AddUsage(VisualElement parent, string label, int value, int capacity)
        {
            float ratio = capacity > 0 ? (float)value / capacity : 0f;
            AlicizaEditorUI.AddRow(
                parent,
                label,
                AlicizaEditorUI.CreateProgressBar(Mathf.Clamp01(ratio), ratio.ToString("P1")));
        }

        private static int AlignCapacity(int value)
        {
            int aligned = ((value + CAPACITY_STEP - 1) / CAPACITY_STEP) * CAPACITY_STEP;
            if (aligned < MIN_INITIAL_CAPACITY)
            {
                return MIN_INITIAL_CAPACITY;
            }

            return aligned > MAX_INITIAL_CAPACITY ? MAX_INITIAL_CAPACITY : aligned;
        }
    }
}
