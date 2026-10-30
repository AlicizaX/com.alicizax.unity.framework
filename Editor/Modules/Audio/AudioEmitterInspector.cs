using AlicizaX.Audio.Runtime;
using AlicizaX.Editor.UIToolkit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Audio.Editor
{
    [CustomEditor(typeof(AudioEmitter))]
    internal sealed class AudioEmitterInspector : AlicizaUIEditor
    {
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

        private VisualElement _clipRow;
        private VisualElement _addressRow;
        private VisualElement _asyncRow;
        private VisualElement _cacheClipRow;
        private VisualElement _clipWarning;
        private VisualElement _addressWarning;
        private VisualElement _triggerDetails;
        private VisualElement _gizmoDetails;
        private VisualElement _maxDistanceWarning;

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

        protected override void BuildInspector(VisualElement root)
        {
            AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
            root.Add(panel);
            BuildPlayback(panel.Body);
            BuildSpatial(panel.Body);
            BuildTrigger(panel.Body);
            BuildGizmos(panel.Body);

            RefreshVisibility();
            AlicizaEditorUI.RefreshEvery(root, RefreshVisibility, 150);
        }

        private void BuildPlayback(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Playback");
            parent.Add(section);

            AlicizaEditorUI.AddPropertyRow(section.Body, "Audio Type", _audioType);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Clip Mode", _clipMode);

            _clipRow = AlicizaEditorUI.AddPropertyRow(
                section.Body, "Clip", _clip, true, typeof(AudioClip), false);
            _clipWarning = CreateWarning(section.Body, "Clip mode requires an AudioClip.");

            _addressRow = AlicizaEditorUI.AddPropertyRow(section.Body, "Address", _address);
            _asyncRow = AlicizaEditorUI.AddPropertyRow(section.Body, "Async", _async);
            _cacheClipRow = AlicizaEditorUI.AddPropertyRow(section.Body, "Cache Clip", _cacheClip);
            _addressWarning = CreateWarning(section.Body, "Address mode requires a resource address.");

            AlicizaEditorUI.AddPropertyRow(section.Body, "Play On Enable", _playOnEnable);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Loop", _loop);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Volume", _volume);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Stop With Fadeout", _stopWithFadeout);
        }

        private void BuildSpatial(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Spatial");
            parent.Add(section);

            AlicizaEditorUI.AddPropertyRow(section.Body, "Follow Self", _followSelf);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Follow Offset", _followOffset);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Spatial Blend", _spatialBlend);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Rolloff Mode", _rolloffMode);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Min Distance", _minDistance);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Max Distance", _maxDistance);
            _maxDistanceWarning = CreateWarning(
                section.Body, "Max Distance will be clamped to Min Distance.");
        }

        private void BuildTrigger(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Trigger");
            parent.Add(section);

            AlicizaEditorUI.AddPropertyRow(section.Body, "Use Trigger Range", _useTriggerRange);

            _triggerDetails = new VisualElement();
            section.Body.Add(_triggerDetails);
            AlicizaEditorUI.AddPropertyRow(_triggerDetails, "Trigger Range", _triggerRange);
            AlicizaEditorUI.AddPropertyRow(_triggerDetails, "Trigger Hysteresis", _triggerHysteresis);
        }

        private void BuildGizmos(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Gizmos");
            parent.Add(section);

            AlicizaEditorUI.AddPropertyRow(section.Body, "Draw Gizmos", _drawGizmos);

            _gizmoDetails = new VisualElement();
            section.Body.Add(_gizmoDetails);
            AlicizaEditorUI.AddPropertyRow(_gizmoDetails, "Only When Selected", _drawOnlyWhenSelected);
            AlicizaEditorUI.AddPropertyRow(_gizmoDetails, "Trigger Color", _triggerColor);
            AlicizaEditorUI.AddPropertyRow(_gizmoDetails, "Min Distance Color", _minDistanceColor);
            AlicizaEditorUI.AddPropertyRow(_gizmoDetails, "Max Distance Color", _maxDistanceColor);
        }

        private static VisualElement CreateWarning(VisualElement parent, string message)
        {
            Label warning = AlicizaEditorUI.CreateValueLabel(message, AlicizaValueTone.Warning);
            warning.style.marginLeft = 140f;
            warning.style.marginTop = 2f;
            warning.style.marginBottom = 4f;
            warning.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(warning);
            return warning;
        }

        private void RefreshVisibility()
        {
            if (_clipMode == null)
            {
                return;
            }

            bool clipMode = _clipMode.enumValueIndex == 1;
            SetVisible(_clipRow, clipMode);
            SetVisible(_clipWarning, clipMode && _clip != null && _clip.objectReferenceValue == null);

            SetVisible(_addressRow, !clipMode);
            SetVisible(_asyncRow, !clipMode);
            SetVisible(_cacheClipRow, !clipMode);
            SetVisible(_addressWarning, !clipMode && _address != null && string.IsNullOrEmpty(_address.stringValue));

            bool useTrigger = _useTriggerRange != null && _useTriggerRange.boolValue;
            SetVisible(_triggerDetails, useTrigger);

            bool drawGizmos = _drawGizmos != null && _drawGizmos.boolValue;
            SetVisible(_gizmoDetails, drawGizmos);
            SetVisible(
                _maxDistanceWarning,
                _maxDistance != null &&
                _minDistance != null &&
                _maxDistance.floatValue < _minDistance.floatValue);
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
