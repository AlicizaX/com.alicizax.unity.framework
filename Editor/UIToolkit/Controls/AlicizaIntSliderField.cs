using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaIntSliderField : BaseField<int>
    {
        private sealed class SliderSurface : VisualElement
        {
            public readonly SliderInt Slider;
            public readonly IntegerField ValueField;

            public SliderSurface(int min, int max, float valueFieldWidth)
            {
                AddToClassList("alicia-slider");

                Slider = new SliderInt(min, max) { label = string.Empty };
                Slider.AddToClassList("alicia-slider__track");
                Slider.showInputField = false;
                Slider.style.flexShrink = 1f;
                Slider.style.minWidth = 80f;

                ValueField = new IntegerField { label = string.Empty };
                ValueField.AddToClassList("alicia-slider__value");
                ValueField.style.width = valueFieldWidth;
                ValueField.style.minWidth = valueFieldWidth;
                ValueField.style.flexShrink = 0f;

                Add(Slider);
                Add(ValueField);
            }
        }

        private readonly SliderSurface _surface;
        private readonly int _min;
        private readonly int _max;

        public AlicizaIntSliderField(int min, int max, float valueFieldWidth = 58f)
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

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(newValue);
            SyncControls(newValue);
        }

        private void SetValue(int newValue)
        {
            int clamped = Mathf.Clamp(newValue, _min, _max);
            if (clamped == value)
            {
                if (newValue != clamped)
                {
                    SyncControls(clamped);
                }

                return;
            }

            value = clamped;
        }

        private void SyncControls(int newValue)
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
