using AlicizaX;
using AlicizaX.ObjectPool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Editor
{
    [CustomEditor(typeof(ObjectPoolComponent))]
    internal sealed class ObjectPoolComponentInspector : GameFrameworkInspector
    {
        private const float ToolbarHeight = 30f;
        private const float RowHeight = 24f;
        private const float RowLabelWidth = 142f;
        private const float ActionButtonWidth = 112f;
        private const float ExportButtonWidth = 104f;
        private const float HeaderKindWidth = 44f;
        private const float HeaderSummaryWidth = 156f;
        private const float ObjectNameColumnWidth = 142f;
        private const float BoolColumnWidth = 54f;
        private const float CountColumnWidth = 50f;
        private const float FlagColumnWidth = 50f;
        private const float LastUseColumnWidth = 84f;

        private readonly HashSet<string> m_OpenedItems = new HashSet<string>();
        private ObjectPoolBase[] m_ObjectPools = new ObjectPoolBase[1];
        private ObjectInfo[] m_ObjectInfos = new ObjectInfo[1];
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
            EnsureStyles();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawToolbar("Object Pool Component");

            if (!EditorApplication.isPlaying)
            {
                EditorUtils.TrHelpIconText("Available during runtime only.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            ObjectPoolComponent component = (ObjectPoolComponent)target;
            if (IsPrefabInHierarchy(component.gameObject))
            {
                DrawSummary(component);
                DrawObjectPools(component);
            }
            else
            {
                DrawEmptyState("Select a scene instance to inspect object pools.");
            }

            EditorGUILayout.EndVertical();
            Repaint();
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

        private void DrawSummary(ObjectPoolComponent component)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField("Object Pool Count", _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            EditorGUILayout.LabelField(component.Count.ToString(), component.Count > 0 ? _rowLabelStyle : _mutedLabelStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawObjectPools(ObjectPoolComponent component)
        {
            int objectPoolCount = EnsureObjectPoolBuffer(component.Count);
            objectPoolCount = component.GetAllObjectPools(true, m_ObjectPools);
            if (objectPoolCount == 0)
            {
                DrawEmptyState("No object pools.");
                return;
            }

            for (int i = 0; i < objectPoolCount; i++)
            {
                ObjectPoolBase objectPool = m_ObjectPools[i];
                if (objectPool != null)
                {
                    DrawObjectPool(objectPool);
                }
            }
        }

        private void DrawObjectPool(ObjectPoolBase objectPool)
        {
            bool lastState = m_OpenedItems.Contains(objectPool.FullName);
            bool currentState = DrawObjectPoolHeader(objectPool, lastState);
            if (currentState != lastState)
            {
                if (currentState)
                {
                    m_OpenedItems.Add(objectPool.FullName);
                }
                else
                {
                    m_OpenedItems.Remove(objectPool.FullName);
                }
            }

            if (!currentState)
            {
                return;
            }

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawDebugRow("Name", string.IsNullOrEmpty(objectPool.Name) ? "<None>" : objectPool.Name);
            DrawDebugRow("Type", objectPool.ObjectType.FullName);
            DrawDebugRow("Auto Release Interval", objectPool.AutoReleaseInterval.ToString());
            DrawDebugRow("Capacity", objectPool.Capacity.ToString());
            DrawDebugRow("Used Count", objectPool.Count.ToString(), objectPool.Count > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawDebugRow("Expire Time", objectPool.ExpireTime.ToString());
            DrawDebugRow("Priority", objectPool.Priority.ToString());

            int objectInfoCount = EnsureObjectInfoBuffer(objectPool.Count);
            objectInfoCount = objectPool.GetAllObjectInfos(m_ObjectInfos);
            if (objectInfoCount > 0)
            {
                DrawObjectInfoHeader(objectPool.AllowMultiSpawn);
                for (int i = 0; i < objectInfoCount; i++)
                {
                    DrawObjectInfo(objectPool, m_ObjectInfos[i]);
                }

                DrawActions(objectPool, objectInfoCount);
            }
            else
            {
                DrawEmptyState("Object Pool is empty.");
            }

            EditorGUILayout.EndVertical();
        }

        private bool DrawObjectPoolHeader(ObjectPoolBase objectPool, bool expanded)
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
            GUI.Label(titleRect, objectPool.FullName, _rowLabelStyle);
            GUI.Label(summaryRect, "Used " + objectPool.Count + " | Capacity " + objectPool.Capacity, _mutedMiniLabelStyle);

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && rowRect.Contains(currentEvent.mousePosition))
            {
                expanded = !expanded;
                GUI.FocusControl(string.Empty);
                currentEvent.Use();
            }

            return expanded;
        }

        private void DrawObjectInfoHeader(bool allowMultiSpawn)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            DrawTableCell("Object", _fieldLabelStyle, ObjectNameColumnWidth);
            DrawTableCell("Locked", _fieldLabelStyle, BoolColumnWidth);
            DrawTableCell(allowMultiSpawn ? "Count" : "In Use", _fieldLabelStyle, CountColumnWidth);
            DrawTableCell("Flag", _fieldLabelStyle, FlagColumnWidth);
            DrawTableCell("Last Use Time", _fieldLabelStyle, LastUseColumnWidth);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawObjectInfo(ObjectPoolBase objectPool, ObjectInfo objectInfo)
        {
            string objectName = string.IsNullOrEmpty(objectInfo.Name) ? "<None>" : objectInfo.Name;
            string lastUse = Utility.Text.Format("{0:F1}s ago", Time.realtimeSinceStartup - objectInfo.LastUseTime);
            GUIStyle valueStyle = objectInfo.Locked || objectInfo.CustomCanReleaseFlag ? _warningLabelStyle : _rowLabelStyle;

            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            DrawTableCell(objectName, _fieldLabelStyle, ObjectNameColumnWidth);
            DrawTableCell(objectInfo.Locked.ToString(), valueStyle, BoolColumnWidth);
            DrawTableCell(objectPool.AllowMultiSpawn ? objectInfo.SpawnCount.ToString() : objectInfo.IsInUse.ToString(), valueStyle, CountColumnWidth);
            DrawTableCell(objectInfo.CustomCanReleaseFlag.ToString(), valueStyle, FlagColumnWidth);
            DrawTableCell(lastUse, valueStyle, LastUseColumnWidth);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActions(ObjectPoolBase objectPool, int objectInfoCount)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            GUILayout.FlexibleSpace();
            if (AlicizaEditorGUI.DrawInlineButton("Release", ActionButtonWidth))
            {
                objectPool.Release();
            }

            if (AlicizaEditorGUI.DrawInlineButton("Release All", ActionButtonWidth))
            {
                objectPool.ReleaseAllUnused();
            }

            if (AlicizaEditorGUI.DrawInlineButton("Export CSV", ExportButtonWidth))
            {
                ExportCsv(objectPool, objectInfoCount);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawDebugRow(string label, string value)
        {
            DrawDebugRow(label, value, string.IsNullOrEmpty(value) ? _mutedLabelStyle : _rowLabelStyle);
        }

        private void DrawDebugRow(string label, string value, GUIStyle valueStyle)
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

        private void ExportCsv(ObjectPoolBase objectPool, int objectInfoCount)
        {
            string exportFileName = EditorUtility.SaveFilePanel("Export CSV Data", string.Empty, Utility.Text.Format("Object Pool Data - {0}.csv", objectPool.Name), string.Empty);
            if (string.IsNullOrEmpty(exportFileName))
            {
                return;
            }

            try
            {
                int index = 0;
                string[] data = new string[objectInfoCount + 1];
                data[index++] = Utility.Text.Format("Name,Locked,{0},Custom Can Release Flag,Last Use Time", objectPool.AllowMultiSpawn ? "Count" : "In Use");
                for (int i = 0; i < objectInfoCount; i++)
                {
                    ObjectInfo objectInfo = m_ObjectInfos[i];
                    string csvLastUse = Utility.Text.Format("{0:F1}s ago", Time.realtimeSinceStartup - objectInfo.LastUseTime);
                    data[index++] = objectPool.AllowMultiSpawn
                        ? Utility.Text.Format("{0},{1},{2},{3},{4}", objectInfo.Name, objectInfo.Locked, objectInfo.SpawnCount, objectInfo.CustomCanReleaseFlag, csvLastUse)
                        : Utility.Text.Format("{0},{1},{2},{3},{4}", objectInfo.Name, objectInfo.Locked, objectInfo.IsInUse, objectInfo.CustomCanReleaseFlag, csvLastUse);
                }

                File.WriteAllLines(exportFileName, data, Encoding.UTF8);
                Debug.Log(Utility.Text.Format("Export object pool CSV data to '{0}' success.", exportFileName));
            }
            catch (Exception exception)
            {
                Debug.LogError(Utility.Text.Format("Export object pool CSV data to '{0}' failure, exception is '{1}'.", exportFileName, exception));
            }
        }

        private int EnsureObjectPoolBuffer(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (m_ObjectPools == null || m_ObjectPools.Length < count)
            {
                m_ObjectPools = new ObjectPoolBase[count];
            }

            return count;
        }

        private int EnsureObjectInfoBuffer(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (m_ObjectInfos == null || m_ObjectInfos.Length < count)
            {
                m_ObjectInfos = new ObjectInfo[count];
            }

            return count;
        }
    }
}
