using System;
using System.Collections.Generic;
using AlicizaX.Editor.UIToolkit;
using AlicizaX.Resource.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YooAsset;
using YooAsset.Editor;

namespace AlicizaX.Resource.Editor
{
    [CustomEditor(typeof(ResourceComponent))]
    internal sealed class ResourceComponentInspector : AlicizaUIEditor
    {
        private const string NoneOptionName = "<None>";
        private const string ShowReleasedAssetSlotsKey = "AlicizaX.Resource.Editor.ShowReleasedAssetSlots";
        private const int DebugPageSize = 32;
        private const int DownloadingMaxNumMin = 1;
        private const int DownloadingMaxNumMax = 48;
        private const int FailedTryAgainMin = 1;
        private const int FailedTryAgainMax = 48;
        private const float MaxUnloadIntervalMin = 0f;
        private const float MaxUnloadIntervalMax = 3600f;

        private static readonly string[] PlayModeNames =
        {
            "None (未指定允许模式)",
            "EditorSimulateMode (编辑器下的模拟模式)",
            "OfflinePlayMode (单机模式)",
            "HostPlayMode (联机运行模式)",
            "WebPlayMode (WebGL运行模式)"
        };

        private readonly List<string> _decryptionServiceTypeNames = new List<string>();
        private readonly List<string> _packageNameBuffer = new List<string>();
        private readonly ResourceAssetInfo[] _assetInfos = new ResourceAssetInfo[DebugPageSize];
        private readonly ResourceOwnerInfo[] _ownerInfos = new ResourceOwnerInfo[DebugPageSize];
        private readonly ResourceBindingInfo[] _bindingInfos = new ResourceBindingInfo[DebugPageSize];

        private SerializedProperty _milliseconds = null;
        private SerializedProperty _maxUnloadUnusedAssetsInterval = null;
        private SerializedProperty _useSystemUnloadUnusedAssets = null;
        private SerializedProperty _idleAssetExpireTime = null;
        private SerializedProperty _idleAssetCapacity = null;
        private SerializedProperty _expireProcessCountPerFrame = null;
        private SerializedProperty _expireProcessCountWhenUnloading = null;
        private SerializedProperty _downloadingMaxNum = null;
        private SerializedProperty _failedTryAgain = null;
        private SerializedProperty _packageName = null;
        private SerializedProperty _decryptionServices = null;
        private SerializedProperty _playMode = null;
        private SerializedProperty _autoUnloadBundleWhenUnused = null;

        private string[] _packageNames = Array.Empty<string>();
        private string[] _decryptionServiceOptions = Array.Empty<string>();
        private string _packageSignature = string.Empty;

        private AlicizaSection _loadModeSection;
        private VisualElement _packageRowHost;
        private VisualElement _cacheRowsHost;
        private VisualElement _runtimeRoot;
        private AlicizaEnumField _playModeField;
        private AlicizaEnumField _decryptionField;
        private Label _cacheHint;
        private bool _runtimeShown;

        private bool _showResourceDebug = true;
        private bool _showAssetDebug = true;
        private bool _showOwnerDebug = true;
        private bool _showBindingDebug = true;
        private bool _showReleasedAssetSlots;

        private void OnEnable()
        {
            CacheProperties();
            RefreshDecryptionServices();
            _showReleasedAssetSlots = EditorPrefs.GetBool(ShowReleasedAssetSlotsKey, false);
        }

        private void CacheProperties()
        {
            _milliseconds = Find("milliseconds");
            _maxUnloadUnusedAssetsInterval = Find("maxUnloadUnusedAssetsInterval");
            _useSystemUnloadUnusedAssets = Find("useSystemUnloadUnusedAssets");
            _idleAssetExpireTime = Find("idleAssetExpireTime");
            _idleAssetCapacity = Find("idleAssetCapacity");
            _expireProcessCountPerFrame = Find("expireProcessCountPerFrame");
            _expireProcessCountWhenUnloading = Find("expireProcessCountWhenUnloading");
            _downloadingMaxNum = Find("downloadingMaxNum");
            _failedTryAgain = Find("failedTryAgain");
            _packageName = Find("packageName");
            _decryptionServices = Find("decryptionServices");
            _playMode = Find("_playMode");
            _autoUnloadBundleWhenUnused = Find("autoUnloadBundleWhenUnused");
        }

        protected override void BuildInspector(VisualElement root)
        {
            if (_milliseconds == null)
            {
                CacheProperties();
            }

            AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
            root.Add(panel);

            BuildLoadModeSection(panel.Body);
            BuildLoadingSection(panel.Body);
            BuildUnloadSection(panel.Body);
            BuildCacheSection(panel.Body);
            BuildEditorSection(panel.Body);

            _runtimeRoot = new VisualElement { name = "alicia-resource-runtime" };
            _runtimeShown = false;
            panel.Body.Add(_runtimeRoot);

            RefreshRuntime();
            AlicizaEditorUI.RefreshEvery(root, RefreshRuntime, 250);
        }

        private void BuildLoadModeSection(VisualElement parent)
        {
            _loadModeSection = AlicizaEditorUI.CreateStaticSection("Load Mode");
            parent.Add(_loadModeSection);

            AddPlayModeRow(_loadModeSection.Body);
            AddDecryptionServiceRow(_loadModeSection.Body);

            _packageRowHost = new VisualElement { name = "alicia-resource-package-row" };
            _loadModeSection.Body.Add(_packageRowHost);
            RefreshPackageNames();
            _packageSignature = GetCurrentPackageSignature();
            RebuildPackageRow();

            AlicizaRow autoUnloadRow = AlicizaEditorUI.AddPropertyRow(
                _loadModeSection.Body,
                "Auto Unload Bundle",
                _autoUnloadBundleWhenUnused);
            SetCaptionTooltip(autoUnloadRow, "Automatically unload a bundle once its reference count reaches zero.");
        }

        private void AddPlayModeRow(VisualElement parent)
        {
            if (_playMode == null)
            {
                parent.Add(AlicizaEditorUI.CreateHint("Play mode property was not found on the resource component."));
                return;
            }

            var field = new AlicizaEnumField(PlayModeNames, GetPlayModePreferenceIndex());
            field.AddToClassList("alicia-control--grow");
            field.RegisterValueChangedCallback(evt =>
            {
                int selected = Mathf.Clamp(evt.newValue, 0, PlayModeNames.Length - 1);
                EditorPrefs.SetInt(ResourceComponent.PrefsKey, selected);
                if (_playMode.enumValueIndex == selected)
                {
                    return;
                }

                _playMode.enumValueIndex = selected;
                _playMode.serializedObject.ApplyModifiedProperties();
            });
            _playModeField = field;

            AlicizaRow row = AlicizaEditorUI.AddRow(parent, "Play Mode", field);
            SetCaptionTooltip(
                row,
                "Stored per machine in EditorPrefs and applied when the resource component awakens. Editor-only override.");
        }

        private void AddDecryptionServiceRow(VisualElement parent)
        {
            if (_decryptionServices == null)
            {
                parent.Add(AlicizaEditorUI.CreateHint("Decryption Service property was not found."));
                return;
            }

            if (_decryptionServiceOptions.Length == 0)
            {
                RefreshDecryptionServices();
            }

            string currentService = _decryptionServices.stringValue;
            int selectedIndex = 0;
            bool missingService = false;
            if (!string.IsNullOrEmpty(currentService))
            {
                selectedIndex = Array.IndexOf(_decryptionServiceOptions, currentService);
                if (selectedIndex < 0)
                {
                    selectedIndex = 0;
                    missingService = true;
                }
            }

            var field = new AlicizaEnumField(_decryptionServiceOptions, selectedIndex);
            field.AddToClassList("alicia-control--grow");
            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue < 0 || evt.newValue >= _decryptionServiceOptions.Length)
                {
                    return;
                }

                string selected = _decryptionServiceOptions[evt.newValue];
                string serviceName = string.Equals(selected, NoneOptionName, StringComparison.Ordinal)
                    ? string.Empty
                    : selected;
                if (string.Equals(_decryptionServices.stringValue, serviceName, StringComparison.Ordinal))
                {
                    return;
                }

                _decryptionServices.stringValue = serviceName;
                _decryptionServices.serializedObject.ApplyModifiedProperties();
            });
            _decryptionField = field;

            AlicizaRow row = AlicizaEditorUI.AddRow(parent, "Decryption Service", field);
            SetCaptionTooltip(row, "Optional IBundleDecryptor implementation used to decrypt bundles.");

            if (missingService)
            {
                parent.Add(AlicizaEditorUI.CreateHint(
                    $"Configured service '{currentService}' is no longer available. Select a valid service."));
            }
            else if (_decryptionServiceOptions.Length <= 1)
            {
                parent.Add(AlicizaEditorUI.CreateHint("No IBundleDecryptor implementations were found."));
            }
        }

        private void BuildLoadingSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Loading");
            parent.Add(section);

            if (_milliseconds != null)
            {
                var millisecondsField = new LongField { label = string.Empty, isDelayed = true };
                millisecondsField.AddToClassList("alicia-control");
                millisecondsField.AddToClassList("alicia-control--grow");
                millisecondsField.BindProperty(_milliseconds);
                AlicizaRow row = AlicizaEditorUI.AddRow(section.Body, "Milliseconds", millisecondsField);
                SetCaptionTooltip(row, "Maximum per-frame time slice for asynchronous operations, in milliseconds.");
            }
            else
            {
                section.Body.Add(AlicizaEditorUI.CreateHint("Milliseconds property was not found."));
            }

            AddIntSliderRow(
                section.Body,
                "Max Downloading",
                _downloadingMaxNum,
                DownloadingMaxNumMin,
                DownloadingMaxNumMax,
                "Max Downloading Num: maximum number of concurrent downloads.");
            AddIntSliderRow(
                section.Body,
                "Max Retry Count",
                _failedTryAgain,
                FailedTryAgainMin,
                FailedTryAgainMax,
                "Max FailedTryAgain Count: maximum failed retry attempts.");
        }

        private void BuildUnloadSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Unload");
            parent.Add(section);

            AlicizaRow useSystemRow = AlicizaEditorUI.AddPropertyRow(
                section.Body,
                "Use System Unload",
                _useSystemUnloadUnusedAssets);
            SetCaptionTooltip(useSystemRow, "Calls Resources.UnloadUnusedAssets after unloading unused resource records.");

            if (_maxUnloadUnusedAssetsInterval != null)
            {
                var slider = new AlicizaSliderField(MaxUnloadIntervalMin, MaxUnloadIntervalMax);
                slider.BindProperty(_maxUnloadUnusedAssetsInterval);
                AlicizaRow row = AlicizaEditorUI.AddRow(section.Body, "Max Unload Interval", slider);
                SetCaptionTooltip(row, "Max Unload Unused Assets Interval, in seconds.");
            }

            AlicizaRow perFrameRow = AlicizaEditorUI.AddPropertyRow(
                section.Body,
                "Expire Per Frame",
                _expireProcessCountPerFrame);
            MakeIntegerRowDelayed(perFrameRow);
            SetCaptionTooltip(perFrameRow, "Expire Process Count Per Frame.");

            AlicizaRow unloadingRow = AlicizaEditorUI.AddPropertyRow(
                section.Body,
                "Expire On Unload",
                _expireProcessCountWhenUnloading);
            MakeIntegerRowDelayed(unloadingRow);
            SetCaptionTooltip(unloadingRow, "Expire Process Count When Unloading. Always at least Expire Per Frame.");
        }

        private void BuildCacheSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Resource Cache");
            parent.Add(section);

            _cacheHint = AlicizaEditorUI.CreateHint("Cache settings are editable in edit mode only.");
            _cacheHint.style.display = DisplayStyle.None;
            section.Body.Add(_cacheHint);

            _cacheRowsHost = new VisualElement { name = "alicia-resource-cache-rows" };
            section.Body.Add(_cacheRowsHost);

            AddNonNegativeIntRow(_cacheRowsHost, "Idle Asset Capacity", _idleAssetCapacity);
            AlicizaRow idleExpireRow = AlicizaEditorUI.AddPropertyRow(
                _cacheRowsHost,
                "Idle Expire Time",
                _idleAssetExpireTime);
            MakeFloatRowDelayed(idleExpireRow);
        }

        private void BuildEditorSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Editor");
            parent.Add(section);

            var toggle = new AlicizaPillToggle();
            toggle.SetValueWithoutNotify(_showReleasedAssetSlots);
            toggle.RegisterValueChangedCallback(evt =>
            {
                _showReleasedAssetSlots = evt.newValue;
                EditorPrefs.SetBool(ShowReleasedAssetSlotsKey, _showReleasedAssetSlots);
            });

            AlicizaRow row = AlicizaEditorUI.AddRow(section.Body, "Show Released Slots", toggle);
            SetCaptionTooltip(row, "Shows released asset slots without references in the runtime Assets table.");

            section.Body.Add(AlicizaEditorUI.CreateHint(
                "Editor-only preference. It filters the released asset slots shown in the runtime Assets table."));
        }

        private void RefreshRuntime()
        {
            if (_runtimeRoot == null)
            {
                return;
            }

            UpdateConfigurationAvailability();
            RefreshPackageOptions();
            SyncEnumFields();

            ResourceComponent component = target as ResourceComponent;
            bool runtimeAvailable = EditorApplication.isPlaying &&
                                    component != null &&
                                    IsPrefabInHierarchy(component.gameObject);

            if (runtimeAvailable != _runtimeShown)
            {
                _runtimeRoot.Clear();
                _runtimeShown = runtimeAvailable;
            }

            if (!runtimeAvailable)
            {
                if (_runtimeRoot.childCount == 0)
                {
                    _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                        "Runtime status and resource debug data are available in Play Mode for a scene instance."));
                }

                return;
            }

            _runtimeRoot.Clear();
            BuildRuntimeContent(component);
        }

        private void UpdateConfigurationAvailability()
        {
            bool loadModeEditable = !EditorApplication.isPlayingOrWillChangePlaymode;
            if (_loadModeSection != null)
            {
                _loadModeSection.Body.SetEnabled(loadModeEditable);
            }

            bool cacheEditable = !EditorApplication.isPlaying;
            if (_cacheRowsHost != null)
            {
                _cacheRowsHost.SetEnabled(cacheEditable);
            }

            if (_cacheHint != null)
            {
                _cacheHint.style.display = cacheEditable ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void BuildRuntimeContent(ResourceComponent component)
        {
            IResourceService resourceService = null;
            AppServices.TryGet<IResourceService>(out resourceService);

            AlicizaSection status = AlicizaEditorUI.CreateStaticSection("Runtime Status");
            _runtimeRoot.Add(status);

            AlicizaEditorUI.AddValueRow(
                status.Body,
                "Unload Unused Assets",
                $"{component.LastUnloadUnusedAssetsOperationElapseSeconds:F2} / {component.MaxUnloadUnusedAssetsInterval:F2}");

            string gameVersion = resourceService != null ? resourceService.ApplicableGameVersion : null;
            bool hasGameVersion = !string.IsNullOrEmpty(gameVersion);
            AlicizaRow versionRow = AlicizaEditorUI.AddValueRow(
                status.Body,
                "Game Version",
                hasGameVersion ? gameVersion : "<Unknown>",
                hasGameVersion ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            SetCaptionTooltip(versionRow, "Applicable Game Version reported by the resource service.");

            AlicizaFoldoutCard debugCard = AlicizaEditorUI.CreateFoldoutCard("Resource Debug", _showResourceDebug);
            debugCard.ExpandedChanged += expanded => { _showResourceDebug = expanded; };
            _runtimeRoot.Add(debugCard);

            if (!debugCard.Expanded)
            {
                return;
            }

            if (resourceService == null)
            {
                debugCard.Body.Add(AlicizaEditorUI.CreateEmptyState("Resource service is not initialized."));
                return;
            }

            BuildAssetDebug(debugCard.Body, resourceService);

            IResourceBindingService bindingService = resourceService.BindingService;
            if (bindingService == null)
            {
                debugCard.Body.Add(AlicizaEditorUI.CreateEmptyState("Resource binding service is not available."));
                return;
            }

            BuildOwnerDebug(debugCard.Body, bindingService);
            BuildBindingDebug(debugCard.Body, bindingService);
        }

        private void BuildAssetDebug(VisualElement parent, IResourceService resourceService)
        {
            AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard("Assets", _showAssetDebug);
            card.ExpandedChanged += expanded => { _showAssetDebug = expanded; };
            parent.Add(card);

            int total = resourceService.GetAssetInfos(_assetInfos, 0, 0);
            card.Meta.text = total == 1 ? "1 slot" : $"{total} slots";

            if (!card.Expanded)
            {
                return;
            }

            if (total == 0)
            {
                card.Body.Add(AlicizaEditorUI.CreateEmptyState("No asset slots are currently recorded."));
                return;
            }

            GetAssetSummary(
                resourceService,
                out int visibleCount,
                out int activeCount,
                out int idleCount,
                out int releasedCount,
                out int handleCount,
                out int directRefTotal,
                out int legacyRefTotal,
                out int bindingRefTotal);

            AddDenseValueRow(
                card.Body,
                "Visible Assets",
                $"{visibleCount} | Active:{activeCount} Idle:{idleCount} Released:{releasedCount}");
            AddDenseValueRow(
                card.Body,
                "Handles / Refs",
                $"Handle:{handleCount} | Direct:{directRefTotal} Legacy:{legacyRefTotal} Binding:{bindingRefTotal}");

            int count = resourceService.GetAssetInfos(_assetInfos, 0, _assetInfos.Length);
            count = Mathf.Min(count, _assetInfos.Length);
            int shownCount = 0;
            for (int i = 0; i < count; i++)
            {
                ResourceAssetInfo info = _assetInfos[i];
                if (ShouldHideReleasedAssetSlot(info))
                {
                    continue;
                }

                shownCount++;
                AddDenseValueRow(
                    card.Body,
                    "#" + i,
                    AlicizaX.Utility.Text.Format(
                        "{0} | {1}/{2} | {3} | {4} | D:{5} L:{6} B:{7} | Idle:{8:F0}s",
                        info.State,
                        string.IsNullOrEmpty(info.Package) ? "<None>" : info.Package,
                        info.Location,
                        string.IsNullOrEmpty(info.TypeName) ? "<Unknown>" : info.TypeName,
                        GetHandleKindName(info.HandleKind),
                        info.DirectRefCount,
                        info.LegacyDirectRefCount,
                        info.BindingRefCount,
                        info.IdleExpireIn),
                    GetAssetStateTone(info.State));
            }

            if (shownCount == 0)
            {
                card.Body.Add(AlicizaEditorUI.CreateEmptyState(
                    _showReleasedAssetSlots
                        ? "No asset slots are currently visible."
                        : "Released slots are hidden. Enable Show Released Slots in the Editor section to inspect them."));
            }
            else if (total > count)
            {
                card.Body.Add(AlicizaEditorUI.CreateHint($"Showing the first {count} of {total} asset slots."));
            }
        }

        private void BuildOwnerDebug(VisualElement parent, IResourceBindingService bindingService)
        {
            AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard("Owners", _showOwnerDebug);
            card.ExpandedChanged += expanded => { _showOwnerDebug = expanded; };
            parent.Add(card);

            int total = bindingService.GetOwnerInfos(_ownerInfos, 0, 0);
            card.Meta.text = total == 1 ? "1 owner" : $"{total} owners";

            if (!card.Expanded)
            {
                return;
            }

            if (total == 0)
            {
                card.Body.Add(AlicizaEditorUI.CreateEmptyState("No resource owners are registered."));
                return;
            }

            int count = bindingService.GetOwnerInfos(_ownerInfos, 0, _ownerInfos.Length);
            count = Mathf.Min(count, _ownerInfos.Length);
            int activeCount = 0;
            for (int i = 0; i < count; i++)
            {
                ResourceOwnerInfo info = _ownerInfos[i];
                if (!info.Active)
                {
                    continue;
                }

                activeCount++;
                AddDenseValueRow(
                    card.Body,
                    "Owner " + info.OwnerId,
                    AlicizaX.Utility.Text.Format(
                        "{0} | GO:{1} Gen:{2} Bind:{3} Target:{4}",
                        GetOwnerDisplayName(info),
                        info.GameObjectId,
                        info.Generation,
                        info.BindingCount,
                        info.RegisteredTargetCount));
                AddObjectRow(card.Body, "Owner Object", info.OwnerObject);
            }

            if (activeCount == 0)
            {
                card.Body.Add(AlicizaEditorUI.CreateEmptyState(
                    total > count
                        ? $"No active owners in the first {count} owner slots."
                        : "No active owners are registered."));
            }
            else if (total > count)
            {
                card.Body.Add(AlicizaEditorUI.CreateHint($"Showing the first {count} of {total} owner slots."));
            }
        }

        private void BuildBindingDebug(VisualElement parent, IResourceBindingService bindingService)
        {
            AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard("Bindings", _showBindingDebug);
            card.ExpandedChanged += expanded => { _showBindingDebug = expanded; };
            parent.Add(card);

            int total = bindingService.GetBindingInfos(_bindingInfos, 0, 0);
            card.Meta.text = total == 1 ? "1 binding" : $"{total} bindings";

            if (!card.Expanded)
            {
                return;
            }

            if (total == 0)
            {
                card.Body.Add(AlicizaEditorUI.CreateEmptyState("No resource bindings are registered."));
                return;
            }

            int count = bindingService.GetBindingInfos(_bindingInfos, 0, _bindingInfos.Length);
            count = Mathf.Min(count, _bindingInfos.Length);
            int activeCount = 0;
            for (int i = 0; i < count; i++)
            {
                ResourceBindingInfo info = _bindingInfos[i];
                if (!info.Active)
                {
                    continue;
                }

                activeCount++;
                AddDenseValueRow(
                    card.Body,
                    info.SlotType.ToString(),
                    AlicizaX.Utility.Text.Format(
                        "Owner:{0} Target:{1} ({2}) Asset:{3} Ver:{4} Runtime:{5}",
                        info.OwnerId,
                        GetBindingTargetDisplayName(info),
                        info.TargetComponentId,
                        info.AssetId,
                        info.Version,
                        info.HasRuntimeObject ? "Yes" : "No"));
                AddObjectRow(card.Body, "Target Object", info.TargetObject);
            }

            if (activeCount == 0)
            {
                card.Body.Add(AlicizaEditorUI.CreateEmptyState(
                    total > count
                        ? $"No active bindings in the first {count} binding slots."
                        : "No active bindings are registered."));
            }
            else if (total > count)
            {
                card.Body.Add(AlicizaEditorUI.CreateHint($"Showing the first {count} of {total} binding slots."));
            }
        }

        private int GetAssetSummary(
            IResourceService resourceService,
            out int visibleCount,
            out int activeCount,
            out int idleCount,
            out int releasedCount,
            out int handleCount,
            out int directRefTotal,
            out int legacyRefTotal,
            out int bindingRefTotal)
        {
            visibleCount = 0;
            activeCount = 0;
            idleCount = 0;
            releasedCount = 0;
            handleCount = 0;
            directRefTotal = 0;
            legacyRefTotal = 0;
            bindingRefTotal = 0;

            int total = resourceService.GetAssetInfos(_assetInfos, 0, _assetInfos.Length);
            int startIndex = 0;
            while (startIndex < total)
            {
                int count = resourceService.GetAssetInfos(_assetInfos, startIndex, _assetInfos.Length);
                int pageCount = Mathf.Min(_assetInfos.Length, Mathf.Max(0, count - startIndex));
                for (int i = 0; i < pageCount; i++)
                {
                    ResourceAssetInfo info = _assetInfos[i];
                    switch (info.State)
                    {
                        case ResourceAssetState.Active:
                            activeCount++;
                            break;
                        case ResourceAssetState.Idle:
                            idleCount++;
                            break;
                        case ResourceAssetState.Released:
                            releasedCount++;
                            break;
                    }

                    if (info.HandleValid)
                    {
                        handleCount++;
                    }

                    directRefTotal += info.DirectRefCount;
                    legacyRefTotal += info.LegacyDirectRefCount;
                    bindingRefTotal += info.BindingRefCount;

                    if (!ShouldHideReleasedAssetSlot(info))
                    {
                        visibleCount++;
                    }
                }

                if (pageCount <= 0)
                {
                    break;
                }

                startIndex += pageCount;
            }

            return total;
        }

        private bool ShouldHideReleasedAssetSlot(ResourceAssetInfo info)
        {
            return !_showReleasedAssetSlots &&
                   info.State == ResourceAssetState.Released &&
                   !info.HandleValid &&
                   info.DirectRefCount == 0 &&
                   info.LegacyDirectRefCount == 0 &&
                   info.BindingRefCount == 0;
        }

        private void AddIntSliderRow(
            VisualElement parent,
            string caption,
            SerializedProperty property,
            int minValue,
            int maxValue,
            string tooltip)
        {
            if (property == null)
            {
                parent.Add(AlicizaEditorUI.CreateHint($"{caption} property was not found."));
                return;
            }

            var slider = new AlicizaIntSliderField(minValue, maxValue);
            slider.BindProperty(property);
            AlicizaRow row = AlicizaEditorUI.AddRow(parent, caption, slider);
            SetCaptionTooltip(row, tooltip);
        }

        private void AddNonNegativeIntRow(VisualElement parent, string caption, SerializedProperty property)
        {
            if (property == null)
            {
                parent.Add(AlicizaEditorUI.CreateHint($"{caption} property was not found."));
                return;
            }

            var field = new IntegerField { label = string.Empty, isDelayed = true };
            field.AddToClassList("alicia-control");
            field.AddToClassList("alicia-control--grow");
            field.BindProperty(property);
            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue >= 0)
                {
                    return;
                }

                property.intValue = 0;
                property.serializedObject.ApplyModifiedProperties();
                field.SetValueWithoutNotify(0);
            });

            AlicizaEditorUI.AddRow(parent, caption, field);
        }

        private static void MakeIntegerRowDelayed(AlicizaRow row)
        {
            IntegerField field = row?.Content.Q<IntegerField>();
            if (field != null)
            {
                field.isDelayed = true;
            }
        }

        private static void MakeFloatRowDelayed(AlicizaRow row)
        {
            FloatField field = row?.Content.Q<FloatField>();
            if (field != null)
            {
                field.isDelayed = true;
            }
        }

        private static void AddDenseValueRow(
            VisualElement parent,
            string caption,
            string value,
            AlicizaValueTone tone = AlicizaValueTone.Default)
        {
            Label label = AlicizaEditorUI.CreateValueLabel(value, tone);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.tooltip = value;
            AlicizaEditorUI.AddRow(parent, caption, label);
        }

        private static void AddObjectRow(VisualElement parent, string caption, UnityEngine.Object value)
        {
            var field = new ObjectField
            {
                label = string.Empty,
                objectType = typeof(UnityEngine.Object),
                allowSceneObjects = true,
                value = value
            };
            field.AddToClassList("alicia-control");
            field.AddToClassList("alicia-control--grow");
            field.SetEnabled(false);
            AlicizaEditorUI.AddRow(parent, caption, field);
        }

        private static void SetCaptionTooltip(AlicizaRow row, string tooltip)
        {
            if (row?.Caption != null)
            {
                row.Caption.tooltip = tooltip;
            }
        }

        private void SyncEnumFields()
        {
            if (_playModeField != null)
            {
                int playModeIndex = GetPlayModePreferenceIndex();
                if (_playModeField.value != playModeIndex)
                {
                    _playModeField.SetValueWithoutNotify(playModeIndex);
                }
            }

            if (_decryptionField == null || _decryptionServices == null)
            {
                return;
            }

            string currentService = _decryptionServices.stringValue;
            int serviceIndex = string.IsNullOrEmpty(currentService)
                ? 0
                : Array.IndexOf(_decryptionServiceOptions, currentService);
            if (serviceIndex < 0)
            {
                serviceIndex = 0;
            }

            if (_decryptionField.value != serviceIndex)
            {
                _decryptionField.SetValueWithoutNotify(serviceIndex);
            }
        }

        private void RefreshPackageOptions()
        {
            if (_packageRowHost == null)
            {
                return;
            }

            RefreshPackageNames();
            string signature = GetCurrentPackageSignature();
            if (string.Equals(signature, _packageSignature, StringComparison.Ordinal))
            {
                return;
            }

            _packageSignature = signature;
            RebuildPackageRow();
        }

        private string GetCurrentPackageSignature()
        {
            string packageValue = _packageName != null ? _packageName.stringValue : string.Empty;
            return GetPackageSignature(_packageNames) + "\u001E" + packageValue;
        }

        private void RebuildPackageRow()
        {
            _packageRowHost.Clear();

            if (_packageName == null)
            {
                _packageRowHost.Add(AlicizaEditorUI.CreateHint("Package name property was not found."));
                return;
            }

            if (_packageNames.Length == 0)
            {
                AlicizaEditorUI.AddValueRow(
                    _packageRowHost,
                    "Package Name",
                    "<No YooAsset Package>",
                    AlicizaValueTone.Warning);
                _packageRowHost.Add(AlicizaEditorUI.CreateHint(
                    "No YooAsset package is configured. Open the YooAsset Collector window to add one."));
                return;
            }

            string currentPackage = _packageName.stringValue;
            int selectedIndex = Array.IndexOf(_packageNames, currentPackage);
            bool missingPackage = selectedIndex < 0;
            string[] options = _packageNames;
            if (missingPackage)
            {
                options = new string[_packageNames.Length + 1];
                options[0] = string.IsNullOrEmpty(currentPackage)
                    ? "<Unset>"
                    : "<Missing: " + currentPackage + ">";
                Array.Copy(_packageNames, 0, options, 1, _packageNames.Length);
                selectedIndex = 0;
            }

            var field = new AlicizaEnumField(options, selectedIndex);
            field.AddToClassList("alicia-control--grow");
            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue < 0 || evt.newValue >= options.Length)
                {
                    return;
                }

                if (missingPackage && evt.newValue == 0)
                {
                    return;
                }

                string selected = options[evt.newValue];
                if (string.Equals(_packageName.stringValue, selected, StringComparison.Ordinal))
                {
                    return;
                }

                _packageName.stringValue = selected;
                _packageName.serializedObject.ApplyModifiedProperties();
            });

            AlicizaRow row = AlicizaEditorUI.AddRow(_packageRowHost, "Package Name", field);
            SetCaptionTooltip(row, "YooAsset package used by the resource service.");
            if (missingPackage)
            {
                _packageRowHost.Add(AlicizaEditorUI.CreateHint(
                    string.IsNullOrEmpty(currentPackage)
                        ? "No package is selected. Choose one from the YooAsset package list."
                        : $"Configured package '{currentPackage}' is not in the YooAsset package list. Choose a package to update it."));
            }
        }

        private void RefreshDecryptionServices()
        {
            _decryptionServiceTypeNames.Clear();
            _decryptionServiceTypeNames.Add(NoneOptionName);
            _decryptionServiceTypeNames.AddRange(
                AlicizaX.Utility.Assembly.GetRuntimeTypeNames(typeof(IBundleDecryptor)));
            _decryptionServiceOptions = _decryptionServiceTypeNames.ToArray();
        }

        private void RefreshPackageNames()
        {
            _packageNameBuffer.Clear();
            BundleCollectorSetting setting = BundleCollectorSettingData.Setting;
            if (setting != null)
            {
                foreach (BundleCollectorPackage package in setting.Packages)
                {
                    _packageNameBuffer.Add(package.PackageName);
                }
            }

            _packageNames = _packageNameBuffer.ToArray();
        }

        private static string GetPackageSignature(string[] packageNames)
        {
            return string.Join("\u001F", packageNames);
        }

        private static int GetPlayModePreferenceIndex()
        {
            return Mathf.Clamp(
                EditorPrefs.GetInt(ResourceComponent.PrefsKey, 0),
                0,
                PlayModeNames.Length - 1);
        }

        private static string GetHandleKindName(byte handleKind)
        {
            return handleKind switch
            {
                1 => "AssetHandle",
                2 => "SubAssetsHandle",
                _ => "None"
            };
        }

        private static AlicizaValueTone GetAssetStateTone(ResourceAssetState state)
        {
            return state switch
            {
                ResourceAssetState.Active => AlicizaValueTone.Success,
                ResourceAssetState.Idle => AlicizaValueTone.Warning,
                ResourceAssetState.Loading => AlicizaValueTone.Accent,
                _ => AlicizaValueTone.Muted
            };
        }

        private static string GetOwnerDisplayName(ResourceOwnerInfo info)
        {
            return info.OwnerObject != null ? info.OwnerObject.name : "<Missing Owner>";
        }

        private static string GetBindingTargetDisplayName(ResourceBindingInfo info)
        {
            if (info.TargetObject != null)
            {
                return info.TargetObject.name;
            }

            return "<Missing Target>";
        }

        private static bool IsPrefabInHierarchy(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return false;
            }

#if UNITY_2018_3_OR_NEWER
            return PrefabUtility.GetPrefabAssetType(obj) != PrefabAssetType.Regular;
#else
            return PrefabUtility.GetPrefabType(obj) != PrefabType.Prefab;
#endif
        }
    }
}
