using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaEnumField : BaseField<int>
    {
        private sealed class DropdownSurface : VisualElement
        {
            public readonly Label Value;
            public readonly Label Arrow;

            public DropdownSurface()
            {
                AddToClassList("alicia-enum__surface");

                Value = new Label { pickingMode = PickingMode.Ignore };
                Value.AddToClassList("alicia-enum__value");
                Add(Value);

                var arrowArea = new VisualElement { pickingMode = PickingMode.Ignore };
                arrowArea.AddToClassList("alicia-enum__arrow-area");

                Arrow = new Label("▼") { pickingMode = PickingMode.Ignore };
                Arrow.AddToClassList("alicia-enum__arrow");
                arrowArea.Add(Arrow);
                Add(arrowArea);
            }
        }

        private readonly string[] _names;
        private readonly DropdownSurface _surface;
        private readonly SerializedProperty _property;

        public AlicizaEnumField(string[] names, int index) : base(null, new DropdownSurface())
        {
            _names = names == null ? Array.Empty<string>() : (string[])names.Clone();
            _property = null;

            AddToClassList("alicia-enum");
            _surface = this.Q<DropdownSurface>();

            _surface.RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            focusable = true;
            tabIndex = 0;

            SetValueWithoutNotify(index);
        }

        public AlicizaEnumField(SerializedProperty property)
            : this(property == null ? Array.Empty<string>() : property.enumNames, GetInitialIndex(property))
        {
            _property = property;
            if (_property != null)
            {
                schedule.Execute(SyncFromProperty).Every(200);
                SyncFromProperty();
            }
        }

        private static int GetInitialIndex(SerializedProperty property)
        {
            if (property == null || property.enumNames == null || property.enumNames.Length == 0)
            {
                return 0;
            }

            return Mathf.Clamp(property.enumValueIndex, 0, property.enumNames.Length - 1);
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            int length = _names == null ? 0 : _names.Length;
            int clamped = length == 0 ? 0 : Mathf.Clamp(newValue, 0, length - 1);
            base.SetValueWithoutNotify(clamped);
            UpdateLabel();
        }

        protected override void UpdateMixedValueContent()
        {
            base.UpdateMixedValueContent();
            if (_surface == null)
            {
                return;
            }

            if (showMixedValue)
            {
                _surface.Value.text = "—";
            }
            else
            {
                UpdateLabel();
            }
        }

        private void UpdateLabel()
        {
            if (_surface == null)
            {
                return;
            }

            bool valid = value >= 0 && value < _names.Length;
            _surface.Value.text = valid ? _names[value] : "—";
            tooltip = valid ? _names[value] : string.Empty;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            Focus();
            ShowMenu();
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

            ShowMenu();
            evt.StopPropagation();
        }

        private void SyncFromProperty()
        {
            if (_property == null || _names.Length == 0)
            {
                return;
            }

            if (_property.hasMultipleDifferentValues)
            {
                _surface.Value.text = "—";
                return;
            }

            int index = Mathf.Clamp(_property.enumValueIndex, 0, _names.Length - 1);
            if (value != index)
            {
                SetValueWithoutNotify(index);
            }
        }

        private void ShowMenu()
        {
            if (_names.Length == 0)
            {
                return;
            }

            var menu = new GenericMenu();
            for (int i = 0; i < _names.Length; i++)
            {
                int index = i;
                menu.AddItem(
                    new GUIContent(_names[i]),
                    value == index,
                    () => SelectValue(index));
            }

            Rect rect = worldBound;
            menu.DropDown(new Rect(rect.x, rect.yMax, rect.width, 1f));
        }

        private void SelectValue(int index)
        {
            if (_property != null)
            {
                _property.enumValueIndex = index;
                _property.serializedObject.ApplyModifiedProperties();
            }

            value = index;
        }
    }
}
