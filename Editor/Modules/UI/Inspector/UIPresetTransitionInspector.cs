using AlicizaX.Editor.UIToolkit;
using AlicizaX.UI.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.UI.Editor
{
    [CustomEditor(typeof(UIPresetTransition))]
    internal sealed class UIPresetTransitionInspector : AlicizaUIEditor
    {
        private enum PreviewMode
        {
            Open,
            Close,
        }

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
        private SerializedProperty _openDuration;
        private SerializedProperty _closeDuration;
        private SerializedProperty _slideDistance;
        private SerializedProperty _toastDistance;
        private SerializedProperty _closedScale;
        private Label _previewState;
        private Label _canvasGroupHint;
        private Slider _previewSlider;
        private FloatField _previewField;

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

        protected override void BuildInspector(VisualElement root)
        {
            AlicizaPanel animationPanel = AlicizaEditorUI.CreatePanel(null);
            root.Add(animationPanel);
            BuildAnimationSection(animationPanel.Body);

            AlicizaPanel previewPanel = AlicizaEditorUI.CreatePanel(null);
            root.Add(previewPanel);
            BuildPreviewSection(previewPanel.Body);

            AlicizaEditorUI.RefreshEvery(root, RefreshTransientState, 250);
        }

        private void BuildAnimationSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Animation");
            parent.Add(section);

            AlicizaEditorUI.AddPropertyRow(
                section.Body, "Target Rect", _targetRect, true, typeof(RectTransform), true);
            AlicizaEditorUI.AddPropertyRow(
                section.Body, "Canvas Group", _canvasGroup, true, typeof(CanvasGroup), true);

            _canvasGroupHint = AlicizaEditorUI.CreateHint(
                "Fade presets need a CanvasGroup on widget holders. Windows should not use alpha presets.");
            _canvasGroupHint.style.marginTop = 4f;
            _canvasGroupHint.style.marginBottom = 4f;
            section.Body.Add(_canvasGroupHint);

            AddEnumRow(section.Body, "Open Preset", _openPreset);
            AddEnumRow(section.Body, "Close Preset", _closePreset);
            AddEnumRow(section.Body, "Open Ease", _openEase);
            AddEnumRow(section.Body, "Close Ease", _closeEase);

            AlicizaEditorUI.AddPropertyRow(section.Body, "Open Duration", _openDuration);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Close Duration", _closeDuration);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Slide Distance", _slideDistance);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Toast Distance", _toastDistance);
            AlicizaEditorUI.AddPropertyRow(section.Body, "Closed Scale", _closedScale);

            var unscaled = new AlicizaPillToggle();
            unscaled.BindProperty(_useUnscaledTime);
            AlicizaEditorUI.AddRow(section.Body, "Unscaled Time", unscaled);

            RefreshCanvasGroupHint();
        }

        private void BuildPreviewSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Editor Preview");
            parent.Add(section);

            var header = new VisualElement();
            header.AddToClassList("alicia-preview-header");
            _previewState = AlicizaEditorUI.CreateBadge("IDLE", AlicizaValueTone.Muted);
            header.Add(_previewState);
            section.Header.Add(header);

            if (targets.Length != 1)
            {
                section.Body.Add(AlicizaEditorUI.CreateHint(
                    "Preview is available when a single UIPresetTransition is selected."));
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                section.Body.Add(AlicizaEditorUI.CreateHint("Preset preview is only available in edit mode."));
                return;
            }

            Label info = AlicizaEditorUI.CreateHint(
                "Preview scrubs the selected transition in edit mode and restores automatically when this inspector closes.");
            info.style.marginBottom = 6f;
            section.Body.Add(info);

            var modeField = new AlicizaEnumField(PreviewModeOptions, (int)_previewMode);
            modeField.AddToClassList("alicia-control--grow");
            modeField.RegisterValueChangedCallback(evt =>
            {
                _previewMode = (PreviewMode)evt.newValue;
                ApplyPreview();
            });
            AlicizaEditorUI.AddRow(section.Body, "Transition", modeField);

            AddProgressRow(section.Body);

            var actions = new VisualElement();
            actions.AddToClassList("alicia-actions");

            Button atStart = AlicizaEditorUI.CreateTextButton("At Start");
            atStart.clicked += () => ApplyPreviewProgress(0f);
            actions.Add(atStart);

            Button atEnd = AlicizaEditorUI.CreateTextButton("At End");
            atEnd.clicked += () => ApplyPreviewProgress(1f);
            actions.Add(atEnd);

            Button restore = AlicizaEditorUI.CreateTextButton(
                "Restore", "Stop preview and restore the transition.", AlicizaButtonTone.Warning);
            restore.clicked += StopPreview;
            actions.Add(restore);
            section.Body.Add(actions);

            var previewActions = new VisualElement();
            previewActions.AddToClassList("alicia-actions");

            Button previewOpen = AlicizaEditorUI.CreateTextButton(
                "Preview Open", "Preview the open transition at its end state.", AlicizaButtonTone.Accent);
            previewOpen.clicked += () =>
            {
                _previewMode = PreviewMode.Open;
                modeField.SetValueWithoutNotify((int)PreviewMode.Open);
                ApplyPreviewProgress(1f);
            };
            previewActions.Add(previewOpen);

            Button previewClose = AlicizaEditorUI.CreateTextButton(
                "Preview Close", "Preview the close transition at its end state.", AlicizaButtonTone.Warning);
            previewClose.clicked += () =>
            {
                _previewMode = PreviewMode.Close;
                modeField.SetValueWithoutNotify((int)PreviewMode.Close);
                ApplyPreviewProgress(1f);
            };
            previewActions.Add(previewClose);
            section.Body.Add(previewActions);
        }

        private void AddProgressRow(VisualElement parent)
        {
            var row = new AlicizaRow("Progress");
            _previewSlider = new Slider(0f, 1f) { label = string.Empty, value = _previewProgress };
            _previewSlider.AddToClassList("alicia-slider__track");
            _previewSlider.style.flexGrow = 1f;
            _previewSlider.showInputField = false;

            _previewField = new FloatField { label = string.Empty, value = _previewProgress };
            _previewField.AddToClassList("alicia-control");
            _previewField.style.width = 56f;
            _previewField.style.minWidth = 56f;

            _previewSlider.RegisterValueChangedCallback(evt =>
            {
                _previewProgress = Mathf.Clamp01(evt.newValue);
                _previewField.SetValueWithoutNotify(_previewProgress);
                ApplyPreview();
            });
            _previewField.RegisterValueChangedCallback(evt =>
            {
                _previewProgress = Mathf.Clamp01(evt.newValue);
                _previewSlider.SetValueWithoutNotify(_previewProgress);
                ApplyPreview();
            });

            row.Content.Add(_previewSlider);
            row.Content.Add(_previewField);
            parent.Add(row);
        }

        private void ApplyPreviewProgress(float value)
        {
            _previewProgress = Mathf.Clamp01(value);
            _previewSlider?.SetValueWithoutNotify(_previewProgress);
            _previewField?.SetValueWithoutNotify(_previewProgress);
            ApplyPreview();
        }

        private void RefreshTransientState()
        {
            if (_transition == null)
            {
                return;
            }

            bool active = _transition.EditorHasActivePreview();
            if (_previewState != null)
            {
                _previewState.text = active ? "ACTIVE" : "IDLE";
                _previewState.EnableInClassList("alicia-tone--success", active);
                _previewState.EnableInClassList("alicia-tone--muted", !active);
            }

            RefreshCanvasGroupHint();
        }

        private void RefreshCanvasGroupHint()
        {
            if (_canvasGroupHint == null)
            {
                return;
            }

            bool visible = _canvasGroup != null &&
                           RequiresCanvasGroup() &&
                           !_canvasGroup.hasMultipleDifferentValues &&
                           _canvasGroup.objectReferenceValue == null;
            _canvasGroupHint.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void AddEnumRow(VisualElement parent, string caption, SerializedProperty property)
        {
            if (property == null)
            {
                return;
            }

            var field = new AlicizaEnumField(property);
            field.AddToClassList("alicia-control--grow");
            AlicizaEditorUI.AddRow(parent, caption, field);
        }

        private bool RequiresCanvasGroup()
        {
            return UsesAlpha(_openPreset) || UsesAlpha(_closePreset);
        }

        private static bool UsesAlpha(SerializedProperty property)
        {
            if (property == null || property.hasMultipleDifferentValues)
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
            RefreshTransientState();
        }

        private void StopPreview()
        {
            if (_transition == null)
            {
                return;
            }

            _transition.EditorStopPreview();
            SceneView.RepaintAll();
            RefreshTransientState();
        }
    }
}

