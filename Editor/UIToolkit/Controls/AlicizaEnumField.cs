using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// A flat enum dropdown with a dedicated arrow area, matching the original IMGUI popup look while still
    /// supporting <see cref="UnityEditor.UIElements.BindingExtensions.BindProperty"/>.
    /// </summary>
    public sealed class AlicizaEnumField : BaseField<Enum>
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

        private readonly Enum[] _values;
        private readonly string[] _names;
        private readonly DropdownSurface _surface;
        private readonly SerializedProperty _property;

        /// <summary>
        /// Creates a dropdown for <paramref name="enumType"/> with the supplied initial value.
        /// </summary>
        public AlicizaEnumField(Type enumType, Enum value) : base(null, new DropdownSurface())
        {
            if (enumType == null || !enumType.IsEnum)
            {
                throw new ArgumentException("A valid enum type is required.", nameof(enumType));
            }

            _property = null;
            Array values = Enum.GetValues(enumType);
            _values = new Enum[values.Length];
            _names = Enum.GetNames(enumType);
            for (int i = 0; i < values.Length; i++)
            {
                _values[i] = (Enum)values.GetValue(i);
            }

            AddToClassList("alicia-enum");
            _surface = this.Q<DropdownSurface>();

            _surface.RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            focusable = true;
            tabIndex = 0;

            SetValueWithoutNotify(value ?? (_values.Length > 0 ? _values[0] : null));
        }

        /// <summary>
        /// Creates a dropdown bound directly to an enum SerializedProperty. This overload avoids generic
        /// EnumField binding differences and keeps the displayed value in sync with Undo/Redo.
        /// </summary>
        public AlicizaEnumField(Type enumType, SerializedProperty property)
            : this(enumType, GetInitialValue(enumType, property))
        {
            _property = property;
            if (_property != null)
            {
                schedule.Execute(SyncFromProperty).Every(200);
                SyncFromProperty();
            }
        }

        private static Enum GetInitialValue(Type enumType, SerializedProperty property)
        {
            if (property == null)
            {
                return null;
            }

            Array values = Enum.GetValues(enumType);
            if (values.Length == 0)
            {
                return null;
            }

            int index = Mathf.Clamp(property.enumValueIndex, 0, values.Length - 1);
            return (Enum)values.GetValue(index);
        }

        /// <inheritdoc />
        public override void SetValueWithoutNotify(Enum newValue)
        {
            base.SetValueWithoutNotify(newValue);
            UpdateLabel();
        }

        /// <inheritdoc />
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

            int index = value == null ? -1 : Array.IndexOf(_values, value);
            _surface.Value.text = index >= 0 ? _names[index] : "—";
            tooltip = index >= 0 ? _names[index] : string.Empty;
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
            if (evt.keyCode != UnityEngine.KeyCode.Space &&
                evt.keyCode != UnityEngine.KeyCode.Return &&
                evt.keyCode != UnityEngine.KeyCode.KeypadEnter)
            {
                return;
            }

            ShowMenu();
            evt.StopPropagation();
        }

        private void SyncFromProperty()
        {
            if (_property == null || _values.Length == 0)
            {
                return;
            }

            if (_property.hasMultipleDifferentValues)
            {
                _surface.Value.text = "—";
                return;
            }

            int index = Mathf.Clamp(_property.enumValueIndex, 0, _values.Length - 1);
            if (!Equals(value, _values[index]))
            {
                SetValueWithoutNotify(_values[index]);
            }
        }

        private void ShowMenu()
        {
            if (_values.Length == 0)
            {
                return;
            }

            var menu = new GenericMenu();
            for (int i = 0; i < _values.Length; i++)
            {
                int index = i;
                menu.AddItem(
                    new GUIContent(_names[i]),
                    Equals(value, _values[i]),
                    () => SelectValue(index));
            }

            UnityEngine.Rect rect = worldBound;
            menu.DropDown(new UnityEngine.Rect(rect.x, rect.yMax, rect.width, 1f));
        }

        private void SelectValue(int index)
        {
            if (_property != null)
            {
                _property.enumValueIndex = index;
                _property.serializedObject.ApplyModifiedProperties();
            }

            SetValueWithoutNotify(_values[index]);
        }
    }
}
