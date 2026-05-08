using AlicizaX.Audio.Runtime;
using AlicizaX.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Editor
{
    [CustomEditor(typeof(AudioComponent))]
    internal sealed class AudioComponentInspector : GameFrameworkInspector
    {
        private const float ToolbarHeight = 28f;
        private const float RowLabelWidth = 132f;
        private const float ConfigHeaderHeight = 24f;
        private const float ConfigButtonSize = 20f;
        private const string DefaultAudioMixerPath = "Packages/com.alicizax.unity.framework/Runtime/Audio/Resources/AudioMixer.mixer";

        private readonly AudioServiceDebugInfo _serviceInfo = new AudioServiceDebugInfo();
        private readonly AudioCategoryDebugInfo _categoryInfo = new AudioCategoryDebugInfo();
        private readonly AudioAgentDebugInfo _agentInfo = new AudioAgentDebugInfo();
        private readonly AudioClipCacheDebugInfo _clipCacheInfo = new AudioClipCacheDebugInfo();

        private SerializedProperty _audioListener;
        private SerializedProperty _audioMixer;
        private SerializedProperty _audioGroupConfigs;
        private bool[] _configFoldouts;
        private bool _groupsExpanded = true;
        private bool _debugExpanded = true;
        private bool _categoryDebugExpanded = true;
        private bool _agentDebugExpanded = true;
        private bool _clipCacheDebugExpanded = true;
        private GUIContent _expandAllContent;
        private GUIContent _collapseAllContent;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            AudioComponent component = (AudioComponent)target;
            RepairGroupConfigsIfNeeded(component);

            serializedObject.Update();
            AutoBindDefaultMixer(component);
            AutoBindMissingMixerGroups(component);
            DrawComponentPanel(component);
            serializedObject.ApplyModifiedProperties();

            DrawRuntimeDebugInfo();
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private void OnEnable()
        {
            _audioListener = serializedObject.FindProperty("m_AudioListener");
            _audioMixer = serializedObject.FindProperty("m_AudioMixer");
            _audioGroupConfigs = serializedObject.FindProperty("m_AudioGroupConfigs");
            _configFoldouts = new bool[(int)AudioType.Max];
            for (int i = 0; i < _configFoldouts.Length; i++)
            {
                _configFoldouts[i] = false;
            }

            _expandAllContent = EditorGUIUtility.IconContent("d_scrollup", "Expand all audio groups");
            _collapseAllContent = EditorGUIUtility.IconContent("d_scrolldown", "Collapse all audio groups");
        }

        private void DrawComponentPanel(AudioComponent component)
        {
            EditorGUILayout.BeginVertical(AlicizaEditorGUI.Styles.Panel);
            DrawToolbar("Audio Component", true, component);

            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            DrawPropertyRow("Listener", _audioListener);
            DrawPropertyRow("Audio Mixer", _audioMixer);
            DrawGroupConfigs();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar(string title, bool drawResetButton, AudioComponent component)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);
            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 4f, toolbarRect.width - 46f, 20f);
            GUI.Label(labelRect, title, AlicizaEditorGUI.Styles.RowLabel);

            if (!drawResetButton)
            {
                return;
            }

            Rect buttonRect = new Rect(toolbarRect.xMax - 26f, toolbarRect.y + 4f, 20f, 20f);
            if (AlicizaEditorGUI.DrawToolbarButton(buttonRect, EditorUtils.Styles.RefreshIcon))
            {
                Undo.RecordObject(component, "Reset Audio Group Configs");
                component.ResetGroupConfigsForEditor();
                EditorUtility.SetDirty(component);
                serializedObject.Update();
                AutoBindDefaultMixer(component);
                AutoBindMissingMixerGroups(component);
            }
        }

        private static void DrawPropertyRow(string label, SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(AlicizaEditorGUI.Styles.FieldRow);
            EditorGUILayout.LabelField(label, AlicizaEditorGUI.Styles.FieldLabel, GUILayout.Width(RowLabelWidth));
            EditorGUILayout.PropertyField(property, GUIContent.none);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGroupConfigs()
        {
            EditorGUILayout.Space(2f);
            DrawAudioGroupsHeader();
            if (!_groupsExpanded)
            {
                return;
            }

            if (_audioGroupConfigs == null || !_audioGroupConfigs.isArray)
            {
                EditorUtils.TrHelpIconText("Audio group config array is missing.", MessageType.Error);
                return;
            }

            for (int i = 0; i < _audioGroupConfigs.arraySize; i++)
            {
                SerializedProperty configProperty = _audioGroupConfigs.GetArrayElementAtIndex(i);
                DrawConfig(configProperty, i);
            }
        }

        private void DrawAudioGroupsHeader()
        {
            Rect headerRect = GUILayoutUtility.GetRect(1f, ConfigHeaderHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(headerRect);

            Rect foldRect = new Rect(headerRect.x + 7f, headerRect.y + 2f, 16f, 20f);
            Rect expandAllRect = new Rect(headerRect.xMax - 47f, headerRect.y + 2f, ConfigButtonSize, ConfigButtonSize);
            Rect collapseAllRect = new Rect(headerRect.xMax - 25f, headerRect.y + 2f, ConfigButtonSize, ConfigButtonSize);
            Rect labelRect = new Rect(foldRect.xMax + 4f, headerRect.y + 2f, headerRect.width - 78f, 20f);
            AlicizaEditorGUI.DrawFoldoutIcon(foldRect, _groupsExpanded);
            GUI.Label(labelRect, "Audio Groups", AlicizaEditorGUI.Styles.RowLabel);

            if (AlicizaEditorGUI.DrawToolbarButton(expandAllRect, _expandAllContent))
            {
                SetAllConfigFoldouts(true);
                _groupsExpanded = true;
            }

            if (AlicizaEditorGUI.DrawToolbarButton(collapseAllRect, _collapseAllContent))
            {
                SetAllConfigFoldouts(false);
                _groupsExpanded = true;
            }

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && headerRect.Contains(currentEvent.mousePosition) && !expandAllRect.Contains(currentEvent.mousePosition) && !collapseAllRect.Contains(currentEvent.mousePosition))
            {
                _groupsExpanded = !_groupsExpanded;
                currentEvent.Use();
            }
        }

        private void SetAllConfigFoldouts(bool expanded)
        {
            if (_configFoldouts == null)
            {
                return;
            }

            for (int i = 0; i < _configFoldouts.Length; i++)
            {
                _configFoldouts[i] = expanded;
            }
        }

        private void DrawConfig(SerializedProperty configProperty, int index)
        {
            if (configProperty == null)
            {
                return;
            }

            if ((uint)index >= (uint)_configFoldouts.Length)
            {
                return;
            }

            SerializedProperty typeProperty = configProperty.FindPropertyRelative("AudioType");
            string title = typeProperty != null ? typeProperty.enumDisplayNames[typeProperty.enumValueIndex] : "Audio Group";

            Rect headerRect = GUILayoutUtility.GetRect(1f, ConfigHeaderHeight, GUILayout.ExpandWidth(true));
            bool hovered = headerRect.Contains(Event.current.mousePosition);
            bool expanded = _configFoldouts[index];
            AlicizaEditorGUI.DrawListItemBackground(headerRect, expanded, hovered);

            Rect foldRect = new Rect(headerRect.x + 6f, headerRect.y + 2f, 18f, 20f);
            Rect labelRect = new Rect(headerRect.x + 26f, headerRect.y + 2f, headerRect.width - 58f, 20f);
            Rect resetRect = new Rect(headerRect.xMax - 25f, headerRect.y + 2f, ConfigButtonSize, ConfigButtonSize);
            AlicizaEditorGUI.DrawFoldoutIcon(foldRect, expanded);
            GUI.Label(labelRect, title, AlicizaEditorGUI.Styles.RowLabel);

            if (AlicizaEditorGUI.DrawToolbarButton(resetRect, EditorUtils.Styles.RefreshIcon))
            {
                AudioComponent component = (AudioComponent)target;
                Undo.RecordObject(component, "Reset Audio Group Config");
                ResetConfig(configProperty, (AudioType)index);
                AutoBindDefaultMixer(component);
                BindMixerGroup(configProperty, (AudioType)index, true);
                EditorUtility.SetDirty(component);
            }

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && headerRect.Contains(currentEvent.mousePosition) && !resetRect.Contains(currentEvent.mousePosition))
            {
                _configFoldouts[index] = !expanded;
                currentEvent.Use();
            }

            if (!_configFoldouts[index])
            {
                return;
            }

            EditorGUILayout.BeginVertical(AlicizaEditorGUI.Styles.EntryBody);
            EditorGUI.BeginDisabledGroup(true);
            DrawPropertyRow("Type", typeProperty);
            EditorGUI.EndDisabledGroup();

            DrawPropertyRow("Name", configProperty.FindPropertyRelative("m_Name"));
            DrawPropertyRow("Mixer Group", configProperty.FindPropertyRelative("m_MixerGroup"));
            DrawPropertyRow("Mute", configProperty.FindPropertyRelative("m_Mute"));
            DrawPropertyRow("Volume", configProperty.FindPropertyRelative("m_Volume"));
            DrawPropertyRow("Agent Count", configProperty.FindPropertyRelative("m_AgentHelperCount"));
            DrawPropertyRow("Volume Param", configProperty.FindPropertyRelative("m_ExposedVolumeParameter"));
            DrawPropertyRow("Spatial Blend", configProperty.FindPropertyRelative("m_SpatialBlend"));
            DrawPropertyRow("Doppler", configProperty.FindPropertyRelative("m_DopplerLevel"));
            DrawPropertyRow("Spread", configProperty.FindPropertyRelative("m_Spread"));
            DrawPropertyRow("Priority", configProperty.FindPropertyRelative("m_SourcePriority"));
            DrawPropertyRow("Reverb Mix", configProperty.FindPropertyRelative("m_ReverbZoneMix"));
            DrawPropertyRow("Rolloff", configProperty.FindPropertyRelative("audioRolloffMode"));
            DrawPropertyRow("Min Distance", configProperty.FindPropertyRelative("minDistance"));
            DrawPropertyRow("Max Distance", configProperty.FindPropertyRelative("maxDistance"));

            SerializedProperty occlusionEnabled = configProperty.FindPropertyRelative("m_OcclusionEnabled");
            DrawPropertyRow("Occlusion", occlusionEnabled);
            if (occlusionEnabled != null && occlusionEnabled.boolValue)
            {
                DrawPropertyRow("Occlusion Mask", configProperty.FindPropertyRelative("m_OcclusionMask"));
                DrawPropertyRow("Check Interval", configProperty.FindPropertyRelative("m_OcclusionCheckInterval"));
                DrawPropertyRow("Low Pass", configProperty.FindPropertyRelative("m_OcclusionLowPassCutoff"));
                DrawPropertyRow("Volume Scale", configProperty.FindPropertyRelative("m_OcclusionVolumeMultiplier"));
            }

            EditorGUILayout.EndVertical();
        }

        private static void ResetConfig(SerializedProperty configProperty, AudioType type)
        {
            switch (type)
            {
                case AudioType.Sound:
                    ApplyDefaultConfig(configProperty, type, "Sound", "SoundVolume", 24, 1f, false, 128, 2f, 80f);
                    break;
                case AudioType.UISound:
                    ApplyDefaultConfig(configProperty, type, "UISound", "UISoundVolume", 12, 0f, false, 128, 1f, 25f);
                    break;
                case AudioType.Music:
                    ApplyDefaultConfig(configProperty, type, "Music", "MusicVolume", 2, 0f, false, 32, 1f, 25f);
                    break;
                case AudioType.Voice:
                    ApplyDefaultConfig(configProperty, type, "Voice", "VoiceVolume", 6, 1f, true, 128, 2f, 80f);
                    break;
                case AudioType.Ambient:
                    ApplyDefaultConfig(configProperty, type, "Ambient", "AmbientVolume", 6, 1f, true, 128, 2f, 80f);
                    break;
                default:
                    ApplyDefaultConfig(configProperty, AudioType.Sound, "Sound", "SoundVolume", 24, 1f, false, 128, 2f, 80f);
                    break;
            }
        }

        private static void ApplyDefaultConfig(SerializedProperty configProperty, AudioType type, string name, string exposedParameter, int agentCount, float spatialBlend, bool occlusionEnabled, int priority, float minDistance, float maxDistance)
        {
            configProperty.FindPropertyRelative("AudioType").enumValueIndex = (int)type;
            configProperty.FindPropertyRelative("m_Name").stringValue = name;
            configProperty.FindPropertyRelative("m_Mute").boolValue = false;
            configProperty.FindPropertyRelative("m_Volume").floatValue = 1f;
            configProperty.FindPropertyRelative("m_AgentHelperCount").intValue = agentCount;
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

        private void RepairGroupConfigsIfNeeded(AudioComponent component)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || component == null)
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

        private void AutoBindDefaultMixer(AudioComponent component)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || component == null || _audioMixer == null)
            {
                return;
            }

            if (_audioMixer.objectReferenceValue != null)
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

        private void AutoBindMissingMixerGroups(AudioComponent component)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || component == null || _audioMixer == null || _audioGroupConfigs == null)
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

        private void DrawRuntimeDebugInfo()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            using (new EditorUtils.BoxGroupScope("Runtime Debug", 24f))
            {
                _debugExpanded = EditorUtils.DrawBoxFoldoutHeader("Service", _debugExpanded, 22f);
                if (!_debugExpanded)
                {
                    return;
                }

                if (!AppServices.TryGet<IAudioService>(out IAudioService audioService) || audioService is not IAudioDebugService debugService)
                {
                    EditorUtils.TrHelpIconText("Audio service is not initialized.", MessageType.Info);
                    return;
                }

                debugService.FillServiceDebugInfo(_serviceInfo);
                DrawDebugRow("Initialized", _serviceInfo.Initialized.ToString());
                DrawDebugRow("Unity Audio Disabled", _serviceInfo.UnityAudioDisabled.ToString());
                DrawDebugRow("Enable", _serviceInfo.Enable.ToString());
                DrawDebugRow("Volume", _serviceInfo.Volume.ToString("F3"));
                DrawDebugObjectRow("Listener", _serviceInfo.Listener, typeof(AudioListener));
                DrawDebugObjectRow("Instance Root", _serviceInfo.InstanceRoot, typeof(Transform));
                DrawDebugRow("Active Agents", _serviceInfo.ActiveAgentCount.ToString());
                DrawDebugRow("Handle Capacity", _serviceInfo.HandleCapacity.ToString());
                DrawDebugRow("Clip Cache", _serviceInfo.ClipCacheCount + " / " + _serviceInfo.ClipCacheCapacity);

                DrawCategoryDebugInfo(debugService);
                DrawAgentDebugInfo(debugService);
                DrawClipCacheDebugInfo(debugService);
            }
        }

        private void DrawCategoryDebugInfo(IAudioDebugService debugService)
        {
            _categoryDebugExpanded = EditorUtils.DrawBoxFoldoutHeader("Categories", _categoryDebugExpanded, 22f);
            if (!_categoryDebugExpanded)
            {
                return;
            }

            for (int i = 0; i < debugService.CategoryCount; i++)
            {
                if (!debugService.FillCategoryDebugInfo(i, _categoryInfo))
                {
                    continue;
                }

                DrawDebugRow(
                    _categoryInfo.Type.ToString(),
                    "Enabled " + _categoryInfo.Enabled
                               + " | Volume " + _categoryInfo.Volume.ToString("F2")
                               + " | Active " + _categoryInfo.ActiveCount
                               + " | Free " + _categoryInfo.FreeCount
                               + " | Capacity " + _categoryInfo.Capacity);
            }
        }

        private void DrawAgentDebugInfo(IAudioDebugService debugService)
        {
            _agentDebugExpanded = EditorUtils.DrawBoxFoldoutHeader("Active Agents", _agentDebugExpanded, 22f);
            if (!_agentDebugExpanded)
            {
                return;
            }

            bool hasActive = false;
            for (int typeIndex = 0; typeIndex < debugService.CategoryCount; typeIndex++)
            {
                if (!debugService.FillCategoryDebugInfo(typeIndex, _categoryInfo))
                {
                    continue;
                }

                for (int agentIndex = 0; agentIndex < _categoryInfo.Capacity; agentIndex++)
                {
                    if (!debugService.FillAgentDebugInfo(typeIndex, agentIndex, _agentInfo) || _agentInfo.State == AudioAgentRuntimeState.Free)
                    {
                        continue;
                    }

                    hasActive = true;
                    string clipName = _agentInfo.Clip != null ? _agentInfo.Clip.name : "<None>";
                    string address = string.IsNullOrEmpty(_agentInfo.Address) ? "<Direct Clip>" : _agentInfo.Address;
                    DrawDebugRow(
                        _agentInfo.Type + "[" + _agentInfo.Index + "]",
                        _agentInfo.State
                        + " | Handle " + _agentInfo.Handle
                        + " | Clip " + clipName
                        + " | Address " + address
                        + " | Vol " + _agentInfo.Volume.ToString("F2")
                        + " | 3D " + _agentInfo.Spatial);
                }
            }

            if (!hasActive)
            {
                DrawDebugRow("State", "No active agents.");
            }
        }

        private void DrawClipCacheDebugInfo(IAudioDebugService debugService)
        {
            _clipCacheDebugExpanded = EditorUtils.DrawBoxFoldoutHeader("Clip Cache", _clipCacheDebugExpanded, 22f);
            if (!_clipCacheDebugExpanded)
            {
                return;
            }

            AudioClipCacheEntry entry = debugService.FirstClipCacheEntry;
            if (entry == null)
            {
                DrawDebugRow("State", "Empty.");
                return;
            }

            while (entry != null)
            {
                AudioClipCacheEntry next = entry.AllNext;
                if (debugService.FillClipCacheDebugInfo(entry, _clipCacheInfo))
                {
                    DrawDebugRow(
                        _clipCacheInfo.Address,
                        "Ref " + _clipCacheInfo.RefCount
                               + " | Pending " + _clipCacheInfo.PendingCount
                               + " | Loaded " + _clipCacheInfo.IsLoaded
                               + " | Loading " + _clipCacheInfo.Loading
                               + " | Pinned " + _clipCacheInfo.Pinned
                               + " | LRU " + _clipCacheInfo.InLru);
                }

                entry = next;
            }
        }

        private static void DrawDebugRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal(AlicizaEditorGUI.Styles.FieldRow);
            EditorGUILayout.LabelField(label, AlicizaEditorGUI.Styles.FieldLabel, GUILayout.Width(RowLabelWidth));
            EditorGUILayout.LabelField(value, AlicizaEditorGUI.Styles.RowLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawDebugObjectRow(string label, Object value, System.Type objectType)
        {
            EditorGUILayout.BeginHorizontal(AlicizaEditorGUI.Styles.FieldRow);
            EditorGUILayout.LabelField(label, AlicizaEditorGUI.Styles.FieldLabel, GUILayout.Width(RowLabelWidth));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(value, objectType, true);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }
    }
}
