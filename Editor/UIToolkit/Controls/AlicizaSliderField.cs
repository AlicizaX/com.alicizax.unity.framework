using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaSliderField : BaseField<float>
    {
        private sealed class SliderSurface : VisualElement
        {
            public readonly Slider Slider;
            public readonly FloatField ValueField;

            public SliderSurface(float min, float max, float valueFieldWidth)
            {
                AddToClassList("alicia-slider");

                Slider = new Slider(min, max) { label = string.Empty };
                Slider.AddToClassList("alicia-slider__track");
                Slider.showInputField = false;
                Slider.style.flexShrink = 1f;
                Slider.style.minWidth = 80f;

                ValueField = new FloatField { label = string.Empty };
                ValueField.AddToClassList("alicia-slider__value");
                ValueField.style.width = valueFieldWidth;
                ValueField.style.minWidth = valueFieldWidth;
                ValueField.style.flexShrink = 0f;

                Add(Slider);
                Add(ValueField);
            }
        }

        private readonly SliderSurface _surface;
        private readonly float _min;
        private readonly float _max;

        public AlicizaSliderField(float min, float max, float valueFieldWidth = 46f)
            : base(null, new SliderSurface(min, max, valueFieldWidth))
        {
            AddToClassList("alicia-slider-field");
            style.flexShrink = 1f;
            style.minWidth = 0f;
            _min = min;
            _max = max;
            _surface = this.Q<SliderSurface>();

            _surface.Slider.RegisterValueChangedCallback(evt => SetValue(evt.newValue));
            _surface.ValueField.RegisterValueChangedCallback(evt => SetValue(evt.newValue));
        }

        public override void SetValueWithoutNotify(float newValue)
        {
            base.SetValueWithoutNotify(newValue);
            SyncControls(newValue);
        }

        private void SetValue(float newValue)
        {
            float clamped = Mathf.Clamp(newValue, _min, _max);
            if (Mathf.Approximately(clamped, value))
            {
                if (!Mathf.Approximately(newValue, clamped))
                {
                    SyncControls(clamped);
                }

                return;
            }

            value = clamped;
        }

        private void SyncControls(float newValue)
        {
            if (_surface == null)
            {
                return;
            }

            _surface.Slider.SetValueWithoutNotify(newValue);
            _surface.ValueField.SetValueWithoutNotify(newValue);
        }
    }
}
