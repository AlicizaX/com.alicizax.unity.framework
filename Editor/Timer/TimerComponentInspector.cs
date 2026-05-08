using AlicizaX.Editor;
using AlicizaX.Timer.Runtime;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Timer.Editor
{
    [CustomEditor(typeof(TimerComponent))]
    internal sealed class TimerComponentInspector : GameFrameworkInspector
    {
        private const double UPDATE_INTERVAL = 0.02d;
        private const int DISPLAY_COUNT = 32;
        private const int MIN_INITIAL_CAPACITY = 256;
        private const int MAX_INITIAL_CAPACITY = 16384;
        private const int CAPACITY_STEP = 256;
        private const float ToolbarHeight = 30f;
        private const float RowHeight = 24f;
        private const float RowLabelWidth = 146f;
        private const float SliderValueWidth = 58f;

        private readonly TimerDebugInfo[] _timerBuffer = new TimerDebugInfo[DISPLAY_COUNT];
        private readonly TimerDebugInfo[] _staleBuffer = new TimerDebugInfo[DISPLAY_COUNT];
        private double _lastUpdateTime;
        private SerializedProperty _initialCapacityProperty;
        private GUIStyle _panelStyle;
        private GUIStyle _entryBodyStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _fieldLabelStyle;
        private GUIStyle _rowLabelStyle;
        private GUIStyle _mutedLabelStyle;
        private GUIStyle _warningLabelStyle;
        private GUIStyle _emptyStateStyle;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EnsureStyles();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawToolbar("Timer Component");
            DrawConfiguration();
            serializedObject.ApplyModifiedProperties();
            DrawRuntimeDebugInfo();
            EditorGUILayout.EndVertical();

            RequestRuntimeRepaint();
        }

        private void OnEnable()
        {
            _initialCapacityProperty = serializedObject.FindProperty("_initialCapacity");
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelStyle = AlicizaEditorGUI.Styles.Panel;
            _entryBodyStyle = AlicizaEditorGUI.Styles.EntryBody;
            _fieldRowStyle = AlicizaEditorGUI.Styles.FieldRow;
            _fieldLabelStyle = AlicizaEditorGUI.Styles.FieldLabel;
            _rowLabelStyle = AlicizaEditorGUI.Styles.RowLabel;
            _mutedLabelStyle = AlicizaEditorGUI.Styles.MutedLabel;
            _warningLabelStyle = AlicizaEditorGUI.Styles.WarningLabel;
            _emptyStateStyle = AlicizaEditorGUI.Styles.EmptyState;
        }

        private void DrawToolbar(string title)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
            GUI.Label(labelRect, title, _rowLabelStyle);
        }

        private void DrawConfiguration()
        {
            DrawSectionBegin("Configuration");

            int capacity = _initialCapacityProperty.intValue;
            int sliderValue = DrawIntSliderRow("Initial Capacity", capacity, MIN_INITIAL_CAPACITY, MAX_INITIAL_CAPACITY);
            sliderValue = AlignCapacity(sliderValue);
            if (sliderValue != capacity)
            {
                _initialCapacityProperty.intValue = sliderValue;
            }

            EditorUtils.TrHelpIconText(Utility.Text.Format("Rounded by {0}. Runtime allocates timer pages during Awake/prewarm.", CAPACITY_STEP), MessageType.None);
            DrawSectionEnd();
        }

        private void DrawRuntimeDebugInfo()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtils.TrHelpIconText("Available during runtime only.", MessageType.Info);
                return;
            }

            if (!AppServices.TryGet<ITimerService>(out ITimerService timerService))
            {
                EditorUtils.TrHelpIconText("Timer service is not initialized.", MessageType.Info);
                return;
            }

            if (!(timerService is ITimerDebugService timerDebugService))
            {
                EditorUtils.TrHelpIconText("Timer debug service is not available.", MessageType.Info);
                return;
            }

            timerDebugService.GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);

            DrawSectionBegin("Runtime Debug");
            DrawStatistic("Active Timers", activeCount, activeCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawStatistic("Pool Capacity", poolCapacity, poolCapacity > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawStatistic("Peak Active Count", peakActiveCount, peakActiveCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawStatistic("Free Slots", freeCount, freeCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawUsageBar("Active Usage", activeCount, poolCapacity);
            DrawUsageBar("Peak Usage", peakActiveCount, poolCapacity);
            DrawSectionEnd();

            DrawTimerList(timerDebugService, activeCount);
            DrawStaleTimerList(timerDebugService, activeCount);
        }

        private void DrawTimerList(ITimerDebugService timerDebugService, int activeCount)
        {
            DrawSectionBegin("Active Timer Sample");

            if (activeCount <= 0)
            {
                DrawEmptyState("No active timers.");
                DrawSectionEnd();
                return;
            }

            int timerCount = timerDebugService.GetAllTimers(_timerBuffer);
            if (activeCount > DISPLAY_COUNT)
            {
                EditorUtils.TrHelpIconText(Utility.Text.Format("Showing first {0} timers of {1}.", timerCount, activeCount), MessageType.Info);
            }

            for (int i = 0; i < timerCount; i++)
            {
                DrawTimerInfo(ref _timerBuffer[i]);
            }

            DrawSectionEnd();
        }

        private void DrawStaleTimerList(ITimerDebugService timerDebugService, int activeCount)
        {
            if (activeCount <= 0)
            {
                return;
            }

            if (!(timerDebugService is ITimerEditorDebugService editorDebugService))
            {
                return;
            }

            int staleCount = editorDebugService.GetStaleOneShotTimers(_staleBuffer);
            if (staleCount <= 0)
            {
                return;
            }

            DrawSectionBegin("Stale One-shot Timers");
            EditorUtils.TrHelpIconText("Long-lived one-shot timers detected.", MessageType.Warning);
            for (int i = 0; i < staleCount; i++)
            {
                TimerDebugInfo info = _staleBuffer[i];
                DrawReadOnlyRow(
                    Utility.Text.Format("ID {0}", info.TimerHandle),
                    Utility.Text.Format("Age {0:F1}s | Left {1:F2}s", info.Age, info.LeftTime),
                    _warningLabelStyle);
            }

            DrawSectionEnd();
        }

        private void DrawTimerInfo(ref TimerDebugInfo info)
        {
            byte flags = info.Flags;
            string mode = (flags & TimerDebugFlags.Loop) != 0 ? "Loop" : "Once";
            string scale = (flags & TimerDebugFlags.Unscaled) != 0 ? "Unscaled" : "Scaled";
            string state = (flags & TimerDebugFlags.Running) != 0 ? "Running" : "Paused";
            string title = Utility.Text.Format("ID {0} | {1} | {2}", info.TimerHandle, mode, scale);
            string value = Utility.Text.Format("{0} | Left {1:F2}s | Duration {2:F2}s", state, info.LeftTime, info.Duration);
            DrawReadOnlyRow(title, value, state == "Running" ? _rowLabelStyle : _mutedLabelStyle);
        }

        private void DrawSectionBegin(string title)
        {
            EditorGUILayout.Space(2f);
            Rect headerRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
            bool hovered = headerRect.Contains(Event.current.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(headerRect, true, hovered);

            Rect labelRect = new Rect(headerRect.x + 8f, headerRect.y + 3f, headerRect.width - 16f, 18f);
            GUI.Label(labelRect, title, _rowLabelStyle);
            EditorGUILayout.BeginVertical(_entryBodyStyle);
        }

        private static void DrawSectionEnd()
        {
            EditorGUILayout.EndVertical();
        }

        private int DrawIntSliderRow(string label, int value, int min, int max)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            int nextValue = 0;
            using (new EditorGUI.DisabledGroupScope(Application.isPlaying))
            {
                nextValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(Mathf.Clamp(value, min, max), min, max, GUILayout.MinWidth(90f)));
                nextValue = Mathf.Clamp(EditorGUILayout.IntField(nextValue, GUILayout.Width(SliderValueWidth)), min, max);
            }

            EditorGUILayout.EndHorizontal();
            return nextValue;
        }

        private void DrawStatistic(string label, int value, GUIStyle valueStyle)
        {
            DrawReadOnlyRow(label, value.ToString(), valueStyle);
        }

        private void DrawUsageBar(string label, int value, int capacity)
        {
            float ratio = capacity > 0 ? (float)value / capacity : 0f;
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            Rect barRect = GUILayoutUtility.GetRect(90f, 18f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(barRect, Mathf.Clamp01(ratio), Utility.Text.Format("{0:P1}", ratio));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawReadOnlyRow(string label, string value, GUIStyle valueStyle)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            EditorGUILayout.LabelField(value, valueStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEmptyState(string message)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 36f, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawBodyBackground(rect);
            GUI.Label(rect, message, _emptyStateStyle);
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

        private void RequestRuntimeRepaint()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastUpdateTime < UPDATE_INTERVAL)
            {
                return;
            }

            _lastUpdateTime = currentTime;
            Repaint();
        }
    }
}
