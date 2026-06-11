using AlicizaX.Audio.Runtime;
using AlicizaX.Editor;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Audio.Editor
{
    [CustomEditor(typeof(AudioEmitter))]
    internal sealed class AudioEmitterInspector : UnityEditor.Editor
    {
        private const float SectionHeaderHeight = 24f;
        private const float FieldLabelWidth = 112f;

        private SerializedProperty _audioType;
        private SerializedProperty _clipMode;
        private SerializedProperty _address;
        private SerializedProperty _clip;
        private SerializedProperty _playOnEnable;
        private SerializedProperty _loop;
        private SerializedProperty _volume;
        private SerializedProperty _async;
        private SerializedProperty _cacheClip;
        private SerializedProperty _stopWithFadeout;
        private SerializedProperty _followSelf;
        private SerializedProperty _followOffset;
        private SerializedProperty _spatialBlend;
        private SerializedProperty _rolloffMode;
        private SerializedProperty _minDistance;
        private SerializedProperty _maxDistance;
        private SerializedProperty _useTriggerRange;
        private SerializedProperty _triggerRange;
        private SerializedProperty _triggerHysteresis;
        private SerializedProperty _drawGizmos;
        private SerializedProperty _drawOnlyWhenSelected;
        private SerializedProperty _triggerColor;
        private SerializedProperty _minDistanceColor;
        private SerializedProperty _maxDistanceColor;
        private GUIStyle _panelStyle;
        private GUIStyle _entryBodyStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _fieldLabelStyle;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureStyles();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawPlaybackSection();
            DrawSpatialSection();
            DrawTriggerSection();
            DrawGizmosSection();
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPlaybackSection()
        {
            DrawSectionHeader("Playback");
            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawField(_audioType);
            DrawEnumField(_clipMode);
            if (_clipMode.enumValueIndex == 1)
            {
                DrawField(_clip);
                if (_clip.objectReferenceValue == null)
                {
                    EditorUtils.TrHelpIconText("Clip mode requires an AudioClip.", MessageType.Warning);
                }
            }
            else
            {
                DrawField(_address);
                DrawField(_async);
                DrawField(_cacheClip);
                if (string.IsNullOrEmpty(_address.stringValue))
                {
                    EditorUtils.TrHelpIconText("Address mode requires a resource address.", MessageType.Warning);
                }
            }

            DrawField(_playOnEnable);
            DrawField(_loop);
            DrawField(_volume);
            DrawField(_stopWithFadeout);
            EditorGUILayout.EndVertical();
        }

        private void DrawSpatialSection()
        {
            DrawSectionHeader("Spatial");
            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawField(_followSelf);
            DrawField(_followOffset);
            DrawField(_spatialBlend);
            DrawEnumField(_rolloffMode);
            DrawField(_minDistance);
            DrawField(_maxDistance);
            if (_maxDistance.floatValue < _minDistance.floatValue)
            {
                EditorUtils.TrHelpIconText("Max Distance will be clamped to Min Distance.", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTriggerSection()
        {
            DrawSectionHeader("Trigger");
            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawField(_useTriggerRange);
            if (_useTriggerRange.boolValue)
            {
                DrawField(_triggerRange);
                DrawField(_triggerHysteresis);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawGizmosSection()
        {
            DrawSectionHeader("Gizmos");
            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawField(_drawGizmos);
            if (_drawGizmos.boolValue)
            {
                DrawField(_drawOnlyWhenSelected);
                if (_useTriggerRange.boolValue)
                {
                    DrawField(_triggerColor);
                }

                DrawField(_minDistanceColor);
                DrawField(_maxDistanceColor);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space(4f);
            Rect rect = GUILayoutUtility.GetRect(1f, SectionHeaderHeight);
            AlicizaEditorGUI.DrawToolbarBackground(rect);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, rect.height - 4f), title, _sectionTitleStyle);
        }

        private void DrawField(SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(property.displayName, _fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            EditorGUILayout.PropertyField(property, GUIContent.none, true);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEnumField(SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(property.displayName, _fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            Rect fieldRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
            property.enumValueIndex = AlicizaEditorGUI.DrawStyledPopup(fieldRect, property.enumValueIndex, property.enumDisplayNames);
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (_sectionTitleStyle != null)
            {
                return;
            }

            _panelStyle = AlicizaEditorGUI.Styles.Panel;
            _entryBodyStyle = AlicizaEditorGUI.Styles.EntryBody;
            _fieldRowStyle = AlicizaEditorGUI.Styles.FieldRow;
            _sectionTitleStyle = new GUIStyle(AlicizaEditorGUI.Styles.RowLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _fieldLabelStyle = AlicizaEditorGUI.Styles.FieldLabel;
        }

        private void OnEnable()
        {
            _audioType = serializedObject.FindProperty("m_AudioType");
            _clipMode = serializedObject.FindProperty("m_ClipMode");
            _address = serializedObject.FindProperty("m_Address");
            _clip = serializedObject.FindProperty("m_Clip");
            _playOnEnable = serializedObject.FindProperty("m_PlayOnEnable");
            _loop = serializedObject.FindProperty("m_Loop");
            _volume = serializedObject.FindProperty("m_Volume");
            _async = serializedObject.FindProperty("m_Async");
            _cacheClip = serializedObject.FindProperty("m_CacheClip");
            _stopWithFadeout = serializedObject.FindProperty("m_StopWithFadeout");
            _followSelf = serializedObject.FindProperty("m_FollowSelf");
            _followOffset = serializedObject.FindProperty("m_FollowOffset");
            _spatialBlend = serializedObject.FindProperty("m_SpatialBlend");
            _rolloffMode = serializedObject.FindProperty("m_RolloffMode");
            _minDistance = serializedObject.FindProperty("m_MinDistance");
            _maxDistance = serializedObject.FindProperty("m_MaxDistance");
            _useTriggerRange = serializedObject.FindProperty("m_UseTriggerRange");
            _triggerRange = serializedObject.FindProperty("m_TriggerRange");
            _triggerHysteresis = serializedObject.FindProperty("m_TriggerHysteresis");
            _drawGizmos = serializedObject.FindProperty("m_DrawGizmos");
            _drawOnlyWhenSelected = serializedObject.FindProperty("m_DrawOnlyWhenSelected");
            _triggerColor = serializedObject.FindProperty("m_TriggerColor");
            _minDistanceColor = serializedObject.FindProperty("m_MinDistanceColor");
            _maxDistanceColor = serializedObject.FindProperty("m_MaxDistanceColor");
        }
    }
}
