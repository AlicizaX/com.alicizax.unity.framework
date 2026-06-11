using AlicizaX.Editor;
using AlicizaX.UI.Runtime;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.UI.Editor
{
    [CustomEditor(typeof(UIComponent))]
    internal sealed class UIComponentInspector : GameFrameworkInspector
    {
        private const int CacheDebugInfoCapacity = 64;
        private const float ToolbarHeight = 30f;
        private const float RuntimeToolbarHeight = 26f;
        private const float RowHeight = 24f;
        private const float RowLabelWidth = 118f;
        private const float CounterMinWidth = 84f;
        private const float DefaultButtonSize = 20f;
        private const float RuntimeMinHeight = 260f;
        private const float RuntimeMaxHeight = 720f;
        private const float OperationStuckSeconds = 5f;
        private const int UpdateWindowWarningCount = 8;
        private const int CacheWindowWarningCount = 16;

        private readonly UIServiceDebugInfo _serviceInfo = new UIServiceDebugInfo();
        private readonly UILayerDebugInfo _layerInfo = new UILayerDebugInfo();
        private readonly UIWindowDebugInfo _windowInfo = new UIWindowDebugInfo();
        private readonly UIRouteDebugInfo _routeInfo = new UIRouteDebugInfo();
        private readonly UIRouteWarningInfo _routeWarningInfo = new UIRouteWarningInfo();
        private readonly UIWindowDebugInfo[] _cacheInfos = new UIWindowDebugInfo[CacheDebugInfoCapacity];
        private readonly System.Collections.Generic.List<string> _alerts = new System.Collections.Generic.List<string>(16);
        private readonly System.Collections.Generic.Dictionary<int, int> _visibleDepths = new System.Collections.Generic.Dictionary<int, int>(32);

        private SerializedProperty uiRoot;
        private SerializedProperty _isOrthographic;
        private bool _showRuntimeDebug = true;
        private bool _showRouterRuntimeDebug = true;
        private bool _showReferences;
        private bool _showLayers = true;
        private bool _showCache = true;
        private bool _showEmptyLayers;
        private bool _showRouterHistory = true;
        private bool _showRouterWarnings = true;
        private Vector2 _runtimeScroll;
        private Vector2 _routerRuntimeScroll;
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
        private GUIContent _setDefaultContent;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EnsureStyles();

            DrawComponentSettings();
            serializedObject.ApplyModifiedProperties();

            DrawRuntimeDebugInfo();
            DrawRouterRuntimeDebugInfo();
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private void OnEnable()
        {
            uiRoot = serializedObject.FindProperty("uiRoot");
            _isOrthographic = serializedObject.FindProperty("_isOrthographic");
            for (int i = 0; i < _cacheInfos.Length; i++)
            {
                _cacheInfos[i] = new UIWindowDebugInfo();
            }

            InitializeContents();
        }

        private void InitializeContents()
        {
            _setDefaultContent = new GUIContent(EditorUtils.Styles.Database.image, "Set default UI Root Prefab");
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

        private void DrawComponentSettings()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawToolbar("UI Component");

            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            {
                if (uiRoot.objectReferenceValue == null)
                {
                    EditorUtils.TrHelpIconText("UI Root Prefab can not be null.", MessageType.Error);
                }

                DrawUIRootRow();
                DrawPropertyRow("Orthographic", _isOrthographic);
            }
            EditorGUI.EndDisabledGroup();

            DrawWarningOptionsRow();
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar(string title)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
            GUI.Label(labelRect, title, _rowLabelStyle);
        }

        private void DrawUIRootRow()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField("UI Root Prefab", _fieldLabelStyle, GUILayout.Width(RowLabelWidth));

            EditorGUI.BeginChangeCheck();
            GameObject rootPrefab = (GameObject)EditorGUILayout.ObjectField(uiRoot.objectReferenceValue, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                uiRoot.objectReferenceValue = rootPrefab;
            }

            if (uiRoot.objectReferenceValue == null)
            {
                Rect buttonRect = GUILayoutUtility.GetRect(DefaultButtonSize, DefaultButtonSize, GUILayout.Width(DefaultButtonSize), GUILayout.Height(DefaultButtonSize));
                if (AlicizaEditorGUI.DrawToolbarButton(buttonRect, _setDefaultContent))
                {
                    uiRoot.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(UIGlobalPath.UIPrefabPath);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPropertyRow(string label, SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            EditorGUILayout.PropertyField(property, GUIContent.none);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRuntimeDebugInfo()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(_panelStyle);
            _showRuntimeDebug = DrawFoldoutToolbar("UI RuntimeDebug", _showRuntimeDebug);
            if (!_showRuntimeDebug)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                EditorUtils.TrHelpIconText("Enter Play Mode to inspect runtime UI state.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!AppServices.HasWorld || !AppServices.App.TryGet<IUIService>(out IUIService uiService) || uiService is not IUIDebugService debugService)
            {
                EditorUtils.TrHelpIconText("UI service is not initialized.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            debugService.FillServiceDebugInfo(_serviceInfo);
            DrawServiceSummary();
            DrawRuntimeOptions();

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            _runtimeScroll = GUILayout.BeginScrollView(_runtimeScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.MinHeight(RuntimeMinHeight), GUILayout.MaxHeight(RuntimeMaxHeight));
            DrawRuntimeAlerts(debugService);
            DrawReferences();
            DrawLayerDebugInfo(debugService);
            DrawCacheDebugInfo(debugService);
            GUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private void DrawRouterRuntimeDebugInfo()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(_panelStyle);
            _showRouterRuntimeDebug = DrawFoldoutToolbar("Router RuntimeDebug", _showRouterRuntimeDebug);
            if (!_showRouterRuntimeDebug)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                EditorUtils.TrHelpIconText("Enter Play Mode to inspect runtime UI Router state.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!AppServices.HasWorld || !AppServices.App.TryGet<IUIService>(out IUIService uiService) || uiService.Router is not IUIRouterDebug debug)
            {
                EditorUtils.TrHelpIconText("UI Router is not initialized.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawRouterSummary(debug);
            DrawRouterRuntimeOptions(debug);

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            _routerRuntimeScroll = GUILayout.BeginScrollView(_routerRuntimeScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.MinHeight(220f), GUILayout.MaxHeight(560f));
            DrawRouterHistory(debug);
            DrawRouterWarnings(debug);
            GUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private bool DrawFoldoutToolbar(string title, bool expanded)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect foldRect = new Rect(toolbarRect.x + 7f, toolbarRect.y + 5f, 16f, 20f);
            Rect labelRect = new Rect(foldRect.xMax + 4f, toolbarRect.y + 5f, toolbarRect.width - 34f, 20f);
            AlicizaEditorGUI.DrawFoldoutIcon(foldRect, expanded);
            GUI.Label(labelRect, title, _rowLabelStyle);

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && toolbarRect.Contains(currentEvent.mousePosition))
            {
                expanded = !expanded;
                GUI.FocusControl(string.Empty);
                currentEvent.Use();
            }

            return expanded;
        }

        private void DrawRuntimeOptions()
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, RuntimeToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            float x = toolbarRect.x + 6f;
            float y = toolbarRect.y + 3f;
            _showLayers = DrawToolbarToggle(ref x, y, 58f, "Layers", _showLayers);
            _showCache = DrawToolbarToggle(ref x, y, 58f, "Cache", _showCache);
            _showReferences = DrawToolbarToggle(ref x, y, 86f, "References", _showReferences);
            _showEmptyLayers = DrawToolbarToggle(ref x, y, 96f, "Empty Layers", _showEmptyLayers);
        }

        private static bool DrawToolbarToggle(ref float x, float y, float width, string label, bool value)
        {
            Rect rect = new Rect(x, y, width, 20f);
            x += width + 4f;
            return GUI.Toggle(rect, value, label, value ? AlicizaEditorGUI.Styles.PillOn : AlicizaEditorGUI.Styles.PillOff);
        }

        private void DrawWarningOptionsRow()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField("Editor Warnings", _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            UIWarningSettings.OcclusionWarningsEnabled = DrawPillToggle("Occlusion Warn", UIWarningSettings.OcclusionWarningsEnabled, 112f);
            UIWarningSettings.OtherWarningsEnabled = DrawPillToggle("Other Warn", UIWarningSettings.OtherWarningsEnabled, 92f);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static bool DrawPillToggle(string label, bool value, float width)
        {
            return GUILayout.Toggle(value, label, value ? AlicizaEditorGUI.Styles.PillOn : AlicizaEditorGUI.Styles.PillOff, GUILayout.Width(width), GUILayout.Height(20f));
        }

        private void DrawServiceSummary()
        {
            DrawSectionBegin("Service Summary");
            EditorGUILayout.BeginHorizontal();
            DrawCounter("Initialized", _serviceInfo.Initialized ? "Yes" : "No", _serviceInfo.Initialized ? _rowLabelStyle : _warningLabelStyle);
            DrawCounter("Mode", _serviceInfo.Orthographic ? "Orthographic" : "Perspective", _rowLabelStyle);
            DrawCounter("Layers", _serviceInfo.LayerCount.ToString(), _rowLabelStyle);
            DrawCounter("Open", _serviceInfo.OpenWindowCount.ToString(), _serviceInfo.OpenWindowCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawCounter("Cache", _serviceInfo.CacheWindowCount.ToString(), _serviceInfo.CacheWindowCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            DrawCounter("Update", _serviceInfo.UpdateWindowCount.ToString(), _serviceInfo.UpdateWindowCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawCounter("Block", _serviceInfo.BlockActive ? "Active" : "Inactive", _serviceInfo.BlockActive ? _warningLabelStyle : _mutedLabelStyle);
            DrawCounter("Block Timer", _serviceInfo.BlockTimerHandle.ToString(), _serviceInfo.BlockTimerHandle != 0UL ? _warningLabelStyle : _mutedLabelStyle);
            EditorGUILayout.EndHorizontal();
            DrawSectionEnd();
        }

        private void DrawRouterSummary(IUIRouterDebug debug)
        {
            DrawSectionBegin("Router Summary");
            EditorGUILayout.BeginHorizontal();
            DrawCounter("Current", debug.Current?.Name ?? "None", debug.Current == null ? _mutedLabelStyle : _rowLabelStyle);
            DrawCounter("Can Back", debug.CanBack ? "Yes" : "No", debug.CanBack ? _rowLabelStyle : _mutedLabelStyle);
            DrawCounter("Dirty", debug.IsDirty ? "Yes" : "No", debug.IsDirty ? _warningLabelStyle : _rowLabelStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawCounter("History", debug.HistoryCount.ToString(), debug.HistoryCount > 0 ? _rowLabelStyle : _mutedLabelStyle);
            DrawCounter("Warnings", debug.WarningCount.ToString(), debug.WarningCount > 0 ? _warningLabelStyle : _mutedLabelStyle);
            EditorGUILayout.EndHorizontal();

            if (debug.IsDirty)
            {
                EditorUtils.TrHelpIconText("Router history is dirty. Use ResetTo, ResetHistory, or SyncFromCurrentUI to recover.", MessageType.Warning);
            }

            DrawSectionEnd();
        }

        private void DrawRouterRuntimeOptions(IUIRouterDebug debug)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, RuntimeToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            float x = toolbarRect.x + 6f;
            float y = toolbarRect.y + 3f;
            _showRouterHistory = DrawToolbarToggle(ref x, y, 64f, "History", _showRouterHistory);
            _showRouterWarnings = DrawToolbarToggle(ref x, y, 76f, "Warnings", _showRouterWarnings);

            Rect clearRect = new Rect(toolbarRect.xMax - 106f, y, 100f, 20f);
            EditorGUI.BeginDisabledGroup(debug.WarningCount == 0);
            if (GUI.Button(clearRect, "Clear Warnings", AlicizaEditorGUI.Styles.PillOff))
            {
                debug.ClearWarnings();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawRouterHistory(IUIRouterDebug debug)
        {
            if (!_showRouterHistory)
            {
                return;
            }

            DrawSectionBegin("History");
            if (debug.HistoryCount == 0)
            {
                DrawEmptyLabel("No route history.");
            }

            for (int i = 0; i < debug.HistoryCount; i++)
            {
                if (debug.FillHistoryInfo(i, _routeInfo))
                {
                    DrawRouteInfo(_routeInfo, "HIS");
                }
            }

            DrawSectionEnd();
        }

        private void DrawRouterWarnings(IUIRouterDebug debug)
        {
            if (!_showRouterWarnings)
            {
                return;
            }

            DrawSectionBegin("Warnings");
            if (debug.WarningCount == 0)
            {
                DrawEmptyLabel("No router warnings.");
            }

            for (int i = 0; i < debug.WarningCount; i++)
            {
                if (debug.FillWarningInfo(i, _routeWarningInfo))
                {
                    DrawRouteWarningInfo(_routeWarningInfo);
                }
            }

            DrawSectionEnd();
        }

        private void DrawRouteInfo(UIRouteDebugInfo info, string badge)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
            bool hovered = rowRect.Contains(Event.current.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(rowRect, info.IsRoot, hovered);

            Rect kindRect = new Rect(rowRect.x + 8f, rowRect.y + 3f, 48f, 18f);
            Rect titleRect = new Rect(kindRect.xMax + 6f, rowRect.y + 3f, Mathf.Min(220f, Mathf.Max(128f, rowRect.width * 0.36f)), 18f);
            Rect summaryRect = new Rect(titleRect.xMax + 8f, rowRect.y + 3f, Mathf.Max(0f, rowRect.xMax - titleRect.xMax - 16f), 18f);
            GUI.Label(kindRect, badge, _kindBadgeStyle);
            GUI.Label(titleRect, "#" + info.Index + " " + (info.UITypeName ?? "Unknown"), _rowLabelStyle);
            GUI.Label(summaryRect, GetRouteSummary(info), _mutedMiniLabelStyle);

            if (!string.IsNullOrEmpty(info.ArgsPreview))
            {
                EditorGUILayout.BeginVertical(_entryBodyStyle);
                DrawDebugRow("Args", info.ArgsPreview ?? string.Empty);
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawRouteWarningInfo(UIRouteWarningInfo info)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
            bool hovered = rowRect.Contains(Event.current.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(rowRect, true, hovered);

            Rect kindRect = new Rect(rowRect.x + 8f, rowRect.y + 3f, 48f, 18f);
            Rect titleRect = new Rect(kindRect.xMax + 6f, rowRect.y + 3f, Mathf.Min(220f, Mathf.Max(128f, rowRect.width * 0.36f)), 18f);
            Rect summaryRect = new Rect(titleRect.xMax + 8f, rowRect.y + 3f, Mathf.Max(0f, rowRect.xMax - titleRect.xMax - 16f), 18f);
            GUI.Label(kindRect, "WARN", _kindBadgeStyle);
            GUI.Label(titleRect, "#" + info.Sequence + " " + (info.UITypeName ?? "Unknown"), _warningLabelStyle);
            GUI.Label(summaryRect, string.Empty, _mutedMiniLabelStyle);

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawDebugRow("Message", info.Message ?? string.Empty, _warningLabelStyle);
            EditorGUILayout.EndVertical();
        }

        private static string GetRouteSummary(UIRouteDebugInfo info)
        {
            if (info.Sequence > 0)
            {
                return "Root " + info.IsRoot + " | Seq " + info.Sequence;
            }

            return string.Empty;
        }

        private void DrawReferences()
        {
            if (!_showReferences)
            {
                return;
            }

            DrawSectionBegin("References");
            DrawDebugObjectRow("Root", _serviceInfo.Root, typeof(Transform));
            DrawDebugObjectRow("Canvas Root", _serviceInfo.CanvasRoot, typeof(Transform));
            DrawDebugObjectRow("Canvas", _serviceInfo.Canvas, typeof(Canvas));
            DrawDebugObjectRow("Camera", _serviceInfo.Camera, typeof(Camera));
            DrawSectionEnd();
        }

        private void DrawRuntimeAlerts(IUIDebugService debugService)
        {
            _alerts.Clear();

            if (_serviceInfo.UpdateWindowCount > UpdateWindowWarningCount)
            {
                _alerts.Add("Update window count is high: " + _serviceInfo.UpdateWindowCount);
            }

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

                    AppendWindowAlerts(_windowInfo, _layerInfo, windowIndex);
                }
            }

            int cacheCount = debugService.FillCacheDebugInfo(_cacheInfos, _cacheInfos.Length);
            for (int i = 0; i < cacheCount; i++)
            {
                UIWindowDebugInfo info = _cacheInfos[i];
                if (info.InCache && info.Visible)
                {
                    _alerts.Add(GetWindowName(info) + " is cached but still visible.");
                }
            }

            if (_alerts.Count == 0)
            {
                return;
            }

            DrawSectionBegin("Runtime Alerts");
            for (int i = 0; i < _alerts.Count; i++)
            {
                EditorUtils.TrHelpIconText(_alerts[i], MessageType.Warning);
            }
            DrawSectionEnd();
        }

        private void AppendWindowAlerts(UIWindowDebugInfo info, UILayerDebugInfo layerInfo, int windowIndex)
        {
            string windowName = GetWindowName(info);

            if ((info.ShowInProgress || info.CloseInProgress) && info.StateDuration >= OperationStuckSeconds)
            {
                _alerts.Add(windowName + " has been in " + info.State + " for " + info.StateDuration.ToString("F1") + "s.");
            }

            if (info.InCache && info.Visible)
            {
                _alerts.Add(windowName + " is marked cached while visible.");
            }

            if (info.HolderTransform == null && info.State != UIState.Uninitialized && info.State != UIState.Destroyed)
            {
                _alerts.Add(windowName + " holder transform is missing in state " + info.State + ".");
            }

            if (info.Visible)
            {
                if (_visibleDepths.TryGetValue(info.Depth, out int otherIndex))
                {
                    _alerts.Add(windowName + " has duplicate visible depth " + info.Depth + " with window index " + otherIndex + ".");
                }
                else
                {
                    _visibleDepths.Add(info.Depth, windowIndex);
                }
            }

            if (layerInfo.LastFullscreenIndex >= 0 && windowIndex < layerInfo.LastFullscreenIndex && info.Visible)
            {
                _alerts.Add(windowName + " is visible behind fullscreen window index " + layerInfo.LastFullscreenIndex + ".");
            }
        }

        private void DrawLayerDebugInfo(IUIDebugService debugService)
        {
            if (!_showLayers)
            {
                return;
            }

            DrawSectionBegin("Open Windows");
            bool hasWindow = false;
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
                DrawLayerHeader(_layerInfo);
                EditorGUI.indentLevel++;
                for (int windowIndex = 0; windowIndex < _layerInfo.WindowCount; windowIndex++)
                {
                    if (debugService.FillWindowDebugInfo(layerIndex, windowIndex, _windowInfo))
                    {
                        DrawWindowDebugInfo(_windowInfo, false);
                    }
                }

                EditorGUI.indentLevel--;
            }

            if (!hasWindow && !_showEmptyLayers)
            {
                DrawEmptyLabel("No open UI windows.");
            }

            DrawSectionEnd();
        }

        private void DrawCacheDebugInfo(IUIDebugService debugService)
        {
            if (!_showCache)
            {
                return;
            }

            DrawSectionBegin("Cached Windows");
            int count = debugService.FillCacheDebugInfo(_cacheInfos, _cacheInfos.Length);
            if (count == 0)
            {
                DrawEmptyLabel("No cached UI windows.");
            }

            for (int i = 0; i < count; i++)
            {
                DrawWindowDebugInfo(_cacheInfos[i], true);
            }

            if (debugService.CacheWindowCount > _cacheInfos.Length)
            {
                EditorUtils.TrHelpIconText("Cache list is truncated by inspector buffer capacity.", MessageType.Warning);
            }

            DrawSectionEnd();
        }

        private void DrawLayerHeader(UILayerDebugInfo info)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
            bool hovered = rowRect.Contains(Event.current.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(rowRect, info.WindowCount > 0, hovered);

            Rect kindRect = new Rect(rowRect.x + 8f, rowRect.y + 3f, 48f, 18f);
            Rect titleRect = new Rect(kindRect.xMax + 6f, rowRect.y + 3f, Mathf.Min(160f, Mathf.Max(96f, rowRect.width * 0.30f)), 18f);
            Rect summaryRect = new Rect(titleRect.xMax + 8f, rowRect.y + 3f, Mathf.Max(0f, rowRect.xMax - titleRect.xMax - 16f), 18f);

            GUI.Label(kindRect, "LAYER", _kindBadgeStyle);
            GUI.Label(titleRect, info.Layer.ToString(), _rowLabelStyle);
            GUI.Label(summaryRect, "Windows " + info.WindowCount + " | Last Fullscreen " + info.LastFullscreenIndex, _mutedMiniLabelStyle);
        }

        private void DrawWindowDebugInfo(UIWindowDebugInfo info, bool cached)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
            bool hovered = rowRect.Contains(Event.current.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(rowRect, cached || info.InCache, hovered);

            Rect kindRect = new Rect(rowRect.x + 8f, rowRect.y + 3f, 44f, 18f);
            Rect titleRect = new Rect(kindRect.xMax + 6f, rowRect.y + 3f, Mathf.Min(220f, Mathf.Max(128f, rowRect.width * 0.38f)), 18f);
            Rect summaryRect = new Rect(titleRect.xMax + 8f, rowRect.y + 3f, Mathf.Max(0f, rowRect.xMax - titleRect.xMax - 16f), 18f);
            GUI.Label(kindRect, GetWindowBadge(info, cached), _kindBadgeStyle);
            GUI.Label(titleRect, GetWindowTitle(info, cached), GetWindowTitleStyle(info, cached));
            GUI.Label(summaryRect, GetWindowSummary(info), _mutedMiniLabelStyle);

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawDebugObjectRow("Transform", info.HolderTransform, typeof(Transform));
            DrawDebugRow("State", GetStateLine(info), GetWindowTitleStyle(info, cached));
            DrawDebugRow("Flags", GetFlagLine(info), _mutedLabelStyle);
            if (cached || info.CacheTime > 0f || info.CacheTimerHandle != 0UL)
            {
                DrawDebugRow("Cache", GetCacheLine(info), cached || info.InCache ? _mutedLabelStyle : _warningLabelStyle);
            }

            DrawDebugRow("Holder", string.IsNullOrEmpty(info.HolderTypeName) ? "None" : info.HolderTypeName, string.IsNullOrEmpty(info.HolderTypeName) ? _mutedLabelStyle : _rowLabelStyle);
            EditorGUILayout.EndVertical();
        }

        private static string GetWindowTitle(UIWindowDebugInfo info, bool cached)
        {
            string logicName = GetWindowName(info);
            string prefix = cached ? "[Cache] " : "";
            return prefix + "#" + info.OrderIndex + " L" + info.LayerIndex + "  " + logicName;
        }

        private static string GetWindowName(UIWindowDebugInfo info)
        {
            return string.IsNullOrEmpty(info.LogicTypeName) ? "Unknown" : info.LogicTypeName;
        }

        private static string GetStateLine(UIWindowDebugInfo info)
        {
            return info.State + " | Visible " + info.Visible + " | Depth " + info.Depth + " | Occlusion " + info.OcclusionMode + " | " + info.StateDuration.ToString("F1") + "s";
        }

        private static string GetFlagLine(UIWindowDebugInfo info)
        {
            return "Update " + info.NeedUpdate + " | InCache " + info.InCache + " | ShowOp " + info.ShowInProgress + " | CloseOp " + info.CloseInProgress;
        }

        private static string GetCacheLine(UIWindowDebugInfo info)
        {
            return "Time " + info.CacheTime.ToString("F2") + " | Timer " + info.CacheTimerHandle;
        }

        private static string GetWindowSummary(UIWindowDebugInfo info)
        {
            return info.State + " | " + (info.Visible ? "Visible" : "Hidden") + " | Depth " + info.Depth + " | " + info.StateDuration.ToString("F1") + "s";
        }

        private static string GetWindowBadge(UIWindowDebugInfo info, bool cached)
        {
            if (info.ShowInProgress || info.CloseInProgress)
            {
                return "OP";
            }

            if (cached || info.InCache)
            {
                return "CCH";
            }

            if (!info.Visible)
            {
                return "HID";
            }

            return info.OcclusionMode == UIOcclusionMode.None ? "UI" : "OCC";
        }

        private GUIStyle GetWindowTitleStyle(UIWindowDebugInfo info, bool cached)
        {
            if (info.ShowInProgress || info.CloseInProgress || !info.Visible)
            {
                return _warningLabelStyle;
            }

            if (cached || info.InCache)
            {
                return _mutedLabelStyle;
            }

            return _rowLabelStyle;
        }

        private void DrawCounter(string label, string value, GUIStyle valueStyle)
        {
            EditorGUILayout.BeginVertical(_fieldRowStyle, GUILayout.MinWidth(CounterMinWidth));
            EditorGUILayout.LabelField(label, _fieldLabelStyle);
            EditorGUILayout.LabelField(value, valueStyle);
            EditorGUILayout.EndVertical();
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

        private void DrawDebugRow(string label, string value)
        {
            DrawDebugRow(label, value, _rowLabelStyle);
        }

        private void DrawDebugRow(string label, string value, GUIStyle valueStyle)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            EditorGUILayout.LabelField(value, valueStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDebugObjectRow(string label, Object value, System.Type objectType)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(value, objectType, true);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEmptyLabel(string text)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 36f, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawBodyBackground(rect);
            GUI.Label(rect, text, _emptyStateStyle);
        }
    }

}
