using System.Collections.Generic;
using AlicizaX.Editor.UIToolkit;
using AlicizaX.UI.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.UI.Editor
{
    [CustomEditor(typeof(UIComponent))]
    internal sealed class UIComponentInspector : AlicizaUIEditor
    {
        private const int CacheDebugInfoCapacity = 64;
        private const int CacheWindowWarningCount = 16;
        private const long RuntimeRefreshIntervalMs = 250;

        private readonly UIServiceDebugInfo _serviceInfo = new UIServiceDebugInfo();
        private readonly UILayerDebugInfo _layerInfo = new UILayerDebugInfo();
        private readonly UIWindowDebugInfo _windowInfo = new UIWindowDebugInfo();
        private readonly UIRouteDebugInfo _routeInfo = new UIRouteDebugInfo();
        private readonly UIWindowDebugInfo[] _cacheInfos = new UIWindowDebugInfo[CacheDebugInfoCapacity];
        private readonly List<string> _alerts = new List<string>(16);
        private readonly Dictionary<int, int> _visibleDepths = new Dictionary<int, int>(32);

        private SerializedProperty _uiRootProperty;
        private SerializedProperty _isOrthographicProperty;

        private VisualElement _settingsBody;
        private VisualElement _runtimeControls;
        private VisualElement _runtimeContent;
        private VisualElement _navigationControls;
        private VisualElement _navigationContent;
        private AlicizaFoldoutCard _runtimeCard;
        private AlicizaFoldoutCard _navigationCard;
        private ObjectField _uiRootField;
        private Button _defaultRootButton;
        private VisualElement _uiRootWarning;

        private bool _showRuntimeDebug;
        private bool _showNavigationRuntimeDebug;
        private bool _showReferences;
        private bool _showLayers = true;
        private bool _showCache = true;
        private bool _showEmptyLayers;
        private bool _showNavigationHistory = true;

        private void OnEnable()
        {
            _uiRootProperty = serializedObject.FindProperty("uiRoot");
            _isOrthographicProperty = serializedObject.FindProperty("_isOrthographic");

            for (int i = 0; i < _cacheInfos.Length; i++)
            {
                _cacheInfos[i] = new UIWindowDebugInfo();
            }
        }

        protected override void BuildInspector(VisualElement root)
        {
            AlicizaPanel settingsPanel = AlicizaEditorUI.CreatePanel(null);
            root.Add(settingsPanel);
            BuildSettings(settingsPanel.Body);

            _runtimeCard = AlicizaEditorUI.CreateFoldoutCard("UI RuntimeDebug", _showRuntimeDebug);
            _runtimeCard.ExpandedChanged += OnRuntimeCardExpandedChanged;
            root.Add(_runtimeCard);

            _runtimeControls = BuildRuntimeFilterControls();
            _runtimeCard.Body.Add(_runtimeControls);

            _runtimeContent = new VisualElement();
            _runtimeCard.Body.Add(_runtimeContent);

            _navigationCard = AlicizaEditorUI.CreateFoldoutCard(
                "Navigation RuntimeDebug", _showNavigationRuntimeDebug);
            _navigationCard.ExpandedChanged += OnNavigationCardExpandedChanged;
            root.Add(_navigationCard);

            _navigationControls = BuildNavigationFilterControls();
            _navigationCard.Body.Add(_navigationControls);

            _navigationContent = new VisualElement();
            _navigationCard.Body.Add(_navigationContent);

            RefreshRuntime();
            AlicizaEditorUI.RefreshEvery(root, RefreshRuntime, RuntimeRefreshIntervalMs);
        }

        private void OnRuntimeCardExpandedChanged(bool expanded)
        {
            _showRuntimeDebug = expanded;
            RefreshRuntime();
        }

        private void OnNavigationCardExpandedChanged(bool expanded)
        {
            _showNavigationRuntimeDebug = expanded;
            RefreshRuntime();
        }

        private void BuildSettings(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("UI Component");
            parent.Add(section);
            _settingsBody = section.Body;

            BuildUIRootWarning(section.Body);

            AlicizaRow uiRootRow = AlicizaEditorUI.AddPropertyRow(
                section.Body,
                "UI Root Prefab",
                _uiRootProperty,
                true,
                typeof(GameObject),
                false);
            if (uiRootRow != null)
            {
                _uiRootField = uiRootRow.Q<ObjectField>();
                if (_uiRootField != null)
                {
                    _uiRootField.RegisterValueChangedCallback(_ => RefreshSettingsState());
                }

                _defaultRootButton = AlicizaEditorUI.CreateTextButton(
                    "Default", "Set default UI Root Prefab", AlicizaButtonTone.Accent);
                _defaultRootButton.style.marginLeft = 6f;
                _defaultRootButton.clicked += SetDefaultUIRoot;
                uiRootRow.Content.Add(_defaultRootButton);
            }

            if (_isOrthographicProperty != null)
            {
                var orthographic = new AlicizaPillToggle();
                orthographic.BindProperty(_isOrthographicProperty);
                AlicizaEditorUI.AddRow(section.Body, "Orthographic", orthographic);
            }

            RefreshSettingsState();
        }

        private void BuildUIRootWarning(VisualElement parent)
        {
            _uiRootWarning = new VisualElement();
            _uiRootWarning.style.flexDirection = FlexDirection.Row;
            _uiRootWarning.style.alignItems = Align.Center;
            _uiRootWarning.style.marginTop = 2f;
            _uiRootWarning.style.marginBottom = 6f;
            _uiRootWarning.Add(AlicizaEditorUI.CreateBadge("REQUIRED", AlicizaValueTone.Danger));

            Label message = AlicizaEditorUI.CreateValueLabel(
                "UI Root Prefab can not be null.", AlicizaValueTone.Danger);
            message.style.marginLeft = 8f;
            _uiRootWarning.Add(message);

            parent.Add(_uiRootWarning);
        }

        private void RefreshSettingsState()
        {
            bool locked = EditorApplication.isPlayingOrWillChangePlaymode;
            if (_settingsBody != null)
            {
                _settingsBody.SetEnabled(!locked);
            }

            bool missing = _uiRootProperty != null && _uiRootProperty.objectReferenceValue == null;
            if (_uiRootWarning != null)
            {
                _uiRootWarning.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_defaultRootButton != null)
            {
                _defaultRootButton.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void SetDefaultUIRoot()
        {
            if (_uiRootProperty == null)
            {
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UIGlobalPath.UIPrefabPath);
            if (prefab == null)
            {
                return;
            }

            _uiRootProperty.objectReferenceValue = prefab;
            _uiRootProperty.serializedObject.ApplyModifiedProperties();

            if (_uiRootField != null)
            {
                _uiRootField.SetValueWithoutNotify(prefab);
            }

            RefreshSettingsState();
        }

        private void RefreshRuntime()
        {
            if (_runtimeContent == null || _navigationContent == null)
            {
                return;
            }

            RefreshSettingsState();
            RefreshRuntimeDebug();
            RefreshNavigationDebug();
        }

        private void RefreshRuntimeDebug()
        {
            _runtimeContent.Clear();
            HideControls(_runtimeControls);
            SetCardMeta(_runtimeCard, string.Empty);

            if (!_showRuntimeDebug)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                _runtimeContent.Add(AlicizaEditorUI.CreateHint(
                    "Enter Play Mode to inspect runtime UI state."));
                return;
            }

            if (!TryGetUIDebugService(out IUIDebugService debugService))
            {
                SetCardMeta(_runtimeCard, "No Service");
                _runtimeContent.Add(CreateWarningLabel("UI service is not initialized."));
                return;
            }

            debugService.FillServiceDebugInfo(_serviceInfo);
            SetCardMeta(
                _runtimeCard,
                "Open " + _serviceInfo.OpenWindowCount + " | Cache " + _serviceInfo.CacheWindowCount);
            ShowControls(_runtimeControls);

            BuildServiceSummary();
            BuildRuntimeAlerts(debugService);
            BuildReferences();
            BuildOpenWindows(debugService);
            BuildCachedWindows(debugService);
        }

        private void BuildServiceSummary()
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Service Summary");
            _runtimeContent.Add(section);

            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Initialized",
                _serviceInfo.Initialized ? "Yes" : "No",
                _serviceInfo.Initialized ? AlicizaValueTone.Default : AlicizaValueTone.Warning);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Mode",
                _serviceInfo.Orthographic ? "Orthographic" : "Perspective");
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Open",
                _serviceInfo.OpenWindowCount.ToString(),
                _serviceInfo.OpenWindowCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Cache",
                _serviceInfo.CacheWindowCount.ToString(),
                _serviceInfo.CacheWindowCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Update",
                _serviceInfo.UpdateCount.ToString(),
                _serviceInfo.UpdateCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Block",
                GetBlockLine(_serviceInfo),
                _serviceInfo.BlockActive ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
        }

        private VisualElement BuildRuntimeFilterControls()
        {
            var controls = new VisualElement();
            VisualElement filters = CreateFilterHost();
            AddFilterChip(filters, "Layers", _showLayers, () => _showLayers = !_showLayers);
            AddFilterChip(filters, "Cache", _showCache, () => _showCache = !_showCache);
            AddFilterChip(filters, "References", _showReferences, () => _showReferences = !_showReferences);
            AddFilterChip(filters, "Empty Layers", _showEmptyLayers, () => _showEmptyLayers = !_showEmptyLayers);
            AlicizaEditorUI.AddRow(controls, "Show", filters);
            return controls;
        }

        private static VisualElement CreateFilterHost()
        {
            var host = new VisualElement();
            host.style.flexDirection = FlexDirection.Row;
            host.style.flexWrap = Wrap.Wrap;
            host.style.alignItems = Align.Center;
            host.style.flexGrow = 1f;
            host.style.flexShrink = 1f;
            host.style.minWidth = 0f;
            return host;
        }

        private void AddFilterChip(VisualElement parent, string text, bool selected, System.Action toggle)
        {
            var chip = new AlicizaFilterChip(text);
            chip.SetSelected(selected);
            chip.clicked += () =>
            {
                toggle();
                chip.SetSelected(!chip.Selected);
                RefreshRuntime();
            };
            parent.Add(chip);
        }

        private static void ShowControls(VisualElement controls)
        {
            if (controls != null)
            {
                controls.style.display = DisplayStyle.Flex;
            }
        }

        private static void HideControls(VisualElement controls)
        {
            if (controls != null)
            {
                controls.style.display = DisplayStyle.None;
            }
        }

        private void BuildRuntimeAlerts(IUIDebugService debugService)
        {
            CollectRuntimeAlerts(debugService);
            if (_alerts.Count == 0)
            {
                return;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Runtime Alerts");
            section.Header.Add(AlicizaEditorUI.CreateBadge(
                _alerts.Count.ToString(), AlicizaValueTone.Warning));
            _runtimeContent.Add(section);

            for (int i = 0; i < _alerts.Count; i++)
            {
                section.Body.Add(CreateWarningLabel(_alerts[i]));
            }
        }

        private void CollectRuntimeAlerts(IUIDebugService debugService)
        {
            _alerts.Clear();

            if (_serviceInfo.CacheWindowCount > CacheWindowWarningCount)
            {
                _alerts.Add("Cached window count is high: " + _serviceInfo.CacheWindowCount);
            }

            for (int layerIndex = 0; layerIndex < debugService.LayerCount; layerIndex++)
            {
                if (!debugService.FillLayerDebugInfo(layerIndex, _layerInfo))
                {
                    continue;
                }

                _visibleDepths.Clear();
                for (int windowIndex = 0; windowIndex < _layerInfo.WindowCount; windowIndex++)
                {
                    if (!debugService.FillWindowDebugInfo(layerIndex, windowIndex, _windowInfo))
                    {
                        continue;
                    }

                    AppendWindowAlerts(_windowInfo, windowIndex);
                }
            }

            int cacheCount = debugService.FillCacheDebugInfo(_cacheInfos, _cacheInfos.Length);
            for (int i = 0; i < cacheCount; i++)
            {
                UIWindowDebugInfo info = _cacheInfos[i];
                if (info.Visible)
                {
                    _alerts.Add(GetWindowName(info) + " is cached but still visible.");
                }
            }
        }

        private void AppendWindowAlerts(UIWindowDebugInfo info, int windowIndex)
        {
            string windowName = GetWindowName(info);

            if (!(info.State == UIState.CreatedUI && info.Processing) && info.HolderTransform == null &&
                info.State != UIState.Uninitialized && info.State != UIState.Destroyed)
            {
                _alerts.Add(windowName + " holder transform is missing in state " + info.State + ".");
            }

            if (info.Visible)
            {
                if (_visibleDepths.TryGetValue(info.Depth, out int otherIndex))
                {
                    _alerts.Add(windowName + " has duplicate visible depth " + info.Depth +
                                " with window index " + otherIndex + ".");
                }
                else
                {
                    _visibleDepths.Add(info.Depth, windowIndex);
                }
            }
        }

        private void BuildReferences()
        {
            if (!_showReferences)
            {
                return;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("References");
            _runtimeContent.Add(section);

            AddObjectRow(section.Body, "Root", _serviceInfo.Root, typeof(Transform));
            AddObjectRow(section.Body, "Canvas", _serviceInfo.Canvas, typeof(Canvas));
            AddObjectRow(section.Body, "Camera", _serviceInfo.Camera, typeof(Camera));
        }

        private void BuildOpenWindows(IUIDebugService debugService)
        {
            if (!_showLayers)
            {
                return;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Open Windows");
            _runtimeContent.Add(section);

            bool hasWindow = false;
            int totalWindowCount = 0;

            for (int layerIndex = 0; layerIndex < debugService.LayerCount; layerIndex++)
            {
                if (!debugService.FillLayerDebugInfo(layerIndex, _layerInfo))
                {
                    continue;
                }

                if (_layerInfo.WindowCount == 0 && !_showEmptyLayers)
                {
                    continue;
                }

                hasWindow |= _layerInfo.WindowCount > 0;
                totalWindowCount += _layerInfo.WindowCount;

                AddLayerHeader(section.Body, _layerInfo);

                var windows = new VisualElement();
                windows.style.marginLeft = 8f;
                section.Body.Add(windows);

                for (int windowIndex = 0; windowIndex < _layerInfo.WindowCount; windowIndex++)
                {
                    if (debugService.FillWindowDebugInfo(layerIndex, windowIndex, _windowInfo))
                    {
                        BuildWindowDebugInfo(windows, _windowInfo, false);
                    }
                }
            }

            if (hasWindow)
            {
                section.Header.Add(AlicizaEditorUI.CreateBadge(
                    totalWindowCount.ToString(), AlicizaValueTone.Accent));
            }

            if (!hasWindow && !_showEmptyLayers)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("No open UI windows."));
            }
        }

        private void BuildCachedWindows(IUIDebugService debugService)
        {
            if (!_showCache)
            {
                return;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Cached Windows");
            _runtimeContent.Add(section);

            int count = debugService.FillCacheDebugInfo(_cacheInfos, _cacheInfos.Length);
            section.Header.Add(AlicizaEditorUI.CreateBadge(
                count.ToString(),
                count > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted));

            if (count == 0)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("No cached UI windows."));
            }

            for (int i = 0; i < count; i++)
            {
                BuildWindowDebugInfo(section.Body, _cacheInfos[i], true);
            }

            if (debugService.CacheWindowCount > _cacheInfos.Length)
            {
                section.Body.Add(CreateWarningLabel(
                    "Cache list is truncated by inspector buffer capacity."));
            }
        }

        private static void AddLayerHeader(VisualElement parent, UILayerDebugInfo info)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginTop = 8f;
            header.style.marginBottom = 2f;

            header.Add(AlicizaEditorUI.CreateBadge("LAYER", AlicizaValueTone.Accent));

            var name = new Label(info.Layer.ToString());
            name.AddToClassList("alicia-value");
            name.style.flexGrow = 0f;
            name.style.marginLeft = 8f;
            header.Add(name);

            var count = new Label("Windows " + info.WindowCount);
            count.AddToClassList("alicia-value");
            count.AddToClassList("alicia-tone--muted");
            count.style.flexGrow = 0f;
            count.style.marginLeft = 8f;
            header.Add(count);

            parent.Add(header);
        }

        private void BuildWindowDebugInfo(VisualElement parent, UIWindowDebugInfo info, bool cached)
        {
            Label summary = AlicizaEditorUI.CreateValueLabel(
                GetWindowSummary(info), AlicizaValueTone.Muted);
            AlicizaRow row = AlicizaEditorUI.AddRow(
                parent, GetWindowTitle(info, cached), summary, true);
            if (row != null)
            {
                string captionTone = GetWindowCaptionTone(info, cached);
                if (!string.IsNullOrEmpty(captionTone))
                {
                    row.Caption.AddToClassList(captionTone);
                }

                row.Content.Insert(0, AlicizaEditorUI.CreateBadge(
                    GetWindowBadge(info, cached), GetWindowBadgeTone(info, cached)));
            }

            var details = new VisualElement();
            details.style.marginLeft = 12f;
            details.style.marginBottom = 4f;
            parent.Add(details);

            AddObjectRow(details, "Transform", info.HolderTransform, typeof(Transform));

            if (cached || info.CacheTime != 0 || info.CacheRemaining > 0f)
            {
                AlicizaEditorUI.AddValueRow(
                    details,
                    "Cache",
                    GetCacheLine(info),
                    cached ? AlicizaValueTone.Muted : AlicizaValueTone.Warning);
            }

            if (!string.IsNullOrEmpty(info.HolderTypeName))
            {
                AlicizaEditorUI.AddValueRow(details, "Holder", info.HolderTypeName);
            }
        }

        private void RefreshNavigationDebug()
        {
            _navigationContent.Clear();
            HideControls(_navigationControls);
            SetCardMeta(_navigationCard, string.Empty);

            if (!_showNavigationRuntimeDebug)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                _navigationContent.Add(AlicizaEditorUI.CreateHint(
                    "Enter Play Mode to inspect runtime UI Navigation state."));
                return;
            }

            if (!TryGetNavigationDebug(out IUINavigationDebug debug))
            {
                SetCardMeta(_navigationCard, "No Service");
                _navigationContent.Add(CreateWarningLabel("UI Navigation is not initialized."));
                return;
            }

            SetCardMeta(_navigationCard, "History " + debug.HistoryCount);
            ShowControls(_navigationControls);

            BuildNavigationSummary(debug);
            BuildNavigationHistory(debug);
        }

        private void BuildNavigationSummary(IUINavigationDebug debug)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Navigation Summary");
            _navigationContent.Add(section);

            System.Type current = debug.Current;
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Current",
                current != null ? current.Name : "None",
                current != null ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Can Back",
                debug.CanBack ? "Yes" : "No",
                debug.CanBack ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "History",
                debug.HistoryCount.ToString(),
                debug.HistoryCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
        }

        private VisualElement BuildNavigationFilterControls()
        {
            var controls = new VisualElement();
            VisualElement filters = CreateFilterHost();
            AddFilterChip(
                filters, "History", _showNavigationHistory,
                () => _showNavigationHistory = !_showNavigationHistory);
            AlicizaEditorUI.AddRow(controls, "Show", filters);
            return controls;
        }

        private void BuildNavigationHistory(IUINavigationDebug debug)
        {
            if (!_showNavigationHistory)
            {
                return;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("History");
            section.Header.Add(AlicizaEditorUI.CreateBadge(
                debug.HistoryCount.ToString(),
                debug.HistoryCount > 0 ? AlicizaValueTone.Accent : AlicizaValueTone.Muted));
            _navigationContent.Add(section);

            if (debug.HistoryCount == 0)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("No route history."));
                return;
            }

            for (int i = 0; i < debug.HistoryCount; i++)
            {
                if (debug.FillHistoryInfo(i, _routeInfo))
                {
                    BuildRouteInfo(section.Body, _routeInfo, "HIS");
                }
            }
        }

        private static void BuildRouteInfo(VisualElement parent, UIRouteDebugInfo info, string badge)
        {
            var content = new VisualElement();
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;
            content.style.flexGrow = 1f;

            content.Add(AlicizaEditorUI.CreateBadge(
                badge, info.IsRoot ? AlicizaValueTone.Accent : AlicizaValueTone.Muted));

            var title = new Label(info.UITypeName ?? "Unknown");
            title.AddToClassList("alicia-value");
            title.style.marginLeft = 8f;
            content.Add(title);

            string summary = GetRouteSummary(info);
            if (!string.IsNullOrEmpty(summary))
            {
                var summaryLabel = new Label(summary);
                summaryLabel.AddToClassList("alicia-value");
                summaryLabel.AddToClassList("alicia-tone--muted");
                summaryLabel.style.flexGrow = 0f;
                summaryLabel.style.marginLeft = 8f;
                content.Add(summaryLabel);
            }

            AlicizaEditorUI.AddRow(parent, "#" + info.Index, content, true);

            if (!string.IsNullOrEmpty(info.ArgsPreview))
            {
                var details = new VisualElement();
                details.style.marginLeft = 12f;
                details.style.marginBottom = 4f;
                AlicizaEditorUI.AddValueRow(
                    details, "Args", info.ArgsPreview, AlicizaValueTone.Muted);
                parent.Add(details);
            }
        }

        private static void AddObjectRow(
            VisualElement parent, string caption, UnityEngine.Object value, System.Type objectType)
        {
            var field = new ObjectField(string.Empty)
            {
                objectType = objectType,
                allowSceneObjects = true
            };
            field.AddToClassList("alicia-control");
            field.AddToClassList("alicia-control--grow");
            field.SetValueWithoutNotify(value);
            field.SetEnabled(false);
            field.tooltip = value != null ? value.name : "None";

            AlicizaEditorUI.AddRow(parent, caption, field);
        }

        private static Label CreateWarningLabel(string text)
        {
            Label label = AlicizaEditorUI.CreateValueLabel(text, AlicizaValueTone.Warning);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 2f;
            label.style.marginBottom = 2f;
            return label;
        }

        private static void SetCardMeta(AlicizaFoldoutCard card, string text)
        {
            if (card != null)
            {
                card.Meta.text = text ?? string.Empty;
            }
        }

        private static bool TryGetUIDebugService(out IUIDebugService debugService)
        {
            debugService = null;
            if (!AppServices.HasWorld)
            {
                return false;
            }

            if (!AppServices.App.TryGet<IUIService>(out IUIService uiService))
            {
                return false;
            }

            debugService = uiService as IUIDebugService;
            return debugService != null;
        }

        private static bool TryGetNavigationDebug(out IUINavigationDebug debug)
        {
            debug = null;
            if (!AppServices.HasWorld)
            {
                return false;
            }

            if (!AppServices.App.TryGet<IUIService>(out IUIService uiService))
            {
                return false;
            }

            debug = uiService as IUINavigationDebug;
            return debug != null;
        }

        private static string GetWindowTitle(UIWindowDebugInfo info, bool cached)
        {
            string logicName = GetWindowName(info);
            string prefix = cached ? "[Cache] " : string.Empty;
            return prefix + "#" + info.OrderIndex + " L" + info.LayerIndex + "  " + logicName;
        }

        private static string GetWindowName(UIWindowDebugInfo info)
        {
            return string.IsNullOrEmpty(info.LogicTypeName) ? "Unknown" : info.LogicTypeName;
        }

        private static string GetCacheLine(UIWindowDebugInfo info)
        {
            if (info.CacheTime < 0)
            {
                return "Permanent";
            }

            if (info.CacheRemaining > 0f)
            {
                return "TTL " + info.CacheTime + "s | Left " + info.CacheRemaining.ToString("F1") + "s";
            }

            return "TTL " + info.CacheTime + "s";
        }

        private static string GetWindowSummary(UIWindowDebugInfo info)
        {
            string text = info.State + " | " + (info.Visible ? "Visible" : "Hidden") + " | Depth " + info.Depth;
            if (info.Updating)
            {
                text += " | Updating";
            }

            if (info.Processing)
            {
                text += " | Processing";
            }

            return text;
        }

        private static string GetBlockLine(UIServiceDebugInfo info)
        {
            if (!info.BlockActive)
            {
                return "Inactive";
            }

            if (info.BlockRemaining > 0f)
            {
                return "Active " + info.BlockRemaining.ToString("F1") + "s";
            }

            return "Active";
        }

        private static string GetWindowBadge(UIWindowDebugInfo info, bool cached)
        {
            if (info.Processing)
            {
                return "OP";
            }

            if (cached)
            {
                return "CCH";
            }

            if (!info.Visible)
            {
                return "HID";
            }

            return "UI";
        }

        private static AlicizaValueTone GetWindowBadgeTone(UIWindowDebugInfo info, bool cached)
        {
            if (info.Processing)
            {
                return AlicizaValueTone.Warning;
            }

            if (cached)
            {
                return AlicizaValueTone.Accent;
            }

            return info.Visible ? AlicizaValueTone.Success : AlicizaValueTone.Muted;
        }

        private static string GetWindowCaptionTone(UIWindowDebugInfo info, bool cached)
        {
            if (info.Processing || !info.Visible)
            {
                return "alicia-tone--warning";
            }

            return cached ? "alicia-tone--muted" : null;
        }

        private static string GetRouteSummary(UIRouteDebugInfo info)
        {
            if (info.Sequence > 0)
            {
                return "Root " + info.IsRoot + " | Seq " + info.Sequence;
            }

            return string.Empty;
        }
    }
}
