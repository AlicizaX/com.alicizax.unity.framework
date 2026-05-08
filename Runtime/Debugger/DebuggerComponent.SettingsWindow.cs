using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class SettingsWindow : ScrollableDebuggerWindowBase
        {
            private DebuggerComponent _mDebuggerComponent;
            private float m_LastIconX;
            private float m_LastIconY;
            private float m_LastWindowX;
            private float m_LastWindowY;
            private float m_LastWindowWidth;
            private float m_LastWindowHeight;
            private float m_LastWindowScale;
            private bool m_LastEnableFloatingToggleSnap;

            public override void Initialize(params object[] args)
            {
                _mDebuggerComponent = DebuggerComponent.Instance;
                if (_mDebuggerComponent == null)
                {
                    Log.Error("Debugger component is invalid.");
                    return;
                }

                m_LastIconX = Utility.PlayerPrefsX.GetFloat("Debugger.Icon.X", DefaultIconRect.x);
                m_LastIconY = Utility.PlayerPrefsX.GetFloat("Debugger.Icon.Y", DefaultIconRect.y);
                m_LastWindowX = Utility.PlayerPrefsX.GetFloat("Debugger.Window.X", DefaultWindowRect.x);
                m_LastWindowY = Utility.PlayerPrefsX.GetFloat("Debugger.Window.Y", DefaultWindowRect.y);
                m_LastWindowWidth = Utility.PlayerPrefsX.GetFloat("Debugger.Window.Width", DefaultWindowRect.width);
                m_LastWindowHeight = Utility.PlayerPrefsX.GetFloat("Debugger.Window.Height", DefaultWindowRect.height);
                bool inspectorSnapValue = _mDebuggerComponent.EnableFloatingToggleSnap;
                if (Utility.PlayerPrefsX.HasSetting("Debugger.Icon.Snap"))
                {
                    inspectorSnapValue = Utility.PlayerPrefsX.GetBool("Debugger.Icon.Snap", inspectorSnapValue);
                    _mDebuggerComponent.EnableFloatingToggleSnap = inspectorSnapValue;
                }

                m_LastEnableFloatingToggleSnap = inspectorSnapValue;
                _mDebuggerComponent.WindowScale = m_LastWindowScale = Utility.PlayerPrefsX.GetFloat("Debugger.Window.Scale", DefaultWindowScale);
                _mDebuggerComponent.IconRect = new Rect(m_LastIconX, m_LastIconY, DefaultIconRect.width, DefaultIconRect.height);
                _mDebuggerComponent.WindowRect = new Rect(m_LastWindowX, m_LastWindowY, m_LastWindowWidth, m_LastWindowHeight);
            }

            public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
            {
                if (_mDebuggerComponent == null)
                {
                    return;
                }

                if (!Mathf.Approximately(m_LastIconX, _mDebuggerComponent.IconRect.x))
                {
                    m_LastIconX = _mDebuggerComponent.IconRect.x;
                    Utility.PlayerPrefsX.SetFloat("Debugger.Icon.X", _mDebuggerComponent.IconRect.x);
                }

                if (!Mathf.Approximately(m_LastIconY, _mDebuggerComponent.IconRect.y))
                {
                    m_LastIconY = _mDebuggerComponent.IconRect.y;
                    Utility.PlayerPrefsX.SetFloat("Debugger.Icon.Y", _mDebuggerComponent.IconRect.y);
                }

                if (!Mathf.Approximately(m_LastWindowX, _mDebuggerComponent.WindowRect.x))
                {
                    m_LastWindowX = _mDebuggerComponent.WindowRect.x;
                    Utility.PlayerPrefsX.SetFloat("Debugger.Window.X", _mDebuggerComponent.WindowRect.x);
                }

                if (!Mathf.Approximately(m_LastWindowY, _mDebuggerComponent.WindowRect.y))
                {
                    m_LastWindowY = _mDebuggerComponent.WindowRect.y;
                    Utility.PlayerPrefsX.SetFloat("Debugger.Window.Y", _mDebuggerComponent.WindowRect.y);
                }

                if (!Mathf.Approximately(m_LastWindowWidth, _mDebuggerComponent.WindowRect.width))
                {
                    m_LastWindowWidth = _mDebuggerComponent.WindowRect.width;
                    Utility.PlayerPrefsX.SetFloat("Debugger.Window.Width", _mDebuggerComponent.WindowRect.width);
                }

                if (!Mathf.Approximately(m_LastWindowHeight, _mDebuggerComponent.WindowRect.height))
                {
                    m_LastWindowHeight = _mDebuggerComponent.WindowRect.height;
                    Utility.PlayerPrefsX.SetFloat("Debugger.Window.Height", _mDebuggerComponent.WindowRect.height);
                }

                if (!Mathf.Approximately(m_LastWindowScale, _mDebuggerComponent.WindowScale))
                {
                    m_LastWindowScale = _mDebuggerComponent.WindowScale;
                    Utility.PlayerPrefsX.SetFloat("Debugger.Window.Scale", _mDebuggerComponent.WindowScale);
                }

                if (m_LastEnableFloatingToggleSnap != _mDebuggerComponent.EnableFloatingToggleSnap)
                {
                    m_LastEnableFloatingToggleSnap = _mDebuggerComponent.EnableFloatingToggleSnap;
                    Utility.PlayerPrefsX.SetBool("Debugger.Icon.Snap", _mDebuggerComponent.EnableFloatingToggleSnap);
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
                }));
                layoutCard.Add(CreateRangeControl("Height", _mDebuggerComponent.WindowRect.height, MinWindowHeight, Screen.height - 32f, value =>
                {
                    _mDebuggerComponent.WindowRect = new Rect(_mDebuggerComponent.WindowRect.x, _mDebuggerComponent.WindowRect.y, _mDebuggerComponent.WindowRect.width, value);
                }));
                layoutCard.Add(CreateRangeControl("Scale", _mDebuggerComponent.WindowScale, MinWindowScale, MaxWindowScale, value =>
                {
                    _mDebuggerComponent.WindowScale = value;
                }, 0.01f));
                root.Add(layoutSection);

                VisualElement presets = CreateSection("Scale Presets", out VisualElement presetCard);
                VisualElement row = CreateToolbarRow();
                AddScaleButton(row, "0.5x", 0.5f);
                AddScaleButton(row, "1.0x", 1f);
                AddScaleButton(row, "1.5x", 1.5f);
                AddScaleButton(row, "2.0x", 2f);
                AddScaleButton(row, "2.5x", 2.5f);
                AddScaleButton(row, "3.0x", 3f);
                AddScaleButton(row, "3.5x", 3.5f);
                AddScaleButton(row, "4.0x", 4f);
                presetCard.Add(row);
                root.Add(presets);

                VisualElement actions = CreateSection("Actions", out VisualElement actionCard);
                actionCard.Add(CreateActionButton("Reset Layout", _mDebuggerComponent.ResetLayout, DebuggerTheme.Danger));
                root.Add(actions);
            }

            private VisualElement CreateRangeControl(string title, float value, float min, float max, System.Action<float> onChanged, float step = 1f)
            {
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Column;
                row.style.marginBottom = 8f;

                Label titleLabel = new Label(Utility.Text.Format("{0}: {1:F2}", title, value));
                titleLabel.style.color = DebuggerTheme.PrimaryText;
                titleLabel.style.marginBottom = 4f;
                row.Add(titleLabel);

                VisualElement controls = CreateToolbarRow();
                Button decreaseButton = CreateActionButton("-", () => onChanged(Mathf.Clamp(value - step, min, max)), DebuggerTheme.ButtonSurface);
                decreaseButton.style.marginRight = 8f;
                controls.Add(decreaseButton);
                Slider slider = CreateSlider(min, max, value, onChanged);
                slider.style.marginRight = 8f;
                controls.Add(slider);
                controls.Add(CreateActionButton("+", () => onChanged(Mathf.Clamp(value + step, min, max)), DebuggerTheme.ButtonSurface));
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
