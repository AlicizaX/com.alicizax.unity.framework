using AlicizaX.UI.Runtime;
using AlicizaX.Editor;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.UI.Editor
{
    [CustomEditor(typeof(UIPresetTransition))]
    internal sealed class UIPresetTransitionInspector : UnityEditor.Editor
    {
        private enum PreviewMode
        {
            Open,
            Close,
        }

        private const float ToolbarHeight = 30f;
        private const float SectionHeaderHeight = 24f;
        private const float FieldLabelWidth = 146f;
        private const float SliderValueWidth = 48f;
        private const float PreviewButtonWidth = 92f;
        private const float PreviewWideButtonWidth = 112f;

        private static readonly string[] PreviewModeOptions = { "Open", "Close" };

        private UIPresetTransition _transition;
        private PreviewMode _previewMode = PreviewMode.Open;
        private float _previewProgress = 1f;
        private SerializedProperty _openPreset;
        private SerializedProperty _closePreset;
        private SerializedProperty _openEase;
        private SerializedProperty _closeEase;
        private SerializedProperty _targetRect;
        private SerializedProperty _canvasGroup;
        private SerializedProperty _useUnscaledTime;
        private SerializedProperty _initializeAsClosed;
        private SerializedProperty _disableInteractionWhilePlaying;
        private SerializedProperty _openDuration;
        private SerializedProperty _closeDuration;
        private SerializedProperty _slideDistance;
        private SerializedProperty _toastDistance;
        private SerializedProperty _closedScale;
        private GUIStyle _panelStyle;
        private GUIStyle _entryBodyStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _fieldLabelStyle;
        private GUIStyle _rowLabelStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _pillOnStyle;
        private GUIStyle _pillOffStyle;

        private void OnEnable()
        {
            _transition = (UIPresetTransition)target;
            _openPreset = serializedObject.FindProperty("openPreset");
            _closePreset = serializedObject.FindProperty("closePreset");
            _openEase = serializedObject.FindProperty("openEase");
            _closeEase = serializedObject.FindProperty("closeEase");
            _targetRect = serializedObject.FindProperty("targetRect");
            _canvasGroup = serializedObject.FindProperty("canvasGroup");
            _useUnscaledTime = serializedObject.FindProperty("useUnscaledTime");
            _initializeAsClosed = serializedObject.FindProperty("initializeAsClosed");
            _disableInteractionWhilePlaying = serializedObject.FindProperty("disableInteractionWhilePlaying");
            _openDuration = serializedObject.FindProperty("openDuration");
            _closeDuration = serializedObject.FindProperty("closeDuration");
            _slideDistance = serializedObject.FindProperty("slideDistance");
            _toastDistance = serializedObject.FindProperty("toastDistance");
            _closedScale = serializedObject.FindProperty("closedScale");
        }

        private void OnDisable()
        {
            StopPreview();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureStyles();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawTargetSection();
            EditorGUILayout.EndVertical();

            bool inspectorChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(4f);
            DrawPreviewPanel();

            if (inspectorChanged && _transition != null && _transition.EditorHasActivePreview())
            {
                ApplyPreview();
            }
        }

        private void DrawTargetSection()
        {
            DrawSectionHeader("Animation");
            EditorGUILayout.BeginVertical(_entryBodyStyle);
            DrawField(_targetRect);
            DrawField(_canvasGroup);

            bool alphaRequired = RequiresCanvasGroup();
            if (alphaRequired && !_canvasGroup.hasMultipleDifferentValues && _canvasGroup.objectReferenceValue == null)
            {
                EditorUtils.TrHelpIconText("CanvasGroup is required by alpha or interaction control and will be created automatically.", MessageType.Info);
            }

            DrawEnumField(_openPreset);
            DrawEnumField(_closePreset);
            DrawEnumField(_openEase);
            DrawEnumField(_closeEase);

            DrawField(_openDuration);
            DrawField(_closeDuration);
            DrawField(_slideDistance);
            DrawField(_toastDistance);
            DrawField(_closedScale);

            DrawBoolRow(_useUnscaledTime);
            DrawBoolRow(_initializeAsClosed);
            DrawBoolRow(_disableInteractionWhilePlaying);


            EditorGUILayout.EndVertical();
        }



        private void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawPreviewToolbar();

            if (targets.Length != 1)
            {
                DrawPreviewMessage("Preview is available when a single UIPresetTransition is selected.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                DrawPreviewMessage("Preset preview is only available in edit mode.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            EditorUtils.TrHelpIconText("Preview scrubs the selected transition in edit mode and restores automatically when this inspector closes.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            DrawPreviewModeRow();
            DrawPreviewProgressRow();
            if (EditorGUI.EndChangeCheck())
            {
                ApplyPreview();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("At Start", AlicizaEditorGUI.Styles.InlineButton, GUILayout.Width(PreviewButtonWidth)))
            {
                _previewProgress = 0f;
                ApplyPreview();
            }

            if (GUILayout.Button("At End", AlicizaEditorGUI.Styles.InlineButton, GUILayout.Width(PreviewButtonWidth)))
            {
                _previewProgress = 1f;
                ApplyPreview();
            }

            if (GUILayout.Button("Restore", AlicizaEditorGUI.Styles.InlineButton, GUILayout.Width(PreviewButtonWidth)))
            {
                StopPreview();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Preview Open", AlicizaEditorGUI.Styles.InlineButton, GUILayout.Width(PreviewWideButtonWidth)))
            {
                _previewMode = PreviewMode.Open;
                _previewProgress = 1f;
                ApplyPreview();
            }

            if (GUILayout.Button("Preview Close", AlicizaEditorGUI.Styles.InlineButton, GUILayout.Width(PreviewWideButtonWidth)))
            {
                _previewMode = PreviewMode.Close;
                _previewProgress = 1f;
                ApplyPreview();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar(string title)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
            GUI.Label(labelRect, title, _rowLabelStyle);
        }

        private void DrawPreviewToolbar()
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 96f, 20f);
            GUI.Label(labelRect, "Editor Preview", _rowLabelStyle);

            bool active = _transition != null && _transition.EditorHasActivePreview();
            Rect stateRect = new Rect(toolbarRect.xMax - 78f, toolbarRect.y + 6f, 70f, 18f);
            GUI.Label(stateRect, active ? "ACTIVE" : "IDLE", active ? _pillOnStyle : _pillOffStyle);
        }

        private void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space(4f);
            Rect rect = GUILayoutUtility.GetRect(1f, SectionHeaderHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(rect);

            Rect labelRect = new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, rect.height - 4f);
            GUI.Label(labelRect, title, _sectionTitleStyle);
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
            EditorGUI.BeginChangeCheck();
            bool showMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            int nextIndex = AlicizaEditorGUI.DrawStyledPopup(fieldRect, property.enumValueIndex, property.enumDisplayNames);
            EditorGUI.showMixedValue = showMixedValue;
            if (EditorGUI.EndChangeCheck())
            {
                property.enumValueIndex = nextIndex;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBoolRow(SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(property.displayName, _fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            if (property.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(property, GUIContent.none, true);
            }
            else
            {
                bool value = property.boolValue;
                EditorGUI.BeginChangeCheck();
                bool nextValue = GUILayout.Toggle(
                    value,
                    value ? "Enabled" : "Disabled",
                    value ? _pillOnStyle : _pillOffStyle,
                    GUILayout.Width(82f));
                if (EditorGUI.EndChangeCheck())
                {
                    property.boolValue = nextValue;
                }

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreviewModeRow()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField("Transition", _fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            Rect fieldRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
            _previewMode = (PreviewMode)AlicizaEditorGUI.DrawStyledPopup(fieldRect, (int)_previewMode, PreviewModeOptions);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreviewProgressRow()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField("Progress", _fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            _previewProgress = Mathf.Clamp01(_previewProgress);
            _previewProgress = GUILayout.HorizontalSlider(_previewProgress, 0f, 1f, GUILayout.MinWidth(90f));
            _previewProgress = Mathf.Clamp01(EditorGUILayout.FloatField(_previewProgress, GUILayout.Width(SliderValueWidth)));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreviewMessage(string message, MessageType messageType)
        {
            EditorGUILayout.BeginVertical(_entryBodyStyle);
            EditorUtils.TrHelpIconText(message, messageType);
            EditorGUILayout.EndVertical();
        }

        private bool RequiresCanvasGroup()
        {
            if (!_disableInteractionWhilePlaying.hasMultipleDifferentValues && _disableInteractionWhilePlaying.boolValue)
            {
                return true;
            }

            return UsesAlpha(_openPreset) || UsesAlpha(_closePreset);
        }

        private static bool UsesAlpha(SerializedProperty property)
        {
            if (property.hasMultipleDifferentValues)
            {
                return false;
            }

            return UsesAlpha((UITransitionPreset)property.enumValueIndex);
        }

        private static bool UsesAlpha(UITransitionPreset preset)
        {
            switch (preset)
            {
                case UITransitionPreset.Fade:
                case UITransitionPreset.FadeScale:
                case UITransitionPreset.SlideFromBottom:
                case UITransitionPreset.SlideFromTop:
                case UITransitionPreset.SlideFromLeft:
                case UITransitionPreset.SlideFromRight:
                case UITransitionPreset.Toast:
                    return true;
                default:
                    return false;
            }
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
            _sectionTitleStyle = new GUIStyle(AlicizaEditorGUI.Styles.RowLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _pillOnStyle = AlicizaEditorGUI.Styles.PillOn;
            _pillOffStyle = AlicizaEditorGUI.Styles.PillOff;
        }

        private void ApplyPreview()
        {
            if (_transition == null)
            {
                return;
            }

            if (_previewMode == PreviewMode.Open)
            {
                _transition.EditorPreviewOpen(_previewProgress);
            }
            else
            {
                _transition.EditorPreviewClose(_previewProgress);
            }

            SceneView.RepaintAll();
            Repaint();
        }

        private void StopPreview()
        {
            if (_transition == null)
            {
                return;
            }

            _transition.EditorStopPreview();
            SceneView.RepaintAll();
            Repaint();
        }
    }
}
