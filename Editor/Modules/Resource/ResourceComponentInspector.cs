using System;
using System.Collections.Generic;
using AlicizaX.Editor;
using AlicizaX.Resource.Runtime;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace AlicizaX.Resource.Editor
{
    [CustomEditor(typeof(ResourceComponent))]
    internal sealed class ResourceComponentInspector : GameFrameworkInspector
    {
        private const float ToolbarHeight = 30f;
        private const float SectionHeaderHeight = 24f;
        private const float RowLabelWidth = 190f;
        private const float SliderValueWidth = 58f;
        private const float ToggleWidth = 84f;
        private const int DebugPageSize = 32;
        private const string ShowReleasedAssetSlotsKey = "AlicizaX.Resource.Editor.ShowReleasedAssetSlots";

        private static readonly string[] _playModeNames =
        {
            "None (未指定允许模式)",
            "EditorSimulateMode (编辑器下的模拟模式)",
            "OfflinePlayMode (单机模式)",
            "HostPlayMode (联机运行模式)",
            "WebPlayMode (WebGL运行模式)"
        };

        private readonly List<string> m_DecryptionServicesTypeName = new List<string>();
        private readonly List<string> _packageNameBuffer = new List<string>();
        private readonly ResourceAssetInfo[] _assetInfos = new ResourceAssetInfo[DebugPageSize];
        private readonly ResourceOwnerInfo[] _ownerInfos = new ResourceOwnerInfo[DebugPageSize];
        private readonly ResourceBindingInfo[] _bindingInfos = new ResourceBindingInfo[DebugPageSize];

        private SerializedProperty _milliseconds = null;
        private SerializedProperty _minUnloadUnusedAssetsInterval = null;
        private SerializedProperty _maxUnloadUnusedAssetsInterval = null;
        private SerializedProperty _useSystemUnloadUnusedAssets = null;
        private SerializedProperty _assetRecordCapacity = null;
        private SerializedProperty _assetLeaseCapacity = null;
        private SerializedProperty _bindingOwnerCapacity = null;
        private SerializedProperty _bindingSlotCapacity = null;
        private SerializedProperty _registeredTargetCapacity = null;
        private SerializedProperty _idleAssetExpireTime = null;
        private SerializedProperty _expireProcessCountPerFrame = null;
        private SerializedProperty _expireProcessCountWhenUnloading = null;
        private SerializedProperty _downloadingMaxNum = null;
        private SerializedProperty _failedTryAgain = null;
        private SerializedProperty _packageName = null;
        private SerializedProperty _decryptionServices = null;
        private SerializedProperty _playMode = null;
        private SerializedProperty _autoUnloadBundleWhenUnused = null;
        private int _packageNameIndex = 0;
        private string[] _packageNames = Array.Empty<string>();
        private string[] _decryptionServiceOptions = Array.Empty<string>();
        private int m_DecryptionSelectIndex;
        private int _playModeIndex = 0;
        private GUIStyle _panelStyle;
        private GUIStyle _entryBodyStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _fieldLabelStyle;
        private GUIStyle _rowLabelStyle;
        private GUIStyle _mutedLabelStyle;
        private bool _showResourceDebug = true;
        private bool _showAssetDebug = true;
        private bool _showOwnerDebug = true;
        private bool _showBindingDebug = true;
        private bool _showReleasedAssetSlots;
        private double _nextDebugRepaintTime;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EnsureStyles();

            ResourceComponent component = (ResourceComponent)target;
            DrawComponentPanel(component);

            serializedObject.ApplyModifiedProperties();
            RequestRuntimeRepaint(component);
        }

        protected override void OnCompileComplete()
        {
            base.OnCompileComplete();

            RefreshTypeNames();
        }

        private void OnEnable()
        {
            _milliseconds = serializedObject.FindProperty("milliseconds");
            _minUnloadUnusedAssetsInterval = serializedObject.FindProperty("minUnloadUnusedAssetsInterval");
            _maxUnloadUnusedAssetsInterval = serializedObject.FindProperty("maxUnloadUnusedAssetsInterval");
            _useSystemUnloadUnusedAssets = serializedObject.FindProperty("useSystemUnloadUnusedAssets");
            _assetRecordCapacity = serializedObject.FindProperty("assetRecordCapacity");
            _assetLeaseCapacity = serializedObject.FindProperty("assetLeaseCapacity");
            _bindingOwnerCapacity = serializedObject.FindProperty("bindingOwnerCapacity");
            _bindingSlotCapacity = serializedObject.FindProperty("bindingSlotCapacity");
            _registeredTargetCapacity = serializedObject.FindProperty("registeredTargetCapacity");
            _idleAssetExpireTime = serializedObject.FindProperty("idleAssetExpireTime");
            _expireProcessCountPerFrame = serializedObject.FindProperty("expireProcessCountPerFrame");
            _expireProcessCountWhenUnloading = serializedObject.FindProperty("expireProcessCountWhenUnloading");
            _downloadingMaxNum = serializedObject.FindProperty("downloadingMaxNum");
            _failedTryAgain = serializedObject.FindProperty("failedTryAgain");
            _packageName = serializedObject.FindProperty("packageName");
            _decryptionServices = serializedObject.FindProperty("decryptionServices");
            _playMode = serializedObject.FindProperty("_playMode");
            _autoUnloadBundleWhenUnused = serializedObject.FindProperty("autoUnloadBundleWhenUnused");
            RefreshDecryptionServices();
            RefreshTypeNames();

            _playModeIndex = Mathf.Clamp(EditorPrefs.GetInt(ResourceComponent.PrefsKey, 0), 0, _playModeNames.Length - 1);
            _playMode.enumValueIndex = _playModeIndex;
            _showReleasedAssetSlots = EditorPrefs.GetBool(ShowReleasedAssetSlotsKey, false);
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
        }

        private void DrawComponentPanel(ResourceComponent component)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawToolbar("Resource Component");

            DrawSectionBegin("Load Mode");
            using (new EditorGUI.DisabledGroupScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawPlayModeRow(component);
                DrawDecryptionServicesRow();
                DrawPackageNameRow();
                DrawToggleRow("Auto Unload Bundle", _autoUnloadBundleWhenUnused);
            }

            DrawSectionEnd();

            DrawSectionBegin("Loading");
            int milliseconds = _milliseconds.intValue;
            if (DrawDelayedIntRow("Milliseconds", milliseconds, out milliseconds))
            {
                if (EditorApplication.isPlaying)
                {
                    component.milliseconds = milliseconds;
                }
                else
                {
                    _milliseconds.longValue = milliseconds;
                }
            }

            int downloadingMaxNum = _downloadingMaxNum.intValue;
            if (DrawIntSliderRow("Max Downloading Num", downloadingMaxNum, 1, 48, out downloadingMaxNum))
            {
                if (EditorApplication.isPlaying)
                {
                    component.DownloadingMaxNum = downloadingMaxNum;
                }
                else
                {
                    _downloadingMaxNum.intValue = downloadingMaxNum;
                }
            }

            int failedTryAgain = _failedTryAgain.intValue;
            if (DrawIntSliderRow("Max FailedTryAgain Count", failedTryAgain, 1, 48, out failedTryAgain))
            {
                if (EditorApplication.isPlaying)
                {
                    component.FailedTryAgain = failedTryAgain;
                }
                else
                {
                    _failedTryAgain.intValue = failedTryAgain;
                }
            }

            DrawSectionEnd();

            DrawSectionBegin("Unload");
            DrawToggleRow("Use System Unload", _useSystemUnloadUnusedAssets);

            float minUnloadUnusedAssetsInterval = _minUnloadUnusedAssetsInterval.floatValue;
            if (DrawFloatSliderRow("Min Unload Unused Assets Interval", minUnloadUnusedAssetsInterval, 0f, 3600f, out minUnloadUnusedAssetsInterval))
            {
                if (EditorApplication.isPlaying)
                {
                    component.MinUnloadUnusedAssetsInterval = minUnloadUnusedAssetsInterval;
                }
                else
                {
                    _minUnloadUnusedAssetsInterval.floatValue = minUnloadUnusedAssetsInterval;
                }
            }

            float maxUnloadUnusedAssetsInterval = _maxUnloadUnusedAssetsInterval.floatValue;
            if (DrawFloatSliderRow("Max Unload Unused Assets Interval", maxUnloadUnusedAssetsInterval, 0f, 3600f, out maxUnloadUnusedAssetsInterval))
            {
                if (EditorApplication.isPlaying)
                {
                    component.MaxUnloadUnusedAssetsInterval = maxUnloadUnusedAssetsInterval;
                }
                else
                {
                    _maxUnloadUnusedAssetsInterval.floatValue = maxUnloadUnusedAssetsInterval;
                }
            }

            int expireProcessCountPerFrame = _expireProcessCountPerFrame.intValue;
            if (DrawDelayedIntRow("Expire Process Count Per Frame", expireProcessCountPerFrame, out expireProcessCountPerFrame))
            {
                if (EditorApplication.isPlaying)
                {
                    component.ExpireProcessCountPerFrame = expireProcessCountPerFrame;
                }
                else
                {
                    _expireProcessCountPerFrame.intValue = Mathf.Max(0, expireProcessCountPerFrame);
                }
            }

            int expireProcessCountWhenUnloading = _expireProcessCountWhenUnloading.intValue;
            if (DrawDelayedIntRow("Expire Process Count When Unloading", expireProcessCountWhenUnloading, out expireProcessCountWhenUnloading))
            {
                if (EditorApplication.isPlaying)
                {
                    component.ExpireProcessCountWhenUnloading = expireProcessCountWhenUnloading;
                }
                else
                {
                    _expireProcessCountWhenUnloading.intValue = Mathf.Max(_expireProcessCountPerFrame.intValue, expireProcessCountWhenUnloading);
                }
            }

            DrawSectionEnd();

            DrawSectionBegin("Resource Cache");
            using (new EditorGUI.DisabledGroupScope(EditorApplication.isPlaying))
            {
                int assetRecordCapacity = _assetRecordCapacity.intValue;
                if (DrawDelayedIntRow("Asset Record Capacity", assetRecordCapacity, out assetRecordCapacity))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.AssetRecordCapacity = assetRecordCapacity;
                    }
                    else
                    {
                        _assetRecordCapacity.intValue = assetRecordCapacity;
                    }
                }

                int assetLeaseCapacity = _assetLeaseCapacity.intValue;
                if (DrawDelayedIntRow("Asset Lease Capacity", assetLeaseCapacity, out assetLeaseCapacity))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.AssetLeaseCapacity = assetLeaseCapacity;
                    }
                    else
                    {
                        _assetLeaseCapacity.intValue = assetLeaseCapacity;
                    }
                }

                int bindingOwnerCapacity = _bindingOwnerCapacity.intValue;
                if (DrawDelayedIntRow("Binding Owner Capacity", bindingOwnerCapacity, out bindingOwnerCapacity))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.BindingOwnerCapacity = bindingOwnerCapacity;
                    }
                    else
                    {
                        _bindingOwnerCapacity.intValue = bindingOwnerCapacity;
                    }
                }

                int bindingSlotCapacity = _bindingSlotCapacity.intValue;
                if (DrawDelayedIntRow("Binding Slot Capacity", bindingSlotCapacity, out bindingSlotCapacity))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.BindingSlotCapacity = bindingSlotCapacity;
                    }
                    else
                    {
                        _bindingSlotCapacity.intValue = bindingSlotCapacity;
                    }
                }

                int registeredTargetCapacity = _registeredTargetCapacity.intValue;
                if (DrawDelayedIntRow("Registered Target Capacity", registeredTargetCapacity, out registeredTargetCapacity))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.RegisteredTargetCapacity = registeredTargetCapacity;
                    }
                    else
                    {
                        _registeredTargetCapacity.intValue = registeredTargetCapacity;
                    }
                }

                float idleAssetExpireTime = _idleAssetExpireTime.floatValue;
                if (DrawDelayedFloatRow("Idle Asset Expire Time", idleAssetExpireTime, out idleAssetExpireTime))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.IdleAssetExpireTime = idleAssetExpireTime;
                    }
                    else
                    {
                        _idleAssetExpireTime.floatValue = idleAssetExpireTime;
                    }
                }
            }

            DrawSectionEnd();

            if (EditorApplication.isPlaying && IsPrefabInHierarchy(component.gameObject))
            {
                DrawSectionBegin("Runtime Status");
                DrawReadOnlyRow(
                    "Unload Unused Assets",
                    AlicizaX.Utility.Text.Format("{0:F2} / {1:F2}", component.LastUnloadUnusedAssetsOperationElapseSeconds, component.MaxUnloadUnusedAssetsInterval));
                DrawReadOnlyRow("Applicable Game Version", component.ApplicableGameVersion ?? "<Unknown>");
                DrawSectionEnd();

                DrawResourceDebugPanel();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar(string title)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
            GUI.Label(labelRect, title, _rowLabelStyle);
        }

        private void DrawResourceDebugPanel()
        {
            _showResourceDebug = DrawFoldoutHeader(_showResourceDebug, "Resource Debug");
            if (!_showResourceDebug)
            {
                return;
            }

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            if (!AppServices.HasWorld || !AppServices.App.TryGet<IResourceService>(out IResourceService resourceService))
            {
                DrawReadOnlyRow("State", "Resource service is not initialized.");
                DrawSectionEnd();
                return;
            }

            DrawAssetDebug(resourceService);
            DrawOwnerDebug(resourceService.BindingService);
            DrawBindingDebug(resourceService.BindingService);
            DrawSectionEnd();
        }

        private void DrawAssetDebug(IResourceService resourceService)
        {
            _showAssetDebug = DrawFoldoutHeader(_showAssetDebug, "Assets");
            if (!_showAssetDebug)
            {
                return;
            }

            int total = GetAssetSummary(
                resourceService,
                out int visibleCount,
                out int activeCount,
                out int idleCount,
                out int keepAliveCount,
                out int releasedCount,
                out int handleCount,
                out int directRefTotal,
                out int legacyRefTotal,
                out int bindingRefTotal,
                out int keepAliveRefTotal);
            int count = resourceService.GetAssetInfos(_assetInfos, 0, _assetInfos.Length);
            count = Mathf.Min(count, _assetInfos.Length);
            DrawReadOnlyRow("Asset Slot Count", total.ToString());
            DrawReadOnlyRow(
                "Visible Assets",
                AlicizaX.Utility.Text.Format(
                    "{0} | Active:{1} Idle:{2} KeepAlive:{3} Released:{4}",
                    visibleCount,
                    activeCount,
                    idleCount,
                    keepAliveCount,
                    releasedCount));
            DrawReadOnlyRow(
                "Handles / Refs",
                AlicizaX.Utility.Text.Format(
                    "Handle:{0} | Direct:{1} Legacy:{2} Binding:{3} KeepAlive:{4}",
                    handleCount,
                    directRefTotal,
                    legacyRefTotal,
                    bindingRefTotal,
                    keepAliveRefTotal));

            bool showReleased = DrawToggleValueRow("Show Released Slots", _showReleasedAssetSlots);
            if (showReleased != _showReleasedAssetSlots)
            {
                _showReleasedAssetSlots = showReleased;
                EditorPrefs.SetBool(ShowReleasedAssetSlotsKey, showReleased);
            }

            for (int i = 0; i < count; i++)
            {
                ResourceAssetInfo info = _assetInfos[i];
                if (ShouldHideReleasedAssetSlot(info))
                {
                    continue;
                }

                DrawReadOnlyRow(
                    "#" + i,
                    AlicizaX.Utility.Text.Format(
                        "{0} | {1}/{2} | {3} | {4} | D:{5} L:{6} B:{7} K:{8} | KA:{9:F0}s Idle:{10:F0}s{11}",
                        info.State,
                        string.IsNullOrEmpty(info.Package) ? "<None>" : info.Package,
                        info.Location,
                        string.IsNullOrEmpty(info.TypeName) ? "<Unknown>" : info.TypeName,
                        GetHandleKindName(info.HandleKind),
                        info.DirectRefCount,
                        info.LegacyDirectRefCount,
                        info.BindingRefCount,
                        info.KeepAliveRefCount,
                        info.KeepAliveExpireIn,
                        info.IdleExpireIn,
                        info.IdleReleaseRequested ? " ReleasePending" : string.Empty));
            }
        }

        private int GetAssetSummary(
            IResourceService resourceService,
            out int visibleCount,
            out int activeCount,
            out int idleCount,
            out int keepAliveCount,
            out int releasedCount,
            out int handleCount,
            out int directRefTotal,
            out int legacyRefTotal,
            out int bindingRefTotal,
            out int keepAliveRefTotal)
        {
            visibleCount = 0;
            activeCount = 0;
            idleCount = 0;
            keepAliveCount = 0;
            releasedCount = 0;
            handleCount = 0;
            directRefTotal = 0;
            legacyRefTotal = 0;
            bindingRefTotal = 0;
            keepAliveRefTotal = 0;

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
                        case ResourceAssetState.KeepAlive:
                            keepAliveCount++;
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
                    keepAliveRefTotal += info.KeepAliveRefCount;

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

        private void DrawOwnerDebug(IResourceBindingService bindingService)
        {
            _showOwnerDebug = DrawFoldoutHeader(_showOwnerDebug, "Owners");
            if (!_showOwnerDebug)
            {
                return;
            }

            int total = bindingService.GetOwnerInfos(_ownerInfos, 0, _ownerInfos.Length);
            DrawReadOnlyRow("Owner Count", total.ToString());
            int count = Mathf.Min(total, _ownerInfos.Length);
            for (int i = 0; i < count; i++)
            {
                ResourceOwnerInfo info = _ownerInfos[i];
                if (!info.Active)
                {
                    continue;
                }

                DrawReadOnlyRow(
                    "Owner " + info.OwnerId,
                    AlicizaX.Utility.Text.Format(
                        "{0} | GO:{1} Gen:{2} Bind:{3} Target:{4}",
                        GetOwnerDisplayName(info),
                        info.GameObjectId,
                        info.Generation,
                        info.BindingCount,
                        info.RegisteredTargetCount));
                DrawObjectRow("Owner Object", info.OwnerObject);
            }
        }

        private void DrawBindingDebug(IResourceBindingService bindingService)
        {
            _showBindingDebug = DrawFoldoutHeader(_showBindingDebug, "Bindings");
            if (!_showBindingDebug)
            {
                return;
            }

            int total = bindingService.GetBindingInfos(_bindingInfos, 0, _bindingInfos.Length);
            DrawReadOnlyRow("Binding Count", total.ToString());
            int count = Mathf.Min(total, _bindingInfos.Length);
            for (int i = 0; i < count; i++)
            {
                ResourceBindingInfo info = _bindingInfos[i];
                if (!info.Active)
                {
                    continue;
                }

                DrawReadOnlyRow(
                    info.SlotType.ToString(),
                    AlicizaX.Utility.Text.Format(
                        "Owner:{0} Target:{1} ({2}) Asset:{3} Ver:{4} Runtime:{5}",
                        info.OwnerId,
                        GetBindingTargetDisplayName(info),
                        info.TargetComponentId,
                        info.AssetId,
                        info.Version,
                        info.HasRuntimeObject ? "Yes" : "No"));
                DrawObjectRow("Target Object", info.TargetObject);
            }
        }

        private bool DrawFoldoutHeader(bool value, string title)
        {
            Rect headerRect = GUILayoutUtility.GetRect(1f, SectionHeaderHeight, GUILayout.ExpandWidth(true));
            bool hovered = headerRect.Contains(Event.current.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(headerRect, true, hovered);

            Rect foldRect = new Rect(headerRect.x + 7f, headerRect.y + 2f, 16f, 20f);
            Rect labelRect = new Rect(foldRect.xMax + 4f, headerRect.y + 2f, headerRect.width - 34f, 20f);
            AlicizaEditorGUI.DrawFoldoutIcon(foldRect, value);
            GUI.Label(labelRect, title, _rowLabelStyle);

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && headerRect.Contains(currentEvent.mousePosition))
            {
                value = !value;
                GUI.FocusControl(string.Empty);
                currentEvent.Use();
            }

            return value;
        }

        private bool ShouldHideReleasedAssetSlot(ResourceAssetInfo info)
        {
            return !_showReleasedAssetSlots &&
                   info.State == ResourceAssetState.Released &&
                   !info.HandleValid &&
                   info.DirectRefCount == 0 &&
                   info.LegacyDirectRefCount == 0 &&
                   info.BindingRefCount == 0 &&
                   info.KeepAliveRefCount == 0;
        }

        private bool DrawToggleValueRow(string label, bool value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            bool next = GUILayout.Toggle(
                value,
                value ? "Enabled" : "Disabled",
                value ? AlicizaEditorGUI.Styles.PillOn : AlicizaEditorGUI.Styles.PillOff,
                GUILayout.Width(ToggleWidth));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            return next;
        }

        private static string GetHandleKindName(byte handleKind)
        {
            return handleKind switch
            {
                1 => "AssetHandle",
                2 => "SubAssetsHandle",
                3 => "ExternalHandleLease",
                _ => "None"
            };
        }

        private void DrawObjectRow(string label, UnityEngine.Object value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(value, typeof(UnityEngine.Object), true);
            }
            EditorGUILayout.EndHorizontal();
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

        private void DrawSectionBegin(string title)
        {
            EditorGUILayout.Space(2f);
            Rect headerRect = GUILayoutUtility.GetRect(1f, SectionHeaderHeight, GUILayout.ExpandWidth(true));
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

        private void DrawPlayModeRow(ResourceComponent component)
        {
            int index = EditorApplication.isPlaying && IsPrefabInHierarchy(component.gameObject)
                ? EditorPrefs.GetInt(ResourceComponent.PrefsKey, 0)
                : _playModeIndex;

            int selectedIndex = DrawPopupRow("Play Mode", index, _playModeNames);
            if (selectedIndex == _playModeIndex || EditorApplication.isPlaying)
            {
                return;
            }

            _playModeIndex = selectedIndex;
            _playMode.enumValueIndex = _playModeIndex;
            EditorPrefs.SetInt(ResourceComponent.PrefsKey, selectedIndex);
        }

        private void DrawDecryptionServicesRow()
        {
            if (_decryptionServiceOptions.Length == 0)
            {
                RefreshDecryptionServices();
            }

            m_DecryptionSelectIndex = DrawPopupRow("Decryption Service", m_DecryptionSelectIndex, _decryptionServiceOptions);
            if ((uint)m_DecryptionSelectIndex >= (uint)_decryptionServiceOptions.Length)
            {
                return;
            }

            string selectService = _decryptionServiceOptions[m_DecryptionSelectIndex];
            string serviceName = selectService.Equals(NoneOptionName) ? string.Empty : selectService;
            if (_decryptionServices.stringValue != serviceName)
            {
                _decryptionServices.stringValue = serviceName;
            }
        }

        private void DrawPackageNameRow()
        {
            RefreshPackageNames();
            if (_packageNames.Length == 0)
            {
                DrawReadOnlyRow("Package Name", "<No YooAsset Package>");
                EditorUtils.TrHelpIconText("No YooAsset package is configured.", MessageType.Warning);
                return;
            }

            _packageNameIndex = Array.IndexOf(_packageNames, _packageName.stringValue);
            if (_packageNameIndex < 0)
            {
                _packageNameIndex = 0;
            }

            _packageNameIndex = DrawPopupRow("Package Name", _packageNameIndex, _packageNames);
            if ((uint)_packageNameIndex < (uint)_packageNames.Length && _packageName.stringValue != _packageNames[_packageNameIndex])
            {
                _packageName.stringValue = _packageNames[_packageNameIndex];
            }
        }

        private int DrawPopupRow(string label, int selectedIndex, string[] options)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));

            Rect popupRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
            int nextIndex = AlicizaEditorGUI.DrawStyledPopup(popupRect, selectedIndex, options);

            EditorGUILayout.EndHorizontal();
            return nextIndex;
        }

        private void DrawToggleRow(string label, SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            property.boolValue = GUILayout.Toggle(
                property.boolValue,
                property.boolValue ? "Enabled" : "Disabled",
                property.boolValue ? AlicizaEditorGUI.Styles.PillOn : AlicizaEditorGUI.Styles.PillOff,
                GUILayout.Width(ToggleWidth));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private bool DrawDelayedIntRow(string label, int currentValue, out int value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            value = EditorGUILayout.DelayedIntField(currentValue);
            EditorGUILayout.EndHorizontal();
            return value != currentValue;
        }

        private bool DrawDelayedFloatRow(string label, float currentValue, out float value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            value = EditorGUILayout.DelayedFloatField(currentValue);
            EditorGUILayout.EndHorizontal();
            return Math.Abs(value - currentValue) > 0.01f;
        }

        private bool DrawFloatSliderRow(string label, float currentValue, float minValue, float maxValue, out float value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            value = Mathf.Clamp(currentValue, minValue, maxValue);
            value = GUILayout.HorizontalSlider(value, minValue, maxValue, GUILayout.MinWidth(90f));
            value = Mathf.Clamp(EditorGUILayout.FloatField(value, GUILayout.Width(SliderValueWidth)), minValue, maxValue);
            EditorGUILayout.EndHorizontal();
            return Math.Abs(value - currentValue) > 0.01f;
        }

        private bool DrawIntSliderRow(string label, int currentValue, int minValue, int maxValue, out int value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            value = Mathf.Clamp(currentValue, minValue, maxValue);
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, minValue, maxValue, GUILayout.MinWidth(90f)));
            value = Mathf.Clamp(EditorGUILayout.IntField(value, GUILayout.Width(SliderValueWidth)), minValue, maxValue);
            EditorGUILayout.EndHorizontal();
            return value != currentValue;
        }

        private void DrawReadOnlyRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            EditorGUILayout.LabelField(value, string.IsNullOrEmpty(value) ? _mutedLabelStyle : _rowLabelStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void RefreshDecryptionServices()
        {
            m_DecryptionServicesTypeName.Clear();
            m_DecryptionServicesTypeName.Add(NoneOptionName);
            m_DecryptionServicesTypeName.AddRange(AlicizaX.Utility.Assembly.GetRuntimeTypeNames(typeof(IBundleDecryptor)));
            _decryptionServiceOptions = m_DecryptionServicesTypeName.ToArray();

            string selectedService = string.IsNullOrEmpty(_decryptionServices.stringValue) ? NoneOptionName : _decryptionServices.stringValue;
            m_DecryptionSelectIndex = m_DecryptionServicesTypeName.IndexOf(selectedService);
            if (m_DecryptionSelectIndex < 0)
            {
                m_DecryptionSelectIndex = 0;
            }
        }

        private void RefreshTypeNames()
        {
            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshPackageNames()
        {
            _packageNameBuffer.Clear();

            foreach (var package in BundleCollectorSettingData.Setting.Packages)
            {
                _packageNameBuffer.Add(package.PackageName);
            }

            _packageNames = _packageNameBuffer.ToArray();
        }

        private void RequestRuntimeRepaint(ResourceComponent component)
        {
            if (!EditorApplication.isPlaying || component == null || !IsPrefabInHierarchy(component.gameObject))
            {
                return;
            }

            Repaint();
        }
    }
}
