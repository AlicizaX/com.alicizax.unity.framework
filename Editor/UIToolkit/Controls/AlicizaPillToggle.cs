using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaPillToggle : BaseField<bool>
    {
        private sealed class PillSurface : VisualElement
        {
            public readonly Label Text;

            public PillSurface()
            {
                AddToClassList("alicia-pill__surface");

                Text = new Label { pickingMode = PickingMode.Ignore };
                Text.AddToClassList("alicia-pill__text");
                Add(Text);
            }
        }

        private readonly PillSurface _surface;

        public AlicizaPillToggle() : this(null)
        {
        }

        public AlicizaPillToggle(string label) : base(label, new PillSurface())
        {
            AddToClassList("alicia-pill");
            _surface = this.Q<PillSurface>();

            focusable = true;
            tabIndex = 0;

            _surface.RegisterCallback<ClickEvent>(OnSurfaceClick);
            RegisterCallback<KeyDownEvent>(OnKeyDown);

            UpdateVisual();
        }

        public override void SetValueWithoutNotify(bool newValue)
        {
            base.SetValueWithoutNotify(newValue);
            UpdateVisual();
        }

        private void OnSurfaceClick(ClickEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            Focus();
            value = !value;
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Space &&
                evt.keyCode != KeyCode.Return &&
                evt.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }

            value = !value;
            evt.StopPropagation();
        }

        private void UpdateVisual()
        {
            if (_surface == null)
            {
                return;
            }

            bool isOn = value;
            EnableInClassList("alicia-pill--on", isOn);
            EnableInClassList("alicia-pill--off", !isOn);
            _surface.Text.text = isOn ? "Enabled" : "Disabled";
            tooltip = isOn ? "Click to disable" : "Click to enable";
        }
    }
}
