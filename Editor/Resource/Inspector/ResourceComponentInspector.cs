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

        private static readonly string[] _playModeNames =
        {
            "EditorSimulateMode (编辑器下的模拟模式)",
            "OfflinePlayMode (单机模式)",
            "HostPlayMode (联机运行模式)",
            "WebPlayMode (WebGL运行模式)"
        };

        private readonly List<string> m_DecryptionServicesTypeName = new List<string>();
        private readonly List<string> _packageNameBuffer = new List<string>();

        private SerializedProperty _milliseconds = null;
        private SerializedProperty _minUnloadUnusedAssetsInterval = null;
        private SerializedProperty _maxUnloadUnusedAssetsInterval = null;
        private SerializedProperty _useSystemUnloadUnusedAssets = null;
        private SerializedProperty _assetAutoReleaseInterval = null;
        private SerializedProperty _assetCapacity = null;
        private SerializedProperty _assetExpireTime = null;
        private SerializedProperty _assetPriority = null;
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

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EnsureStyles();

            ResourceComponent component = (ResourceComponent)target;
            DrawComponentPanel(component);

            serializedObject.ApplyModifiedProperties();
            Repaint();
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
            _assetAutoReleaseInterval = serializedObject.FindProperty("assetAutoReleaseInterval");
            _assetCapacity = serializedObject.FindProperty("assetCapacity");
            _assetExpireTime = serializedObject.FindProperty("assetExpireTime");
            _assetPriority = serializedObject.FindProperty("assetPriority");
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

            DrawSectionEnd();

            DrawSectionBegin("Asset Pool");
            using (new EditorGUI.DisabledGroupScope(EditorApplication.isPlaying))
            {
                float assetAutoReleaseInterval = _assetAutoReleaseInterval.floatValue;
                if (DrawDelayedFloatRow("Asset Auto Release Interval", assetAutoReleaseInterval, out assetAutoReleaseInterval))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.AssetAutoReleaseInterval = assetAutoReleaseInterval;
                    }
                    else
                    {
                        _assetAutoReleaseInterval.floatValue = assetAutoReleaseInterval;
                    }
                }

                int assetCapacity = _assetCapacity.intValue;
                if (DrawDelayedIntRow("Asset Capacity", assetCapacity, out assetCapacity))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.AssetCapacity = assetCapacity;
                    }
                    else
                    {
                        _assetCapacity.intValue = assetCapacity;
                    }
                }

                float assetExpireTime = _assetExpireTime.floatValue;
                if (DrawDelayedFloatRow("Asset Expire Time", assetExpireTime, out assetExpireTime))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.AssetExpireTime = assetExpireTime;
                    }
                    else
                    {
                        _assetExpireTime.floatValue = assetExpireTime;
                    }
                }

                int assetPriority = _assetPriority.intValue;
                if (DrawDelayedIntRow("Asset Priority", assetPriority, out assetPriority))
                {
                    if (EditorApplication.isPlaying)
                    {
                        component.AssetPriority = assetPriority;
                    }
                    else
                    {
                        _assetPriority.intValue = assetPriority;
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
            m_DecryptionServicesTypeName.AddRange(AlicizaX.Utility.Assembly.GetRuntimeTypeNames(typeof(IDecryptionServices)));
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

            foreach (var package in AssetBundleCollectorSettingData.Setting.Packages)
            {
                _packageNameBuffer.Add(package.PackageName);
            }

            _packageNames = _packageNameBuffer.ToArray();
        }
    }
}
