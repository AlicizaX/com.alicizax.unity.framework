using System.Collections.Generic;
using AlicizaX.Editor;
using UnityEditor;
using UnityEngine;

namespace AlicizaX
{
    [CustomEditor(typeof(GameObjectPoolComponent))]
    public sealed class GameObjectPoolInspector : GameFrameworkInspector
    {
        private const float ToolbarHeight = 30f;
        private const float RowHeight = 24f;
        private const float RowLabelWidth = 150f;
        private const float HeaderKindWidth = 44f;
        private const float HeaderSummaryWidth = 186f;
        private const float InstanceNameWidth = 190f;
        private const float InstanceStateWidth = 66f;
        private const float InstanceTimeWidth = 76f;
        private const float InstanceObjectWidth = 140f;
        private const int InitialSnapshotBufferSize = 32;

        private readonly Dictionary<string, bool> _foldoutState = new Dictionary<string, bool>(System.StringComparer.Ordinal);
        private GameObjectPoolSnapshot[] _snapshotBuffer = new GameObjectPoolSnapshot[InitialSnapshotBufferSize];
        private GUIStyle _panelStyle;
        private GUIStyle _entryBodyStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _fieldLabelStyle;
        private GUIStyle _rowLabelStyle;
        private GUIStyle _mutedLabelStyle;
        private GUIStyle _mutedMiniLabelStyle;
        private GUIStyle _warningLabelStyle;
        private GUIStyle _kindBadgeStyle;
        private GUIStyle _emptyStateStyle;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EnsureStyles();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawToolbar("GameObject Pool Component");

            if (!EditorApplication.isPlaying)
            {
                EditorUtils.TrHelpIconText("Enter Play Mode to inspect the runtime pool.", MessageType.Info);
                EditorGUILayout.EndVertical();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (!AppServices.HasWorld ||
                !AppServices.App.TryGet<IGameObjectPoolDebugService>(out IGameObjectPoolDebugService debugService))
            {
                EditorUtils.TrHelpIconText("GameObject pool service is not initialized.", MessageType.Info);
                EditorGUILayout.EndVertical();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawRuntimeState(debugService);
            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
            Repaint();
        }

        public override bool RequiresConstantRepaint()
        {
            return EditorApplication.isPlaying && AppServices.HasWorld && AppServices.App.TryGet<IGameObjectPoolDebugService>(out _);
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
            _mutedMiniLabelStyle = AlicizaEditorGUI.Styles.MutedMiniLabel;
            _warningLabelStyle = AlicizaEditorGUI.Styles.WarningLabel;
            _kindBadgeStyle = AlicizaEditorGUI.Styles.KindBadge;
            _emptyStateStyle = AlicizaEditorGUI.Styles.EmptyState;
        }

        private void DrawToolbar(string title)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
            GUI.Label(labelRect, title, _rowLabelStyle);
        }

        private void DrawRuntimeState(IGameObjectPoolDebugService debugService)
        {
            GameObjectPoolSummarySnapshot summary = debugService.GetDebugSummary();
            DrawSummary(summary);

            if (summary.WaitingForBootstrap)
            {
                EditorUtils.TrHelpIconText("Waiting for YooAssets bootstrap before pool catalog build.", MessageType.Info);
                return;
            }

            if (!summary.IsReady)
            {
                EditorUtils.TrHelpIconText("Pool manager is not ready.", MessageType.Warning);
                return;
            }

            int snapshotCount = GetDebugSnapshots(debugService);
            DrawSectionBegin("Pools");
            if (snapshotCount == 0)
            {
                DrawEmptyState("No pooled runtime instances exist yet.");
                DrawSectionEnd();
                return;
            }

            for (int i = 0; i < snapshotCount; i++)
            {
                DrawSnapshot(_snapshotBuffer[i]);
            }

            DrawSectionEnd();
        }

        private void DrawSummary(in GameObjectPoolSummarySnapshot summary)
        {
            DrawSectionBegin("Runtime Summary");
            DrawReadOnlyRow("Ready", summary.IsReady ? "Yes" : "No", summary.IsReady ? _rowLabelStyle : _warningLabelStyle);
            DrawReadOnlyRow("Waiting Bootstrap", summary.WaitingForBootstrap ? "Yes" : "No", summary.WaitingForBootstrap ? _warningLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Pools", summary.PoolCount.ToString(), summary.PoolCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Loaded Prefabs", summary.LoadedPrefabCount.ToString(), summary.LoadedPrefabCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Instances", summary.TotalInstanceCount.ToString(), summary.TotalInstanceCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Active", summary.ActiveInstanceCount.ToString(), summary.ActiveInstanceCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Inactive", summary.InactiveInstanceCount.ToString(), summary.InactiveInstanceCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Pending Maintenance", summary.PendingMaintenanceCount.ToString(), summary.PendingMaintenanceCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawSectionEnd();
        }

        private void DrawSnapshot(GameObjectPoolSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            string entryLabel = GetEntryLabel(snapshot);
            string foldoutKey = string.Concat(entryLabel, "|", snapshot.assetPath);
            if (!_foldoutState.TryGetValue(foldoutKey, out bool expanded))
            {
                expanded = false;
                _foldoutState.Add(foldoutKey, expanded);
            }

            bool nextExpanded = DrawSnapshotHeader(snapshot, entryLabel, expanded);
            if (nextExpanded != expanded)
            {
                _foldoutState[foldoutKey] = nextExpanded;
            }

            if (!nextExpanded)
            {
                return;
            }

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawReadOnlyRow("Rule", entryLabel);
            DrawReadOnlyRow("Group", string.IsNullOrWhiteSpace(snapshot.group) ? "<None>" : snapshot.group);
            DrawReadOnlyRow("Asset Path", snapshot.assetPath);
            DrawReadOnlyRow("Loader", snapshot.loaderType.ToString());
            DrawReadOnlyRow("Min Retained", snapshot.minRetained.ToString());
            DrawReadOnlyRow("Retain Target", snapshot.retainTarget.ToString());
            DrawReadOnlyRow("Soft Capacity", snapshot.softCapacity.ToString());
            DrawReadOnlyRow("Capacity", snapshot.hardCapacity.ToString());
            DrawReadOnlyRow("Runtime Capacity", snapshot.runtimeHardCapacity.ToString());
            DrawReadOnlyRow("Active", snapshot.activeCount.ToString(), snapshot.activeCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Inactive", snapshot.inactiveCount.ToString(), snapshot.inactiveCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Prefab Loaded", snapshot.prefabLoaded ? "Yes" : "No", snapshot.prefabLoaded ? _rowLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Prefab Cold", FormatSeconds(snapshot.prefabIdleDuration));
            DrawReadOnlyRow("Wake Count", snapshot.prefabWakeCount.ToString());
            DrawReadOnlyRow("Wake Gap", snapshot.prefabWakeGap < 0f ? "N/A" : FormatSeconds(snapshot.prefabWakeGap));
            DrawReadOnlyRow("Unload Delay", snapshot.prefabUnloadDelay < 0f ? "N/A" : FormatSeconds(snapshot.prefabUnloadDelay));
            DrawReadOnlyRow("Next Maintenance", snapshot.nextMaintenanceIn < 0f ? "None" : FormatSeconds(snapshot.nextMaintenanceIn));

            DrawSubHeader("Counters");
            DrawReadOnlyRow("Acquire", snapshot.acquireCount.ToString());
            DrawReadOnlyRow("Release", snapshot.releaseCount.ToString());
            DrawReadOnlyRow("Hit", snapshot.hitCount.ToString(), snapshot.hitCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Miss", snapshot.missCount.ToString(), snapshot.missCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Expand", snapshot.expandCount.ToString(), snapshot.expandCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Destroy", snapshot.destroyCount.ToString(), snapshot.destroyCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Peak Active", snapshot.peakActive.ToString());
            DrawReadOnlyRow("Peak Active Short", snapshot.peakActiveShort.ToString());
            DrawReadOnlyRow("Peak Active Long", snapshot.peakActiveLong.ToString());

            if (snapshot.InstanceCount > 0)
            {
                DrawSubHeader("Instances");
                DrawInstanceHeader();
                for (int i = 0; i < snapshot.InstanceCount; i++)
                {
                    DrawInstance(snapshot.GetInstance(i));
                }
            }

            EditorGUILayout.EndVertical();
        }

        private bool DrawSnapshotHeader(GameObjectPoolSnapshot snapshot, string entryLabel, bool expanded)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
            Event currentEvent = Event.current;
            bool hovered = rowRect.Contains(currentEvent.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(rowRect, expanded, hovered);

            Rect foldRect = new Rect(rowRect.x + 7f, rowRect.y + 3f, 16f, 18f);
            Rect kindRect = new Rect(foldRect.xMax + 4f, rowRect.y + 3f, HeaderKindWidth, 18f);
            Rect summaryRect = new Rect(rowRect.xMax - HeaderSummaryWidth - 8f, rowRect.y + 3f, HeaderSummaryWidth, 18f);
            Rect titleRect = new Rect(kindRect.xMax + 6f, rowRect.y + 3f, summaryRect.x - kindRect.xMax - 14f, 18f);

            AlicizaEditorGUI.DrawFoldoutIcon(foldRect, expanded);
            GUI.Label(kindRect, "POOL", _kindBadgeStyle);
            GUI.Label(titleRect, entryLabel, _rowLabelStyle);
            GUI.Label(summaryRect, Utility.Text.Format("Active {0}/{1} | Hit {2}/{3}", snapshot.activeCount, snapshot.totalCount, snapshot.hitCount, snapshot.acquireCount), _mutedMiniLabelStyle);

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && rowRect.Contains(currentEvent.mousePosition))
            {
                expanded = !expanded;
                GUI.FocusControl(string.Empty);
                currentEvent.Use();
            }

            return expanded;
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

        private void DrawSubHeader(string title)
        {
            Rect headerRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
            bool hovered = headerRect.Contains(Event.current.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(headerRect, true, hovered);

            Rect labelRect = new Rect(headerRect.x + 8f, headerRect.y + 3f, headerRect.width - 16f, 18f);
            GUI.Label(labelRect, title, _rowLabelStyle);
        }

        private void DrawReadOnlyRow(string label, string value)
        {
            DrawReadOnlyRow(label, value, string.IsNullOrEmpty(value) ? _mutedLabelStyle : _rowLabelStyle);
        }

        private void DrawReadOnlyRow(string label, string value, GUIStyle valueStyle)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            EditorGUILayout.LabelField(value, valueStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInstanceHeader()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            DrawTableCell("Instance", _fieldLabelStyle, InstanceNameWidth);
            DrawTableCell("State", _fieldLabelStyle, InstanceStateWidth);
            DrawTableCell("Life", _fieldLabelStyle, InstanceTimeWidth);
            DrawTableCell("Idle", _fieldLabelStyle, InstanceTimeWidth);
            GUILayout.FlexibleSpace();
            DrawTableCell("Object", _fieldLabelStyle, InstanceObjectWidth);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInstance(GameObjectPoolInstanceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            GUIStyle stateStyle = snapshot.isActive ? _warningLabelStyle : _mutedLabelStyle;
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            DrawTableCell(snapshot.instanceName, _rowLabelStyle, InstanceNameWidth);
            DrawTableCell(snapshot.isActive ? "Active" : "Inactive", stateStyle, InstanceStateWidth);
            DrawTableCell(FormatSeconds(snapshot.lifeDuration), _rowLabelStyle, InstanceTimeWidth);
            DrawTableCell(snapshot.isActive ? "-" : FormatSeconds(snapshot.idleDuration), snapshot.isActive ? _mutedLabelStyle : _rowLabelStyle, InstanceTimeWidth);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(snapshot.gameObject, typeof(GameObject), true, GUILayout.Width(InstanceObjectWidth));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEmptyState(string message)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 36f, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawBodyBackground(rect);
            GUI.Label(rect, message, _emptyStateStyle);
        }

        private static void DrawTableCell(string value, GUIStyle style, float width)
        {
            EditorGUILayout.LabelField(value, style, GUILayout.Width(width));
        }

        private static string GetEntryLabel(GameObjectPoolSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.entryName))
            {
                return snapshot.entryName;
            }

            return string.IsNullOrWhiteSpace(snapshot.assetPath) ? "<None>" : snapshot.assetPath;
        }

        private static string FormatSeconds(float seconds)
        {
            return Utility.Text.Format("{0:F2}s", seconds);
        }

        private int GetDebugSnapshots(IGameObjectPoolDebugService debugService)
        {
            while (true)
            {
                int count = debugService.GetDebugSnapshots(_snapshotBuffer);
                if (count < _snapshotBuffer.Length)
                {
                    return count;
                }

                _snapshotBuffer = new GameObjectPoolSnapshot[_snapshotBuffer.Length << 1];
            }
        }
    }
}
