using AlicizaX;
using AlicizaX.Editor.UIToolkit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor
{
    [CustomEditor(typeof(MemoryPoolSetting))]
    internal sealed class MemoryPoolComponentInspector : AlicizaUIEditor
    {
        private const int RuntimeRefreshIntervalMs = 250;

        private readonly Dictionary<string, List<int>> _groupedIndices =
            new Dictionary<string, List<int>>(StringComparer.Ordinal);

        private readonly List<string> _activeAssemblyKeys = new List<string>(16);
        private readonly HashSet<string> _openedItems = new HashSet<string>();
        private MemoryPoolInfo[] _infoBuffer = Array.Empty<MemoryPoolInfo>();

        private SerializedProperty _retainFreeCount;
        private SerializedProperty _softFreeCount;
        private SerializedProperty _hardFreeCount;
        private SerializedProperty _trimPerTick;
        private SerializedProperty _peakWindowSeconds;

        private AlicizaSection _configurationSection;
        private VisualElement _runtimeRoot;
        private bool _showFullClassName;
        private bool _showEmptyPools;

        private void OnEnable()
        {
            _retainFreeCount = serializedObject.FindProperty("m_RetainFreeCount");
            _softFreeCount = serializedObject.FindProperty("m_SoftFreeCount");
            _hardFreeCount = serializedObject.FindProperty("m_HardFreeCount");
            _trimPerTick = serializedObject.FindProperty("m_TrimPerTick");
            _peakWindowSeconds = serializedObject.FindProperty("m_PeakWindowSeconds");
        }

        protected override void BuildInspector(VisualElement root)
        {
            AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
            root.Add(panel);

            BuildConfiguration(panel.Body);

            _runtimeRoot = new VisualElement { name = "alicia-memory-pool-runtime" };
            panel.Body.Add(_runtimeRoot);

            RefreshRuntime();
            AlicizaEditorUI.RefreshEvery(root, RefreshRuntime, RuntimeRefreshIntervalMs);
        }

        private void BuildConfiguration(VisualElement parent)
        {
            _configurationSection = AlicizaEditorUI.CreateStaticSection("Configuration");
            parent.Add(_configurationSection);

            VisualElement body = _configurationSection.Body;
            MakeIntegerRowDelayed(AlicizaEditorUI.AddPropertyRow(body, "Retain Free", _retainFreeCount));
            MakeIntegerRowDelayed(AlicizaEditorUI.AddPropertyRow(body, "Soft Free", _softFreeCount));
            MakeIntegerRowDelayed(AlicizaEditorUI.AddPropertyRow(body, "Hard Free", _hardFreeCount));
            MakeIntegerRowDelayed(AlicizaEditorUI.AddPropertyRow(body, "Trim Per Tick", _trimPerTick));
            MakeFloatRowDelayed(AlicizaEditorUI.AddPropertyRow(body, "Peak Window (s)", _peakWindowSeconds));
        }

        private static void MakeIntegerRowDelayed(AlicizaRow row)
        {
            row.Content.Q<IntegerField>().isDelayed = true;
        }

        private static void MakeFloatRowDelayed(AlicizaRow row)
        {
            row.Content.Q<FloatField>().isDelayed = true;
        }

        private void RefreshRuntime()
        {
            _runtimeRoot.Clear();

            bool playing = EditorApplication.isPlaying;
            MemoryPoolSetting setting = (MemoryPoolSetting)target;
            bool sceneInstance = IsPrefabInHierarchy(setting.gameObject);

            _configurationSection.style.display = playing && sceneInstance
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            if (!playing)
            {
                AddRuntimeHint("Memory pool runtime data is available in Play mode.");
                return;
            }

            if (!sceneInstance)
            {
                AddRuntimeHint("Select a scene instance to inspect memory pool runtime data.");
                return;
            }

            BuildRuntimeStatus(_runtimeRoot);
            BuildBudget(_runtimeRoot);

            int infoCount = FetchInfos();
            BuildOverview(_runtimeRoot, infoCount);
            BuildActions(_runtimeRoot);
            BuildPoolGroups(_runtimeRoot, infoCount);
        }

        private void AddRuntimeHint(string text)
        {
            _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(text));
        }

        private void BuildRuntimeStatus(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Runtime");
            parent.Add(section);

            int poolCount = MemoryPool.Count;
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Memory Pool Count",
                poolCount.ToString(),
                poolCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);

            AlicizaEditorUI.AddRow(
                section.Body,
                "Show Full Class Name",
                CreatePillToggle(_showFullClassName, value => _showFullClassName = value));

            AlicizaEditorUI.AddRow(
                section.Body,
                "Show Empty Pools",
                CreatePillToggle(_showEmptyPools, value => _showEmptyPools = value));
        }

        private static AlicizaPillToggle CreatePillToggle(bool value, Action<bool> onValueChanged)
        {
            var toggle = new AlicizaPillToggle();
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(evt => onValueChanged(evt.newValue));
            return toggle;
        }

        private static void BuildBudget(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Budget");
            parent.Add(section);

            MemoryPoolBudget budget = MemoryPoolRegistry.Budget;
            AlicizaEditorUI.AddValueRow(section.Body, "Retain Free", budget.RetainFreeCount.ToString(), AlicizaValueTone.Default);
            AlicizaEditorUI.AddValueRow(section.Body, "Soft Free", budget.SoftFreeCount.ToString(), AlicizaValueTone.Default);
            AlicizaEditorUI.AddValueRow(section.Body, "Hard Free", budget.HardFreeCount.ToString(), AlicizaValueTone.Default);
            AlicizaEditorUI.AddValueRow(section.Body, "Trim Per Tick", budget.TrimPerTick.ToString(), AlicizaValueTone.Default);
            AlicizaEditorUI.AddValueRow(section.Body, "Peak Window (s)", budget.PeakWindowSeconds.ToString("0.##"), AlicizaValueTone.Default);
        }

        private void BuildOverview(VisualElement parent, int infoCount)
        {
            int totalFree = 0;
            int totalLeased = 0;
            int totalCreated = 0;
            for (int i = 0; i < infoCount; i++)
            {
                ref MemoryPoolInfo info = ref _infoBuffer[i];
                totalFree += info.FreeCount;
                totalLeased += info.LeasedCount;
                totalCreated += info.CreatedCount;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Overview");
            parent.Add(section);

            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Total Cached",
                totalFree.ToString(),
                totalFree > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Total In Use",
                totalLeased.ToString(),
                totalLeased > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Total Created",
                totalCreated.ToString(),
                totalCreated > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
        }

        private static void BuildActions(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Actions");
            parent.Add(section);

            var actions = new VisualElement();
            actions.AddToClassList("alicia-actions");

            Button trimAll = AlicizaEditorUI.CreateTextButton(
                "Trim All",
                "Evict every cached free object.",
                AlicizaButtonTone.Warning);
            trimAll.clicked += MemoryPool.TrimAll;
            actions.Add(trimAll);

            section.Body.Add(actions);
        }

        private void BuildPoolGroups(VisualElement parent, int infoCount)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Pools");
            parent.Add(section);

            RebuildGroups(infoCount);
            if (_activeAssemblyKeys.Count == 0)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState(
                    _showEmptyPools ? "No memory pools." : "No visible memory pools."));
                return;
            }

            Comparison<int> comparison = CompareNormalClassName;
            if (_showFullClassName)
            {
                comparison = CompareFullClassName;
            }

            for (int i = 0; i < _activeAssemblyKeys.Count; i++)
            {
                string assemblyName = _activeAssemblyKeys[i];
                List<int> indices = _groupedIndices[assemblyName];
                indices.Sort(comparison);
                section.Body.Add(BuildAssemblyCard(assemblyName, indices));
            }
        }

        private AlicizaFoldoutCard BuildAssemblyCard(string assemblyName, List<int> indices)
        {
            bool expanded = _openedItems.Contains(assemblyName);
            AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard(assemblyName, expanded);
            card.Meta.text = Utility.Text.Format("Pools {0}", indices.Count);

            Button export = AlicizaEditorUI.CreateTextButton(
                "Export CSV",
                "Export this assembly's memory pools as CSV.");
            export.clicked += () => ExportCsv(assemblyName);
            card.Actions.Add(export);

            card.ExpandedChanged += value =>
            {
                if (value)
                {
                    _openedItems.Add(assemblyName);
                    if (card.Body.childCount == 0)
                    {
                        PopulatePoolRows(card.Body, indices);
                    }
                }
                else
                {
                    _openedItems.Remove(assemblyName);
                }
            };

            if (expanded)
            {
                PopulatePoolRows(card.Body, indices);
            }

            return card;
        }

        private void PopulatePoolRows(VisualElement parent, List<int> indices)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                parent.Add(BuildPoolRow(indices[i]));
            }
        }

        private VisualElement BuildPoolRow(int bufferIndex)
        {
            ref MemoryPoolInfo info = ref _infoBuffer[bufferIndex];
            string displayName = _showFullClassName ? info.Type.FullName : info.Type.Name;

            var row = new AlicizaRow(displayName);
            row.Caption.tooltip = info.Type.FullName;

            var details = new VisualElement();
            details.style.flexDirection = FlexDirection.Column;
            details.style.flexGrow = 1f;
            details.style.minWidth = 0f;

            Label counters = AlicizaEditorUI.CreateValueLabel(
                Utility.Text.Format(
                    "Free {0}  ·  Leased {1}  ·  Created {2}",
                    info.FreeCount,
                    info.LeasedCount,
                    info.CreatedCount),
                info.LeasedCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Default);

            Label capacity = AlicizaEditorUI.CreateHint(
                Utility.Text.Format(
                    "Target {0}  ·  Peak {1}",
                    info.TargetFreeCount,
                    info.PeakLeasedCount));

            details.Add(counters);
            details.Add(capacity);
            row.Content.Add(details);
            return row;
        }

        private int FetchInfos()
        {
            int poolCount = MemoryPool.Count;
            if (_infoBuffer.Length < poolCount)
            {
                _infoBuffer = new MemoryPoolInfo[GetBufferCapacity(poolCount)];
            }

            return MemoryPool.GetInfos(_infoBuffer);
        }

        private void RebuildGroups(int infoCount)
        {
            foreach (KeyValuePair<string, List<int>> pair in _groupedIndices)
            {
                pair.Value.Clear();
            }

            _activeAssemblyKeys.Clear();
            for (int i = 0; i < infoCount; i++)
            {
                ref MemoryPoolInfo info = ref _infoBuffer[i];
                if (!_showEmptyPools && IsEmptyPool(in info))
                {
                    continue;
                }

                string assemblyName = info.Type.Assembly.GetName().Name;
                if (!_groupedIndices.TryGetValue(assemblyName, out List<int> indices))
                {
                    indices = new List<int>(8);
                    _groupedIndices.Add(assemblyName, indices);
                }

                if (indices.Count == 0)
                {
                    _activeAssemblyKeys.Add(assemblyName);
                }

                indices.Add(i);
            }

            _activeAssemblyKeys.Sort(StringComparer.Ordinal);
        }

        private static bool IsEmptyPool(in MemoryPoolInfo info)
        {
            return info.FreeCount == 0 && info.LeasedCount == 0;
        }

        private void ExportCsv(string assemblyName)
        {
            string exportFileName = EditorUtility.SaveFilePanel(
                "Export CSV Data",
                string.Empty,
                Utility.Text.Format("Memory Pool Data - {0}.csv", assemblyName),
                string.Empty);
            if (string.IsNullOrEmpty(exportFileName))
            {
                return;
            }

            try
            {
                List<MemoryPoolInfo> poolInfos = CollectAssemblyInfos(assemblyName);
                int index = 0;
                string[] data = new string[poolInfos.Count + 1];
                data[index++] = "Class Name,Full Class Name,Free,Leased,Created,TargetFree,PeakLeased";
                for (int i = 0; i < poolInfos.Count; i++)
                {
                    MemoryPoolInfo info = poolInfos[i];
                    data[index++] = Utility.Text.Format("{0},{1},{2},{3},{4},{5},{6}",
                        info.Type.Name, info.Type.FullName,
                        info.FreeCount, info.LeasedCount,
                        info.CreatedCount, info.TargetFreeCount,
                        info.PeakLeasedCount);
                }

                File.WriteAllLines(exportFileName, data, Encoding.UTF8);
                Debug.Log(Utility.Text.Format("Export memory pool CSV data to '{0}' success.", exportFileName));
            }
            catch (Exception exception)
            {
                Debug.LogError(Utility.Text.Format("Export memory pool CSV data to '{0}' failure, exception is '{1}'.", exportFileName, exception));
            }
        }

        private List<MemoryPoolInfo> CollectAssemblyInfos(string assemblyName)
        {
            int infoCount = FetchInfos();
            var poolInfos = new List<MemoryPoolInfo>(infoCount);
            for (int i = 0; i < infoCount; i++)
            {
                ref MemoryPoolInfo info = ref _infoBuffer[i];
                if (!_showEmptyPools && IsEmptyPool(in info))
                {
                    continue;
                }

                if (!string.Equals(info.Type.Assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                poolInfos.Add(info);
            }

            if (_showFullClassName)
            {
                poolInfos.Sort((left, right) => left.Type.FullName.CompareTo(right.Type.FullName));
            }
            else
            {
                poolInfos.Sort((left, right) => left.Type.Name.CompareTo(right.Type.Name));
            }

            return poolInfos;
        }

        private int CompareNormalClassName(int leftIndex, int rightIndex)
        {
            ref MemoryPoolInfo left = ref _infoBuffer[leftIndex];
            ref MemoryPoolInfo right = ref _infoBuffer[rightIndex];
            return left.Type.Name.CompareTo(right.Type.Name);
        }

        private int CompareFullClassName(int leftIndex, int rightIndex)
        {
            ref MemoryPoolInfo left = ref _infoBuffer[leftIndex];
            ref MemoryPoolInfo right = ref _infoBuffer[rightIndex];
            return left.Type.FullName.CompareTo(right.Type.FullName);
        }

        private static bool IsPrefabInHierarchy(UnityEngine.Object obj)
        {
            return PrefabUtility.GetPrefabAssetType(obj) != PrefabAssetType.Regular;
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
