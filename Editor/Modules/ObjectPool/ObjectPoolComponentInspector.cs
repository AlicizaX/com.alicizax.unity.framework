using AlicizaX;
using AlicizaX.Editor.UIToolkit;
using AlicizaX.ObjectPool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor
{
    [CustomEditor(typeof(ObjectPoolComponent))]
    internal sealed class ObjectPoolComponentInspector : AlicizaUIEditor
    {
        private const int RefreshIntervalMs = 250;
        private const float CompactRowHeight = 24f;
        private const float BoolColumnWidth = 54f;
        private const float CountColumnWidth = 56f;
        private const float FlagColumnWidth = 50f;
        private const float LastUseColumnWidth = 86f;

        private readonly HashSet<string> _expandedPools = new HashSet<string>(StringComparer.Ordinal);
        private ObjectPoolBase[] _objectPools = new ObjectPoolBase[1];
        private ObjectInfo[] _objectInfos = new ObjectInfo[1];
        private VisualElement _runtimeRoot;

        protected override void BuildInspector(VisualElement root)
        {
            AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
            panel.Body.style.paddingBottom = 0f;
            root.Add(panel);

            _runtimeRoot = new VisualElement();
            panel.Body.Add(_runtimeRoot);

            RefreshRuntime();
            AlicizaEditorUI.RefreshEvery(root, RefreshRuntime, RefreshIntervalMs);
        }

        private void RefreshRuntime()
        {
            if (_runtimeRoot == null)
            {
                return;
            }

            _runtimeRoot.Clear();

            if (!EditorApplication.isPlaying)
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                    "Available during runtime only."));
                return;
            }

            if (!(target is ObjectPoolComponent component) || component == null)
            {
                return;
            }

            if (!IsPrefabInHierarchy(component.gameObject))
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                    "Select a scene instance to inspect object pools."));
                return;
            }

            int objectPoolCount = component.Count;

            AlicizaSection overview = AlicizaEditorUI.CreateStaticSection("Overview");
            _runtimeRoot.Add(overview);
            AlicizaEditorUI.AddValueRow(
                overview.Body,
                "Object Pool Count",
                objectPoolCount.ToString(),
                objectPoolCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);

            AlicizaSection pools = AlicizaEditorUI.CreateStaticSection("Object Pools");
            _runtimeRoot.Add(pools);

            if (objectPoolCount <= 0)
            {
                pools.Body.Add(AlicizaEditorUI.CreateEmptyState("No object pools."));
                return;
            }

            EnsureObjectPoolBuffer(objectPoolCount);
            int poolCount = component.GetAllObjectPools(true, _objectPools);
            if (poolCount > _objectPools.Length)
            {
                poolCount = _objectPools.Length;
            }

            if (poolCount <= 0)
            {
                pools.Body.Add(AlicizaEditorUI.CreateEmptyState("No object pools."));
                return;
            }

            for (int i = 0; i < poolCount; i++)
            {
                ObjectPoolBase objectPool = _objectPools[i];
                if (objectPool != null)
                {
                    pools.Body.Add(BuildObjectPoolCard(objectPool));
                }
            }
        }

        private VisualElement BuildObjectPoolCard(ObjectPoolBase objectPool)
        {
            string fullName = objectPool.FullName;
            AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard(
                fullName,
                _expandedPools.Contains(fullName));

            card.Meta.text = "Used " + objectPool.Count + " | Capacity " + objectPool.Capacity;
            card.ExpandedChanged += expanded => SetPoolExpanded(fullName, expanded);

            if (objectPool.AllowMultiSpawn)
            {
                Label multiSpawnBadge = AlicizaEditorUI.CreateBadge(
                    "MULTI SPAWN", AlicizaValueTone.Accent);
                multiSpawnBadge.tooltip = "This pool allows multiple simultaneous spawns.";
                card.Actions.Add(multiSpawnBadge);
            }

            AddPoolDetails(card.Body, objectPool);
            AddObjectInfos(card.Body, objectPool);
            AddPoolActions(card.Body, objectPool);

            return card;
        }

        private void SetPoolExpanded(string fullName, bool expanded)
        {
            if (expanded)
            {
                _expandedPools.Add(fullName);
            }
            else
            {
                _expandedPools.Remove(fullName);
            }
        }

        private static void AddPoolDetails(VisualElement parent, ObjectPoolBase objectPool)
        {
            AlicizaEditorUI.AddValueRow(
                parent,
                "Name",
                string.IsNullOrEmpty(objectPool.Name) ? "<None>" : objectPool.Name);
            AlicizaEditorUI.AddValueRow(parent, "Type", objectPool.ObjectType.FullName);
            AlicizaEditorUI.AddValueRow(
                parent, "Auto Release Interval", objectPool.AutoReleaseInterval.ToString());
            AlicizaEditorUI.AddValueRow(parent, "Capacity", objectPool.Capacity.ToString());
            AlicizaEditorUI.AddValueRow(
                parent,
                "Used Count",
                objectPool.Count.ToString(),
                objectPool.Count > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(parent, "Expire Time", objectPool.ExpireTime.ToString());
            AlicizaEditorUI.AddValueRow(parent, "Priority", objectPool.Priority.ToString());
        }

        private void AddObjectInfos(VisualElement parent, ObjectPoolBase objectPool)
        {
            EnsureObjectInfoBuffer(objectPool.Count);
            int totalObjectInfoCount = objectPool.GetAllObjectInfos(_objectInfos);
            int objectInfoCount = totalObjectInfoCount;
            if (objectInfoCount > _objectInfos.Length)
            {
                objectInfoCount = _objectInfos.Length;
            }

            if (objectInfoCount == 0)
            {
                parent.Add(AlicizaEditorUI.CreateEmptyState("Object Pool is empty."));
                return;
            }

            Label objectsTitle = AlicizaEditorUI.CreateHint("Objects (" + objectInfoCount + ")");
            objectsTitle.style.marginTop = 8f;
            objectsTitle.style.marginBottom = 2f;
            parent.Add(objectsTitle);

            if (totalObjectInfoCount > objectInfoCount)
            {
                parent.Add(AlicizaEditorUI.CreateHint(
                    Utility.Text.Format(
                        "Showing first {0} of {1} objects.", objectInfoCount, totalObjectInfoCount)));
            }

            AddObjectInfoHeader(parent, objectPool.AllowMultiSpawn);

            for (int i = 0; i < objectInfoCount; i++)
            {
                AddObjectInfoRow(parent, objectPool, _objectInfos[i], i);
            }
        }

        private static void AddObjectInfoHeader(VisualElement parent, bool allowMultiSpawn)
        {
            AlicizaRow row = AlicizaEditorUI.AddRow(parent, "Object", null);
            ApplyCompactRowStyle(row);
            row.Caption.AddToClassList("alicia-hint");
            row.Caption.style.whiteSpace = WhiteSpace.NoWrap;
            row.Caption.style.overflow = Overflow.Hidden;

            AddObjectCell(row.Content, "Locked", BoolColumnWidth, true);
            AddObjectCell(
                row.Content, allowMultiSpawn ? "Count" : "In Use", CountColumnWidth, true);
            AddObjectCell(row.Content, "Flag", FlagColumnWidth, true);
            AddObjectCell(row.Content, "Last Use Time", LastUseColumnWidth, true);
        }

        private static void AddObjectInfoRow(
            VisualElement parent,
            ObjectPoolBase objectPool,
            ObjectInfo objectInfo,
            int index)
        {
            string objectName = string.IsNullOrWhiteSpace(objectInfo.Name)
                ? $"{objectPool.ObjectType.Name} #{index + 1}"
                : objectInfo.Name;
            string lastUse = Utility.Text.Format(
                "{0:F1}s ago", Time.realtimeSinceStartup - objectInfo.LastUseTime);
            AlicizaValueTone tone =
                objectInfo.Locked || objectInfo.CustomCanReleaseFlag
                    ? AlicizaValueTone.Warning
                    : AlicizaValueTone.Default;

            AlicizaRow row = AlicizaEditorUI.AddRow(parent, objectName, null);
            ApplyCompactRowStyle(row);
            row.Caption.tooltip = objectName;
            row.Caption.style.whiteSpace = WhiteSpace.NoWrap;
            row.Caption.style.overflow = Overflow.Hidden;

            AddObjectCell(row.Content, objectInfo.Locked.ToString(), BoolColumnWidth, false, tone);
            AddObjectCell(
                row.Content,
                objectPool.AllowMultiSpawn
                    ? objectInfo.SpawnCount.ToString()
                    : objectInfo.IsInUse.ToString(),
                CountColumnWidth,
                false,
                tone);
            AddObjectCell(
                row.Content,
                objectInfo.CustomCanReleaseFlag.ToString(),
                FlagColumnWidth,
                false,
                tone);
            AddObjectCell(row.Content, lastUse, LastUseColumnWidth, false, tone);
        }

        private static void AddObjectCell(
            VisualElement content,
            string text,
            float width,
            bool header,
            AlicizaValueTone tone = AlicizaValueTone.Default)
        {
            Label cell = header
                ? AlicizaEditorUI.CreateHint(text)
                : AlicizaEditorUI.CreateValueLabel(text, tone);

            cell.style.width = width;
            cell.style.minWidth = width;
            cell.style.maxWidth = width;
            cell.style.flexGrow = 0f;
            cell.style.flexShrink = 0f;
            cell.style.marginLeft = 0f;
            cell.style.marginRight = 0f;
            cell.style.whiteSpace = WhiteSpace.NoWrap;
            cell.style.overflow = Overflow.Hidden;

            if (!header)
            {
                cell.style.fontSize = 11f;
            }

            content.Add(cell);
        }

        private static void ApplyCompactRowStyle(VisualElement row)
        {
            row.style.minHeight = CompactRowHeight;
            row.style.paddingTop = 2f;
            row.style.paddingBottom = 2f;
        }

        private void AddPoolActions(VisualElement parent, ObjectPoolBase objectPool)
        {
            var actions = new VisualElement();
            actions.AddToClassList("alicia-actions");

            Button release = AlicizaEditorUI.CreateTextButton(
                "Release All", "Release all unused objects in this pool.", AlicizaButtonTone.Warning);
            release.clicked += () => objectPool.ReleaseAllUnused();
            actions.Add(release);

            Button export = AlicizaEditorUI.CreateTextButton(
                "Export CSV", "Export the object infos of this pool to a CSV file.");
            export.clicked += () => ExportCsv(objectPool);
            actions.Add(export);

            parent.Add(actions);
        }

        private void ExportCsv(ObjectPoolBase objectPool)
        {
            string exportFileName = EditorUtility.SaveFilePanel(
                "Export CSV Data",
                string.Empty,
                Utility.Text.Format("Object Pool Data - {0}.csv", objectPool.Name),
                string.Empty);
            if (string.IsNullOrEmpty(exportFileName))
            {
                return;
            }

            try
            {
                EnsureObjectInfoBuffer(objectPool.Count);
                int objectInfoCount = objectPool.GetAllObjectInfos(_objectInfos);
                if (objectInfoCount > _objectInfos.Length)
                {
                    objectInfoCount = _objectInfos.Length;
                }

                int index = 0;
                string[] data = new string[objectInfoCount + 1];
                data[index++] = Utility.Text.Format(
                    "Name,Locked,{0},Custom Can Release Flag,Last Use Time",
                    objectPool.AllowMultiSpawn ? "Count" : "In Use");
                for (int i = 0; i < objectInfoCount; i++)
                {
                    ObjectInfo objectInfo = _objectInfos[i];
                    string csvLastUse = Utility.Text.Format(
                        "{0:F1}s ago", Time.realtimeSinceStartup - objectInfo.LastUseTime);
                    data[index++] = objectPool.AllowMultiSpawn
                        ? Utility.Text.Format(
                            "{0},{1},{2},{3},{4}",
                            objectInfo.Name,
                            objectInfo.Locked,
                            objectInfo.SpawnCount,
                            objectInfo.CustomCanReleaseFlag,
                            csvLastUse)
                        : Utility.Text.Format(
                            "{0},{1},{2},{3},{4}",
                            objectInfo.Name,
                            objectInfo.Locked,
                            objectInfo.IsInUse,
                            objectInfo.CustomCanReleaseFlag,
                            csvLastUse);
                }

                File.WriteAllLines(exportFileName, data, Encoding.UTF8);
                Debug.Log(Utility.Text.Format(
                    "Export object pool CSV data to '{0}' success.", exportFileName));
            }
            catch (Exception exception)
            {
                Debug.LogError(Utility.Text.Format(
                    "Export object pool CSV data to '{0}' failure, exception is '{1}'.",
                    exportFileName,
                    exception));
            }
        }

        private void EnsureObjectPoolBuffer(int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (_objectPools == null || _objectPools.Length < count)
            {
                _objectPools = new ObjectPoolBase[count];
            }
        }

        private void EnsureObjectInfoBuffer(int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (_objectInfos == null || _objectInfos.Length < count)
            {
                _objectInfos = new ObjectInfo[count];
            }
        }

        private static bool IsPrefabInHierarchy(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return false;
            }

            return PrefabUtility.GetPrefabAssetType(obj) != PrefabAssetType.Regular;
        }
    }
}
