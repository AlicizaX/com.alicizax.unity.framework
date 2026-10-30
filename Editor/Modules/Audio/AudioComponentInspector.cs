using System.Collections.Generic;
using AlicizaX.Audio.Runtime;
using AlicizaX.Editor.UIToolkit;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Editor
{
    [CustomEditor(typeof(AudioComponent))]
    internal sealed class AudioComponentInspector : AlicizaUIEditor
    {
        private const string DefaultAudioMixerPath = "Packages/com.alicizax.unity.framework/Runtime/Modules/Audio/Resources/AudioMixer.mixer";
        private const int ClipCacheCapacityMin = 1;
        private const int ClipCacheCapacityFallbackMax = 512;
        private const int RuntimeStateNone = -1;
        private const int RuntimeStateStopped = 0;
        private const int RuntimeStateUnavailable = 1;
        private const int RuntimeStateActive = 2;

        private enum CachePreset
        {
            Default,
            Mobile,
            Desktop,
        }

        private readonly AudioServiceDebugInfo _serviceInfo = new AudioServiceDebugInfo();
        private readonly AudioCategoryDebugInfo _categoryInfo = new AudioCategoryDebugInfo();
        private readonly AudioAgentDebugInfo _agentInfo = new AudioAgentDebugInfo();
        private readonly AudioClipCacheDebugInfo _clipCacheInfo = new AudioClipCacheDebugInfo();
        private readonly bool[] _groupExpanded = new bool[(int)AudioType.Max];
        private readonly bool[] _runtimeGroupExpanded = new bool[(int)AudioType.Max];
        private readonly List<AlicizaFoldoutCard> _groupCards = new List<AlicizaFoldoutCard>();

        private SerializedProperty _audioListener;
        private SerializedProperty _audioMixer;
        private SerializedProperty _audioGroupConfigs;
        private SerializedProperty _serviceConfig;
        private SerializedProperty _clipCacheCapacity;
        private SerializedProperty _clipCacheTtl;
        private SerializedProperty _defaultCachePolicy;

        private AlicizaIntSliderField _clipCacheCapacityField;
        private FloatField _clipCacheTtlField;
        private AlicizaEnumField _defaultCachePolicyField;

        private VisualElement _groupCardsRoot;
        private VisualElement _runtimeRoot;
        private int _runtimeState = RuntimeStateNone;

        private void OnEnable()
        {
            CacheProperties();
        }

        protected override void BuildInspector(VisualElement root)
        {
            CacheProperties();

            RepairGroupConfigsIfNeeded();
            AutoBindDefaultMixer();
            AutoBindMissingMixerGroups();
            serializedObject.ApplyModifiedProperties();
            CacheProperties();

            bool editable = !EditorApplication.isPlayingOrWillChangePlaymode;
            if (!editable)
            {
                root.Add(AlicizaEditorUI.CreateHint("Audio settings are read-only during Play Mode."));
            }

            AlicizaPanel configPanel = AlicizaEditorUI.CreatePanel(null);
            root.Add(configPanel);
            BuildComponentSection(configPanel.Body);
            BuildCacheSection(configPanel.Body);
            BuildGroupsSection(configPanel.Body);
            configPanel.SetEnabled(editable);

            AlicizaPanel runtimePanel = AlicizaEditorUI.CreatePanel(null);
            runtimePanel.Body.style.paddingBottom = 0f;
            root.Add(runtimePanel);
            _runtimeRoot = new VisualElement { name = "alicia-audio-runtime-root" };
            runtimePanel.Body.Add(_runtimeRoot);
            _runtimeState = RuntimeStateNone;
            RefreshRuntime();
            AlicizaEditorUI.RefreshEvery(root, RefreshRuntime, 250);
        }

        private void CacheProperties()
        {
            _audioListener = Find("m_AudioListener");
            _audioMixer = Find("m_AudioMixer");
            _audioGroupConfigs = Find("m_AudioGroupConfigs");
            _serviceConfig = Find("m_ServiceConfig");
            _clipCacheCapacity = _serviceConfig != null ? _serviceConfig.FindPropertyRelative("ClipCacheCapacity") : null;
            _clipCacheTtl = _serviceConfig != null ? _serviceConfig.FindPropertyRelative("ClipCacheTtl") : null;
            _defaultCachePolicy = _serviceConfig != null ? _serviceConfig.FindPropertyRelative("DefaultClipCachePolicy") : null;
        }

        private void BuildComponentSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Audio Component");
            parent.Add(section);

            AlicizaEditorUI.AddPropertyRow(section.Body, "Listener", _audioListener, true, typeof(AudioListener), true);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Audio Mixer", _audioMixer, true, typeof(AudioMixer));
        }

        private void BuildCacheSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Clip Cache");
            parent.Add(section);

            if (_serviceConfig == null || _clipCacheCapacity == null || _clipCacheTtl == null || _defaultCachePolicy == null)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("Clip cache settings are unavailable on this component."));
                return;
            }

            int capacityMax = Mathf.Max(ClipCacheCapacityFallbackMax, _clipCacheCapacity.intValue);
            _clipCacheCapacityField = new AlicizaIntSliderField(ClipCacheCapacityMin, capacityMax);
            _clipCacheCapacityField.BindProperty(_clipCacheCapacity);
            AlicizaEditorUI.AddRow(section.Body, "Capacity", _clipCacheCapacityField);

            AlicizaRow ttlRow = AlicizaEditorUI.AddPropertyRow(section.Body, "TTL Seconds", _clipCacheTtl);
            _clipCacheTtlField = ttlRow != null ? ttlRow.Content.Q<FloatField>() : null;

            AlicizaRow policyRow = AlicizaEditorUI.AddPropertyRow(section.Body, "Default Policy", _defaultCachePolicy);
            _defaultCachePolicyField = policyRow != null ? policyRow.Content.Q<AlicizaEnumField>() : null;

            var actions = new VisualElement { name = "alicia-audio-cache-actions" };
            actions.AddToClassList("alicia-actions");

            Button resetButton = AlicizaEditorUI.CreateTextButton(
                "Reset", "Restore the default clip cache settings.", AlicizaButtonTone.Warning);
            resetButton.clicked += () => ApplyCachePreset(CachePreset.Default);
            actions.Add(resetButton);

            Button mobileButton = AlicizaEditorUI.CreateTextButton(
                "Mobile", "Apply the mobile clip cache settings.", AlicizaButtonTone.Accent);
            mobileButton.clicked += () => ApplyCachePreset(CachePreset.Mobile);
            actions.Add(mobileButton);

            Button desktopButton = AlicizaEditorUI.CreateTextButton(
                "Desktop", "Apply the desktop clip cache settings.");
            desktopButton.clicked += () => ApplyCachePreset(CachePreset.Desktop);
            actions.Add(desktopButton);

            section.Body.Add(actions);
        }

        private void ApplyCachePreset(CachePreset preset)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            AudioComponent component = target as AudioComponent;
            if (component == null)
            {
                return;
            }

            string undoName;
            switch (preset)
            {
                case CachePreset.Mobile:
                    undoName = "Apply Mobile Audio Cache Settings";
                    break;
                case CachePreset.Desktop:
                    undoName = "Apply Desktop Audio Cache Settings";
                    break;
                default:
                    undoName = "Reset Audio Cache Settings";
                    break;
            }

            Undo.RecordObject(component, undoName);
            switch (preset)
            {
                case CachePreset.Mobile:
                    component.ApplyMobileServiceConfigForEditor();
                    break;
                case CachePreset.Desktop:
                    component.ApplyDesktopServiceConfigForEditor();
                    break;
                default:
                    component.ResetServiceConfigForEditor();
                    break;
            }

            EditorUtility.SetDirty(component);
            RefreshCacheControls();
        }

        private void RefreshCacheControls()
        {
            serializedObject.Update();
            if (_clipCacheCapacityField != null && _clipCacheCapacity != null)
            {
                _clipCacheCapacityField.SetValueWithoutNotify(_clipCacheCapacity.intValue);
            }

            if (_clipCacheTtlField != null && _clipCacheTtl != null)
            {
                _clipCacheTtlField.SetValueWithoutNotify(_clipCacheTtl.floatValue);
            }

            if (_defaultCachePolicyField != null && _defaultCachePolicy != null)
            {
                _defaultCachePolicyField.SetValueWithoutNotify(_defaultCachePolicy.enumValueIndex);
            }
        }

        private void BuildGroupsSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Audio Groups");
            parent.Add(section);

            var actions = new VisualElement { name = "alicia-audio-group-actions" };
            actions.AddToClassList("alicia-actions");

            Button expandAll = AlicizaEditorUI.CreateTextButton("Expand All", "Expand every audio group card.");
            expandAll.clicked += () => SetAllGroupCardsExpanded(true);
            actions.Add(expandAll);

            Button collapseAll = AlicizaEditorUI.CreateTextButton("Collapse All", "Collapse every audio group card.");
            collapseAll.clicked += () => SetAllGroupCardsExpanded(false);
            actions.Add(collapseAll);

            Button resetAll = AlicizaEditorUI.CreateTextButton(
                "Reset All",
                "Reset every audio group config to defaults and rebind the mixer groups.",
                AlicizaButtonTone.Danger);
            resetAll.clicked += ResetAllGroupConfigs;
            actions.Add(resetAll);

            section.Body.Add(actions);

            _groupCardsRoot = new VisualElement { name = "alicia-audio-group-cards" };
            section.Body.Add(_groupCardsRoot);
            RebuildGroupCards();
        }

        private void RebuildGroupCards()
        {
            if (_groupCardsRoot == null)
            {
                return;
            }

            _groupCardsRoot.Clear();
            _groupCards.Clear();

            if (_audioGroupConfigs == null || !_audioGroupConfigs.isArray)
            {
                _groupCardsRoot.Add(AlicizaEditorUI.CreateEmptyState("Audio group config array is missing."));
                return;
            }

            if (_audioGroupConfigs.arraySize == 0)
            {
                _groupCardsRoot.Add(AlicizaEditorUI.CreateEmptyState("No audio groups are configured."));
                return;
            }

            for (int i = 0; i < _audioGroupConfigs.arraySize; i++)
            {
                SerializedProperty configProperty = _audioGroupConfigs.GetArrayElementAtIndex(i);
                if (configProperty == null)
                {
                    continue;
                }

                _groupCardsRoot.Add(CreateGroupCard(configProperty, i));
            }
        }

        private AlicizaFoldoutCard CreateGroupCard(SerializedProperty configProperty, int index)
        {
            SerializedProperty typeProperty = configProperty.FindPropertyRelative("AudioType");
            bool expanded = (uint)index < (uint)_groupExpanded.Length && _groupExpanded[index];
            AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard(GetGroupTitle(typeProperty, index), expanded);
            card.name = "alicia-audio-group-" + index;

            if ((uint)index < (uint)_groupExpanded.Length)
            {
                int capturedIndex = index;
                card.ExpandedChanged += value => _groupExpanded[capturedIndex] = value;
            }

            Button resetButton = AlicizaEditorUI.CreateTextButton(
                "Reset",
                "Reset this audio group to defaults and rebind its mixer group.",
                AlicizaButtonTone.Warning);
            resetButton.clicked += () => ResetGroup(configProperty, index);
            card.Actions.Add(resetButton);

            UpdateGroupCardMeta(card, configProperty);
            AlicizaEditorUI.RefreshEvery(card, () => UpdateGroupCardMeta(card, configProperty), 250);

            BuildGroupCardBody(card.Body, configProperty, typeProperty);
            _groupCards.Add(card);
            return card;
        }

        private void BuildGroupCardBody(VisualElement body, SerializedProperty configProperty, SerializedProperty typeProperty)
        {
            AlicizaRow typeRow = AlicizaEditorUI.AddPropertyRow(body, "Type", typeProperty);
            if (typeRow != null)
            {
                typeRow.SetEnabled(false);
            }

            AlicizaEditorUI.AddPropertyRow(body, "Name", configProperty.FindPropertyRelative("m_Name"));
            AlicizaEditorUI.AddPropertyRow(
                body, "Mixer Group", configProperty.FindPropertyRelative("m_MixerGroup"), true, typeof(AudioMixerGroup));
            AlicizaEditorUI.AddPropertyRow(body, "Mute", configProperty.FindPropertyRelative("m_Mute"));
            AlicizaEditorUI.AddPropertyRow(body, "Volume", configProperty.FindPropertyRelative("m_Volume"));
            AlicizaEditorUI.AddPropertyRow(body, "Initial Sources", configProperty.FindPropertyRelative("m_InitialSourceCount"));
            AlicizaEditorUI.AddPropertyRow(body, "Max Sources", configProperty.FindPropertyRelative("m_MaxSourceCount"));
            AlicizaEditorUI.AddPropertyRow(body, "Volume Param", configProperty.FindPropertyRelative("m_ExposedVolumeParameter"));
            AlicizaEditorUI.AddPropertyRow(body, "Spatial Blend", configProperty.FindPropertyRelative("m_SpatialBlend"));
            AlicizaEditorUI.AddPropertyRow(body, "Doppler", configProperty.FindPropertyRelative("m_DopplerLevel"));
            AlicizaEditorUI.AddPropertyRow(body, "Spread", configProperty.FindPropertyRelative("m_Spread"));
            AlicizaEditorUI.AddPropertyRow(body, "Priority", configProperty.FindPropertyRelative("m_SourcePriority"));
            AlicizaEditorUI.AddPropertyRow(body, "Reverb Mix", configProperty.FindPropertyRelative("m_ReverbZoneMix"));
            AlicizaEditorUI.AddPropertyRow(body, "Rolloff", configProperty.FindPropertyRelative("audioRolloffMode"));
            AlicizaEditorUI.AddPropertyRow(body, "Min Distance", configProperty.FindPropertyRelative("minDistance"));
            AlicizaEditorUI.AddPropertyRow(body, "Max Distance", configProperty.FindPropertyRelative("maxDistance"));

            SerializedProperty occlusionEnabled = configProperty.FindPropertyRelative("m_OcclusionEnabled");
            AlicizaRow occlusionRow = AlicizaEditorUI.AddPropertyRow(body, "Occlusion", occlusionEnabled);

            var occlusionDetails = new VisualElement { name = "alicia-audio-occlusion-details" };
            occlusionDetails.style.marginLeft = 8f;
            body.Add(occlusionDetails);

            AlicizaEditorUI.AddPropertyRow(occlusionDetails, "Occlusion Mask", configProperty.FindPropertyRelative("m_OcclusionMask"));
            AlicizaEditorUI.AddPropertyRow(occlusionDetails, "Check Interval", configProperty.FindPropertyRelative("m_OcclusionCheckInterval"));
            AlicizaEditorUI.AddPropertyRow(occlusionDetails, "Low Pass", configProperty.FindPropertyRelative("m_OcclusionLowPassCutoff"));
            AlicizaEditorUI.AddPropertyRow(occlusionDetails, "Volume Scale", configProperty.FindPropertyRelative("m_OcclusionVolumeMultiplier"));

            SetOcclusionDetailsVisible(occlusionDetails, occlusionEnabled != null && occlusionEnabled.boolValue);
            AlicizaPillToggle occlusionToggle = occlusionRow != null ? occlusionRow.Content.Q<AlicizaPillToggle>() : null;
            if (occlusionToggle != null)
            {
                occlusionToggle.RegisterValueChangedCallback(evt => SetOcclusionDetailsVisible(occlusionDetails, evt.newValue));
            }
        }

        private static void SetOcclusionDetailsVisible(VisualElement details, bool visible)
        {
            if (details != null)
            {
                details.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static void UpdateGroupCardMeta(AlicizaFoldoutCard card, SerializedProperty configProperty)
        {
            if (card == null || configProperty == null)
            {
                return;
            }

            SerializedProperty typeProperty = configProperty.FindPropertyRelative("AudioType");
            SerializedProperty nameProperty = configProperty.FindPropertyRelative("m_Name");
            SerializedProperty mixerGroupProperty = configProperty.FindPropertyRelative("m_MixerGroup");
            SerializedProperty maxSourceProperty = configProperty.FindPropertyRelative("m_MaxSourceCount");

            string title = GetGroupTitle(typeProperty, -1);
            card.Title.text = title;
            card.Title.tooltip = title;
            string groupName = nameProperty != null && !string.IsNullOrEmpty(nameProperty.stringValue)
                ? nameProperty.stringValue
                : "Unnamed";
            string mixerName = mixerGroupProperty != null && mixerGroupProperty.objectReferenceValue != null
                ? mixerGroupProperty.objectReferenceValue.name
                : "No Mixer";
            string maxSources = maxSourceProperty != null ? maxSourceProperty.intValue.ToString() : "?";

            string namePart = groupName == title ? string.Empty : groupName + " | ";
            card.Meta.text = namePart + "Mixer " + mixerName + " | Max " + maxSources;
        }

        private void SetAllGroupCardsExpanded(bool expanded)
        {
            for (int i = 0; i < _groupCards.Count; i++)
            {
                AlicizaFoldoutCard card = _groupCards[i];
                if (card != null)
                {
                    card.SetExpanded(expanded);
                }
            }

            for (int i = 0; i < _groupExpanded.Length; i++)
            {
                _groupExpanded[i] = expanded;
            }
        }

        private void ResetGroup(SerializedProperty configProperty, int index)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            AudioComponent component = target as AudioComponent;
            if (component == null || configProperty == null)
            {
                return;
            }

            AudioType audioType = GetAudioType(configProperty, index);
            serializedObject.Update();
            Undo.RecordObject(component, "Reset Audio Group Config");
            ResetGroupConfigValues(configProperty, audioType);
            AutoBindDefaultMixer();
            BindMixerGroup(configProperty, audioType, true);
            EditorUtility.SetDirty(component);
            serializedObject.ApplyModifiedProperties();
            RebuildGroupCards();
        }

        private void ResetAllGroupConfigs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            AudioComponent component = target as AudioComponent;
            if (component == null)
            {
                return;
            }

            Undo.RecordObject(component, "Reset Audio Group Configs");
            component.ResetGroupConfigsForEditor();
            EditorUtility.SetDirty(component);
            serializedObject.Update();
            AutoBindDefaultMixer();
            AutoBindMissingMixerGroups();
            serializedObject.ApplyModifiedProperties();
            CacheProperties();
            RebuildGroupCards();
        }

        private static AudioType GetAudioType(SerializedProperty configProperty, int fallbackIndex)
        {
            SerializedProperty typeProperty = configProperty != null ? configProperty.FindPropertyRelative("AudioType") : null;
            if (typeProperty != null &&
                typeProperty.enumValueIndex >= 0 &&
                typeProperty.enumValueIndex < (int)AudioType.Max)
            {
                return (AudioType)typeProperty.enumValueIndex;
            }

            return (uint)fallbackIndex < (uint)AudioType.Max ? (AudioType)fallbackIndex : AudioType.Sound;
        }

        private static string GetGroupTitle(SerializedProperty typeProperty, int index)
        {
            if (typeProperty != null &&
                typeProperty.enumDisplayNames != null &&
                typeProperty.enumValueIndex >= 0 &&
                typeProperty.enumValueIndex < typeProperty.enumDisplayNames.Length)
            {
                return typeProperty.enumDisplayNames[typeProperty.enumValueIndex];
            }

            return (uint)index < (uint)AudioType.Max ? ((AudioType)index).ToString() : "Audio Group";
        }

        private static void ResetGroupConfigValues(SerializedProperty configProperty, AudioType type)
        {
            switch (type)
            {
                case AudioType.Sound:
                    ApplyDefaultGroupConfig(configProperty, type, "Sound", "SoundVolume", 4, 24, 1f, false, 128, 2f, 80f);
                    break;
                case AudioType.UISound:
                    ApplyDefaultGroupConfig(configProperty, type, "UISound", "UISoundVolume", 2, 12, 0f, false, 128, 1f, 25f);
                    break;
                case AudioType.Music:
                    ApplyDefaultGroupConfig(configProperty, type, "Music", "MusicVolume", 1, 2, 0f, false, 32, 1f, 25f);
                    break;
                case AudioType.Voice:
                    ApplyDefaultGroupConfig(configProperty, type, "Voice", "VoiceVolume", 2, 6, 1f, true, 128, 2f, 80f);
                    break;
                case AudioType.Ambient:
                    ApplyDefaultGroupConfig(configProperty, type, "Ambient", "AmbientVolume", 2, 6, 1f, true, 128, 2f, 80f);
                    break;
                default:
                    ApplyDefaultGroupConfig(configProperty, AudioType.Sound, "Sound", "SoundVolume", 4, 24, 1f, false, 128, 2f, 80f);
                    break;
            }
        }

        private static void ApplyDefaultGroupConfig(
            SerializedProperty configProperty,
            AudioType type,
            string name,
            string exposedParameter,
            int initialSourceCount,
            int maxSourceCount,
            float spatialBlend,
            bool occlusionEnabled,
            int priority,
            float minDistance,
            float maxDistance)
        {
            configProperty.FindPropertyRelative("AudioType").enumValueIndex = (int)type;
            configProperty.FindPropertyRelative("m_Name").stringValue = name;
            configProperty.FindPropertyRelative("m_Mute").boolValue = false;
            configProperty.FindPropertyRelative("m_Volume").floatValue = 1f;
            configProperty.FindPropertyRelative("m_InitialSourceCount").intValue = initialSourceCount;
            configProperty.FindPropertyRelative("m_MaxSourceCount").intValue = maxSourceCount;
            configProperty.FindPropertyRelative("m_ExposedVolumeParameter").stringValue = exposedParameter;
            configProperty.FindPropertyRelative("m_SpatialBlend").floatValue = spatialBlend;
            configProperty.FindPropertyRelative("m_DopplerLevel").floatValue = 1f;
            configProperty.FindPropertyRelative("m_Spread").floatValue = 0f;
            configProperty.FindPropertyRelative("m_SourcePriority").intValue = priority;
            configProperty.FindPropertyRelative("m_ReverbZoneMix").floatValue = 1f;
            configProperty.FindPropertyRelative("m_OcclusionEnabled").boolValue = occlusionEnabled;
            configProperty.FindPropertyRelative("m_OcclusionMask").intValue = -1;
            configProperty.FindPropertyRelative("m_OcclusionCheckInterval").floatValue = 0.12f;
            configProperty.FindPropertyRelative("m_OcclusionLowPassCutoff").floatValue = 1200f;
            configProperty.FindPropertyRelative("m_OcclusionVolumeMultiplier").floatValue = 0.55f;
            configProperty.FindPropertyRelative("audioRolloffMode").enumValueIndex = (int)AudioRolloffMode.Logarithmic;
            configProperty.FindPropertyRelative("minDistance").floatValue = minDistance;
            configProperty.FindPropertyRelative("maxDistance").floatValue = maxDistance;
        }

        private void RepairGroupConfigsIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            AudioComponent component = target as AudioComponent;
            if (component == null)
            {
                return;
            }

            if (!NeedsGroupConfigRepair())
            {
                return;
            }

            Undo.RecordObject(component, "Repair Audio Group Configs");
            component.EnsureGroupConfigsForEditor();
            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        private void AutoBindDefaultMixer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || _audioMixer == null)
            {
                return;
            }

            if (_audioMixer.objectReferenceValue != null)
            {
                return;
            }

            AudioComponent component = target as AudioComponent;
            if (component == null)
            {
                return;
            }

            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(DefaultAudioMixerPath);
            if (mixer == null)
            {
                return;
            }

            Undo.RecordObject(component, "Bind Default Audio Mixer");
            _audioMixer.objectReferenceValue = mixer;
            EditorUtility.SetDirty(component);
        }

        private void AutoBindMissingMixerGroups()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || _audioMixer == null || _audioGroupConfigs == null)
            {
                return;
            }

            AudioComponent component = target as AudioComponent;
            if (component == null)
            {
                return;
            }

            AudioMixer mixer = _audioMixer.objectReferenceValue as AudioMixer;
            if (mixer == null || !_audioGroupConfigs.isArray)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < _audioGroupConfigs.arraySize; i++)
            {
                SerializedProperty configProperty = _audioGroupConfigs.GetArrayElementAtIndex(i);
                SerializedProperty mixerGroupProperty = configProperty.FindPropertyRelative("m_MixerGroup");
                if (mixerGroupProperty == null || mixerGroupProperty.objectReferenceValue != null)
                {
                    continue;
                }

                SerializedProperty typeProperty = configProperty.FindPropertyRelative("AudioType");
                if (typeProperty == null)
                {
                    continue;
                }

                AudioMixerGroup mixerGroup = ResolveMixerGroup(mixer, (AudioType)typeProperty.enumValueIndex);
                if (mixerGroup == null)
                {
                    continue;
                }

                if (!changed)
                {
                    Undo.RecordObject(component, "Bind Audio Mixer Groups");
                    changed = true;
                }

                mixerGroupProperty.objectReferenceValue = mixerGroup;
            }

            if (changed)
            {
                EditorUtility.SetDirty(component);
            }
        }

        private void BindMixerGroup(SerializedProperty configProperty, AudioType type, bool overwrite)
        {
            if (_audioMixer == null)
            {
                return;
            }

            AudioMixer mixer = _audioMixer.objectReferenceValue as AudioMixer;
            if (mixer != null)
            {
                BindMixerGroup(configProperty, mixer, type, overwrite);
            }
        }

        private static bool BindMixerGroup(SerializedProperty configProperty, AudioMixer mixer, AudioType type, bool overwrite)
        {
            if (configProperty == null || mixer == null)
            {
                return false;
            }

            SerializedProperty mixerGroupProperty = configProperty.FindPropertyRelative("m_MixerGroup");
            if (mixerGroupProperty == null || (!overwrite && mixerGroupProperty.objectReferenceValue != null))
            {
                return false;
            }

            AudioMixerGroup mixerGroup = ResolveMixerGroup(mixer, type);
            if (mixerGroup == null)
            {
                return false;
            }

            mixerGroupProperty.objectReferenceValue = mixerGroup;
            return true;
        }

        private static AudioMixerGroup ResolveMixerGroup(AudioMixer mixer, AudioType type)
        {
            string path = GetMixerGroupPath(type);
            AudioMixerGroup[] groups = mixer.FindMatchingGroups(path);
            if (groups != null)
            {
                string groupName = GetMixerGroupName(type);
                for (int i = 0; i < groups.Length; i++)
                {
                    AudioMixerGroup group = groups[i];
                    if (group != null && group.name == groupName)
                    {
                        return group;
                    }
                }

                if (groups.Length == 1)
                {
                    return groups[0];
                }
            }

            return null;
        }

        private static string GetMixerGroupPath(AudioType type)
        {
            switch (type)
            {
                case AudioType.Sound:
                    return "Master/Sound";
                case AudioType.UISound:
                    return "Master/UISound";
                case AudioType.Music:
                    return "Master/Music";
                case AudioType.Voice:
                    return "Master/Voice";
                case AudioType.Ambient:
                    return "Master/Ambient";
                default:
                    return "Master/Sound";
            }
        }

        private static string GetMixerGroupName(AudioType type)
        {
            switch (type)
            {
                case AudioType.Sound:
                    return "Sound";
                case AudioType.UISound:
                    return "UISound";
                case AudioType.Music:
                    return "Music";
                case AudioType.Voice:
                    return "Voice";
                case AudioType.Ambient:
                    return "Ambient";
                default:
                    return "Sound";
            }
        }

        private bool NeedsGroupConfigRepair()
        {
            serializedObject.Update();
            if (_audioGroupConfigs == null || !_audioGroupConfigs.isArray || _audioGroupConfigs.arraySize != (int)AudioType.Max)
            {
                return true;
            }

            bool[] found = new bool[(int)AudioType.Max];
            for (int i = 0; i < _audioGroupConfigs.arraySize; i++)
            {
                SerializedProperty element = _audioGroupConfigs.GetArrayElementAtIndex(i);
                SerializedProperty typeProperty = element.FindPropertyRelative("AudioType");
                if (typeProperty == null)
                {
                    return true;
                }

                int typeIndex = typeProperty.enumValueIndex;
                if ((uint)typeIndex >= (uint)found.Length || found[typeIndex])
                {
                    return true;
                }

                found[typeIndex] = true;
            }

            for (int i = 0; i < found.Length; i++)
            {
                if (!found[i])
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshRuntime()
        {
            if (_runtimeRoot == null)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                if (_runtimeState != RuntimeStateStopped)
                {
                    _runtimeRoot.Clear();
                    _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                        "Enter Play Mode to inspect runtime audio state."));
                    _runtimeState = RuntimeStateStopped;
                }

                return;
            }

            if (!AppServices.HasWorld ||
                !AppServices.App.TryGet<IAudioService>(out IAudioService audioService) ||
                audioService is not IAudioDebugService debugService)
            {
                if (_runtimeState != RuntimeStateUnavailable)
                {
                    _runtimeRoot.Clear();
                    _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                        "Audio service is not initialized."));
                    _runtimeState = RuntimeStateUnavailable;
                }

                return;
            }

            _runtimeState = RuntimeStateActive;
            _runtimeRoot.Clear();

            debugService.FillServiceDebugInfo(_serviceInfo);
            BuildRuntimeServiceSection();
            BuildRuntimeGroupsSection(debugService);
            BuildRuntimeAgentsSection(debugService);
            BuildRuntimeClipCacheSection(debugService);
        }

        private void BuildRuntimeServiceSection()
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Runtime Service");
            _runtimeRoot.Add(section);
            AddHeaderBadge(
                section,
                _serviceInfo.Initialized ? "LIVE" : "IDLE",
                _serviceInfo.Initialized ? AlicizaValueTone.Success : AlicizaValueTone.Muted);

            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Initialized",
                _serviceInfo.Initialized.ToString(),
                _serviceInfo.Initialized ? AlicizaValueTone.Success : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Enable",
                _serviceInfo.Enable.ToString(),
                _serviceInfo.Enable ? AlicizaValueTone.Success : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(section.Body, "Volume", _serviceInfo.Volume.ToString("F3"));
            AddObjectValueRow(section.Body, "Listener", _serviceInfo.Listener);
            AddObjectValueRow(section.Body, "Instance Root", _serviceInfo.InstanceRoot);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Sources",
                _serviceInfo.ActiveSourceCount + " / " + _serviceInfo.TotalSourceCount);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Active Agents",
                _serviceInfo.ActiveAgentCount.ToString(),
                _serviceInfo.ActiveAgentCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(section.Body, "Audio Groups", _serviceInfo.CategoryCount.ToString());
            AlicizaEditorUI.AddValueRow(section.Body, "Handle Capacity", _serviceInfo.HandleCapacity.ToString());
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Clip Cache",
                _serviceInfo.ClipCacheCount + " / " + _serviceInfo.ClipCacheCapacity);
            AlicizaEditorUI.AddValueRow(section.Body, "Cache TTL", _serviceInfo.ClipCacheTtl.ToString("F1") + "s");
            AlicizaEditorUI.AddValueRow(section.Body, "Default Cache Policy", _serviceInfo.DefaultCachePolicy.ToString());
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Cache Loading/Pinned",
                _serviceInfo.LoadingClipCount + " / " + _serviceInfo.PinnedClipCount);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Cache Policies",
                "None " + _serviceInfo.CachePolicyNoneCount +
                " | Ttl " + _serviceInfo.CachePolicyTtlCount +
                " | Pin " + _serviceInfo.CachePolicyPinCount);
        }

        private void BuildRuntimeGroupsSection(IAudioDebugService debugService)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Runtime Groups");
            _runtimeRoot.Add(section);
            AddHeaderBadge(section, debugService.CategoryCount.ToString(), AlicizaValueTone.Muted);

            if (debugService.CategoryCount <= 0)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("No audio groups are registered."));
                return;
            }

            bool any = false;
            for (int i = 0; i < debugService.CategoryCount; i++)
            {
                if (!debugService.FillCategoryDebugInfo(i, _categoryInfo))
                {
                    continue;
                }

                any = true;
                section.Body.Add(CreateRuntimeGroupCard());
            }

            if (!any)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("No audio group runtime state is available."));
            }
        }

        private AlicizaFoldoutCard CreateRuntimeGroupCard()
        {
            int typeIndex = (int)_categoryInfo.Type;
            bool expanded = (uint)typeIndex < (uint)_runtimeGroupExpanded.Length && _runtimeGroupExpanded[typeIndex];
            AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard(_categoryInfo.Type.ToString(), expanded);
            card.name = "alicia-audio-runtime-group-" + typeIndex;

            if ((uint)typeIndex < (uint)_runtimeGroupExpanded.Length)
            {
                int capturedIndex = typeIndex;
                card.ExpandedChanged += value => _runtimeGroupExpanded[capturedIndex] = value;
            }

            card.Meta.text = "Active " + _categoryInfo.ActiveCount + "/" + _categoryInfo.Capacity +
                             " | Free " + _categoryInfo.FreeCount;
            card.Actions.Add(AlicizaEditorUI.CreateBadge(
                _categoryInfo.Enabled ? "ON" : "OFF",
                _categoryInfo.Enabled ? AlicizaValueTone.Success : AlicizaValueTone.Muted));

            AlicizaEditorUI.AddValueRow(card.Body, "Volume", _categoryInfo.Volume.ToString("F2"));
            AlicizaEditorUI.AddValueRow(card.Body, "Capacity", _categoryInfo.Capacity.ToString());
            AlicizaEditorUI.AddValueRow(card.Body, "Created", _categoryInfo.CreatedCount.ToString());
            AlicizaEditorUI.AddValueRow(
                card.Body,
                "Active",
                _categoryInfo.ActiveCount.ToString(),
                _categoryInfo.ActiveCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(card.Body, "Free", _categoryInfo.FreeCount.ToString(), AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(card.Body, "Heap", _categoryInfo.HeapCount.ToString(), AlicizaValueTone.Muted);
            return card;
        }

        private void BuildRuntimeAgentsSection(IAudioDebugService debugService)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Active Agents");
            _runtimeRoot.Add(section);
            AddHeaderBadge(
                section,
                _serviceInfo.ActiveAgentCount.ToString(),
                _serviceInfo.ActiveAgentCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);

            int shown = 0;
            for (int typeIndex = 0; typeIndex < debugService.CategoryCount; typeIndex++)
            {
                if (!debugService.FillCategoryDebugInfo(typeIndex, _categoryInfo))
                {
                    continue;
                }

                for (int agentIndex = 0; agentIndex < _categoryInfo.Capacity; agentIndex++)
                {
                    if (!debugService.FillAgentDebugInfo(typeIndex, agentIndex, _agentInfo) ||
                        _agentInfo.State == AudioAgentRuntimeState.Free)
                    {
                        continue;
                    }

                    shown++;
                    string clipName = _agentInfo.Clip != null ? _agentInfo.Clip.name : "<None>";
                    string address = string.IsNullOrEmpty(_agentInfo.Address) ? "<Direct Clip>" : _agentInfo.Address;
                    AlicizaValueTone tone = _agentInfo.State == AudioAgentRuntimeState.Playing
                        ? AlicizaValueTone.Success
                        : _agentInfo.State == AudioAgentRuntimeState.Loading
                            ? AlicizaValueTone.Warning
                            : AlicizaValueTone.Default;

                    AlicizaEditorUI.AddValueRow(
                        section.Body,
                        _agentInfo.Type + "[" + _agentInfo.Index + "]",
                        _agentInfo.State +
                        " | Handle " + _agentInfo.Handle +
                        " | Clip " + clipName +
                        " | Address " + address +
                        " | Vol " + _agentInfo.Volume.ToString("F2") +
                        " | 3D " + _agentInfo.Spatial,
                        tone);
                }
            }

            if (shown == 0)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("No active agents."));
            }
        }

        private void BuildRuntimeClipCacheSection(IAudioDebugService debugService)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Clip Cache");
            _runtimeRoot.Add(section);
            AddHeaderBadge(
                section,
                debugService.ClipCacheCount + " / " + debugService.ClipCacheCapacity,
                AlicizaValueTone.Muted);

            AudioClipCacheEntry entry = debugService.FirstClipCacheEntry;
            if (entry == null)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("Clip cache is empty."));
                return;
            }

            int shown = 0;
            while (entry != null)
            {
                AudioClipCacheEntry next = entry.AllNext;
                if (debugService.FillClipCacheDebugInfo(entry, _clipCacheInfo))
                {
                    shown++;
                    string address = string.IsNullOrEmpty(_clipCacheInfo.Address) ? "<No Address>" : _clipCacheInfo.Address;
                    AlicizaValueTone tone = _clipCacheInfo.Loading
                        ? AlicizaValueTone.Warning
                        : _clipCacheInfo.IsLoaded
                            ? AlicizaValueTone.Default
                            : AlicizaValueTone.Muted;

                    AlicizaEditorUI.AddValueRow(
                        section.Body,
                        address,
                        "Ref " + _clipCacheInfo.RefCount +
                        " | Pending " + _clipCacheInfo.PendingCount +
                        " | Loaded " + _clipCacheInfo.IsLoaded +
                        " | Policy " + _clipCacheInfo.CachePolicy +
                        " | Loading " + _clipCacheInfo.Loading +
                        " | Pinned " + _clipCacheInfo.Pinned +
                        " | LRU " + _clipCacheInfo.InLru,
                        tone);
                }

                entry = next;
            }

            if (shown == 0)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("Clip cache is empty."));
            }
        }

        private static void AddHeaderBadge(AlicizaSection section, string text, AlicizaValueTone tone)
        {
            if (section == null)
            {
                return;
            }

            var host = new VisualElement { name = "alicia-section-badge" };
            host.AddToClassList("alicia-preview-header");
            host.Add(AlicizaEditorUI.CreateBadge(text, tone));
            section.Header.Add(host);
        }

        private static void AddObjectValueRow(VisualElement parent, string caption, Object value)
        {
            string text = value != null ? value.name : "<None>";
            AlicizaEditorUI.AddValueRow(
                parent,
                caption,
                text,
                value != null ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
        }
    }
}
