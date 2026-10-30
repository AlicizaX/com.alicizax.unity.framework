using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// A compact editor for a string array. It supports add, remove, and undo through the owning SerializedObject.
    /// </summary>
    public sealed class AlicizaStringListField : VisualElement
    {
        private readonly SerializedProperty _arrayProperty;
        private readonly VisualElement _items;
        private readonly Label _countLabel;
        private readonly Button _addButton;
        private bool _rebuilding;
        private int _itemCount;
        private int _focusIndex = -1;

        /// <summary>
        /// Creates a string list bound to <paramref name="arrayProperty"/>.
        /// </summary>
        public AlicizaStringListField(SerializedProperty arrayProperty) : this(arrayProperty, null)
        {
        }

        /// <summary>
        /// Creates a string list with an overline header.
        /// </summary>
        public AlicizaStringListField(SerializedProperty arrayProperty, string title)
        {
            _arrayProperty = arrayProperty;
            AddToClassList("alicia-string-list");

            var header = new VisualElement { name = "alicia-string-list-header" };
            header.AddToClassList("alicia-string-list__header");

            var titleLabel = new Label(string.IsNullOrEmpty(title) ? "ITEMS" : title.ToUpperInvariant());
            titleLabel.AddToClassList("alicia-string-list__title");
            header.Add(titleLabel);

            _countLabel = new Label { name = "alicia-string-list-count" };
            _countLabel.AddToClassList("alicia-string-list__count");
            header.Add(_countLabel);

            _addButton = new Button(AddItem)
            {
                name = "alicia-string-list-add",
                text = "+ Add",
                tooltip = "Add a command assembly"
            };
            _addButton.AddToClassList("alicia-string-list__add");
            header.Add(_addButton);
            Add(header);

            _items = new VisualElement { name = "alicia-string-list-items" };
            _items.AddToClassList("alicia-string-list__items");
            Add(_items);

            if (_arrayProperty != null)
            {
                this.TrackPropertyValue(_arrayProperty, OnArrayPropertyChanged);
            }

            Rebuild();
        }

        private void OnArrayPropertyChanged(SerializedProperty property)
        {
            if (_rebuilding || property == null)
            {
                return;
            }

            if (property.serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            if (property.arraySize != _itemCount)
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _rebuilding = true;
            _items.Clear();
            _itemCount = 0;
            _countLabel.text = "—";
            _countLabel.tooltip = string.Empty;
            _addButton.SetEnabled(false);

            if (_arrayProperty == null)
            {
                _items.Add(AlicizaEditorUI.CreateHint("Array property not found."));
                _rebuilding = false;
                return;
            }

            SerializedObject serializedObject = _arrayProperty.serializedObject;
            if (serializedObject.isEditingMultipleObjects)
            {
                _items.Add(AlicizaEditorUI.CreateHint("Multi-object editing is not supported."));
                _rebuilding = false;
                return;
            }

            serializedObject.Update();
            int size = _arrayProperty.arraySize;
            _countLabel.text = size.ToString();
            _countLabel.tooltip = size == 1 ? "1 assembly" : $"{size} assemblies";
            _addButton.SetEnabled(true);

            if (size == 0)
            {
                _items.Add(CreateEmptyState());
                _rebuilding = false;
                return;
            }

            for (int i = 0; i < size; i++)
            {
                _items.Add(CreateItem(i));
                _itemCount++;
            }

            _rebuilding = false;
        }

        private VisualElement CreateItem(int index)
        {
            var row = new VisualElement { name = $"alicia-string-list-item-{index}" };
            row.AddToClassList("alicia-string-list__item");
            if (index == _arrayProperty.arraySize - 1)
            {
                row.AddToClassList("alicia-string-list__item--last");
            }

            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexWrap = Wrap.NoWrap;

            var indexLabel = new Label((index + 1).ToString("00"));
            indexLabel.AddToClassList("alicia-string-list__index");

            var field = new TextField { label = string.Empty };
            field.AddToClassList("alicia-string-list__field");
            field.style.width = Length.Percent(100f);
            field.style.minWidth = 0f;
            field.style.marginRight = 0f;
            field.SetValueWithoutNotify(_arrayProperty.GetArrayElementAtIndex(index).stringValue);

            var fieldHost = new VisualElement { name = "alicia-string-list-field-host" };
            fieldHost.AddToClassList("alicia-string-list__field-host");
            fieldHost.style.width = 0f;
            fieldHost.style.flexGrow = 1f;
            fieldHost.style.flexShrink = 1f;
            fieldHost.style.minWidth = 0f;
            fieldHost.Add(field);

            int capturedIndex = index;
            field.RegisterValueChangedCallback(evt =>
            {
                if (_rebuilding)
                {
                    return;
                }

                SerializedProperty property = _arrayProperty.GetArrayElementAtIndex(capturedIndex);
                property.stringValue = evt.newValue;
                _arrayProperty.serializedObject.ApplyModifiedProperties();
            });

            var removeButton = new Button(() => RemoveItem(capturedIndex))
            {
                text = "×",
                tooltip = "Remove entry",
                focusable = false
            };
            removeButton.AddToClassList("alicia-string-list__remove");

            if (_focusIndex == index)
            {
                _focusIndex = -1;
                field.schedule.Execute(() => field.Focus()).StartingIn(0);
            }

            row.Add(indexLabel);
            row.Add(fieldHost);
            row.Add(removeButton);
            return row;
        }

        private static VisualElement CreateEmptyState()
        {
            var empty = new VisualElement { name = "alicia-string-list-empty" };
            empty.AddToClassList("alicia-string-list__empty");

            var title = new Label("No command assemblies yet");
            title.AddToClassList("alicia-string-list__empty-title");
            empty.Add(title);

            var hint = new Label("Add an assembly to expose its console commands.");
            hint.AddToClassList("alicia-string-list__empty-hint");
            empty.Add(hint);

            return empty;
        }

        private void AddItem()
        {
            if (_arrayProperty == null || _arrayProperty.serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            SerializedObject serializedObject = _arrayProperty.serializedObject;
            _rebuilding = true;
            serializedObject.Update();

            int index = _arrayProperty.arraySize;
            _arrayProperty.InsertArrayElementAtIndex(index);
            _arrayProperty.GetArrayElementAtIndex(index).stringValue = string.Empty;
            serializedObject.ApplyModifiedProperties();

            _rebuilding = false;
            _focusIndex = index;
            Rebuild();
        }

        private void RemoveItem(int index)
        {
            if (_arrayProperty == null || _arrayProperty.serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            if (index < 0 || index >= _arrayProperty.arraySize)
            {
                return;
            }

            SerializedObject serializedObject = _arrayProperty.serializedObject;
            _rebuilding = true;
            serializedObject.Update();
            _arrayProperty.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();

            _rebuilding = false;
            Rebuild();
        }
    }
}
