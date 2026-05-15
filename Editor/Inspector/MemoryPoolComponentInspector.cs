using AlicizaX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Editor
{
    [CustomEditor(typeof(MemoryPoolSetting))]
    internal sealed class MemoryPoolComponentInspector : GameFrameworkInspector
    {
        private const float ToolbarHeight = 30f;
        private const float RowHeight = 24f;
        private const float RowLabelWidth = 154f;
        private const float ActionButtonWidth = 124f;
        private const float ExportButtonWidth = 104f;
        private const float HeaderKindWidth = 44f;
        private const float HeaderSummaryWidth = 92f;
        private const float PoolNameColumnWidth = 154f;
        private const float CountColumnWidth = 48f;
        private const float WideCountColumnWidth = 58f;
        private const float CompactCountColumnWidth = 38f;

        private readonly Dictionary<string, List<int>> m_GroupedIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private readonly List<string> m_ActiveAssemblyKeys = new List<string>(16);
        private readonly HashSet<string> m_OpenedItems = new HashSet<string>();
        private MemoryPoolInfo[] m_InfoBuffer = Array.Empty<MemoryPoolInfo>();

        private SerializedProperty m_EnableStrictCheck;
        private SerializedProperty m_ShortDecayStartFrames;
        private SerializedProperty m_LongDecayStartFrames;
        private SerializedProperty m_UnscheduleIdleFrames;
        private bool m_ShowFullClassName;
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

            MemoryPoolSetting setting = (MemoryPoolSetting)target;
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawToolbar("Memory Pool Component");

            if (EditorApplication.isPlaying && IsPrefabInHierarchy(setting.gameObject))
            {
                DrawRuntimeInspector(setting);
            }
            else
            {
                DrawConfiguration();
            }

            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
            Repaint();
        }

        private void OnEnable()
        {
            m_EnableStrictCheck = serializedObject.FindProperty("m_EnableStrictCheck");
            m_ShortDecayStartFrames = serializedObject.FindProperty("m_ShortDecayStartFrames");
            m_LongDecayStartFrames = serializedObject.FindProperty("m_LongDecayStartFrames");
            m_UnscheduleIdleFrames = serializedObject.FindProperty("m_UnscheduleIdleFrames");
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

        private void DrawConfiguration()
        {
            DrawEnumPropertyRow("Strict Check", m_EnableStrictCheck);
            DrawIntPropertyRow("Short Decay Start", m_ShortDecayStartFrames);
            DrawIntPropertyRow("Long Decay Start", m_LongDecayStartFrames);
            DrawIntPropertyRow("Unschedule Idle", m_UnscheduleIdleFrames);
        }

        private void DrawRuntimeInspector(MemoryPoolSetting setting)
        {
            DrawSectionBegin("Configuration");
            bool enableStrictCheck = DrawBoolValueRow("Enable Strict Check", setting.EnableStrictCheck);
            if (enableStrictCheck != setting.EnableStrictCheck)
            {
                setting.EnableStrictCheck = enableStrictCheck;
            }

            DrawReadOnlyRow("Memory Pool Count", MemoryPool.Count.ToString(), MemoryPool.Count > 0 ? _rowLabelStyle : _mutedLabelStyle);
            m_ShowFullClassName = DrawBoolValueRow("Show Full Class Name", m_ShowFullClassName);
            DrawSectionEnd();

            DrawSectionBegin("Idle Trim");
            int shortDecay = DrawIntValueRow("Short Decay Start", MemoryPool.ShortDecayStartFrames);
            if (shortDecay != MemoryPool.ShortDecayStartFrames)
            {
                MemoryPool.ShortDecayStartFrames = shortDecay;
            }

            int longDecay = DrawIntValueRow("Long Decay Start", MemoryPool.LongDecayStartFrames);
            if (longDecay != MemoryPool.LongDecayStartFrames)
            {
                MemoryPool.LongDecayStartFrames = longDecay;
            }

            int unschedule = DrawIntValueRow("Unschedule Idle", MemoryPool.UnscheduleIdleFrames);
            if (unschedule != MemoryPool.UnscheduleIdleFrames)
            {
                MemoryPool.UnscheduleIdleFrames = unschedule;
            }

            DrawSectionEnd();

            int infoCount = FetchInfos();
            DrawOverview(infoCount);
            DrawActions();
            DrawMemoryPoolGroups(infoCount);
        }

        private void DrawOverview(int infoCount)
        {
            int totalUnused = 0;
            int totalUsing = 0;
            int totalArrayLen = 0;
            for (int i = 0; i < infoCount; i++)
            {
                ref MemoryPoolInfo info = ref m_InfoBuffer[i];
                totalUnused += info.UnusedCount;
                totalUsing += info.UsingCount;
                totalArrayLen += info.PoolArrayLength;
            }

            DrawSectionBegin("Overview");
            DrawReadOnlyRow("Total Cached", totalUnused.ToString(), totalUnused > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Total In Use", totalUsing.ToString(), totalUsing > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawReadOnlyRow("Total Array Capacity", totalArrayLen.ToString(), totalArrayLen > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawSectionEnd();
        }

        private void DrawActions()
        {
            DrawSectionBegin("Actions");
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            GUILayout.FlexibleSpace();
            if (AlicizaEditorGUI.DrawInlineButton("Clear All Pools", ActionButtonWidth))
            {
                MemoryPoolRegistry.ClearAll();
            }

            EditorGUILayout.EndHorizontal();
            DrawSectionEnd();
        }

        private void DrawMemoryPoolGroups(int infoCount)
        {
            RebuildGroups(infoCount);
            if (m_ActiveAssemblyKeys.Count == 0)
            {
                DrawSectionBegin("Pools");
                DrawEmptyState("No memory pools.");
                DrawSectionEnd();
                return;
            }

            DrawSectionBegin("Pools");
            for (int i = 0; i < m_ActiveAssemblyKeys.Count; i++)
            {
                string assemblyName = m_ActiveAssemblyKeys[i];
                List<int> indices = m_GroupedIndices[assemblyName];
                bool lastState = m_OpenedItems.Contains(assemblyName);
                bool currentState = DrawAssemblyHeader(assemblyName, indices.Count, lastState);
                if (currentState != lastState)
                {
                    if (currentState)
                    {
                        m_OpenedItems.Add(assemblyName);
                    }
                    else
                    {
                        m_OpenedItems.Remove(assemblyName);
                    }
                }

                if (!currentState)
                {
                    continue;
                }

                indices.Sort(m_ShowFullClassName ? CompareFullClassName : CompareNormalClassName);
                EditorGUILayout.BeginVertical(_entryBodyStyle);
                DrawPoolInfoHeader();
                for (int j = 0; j < indices.Count; j++)
                {
                    DrawMemoryPoolInfo(indices[j]);
                }

                EditorGUILayout.BeginHorizontal(_fieldRowStyle);
                GUILayout.FlexibleSpace();
                if (AlicizaEditorGUI.DrawInlineButton("Export CSV", ExportButtonWidth))
                {
                    ExportCsv(assemblyName, indices);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            DrawSectionEnd();
        }

        private int FetchInfos()
        {
            int poolCount = MemoryPool.Count;
            if (m_InfoBuffer.Length < poolCount)
            {
                m_InfoBuffer = new MemoryPoolInfo[GetBufferCapacity(poolCount)];
            }

            return MemoryPool.GetAllMemoryPoolInfos(m_InfoBuffer);
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

        private bool DrawAssemblyHeader(string assemblyName, int count, bool expanded)
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
            GUI.Label(kindRect, "ASM", _kindBadgeStyle);
            GUI.Label(titleRect, assemblyName, _rowLabelStyle);
            GUI.Label(summaryRect, "Pools " + count, _mutedMiniLabelStyle);

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && rowRect.Contains(currentEvent.mousePosition))
            {
                expanded = !expanded;
                GUI.FocusControl(string.Empty);
                currentEvent.Use();
            }

            return expanded;
        }

        private void DrawEnumPropertyRow(string label, SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            Rect popupRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
            property.enumValueIndex = AlicizaEditorGUI.DrawStyledPopup(popupRect, property.enumValueIndex, property.enumDisplayNames);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawIntPropertyRow(string label, SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            property.intValue = EditorGUILayout.IntField(property.intValue);
            EditorGUILayout.EndHorizontal();
        }

        private int DrawIntValueRow(string label, int value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            int result = EditorGUILayout.IntField(value);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private bool DrawBoolValueRow(string label, bool value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            bool nextValue = GUILayout.Toggle(
                value,
                value ? "Enabled" : "Disabled",
                value ? AlicizaEditorGUI.Styles.PillOn : AlicizaEditorGUI.Styles.PillOff,
                GUILayout.Width(84f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            return nextValue;
        }

        private void DrawPoolInfoHeader()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            DrawTableCell(m_ShowFullClassName ? "Full Class Name" : "Class Name", _fieldLabelStyle, PoolNameColumnWidth);
            DrawTableCell("Unused", _fieldLabelStyle, CountColumnWidth);
            DrawTableCell("Using", _fieldLabelStyle, CountColumnWidth);
            DrawTableCell("Acquire", _fieldLabelStyle, WideCountColumnWidth);
            DrawTableCell("Release", _fieldLabelStyle, WideCountColumnWidth);
            DrawTableCell("Created", _fieldLabelStyle, WideCountColumnWidth);
            DrawTableCell("HiWater", _fieldLabelStyle, WideCountColumnWidth);
            DrawTableCell("MaxCap", _fieldLabelStyle, WideCountColumnWidth);
            DrawTableCell("Idle", _fieldLabelStyle, CompactCountColumnWidth);
            DrawTableCell("ArrLen", _fieldLabelStyle, CountColumnWidth);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMemoryPoolInfo(int bufferIndex)
        {
            ref MemoryPoolInfo info = ref m_InfoBuffer[bufferIndex];
            string name = m_ShowFullClassName ? info.Type.FullName : info.Type.Name;
            GUIStyle valueStyle = info.UsingCount > 0 ? _warningLabelStyle : _rowLabelStyle;
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            DrawTableCell(name, _fieldLabelStyle, PoolNameColumnWidth);
            DrawTableCell(info.UnusedCount.ToString(), valueStyle, CountColumnWidth);
            DrawTableCell(info.UsingCount.ToString(), valueStyle, CountColumnWidth);
            DrawTableCell(info.AcquireCount.ToString(), valueStyle, WideCountColumnWidth);
            DrawTableCell(info.ReleaseCount.ToString(), valueStyle, WideCountColumnWidth);
            DrawTableCell(info.CreateCount.ToString(), valueStyle, WideCountColumnWidth);
            DrawTableCell(info.HighWaterMark.ToString(), valueStyle, WideCountColumnWidth);
            DrawTableCell(info.MaxCapacity.ToString(), valueStyle, WideCountColumnWidth);
            DrawTableCell(info.IdleFrames.ToString(), valueStyle, CompactCountColumnWidth);
            DrawTableCell(info.PoolArrayLength.ToString(), valueStyle, CountColumnWidth);
            GUILayout.FlexibleSpace();
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

        private static void DrawTableCell(string value, GUIStyle style, float width)
        {
            EditorGUILayout.LabelField(value, style, GUILayout.Width(width));
        }

        private void RebuildGroups(int infoCount)
        {
            foreach (KeyValuePair<string, List<int>> pair in m_GroupedIndices)
            {
                pair.Value.Clear();
            }

            m_ActiveAssemblyKeys.Clear();
            for (int i = 0; i < infoCount; i++)
            {
                ref MemoryPoolInfo info = ref m_InfoBuffer[i];
                string assemblyName = info.Type.Assembly.GetName().Name;
                if (!m_GroupedIndices.TryGetValue(assemblyName, out List<int> indices))
                {
                    indices = new List<int>(8);
                    m_GroupedIndices.Add(assemblyName, indices);
                }

                if (indices.Count == 0)
                {
                    m_ActiveAssemblyKeys.Add(assemblyName);
                }

                indices.Add(i);
            }

            m_ActiveAssemblyKeys.Sort(StringComparer.Ordinal);
        }

        private void ExportCsv(string assemblyName, List<int> indices)
        {
            string exportFileName = EditorUtility.SaveFilePanel("Export CSV Data", string.Empty, Utility.Text.Format("Memory Pool Data - {0}.csv", assemblyName), string.Empty);
            if (string.IsNullOrEmpty(exportFileName))
            {
                return;
            }

            try
            {
                int index = 0;
                string[] data = new string[indices.Count + 1];
                data[index++] = "Class Name,Full Class Name,Unused,Using,Acquire,Release,Created,HighWaterMark,MaxCapacity,IdleFrames,ArrayLength";
                for (int i = 0; i < indices.Count; i++)
                {
                    ref MemoryPoolInfo info = ref m_InfoBuffer[indices[i]];
                    data[index++] = Utility.Text.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}",
                        info.Type.Name, info.Type.FullName,
                        info.UnusedCount, info.UsingCount,
                        info.AcquireCount, info.ReleaseCount,
                        info.CreateCount, info.HighWaterMark,
                        info.MaxCapacity, info.IdleFrames, info.PoolArrayLength);
                }

                File.WriteAllLines(exportFileName, data, Encoding.UTF8);
                Debug.Log(Utility.Text.Format("Export memory pool CSV data to '{0}' success.", exportFileName));
            }
            catch (Exception exception)
            {
                Debug.LogError(Utility.Text.Format("Export memory pool CSV data to '{0}' failure, exception is '{1}'.", exportFileName, exception));
            }
        }

        private int CompareNormalClassName(int leftIndex, int rightIndex)
        {
            ref MemoryPoolInfo left = ref m_InfoBuffer[leftIndex];
            ref MemoryPoolInfo right = ref m_InfoBuffer[rightIndex];
            return left.Type.Name.CompareTo(right.Type.Name);
        }

        private int CompareFullClassName(int leftIndex, int rightIndex)
        {
            ref MemoryPoolInfo left = ref m_InfoBuffer[leftIndex];
            ref MemoryPoolInfo right = ref m_InfoBuffer[rightIndex];
            return left.Type.FullName.CompareTo(right.Type.FullName);
        }

        private static int GetBufferCapacity(int count)
        {
            int capacity = 8;
            while (capacity < count)
            {
                capacity <<= 1;
            }

            return capacity;
        }
    }
}
