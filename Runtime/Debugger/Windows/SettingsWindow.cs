using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class SettingsWindow : ScrollableDebuggerWindowBase
        {
            private const float DefaultScaleSliderMax = 4f;

            private DebuggerComponent _mDebuggerComponent;

            public override void Initialize(params object[] args)
            {
                _mDebuggerComponent = DebuggerComponent.Instance;
                if (_mDebuggerComponent == null)
                {
                    Log.Error("Debugger component is invalid.");
                }
            }

            protected override void BuildWindow(VisualElement root)
            {
                if (_mDebuggerComponent == null)
                {
                    return;
                }

                VisualElement layoutSection = CreateSection("Window Settings", out VisualElement layoutCard);
                layoutCard.Add(CreateRow("Position", "Drag window header or floating entry to move."));
                layoutCard.Add(CreateToggle("Enable floating entry edge snap", _mDebuggerComponent.EnableFloatingToggleSnap, value =>
                {
                    _mDebuggerComponent.EnableFloatingToggleSnap = value;
                }));
                layoutCard.Add(CreateRangeControl("Width", _mDebuggerComponent.WindowRect.width, MinWindowWidth, Screen.width - 32f, value =>
                {
                    _mDebuggerComponent.WindowRect = new Rect(_mDebuggerComponent.WindowRect.x, _mDebuggerComponent.WindowRect.y, value, _mDebuggerComponent.WindowRect.height);
                    _mDebuggerComponent.SaveWindowLayoutSettings();
                }, allowIncreaseBeyondMax: true));
                layoutCard.Add(CreateRangeControl("Height", _mDebuggerComponent.WindowRect.height, MinWindowHeight, Screen.height - 32f, value =>
                {
                    _mDebuggerComponent.WindowRect = new Rect(_mDebuggerComponent.WindowRect.x, _mDebuggerComponent.WindowRect.y, _mDebuggerComponent.WindowRect.width, value);
                    _mDebuggerComponent.SaveWindowLayoutSettings();
                }, allowIncreaseBeyondMax: true));
                layoutCard.Add(CreateRangeControl("Scale", _mDebuggerComponent.WindowScale, MinWindowScale, DefaultScaleSliderMax, value =>
                {
                    _mDebuggerComponent.WindowScale = value;
                }, 0.01f, true));
                root.Add(layoutSection);

                VisualElement presets = CreateSection("Scale Presets", out VisualElement presetCard);
                VisualElement row = CreateToolbarRow();
                AddScaleButton(row, "1.0x", 1f);
                AddScaleButton(row, "1.5x", 1.5f);
                AddScaleButton(row, "2.0x", 2f);
                presetCard.Add(row);
                root.Add(presets);
            }

            private VisualElement CreateRangeControl(string title, float value, float min, float max, System.Action<float> onChanged, float step = 1f, bool allowIncreaseBeyondMax = false)
            {
                float currentValue = allowIncreaseBeyondMax ? Mathf.Max(value, min) : Mathf.Clamp(value, min, max);
                float sliderMax = Mathf.Max(max, currentValue);

                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Column;
                row.style.marginBottom = 8f;

                Label titleLabel = new Label(Utility.Text.Format("{0}: {1:F2}", title, currentValue));
                titleLabel.style.color = DebuggerTheme.PrimaryText;
                titleLabel.style.marginBottom = 4f;
                row.Add(titleLabel);

                Slider slider = null;
                void SetControlValue(float requestedValue)
                {
                    currentValue = allowIncreaseBeyondMax ? Mathf.Max(requestedValue, min) : Mathf.Clamp(requestedValue, min, max);
                    sliderMax = Mathf.Max(sliderMax, currentValue);
                    titleLabel.text = Utility.Text.Format("{0}: {1:F2}", title, currentValue);

                    if (slider != null)
                    {
                        slider.highValue = sliderMax;
                        slider.SetValueWithoutNotify(currentValue);
                    }

                    onChanged?.Invoke(currentValue);
                }

                VisualElement controls = CreateToolbarRow();
                Button decreaseButton = CreateActionButton("-", () => SetControlValue(currentValue - step), DebuggerTheme.ButtonSurface);
                decreaseButton.style.marginRight = 8f;
                controls.Add(decreaseButton);
                slider = CreateSlider(min, sliderMax, currentValue, SetControlValue);
                slider.style.marginRight = 8f;
                controls.Add(slider);
                controls.Add(CreateActionButton("+", () => SetControlValue(currentValue + step), DebuggerTheme.ButtonSurface));
                row.Add(controls);
                return row;
            }

            private void AddScaleButton(VisualElement row, string title, float scale)
            {
                Button button = CreateActionButton(title, () =>
                {
                    if (_mDebuggerComponent != null)
                    {
                        _mDebuggerComponent.WindowScale = scale;
                    }
                }, DebuggerTheme.ButtonSurface);
                button.style.marginRight = 8f;
                row.Add(button);
            }
        }
    }
}
