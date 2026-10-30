using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaProgressBar : VisualElement
    {
        private readonly VisualElement _fill;
        private readonly Label _label;

        public AlicizaProgressBar()
        {
            AddToClassList("alicia-progress");

            var track = new VisualElement { name = "alicia-progress-track" };
            track.AddToClassList("alicia-progress__track");

            _fill = new VisualElement { name = "alicia-progress-fill", pickingMode = PickingMode.Ignore };
            _fill.AddToClassList("alicia-progress__fill");
            track.Add(_fill);

            _label = new Label();
            _label.AddToClassList("alicia-progress__label");

            Add(track);
            Add(_label);
        }

        public void SetValue(float ratio, string text = null)
        {
            float clamped = Mathf.Clamp01(ratio);
            _fill.style.width = Length.Percent(clamped * 100f);
            _label.text = text ?? clamped.ToString("P1");
        }
    }
}
