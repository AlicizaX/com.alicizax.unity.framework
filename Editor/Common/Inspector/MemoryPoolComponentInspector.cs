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

        private SerializedProperty _shortDecayStartFrames;
        private SerializedProperty _unscheduleIdleFrames;
        private SerializedProperty _zeroFreeReserveStartFrames;
        private SerializedProperty _autoTrimNativeMetadataFrames;
        private SerializedProperty _softFreeReserveLimit;
        private SerializedProperty _hardFreeReserveLimit;

        private IntegerField[] _configurationFields;
        private AlicizaSection _configurationSection;
        private VisualElement _runtimeRoot;
        private bool _showFullClassName;
        private bool _showEmptyPools;
        private bool _syncingConfiguration;

        private void OnEnable()
        {
            _shortDecayStartFrames = serializedObject.FindProperty("m_ShortDecayStartFrames");
            _unscheduleIdleFrames = serializedObject.FindProperty("m_UnscheduleIdleFrames");
            _zeroFreeReserveStartFrames = serializedObject.FindProperty("m_ZeroFreeReserveStartFrames");
            _autoTrimNativeMetadataFrames = serializedObject.FindProperty("m_AutoTrimNativeMetadataFrames");
            _softFreeReserveLimit = serializedObject.FindProperty("m_SoftFreeReserveLimit");
            _hardFreeReserveLimit = serializedObject.FindProperty("m_HardFreeReserveLimit");
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

            if (!HasConfigurationProperties())
            {
                _configurationSection.Body.Add(
                    AlicizaEditorUI.CreateHint("Memory pool configuration properties are unavailable."));
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                NormalizeSerializedConfiguration();
                serializedObject.ApplyModifiedProperties();
            }

            _configurationFields = new IntegerField[6];
            AddConfigurationRow(_configurationSection.Body, "Short Decay Start", _shortDecayStartFrames, 0);
            AddConfigurationRow(_configurationSection.Body, "Zero Free Start", _zeroFreeReserveStartFrames, 1);
            AddConfigurationRow(_configurationSection.Body, "Unschedule Idle", _unscheduleIdleFrames, 2);
            AddConfigurationRow(_configurationSection.Body, "Auto Trim Native", _autoTrimNativeMetadataFrames, 3);
            AddConfigurationRow(_configurationSection.Body, "Soft Free Limit", _softFreeReserveLimit, 4);
            AddConfigurationRow(_configurationSection.Body, "Hard Free Limit", _hardFreeReserveLimit, 5);
            SyncConfigurationFields();
        }

        private bool HasConfigurationProperties()
        {
            return _shortDecayStartFrames != null &&
                   _zeroFreeReserveStartFrames != null &&
                   _unscheduleIdleFrames != null &&
                   _autoTrimNativeMetadataFrames != null &&
                   _softFreeReserveLimit != null &&
                   _hardFreeReserveLimit != null;
        }

        private void AddConfigurationRow(
            VisualElement parent,
            string caption,
            SerializedProperty property,
            int index)
        {
            AlicizaRow row = AlicizaEditorUI.AddPropertyRow(parent, caption, property);
            if (row == null || _configurationFields == null || index >= _configurationFields.Length)
            {
                return;
            }

            IntegerField field = row.Content.Q<IntegerField>();
            if (field == null)
            {
                return;
            }

            field.isDelayed = true;
            _configurationFields[index] = field;
            field.RegisterValueChangedCallback(evt =>
            {
                if (_syncingConfiguration)
                {
                    return;
                }

                property.intValue = evt.newValue;
                ApplyConfigurationChanges();
            });
        }

        private void ApplyConfigurationChanges()
        {
            _syncingConfiguration = true;
            try
            {
                NormalizeSerializedConfiguration();
                serializedObject.ApplyModifiedProperties();
                SyncConfigurationFields();
            }
            finally
            {
                _syncingConfiguration = false;
            }
        }

        private void SyncConfigurationFields()
        {
            if (_configurationFields == null)
            {
                return;
            }

            SyncConfigurationField(0, _shortDecayStartFrames);
            SyncConfigurationField(1, _zeroFreeReserveStartFrames);
            SyncConfigurationField(2, _unscheduleIdleFrames);
            SyncConfigurationField(3, _autoTrimNativeMetadataFrames);
            SyncConfigurationField(4, _softFreeReserveLimit);
            SyncConfigurationField(5, _hardFreeReserveLimit);
        }

        private void SyncConfigurationField(int index, SerializedProperty property)
        {
            if (_configurationFields == null ||
                index < 0 ||
                index >= _configurationFields.Length ||
                property == null)
            {
                return;
            }

            IntegerField field = _configurationFields[index];
            if (field != null)
            {
                field.SetValueWithoutNotify(property.intValue);
            }
        }

        private void RefreshRuntime()
        {
            if (_runtimeRoot == null)
            {
                return;
            }

            bool playing = EditorApplication.isPlaying;
            if (playing && IsRuntimeTextInputFocused())
            {
                return;
            }

            _runtimeRoot.Clear();

            MemoryPoolSetting setting = target as MemoryPoolSetting;
            bool sceneInstance = setting != null && IsPrefabInHierarchy(setting.gameObject);

            if (_configurationSection != null)
            {
                _configurationSection.style.display = playing && sceneInstance
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

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
            BuildIdleTrim(_runtimeRoot);
            BuildCapacity(_runtimeRoot);

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
            toggle.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
            return toggle;
        }

        private void BuildIdleTrim(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Idle Trim");
            parent.Add(section);

            var fields = new IntegerField[4];
            Action syncFields = () =>
            {
                SyncRuntimeField(fields[0], MemoryPool.ShortDecayStartFrames);
                SyncRuntimeField(fields[1], MemoryPool.ZeroFreeReserveStartFrames);
                SyncRuntimeField(fields[2], MemoryPool.UnscheduleIdleFrames);
                SyncRuntimeField(fields[3], MemoryPool.AutoTrimNativeMetadataFrames);
            };

            fields[0] = AddRuntimeIntRow(
                section.Body,
                "Short Decay Start",
                MemoryPool.ShortDecayStartFrames,
                ApplyShortDecayStart,
                syncFields);
            fields[1] = AddRuntimeIntRow(
                section.Body,
                "Zero Free Start",
                MemoryPool.ZeroFreeReserveStartFrames,
                ApplyZeroFreeStart,
                syncFields);
            fields[2] = AddRuntimeIntRow(
                section.Body,
                "Unschedule Idle",
                MemoryPool.UnscheduleIdleFrames,
                ApplyUnscheduleIdle,
                syncFields);
            fields[3] = AddRuntimeIntRow(
                section.Body,
                "Auto Trim Native",
                MemoryPool.AutoTrimNativeMetadataFrames,
                ApplyAutoTrimNative,
                syncFields);
        }

        private void BuildCapacity(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Capacity");
            parent.Add(section);

            var fields = new IntegerField[2];
            Action syncFields = () =>
            {
                SyncRuntimeField(fields[0], MemoryPool.DefaultSoftFreeReserveLimit);
                SyncRuntimeField(fields[1], MemoryPool.DefaultHardFreeReserveLimit);
            };

            fields[0] = AddRuntimeIntRow(
                section.Body,
                "Soft Free Limit",
                MemoryPool.DefaultSoftFreeReserveLimit,
                ApplySoftFreeLimit,
                syncFields);
            fields[1] = AddRuntimeIntRow(
                section.Body,
                "Hard Free Limit",
                MemoryPool.DefaultHardFreeReserveLimit,
                ApplyHardFreeLimit,
                syncFields);
        }

        private static IntegerField AddRuntimeIntRow(
            VisualElement parent,
            string caption,
            int value,
            Func<int, int> apply,
            Action afterApply)
        {
            var field = new IntegerField { label = string.Empty, value = value, isDelayed = true };
            field.AddToClassList("alicia-control");
            field.AddToClassList("alicia-control--narrow");
            field.RegisterValueChangedCallback(evt =>
            {
                int applied = apply(evt.newValue);
                if (applied != evt.newValue)
                {
                    field.SetValueWithoutNotify(applied);
                }

                afterApply?.Invoke();
            });

            AlicizaEditorUI.AddRow(parent, caption, field);
            return field;
        }

        private static void SyncRuntimeField(IntegerField field, int value)
        {
            if (field != null && field.value != value)
            {
                field.SetValueWithoutNotify(value);
            }
        }

        private static int ApplyShortDecayStart(int value)
        {
            MemoryPool.ShortDecayStartFrames = Math.Max(0, value);
            NormalizeRuntimeIdleTrim();
            return MemoryPool.ShortDecayStartFrames;
        }

        private static int ApplyZeroFreeStart(int value)
        {
            MemoryPool.ZeroFreeReserveStartFrames = value;
            NormalizeRuntimeIdleTrim();
            return MemoryPool.ZeroFreeReserveStartFrames;
        }

        private static int ApplyUnscheduleIdle(int value)
        {
            MemoryPool.UnscheduleIdleFrames = value;
            NormalizeRuntimeIdleTrim();
            return MemoryPool.UnscheduleIdleFrames;
        }

        private static int ApplyAutoTrimNative(int value)
        {
            MemoryPool.AutoTrimNativeMetadataFrames = value;
            NormalizeRuntimeIdleTrim();
            return MemoryPool.AutoTrimNativeMetadataFrames;
        }

        private static void NormalizeRuntimeIdleTrim()
        {
            MemoryPool.ShortDecayStartFrames = Math.Max(0, MemoryPool.ShortDecayStartFrames);
            MemoryPool.ZeroFreeReserveStartFrames = Math.Max(
                MemoryPool.ShortDecayStartFrames,
                MemoryPool.ZeroFreeReserveStartFrames);
            MemoryPool.UnscheduleIdleFrames = Math.Max(
                MemoryPool.ZeroFreeReserveStartFrames,
                MemoryPool.UnscheduleIdleFrames);
            MemoryPool.AutoTrimNativeMetadataFrames =
                MemoryPool.AutoTrimNativeMetadataFrames < 0
                    ? -1
                    : Math.Max(
                        MemoryPool.ZeroFreeReserveStartFrames,
                        MemoryPool.AutoTrimNativeMetadataFrames);
        }

        private static int ApplySoftFreeLimit(int value)
        {
            int soft = Math.Max(MemoryPool.MinimumFreeReserveLimit, value);
            int hard = Math.Max(soft, MemoryPool.DefaultHardFreeReserveLimit);
            if (soft != MemoryPool.DefaultSoftFreeReserveLimit ||
                hard != MemoryPool.DefaultHardFreeReserveLimit)
            {
                MemoryPool.SetDefaultCapacity(soft, hard);
            }

            return soft;
        }

        private static int ApplyHardFreeLimit(int value)
        {
            int soft = MemoryPool.DefaultSoftFreeReserveLimit;
            int hard = Math.Max(soft, value);
            if (hard != MemoryPool.DefaultHardFreeReserveLimit)
            {
                MemoryPool.SetDefaultCapacity(soft, hard);
            }

            return hard;
        }

        private void BuildOverview(VisualElement parent, int infoCount)
        {
            int totalUnused = 0;
            int totalUsing = 0;
            int totalPageCapacity = 0;
            for (int i = 0; i < infoCount; i++)
            {
                ref MemoryPoolInfo info = ref _infoBuffer[i];
                totalUnused += info.UnusedCount;
                totalUsing += info.UsingCount;
                totalPageCapacity += info.PageCapacity;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Overview");
            parent.Add(section);

            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Total Cached",
                totalUnused.ToString(),
                totalUnused > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Total In Use",
                totalUsing.ToString(),
                totalUsing > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Total Page Capacity",
                totalPageCapacity.ToString(),
                totalPageCapacity > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
        }

        private static void BuildActions(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Actions");
            parent.Add(section);

            var actions = new VisualElement();
            actions.AddToClassList("alicia-actions");

            Button clearCached = AlicizaEditorUI.CreateTextButton(
                "Clear Cached",
                "Release every cached object and its pages.",
                AlicizaButtonTone.Warning);
            clearCached.clicked += MemoryPoolRegistry.ClearAll;
            actions.Add(clearCached);

            Button trimNative = AlicizaEditorUI.CreateTextButton(
                "Trim Native",
                "Release native metadata held by every memory pool.",
                AlicizaButtonTone.Accent);
            trimNative.clicked += MemoryPool.TrimAllNativeMetadata;
            actions.Add(trimNative);

            Button resetStats = AlicizaEditorUI.CreateTextButton(
                "Reset Stats",
                "Reset acquire, release and create counters.",
                AlicizaButtonTone.Danger);
            resetStats.clicked += MemoryPool.ResetAllStats;
            actions.Add(resetStats);

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
                    "Unused {0}  ·  Using {1}  ·  Acquire {2}  ·  Release {3}",
                    info.UnusedCount,
                    info.UsingCount,
                    info.AcquireCount,
                    info.ReleaseCount),
                info.UsingCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Default);

            Label capacity = AlicizaEditorUI.CreateHint(
                Utility.Text.Format(
                    "Created {0}  ·  Target {1}  ·  MaxCap {2}  ·  Idle {3}  ·  PageCap {4}",
                    info.CreateCount,
                    info.TargetFreeReserve,
                    info.MaxCapacity,
                    info.IdleFrames,
                    info.PageCapacity));

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

            return MemoryPool.GetAllMemoryPoolInfos(_infoBuffer);
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
                if (!_showEmptyPools && IsEmptyPool(info))
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

        private void NormalizeSerializedConfiguration()
        {
            _shortDecayStartFrames.intValue = Math.Max(0, _shortDecayStartFrames.intValue);
            _zeroFreeReserveStartFrames.intValue = Math.Max(
                _shortDecayStartFrames.intValue,
                _zeroFreeReserveStartFrames.intValue);
            _unscheduleIdleFrames.intValue = Math.Max(
                _zeroFreeReserveStartFrames.intValue,
                _unscheduleIdleFrames.intValue);
            if (_autoTrimNativeMetadataFrames.intValue < 0)
            {
                _autoTrimNativeMetadataFrames.intValue = -1;
            }
            else
            {
                _autoTrimNativeMetadataFrames.intValue = Math.Max(
                    _zeroFreeReserveStartFrames.intValue,
                    _autoTrimNativeMetadataFrames.intValue);
            }

            _softFreeReserveLimit.intValue = Math.Max(
                MemoryPool.MinimumFreeReserveLimit,
                _softFreeReserveLimit.intValue);
            _hardFreeReserveLimit.intValue = Math.Max(
                _softFreeReserveLimit.intValue,
                _hardFreeReserveLimit.intValue);
        }

        private static bool IsEmptyPool(MemoryPoolInfo info)
        {
            return info.UnusedCount == 0 && info.UsingCount == 0 && info.PageCapacity == 0;
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
                data[index++] = "Class Name,Full Class Name,Unused,Using,Acquire,Release,Created,TargetFreeReserve,MaxCapacity,IdleFrames,PageCapacity";
                for (int i = 0; i < poolInfos.Count; i++)
                {
                    MemoryPoolInfo info = poolInfos[i];
                    data[index++] = Utility.Text.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}",
                        info.Type.Name, info.Type.FullName,
                        info.UnusedCount, info.UsingCount,
                        info.AcquireCount, info.ReleaseCount,
                        info.CreateCount, info.TargetFreeReserve,
                        info.MaxCapacity, info.IdleFrames, info.PageCapacity);
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
                if (!_showEmptyPools && IsEmptyPool(info))
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

        private bool IsRuntimeTextInputFocused()
        {
            if (_runtimeRoot == null || _runtimeRoot.panel == null)
            {
                return false;
            }

            VisualElement focused =
                _runtimeRoot.panel.focusController?.focusedElement as VisualElement;
            if (focused == null || !IsDescendantOf(focused, _runtimeRoot))
            {
                return false;
            }

            return IsRuntimeTextInput(focused);
        }

        private static bool IsRuntimeTextInput(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current is IntegerField || current is FloatField || current is TextField)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPrefabInHierarchy(UnityEngine.Object obj)
        {
            return obj != null &&
                   PrefabUtility.GetPrefabAssetType(obj) != PrefabAssetType.Regular;
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
