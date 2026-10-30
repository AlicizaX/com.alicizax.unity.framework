using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public enum AlicizaValueTone
    {
        Default,
        Muted,
        Accent,
        Success,
        Warning,
        Danger
    }

    public enum AlicizaButtonTone
    {
        Neutral,
        Accent,
        Warning,
        Danger
    }

    public static class AlicizaEditorUI
    {

        public const float RowCaptionWidth = 140f;

        public static VisualElement CreateInspectorRoot()
        {
            var root = new VisualElement { name = "alicia-inspector-root" };
            AlicizaEditorTheme.Apply(root);
            return root;
        }

        public static AlicizaPanel CreatePanel(string title)
        {
            return new AlicizaPanel(title);
        }

        public static AlicizaRow CreateRow(string caption)
        {
            return new AlicizaRow(caption);
        }

        public static AlicizaRow AddRow(VisualElement parent, string caption, VisualElement content, bool alignTop = false)
        {
            var row = new AlicizaRow(caption);
            if (alignTop)
            {
                row.AddToClassList("alicia-row--top");
            }

            if (content != null)
            {
                row.Content.Add(content);
            }

            parent.Add(row);
            return row;
        }

        public static AlicizaRow AddRow(VisualElement parent, string caption)
        {
            return AddRow(parent, caption, null);
        }

        public static Foldout CreateSection(string title, bool expanded = true)
        {
            var section = new Foldout { text = title ?? string.Empty, value = expanded };
            section.AddToClassList("alicia-section");

            VisualElement header = section.Q(className: Foldout.toggleUssClassName);
            if (header != null)
            {
                header.AddToClassList("alicia-section__header");
            }

            section.contentContainer.AddToClassList("alicia-section__content");
            section.contentContainer.style.marginLeft = 0f;
            return section;
        }

        public static AlicizaSection CreateStaticSection(string title)
        {
            return new AlicizaSection(title);
        }

        public static Label CreateHint(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("alicia-hint");
            return label;
        }

        public static Button CreateIconButton(string text, string tooltip = null)
        {
            var button = new Button { text = text ?? string.Empty, tooltip = tooltip };
            button.AddToClassList("alicia-icon-button");
            return button;
        }

        public static Button CreateTextButton(
            string text,
            string tooltip = null,
            AlicizaButtonTone tone = AlicizaButtonTone.Neutral)
        {
            var button = new Button { text = text ?? string.Empty, tooltip = tooltip };
            button.AddToClassList("alicia-text-button");
            switch (tone)
            {
                case AlicizaButtonTone.Accent:
                    button.AddToClassList("alicia-button--accent");
                    break;
                case AlicizaButtonTone.Warning:
                    button.AddToClassList("alicia-button--warning");
                    break;
                case AlicizaButtonTone.Danger:
                    button.AddToClassList("alicia-button--danger");
                    break;
            }

            return button;
        }

        public static Label CreateValueLabel(string text, AlicizaValueTone tone = AlicizaValueTone.Default)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("alicia-value");
            label.AddToClassList(GetToneClass(tone));
            return label;
        }

        public static AlicizaRow AddValueRow(
            VisualElement parent,
            string caption,
            string value,
            AlicizaValueTone tone = AlicizaValueTone.Default)
        {
            var row = new AlicizaRow(caption);
            row.Content.Add(CreateValueLabel(value, tone));
            parent.Add(row);
            return row;
        }

        public static Label CreateBadge(string text, AlicizaValueTone tone = AlicizaValueTone.Muted)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("alicia-badge");
            label.AddToClassList(GetToneClass(tone));
            return label;
        }

        public static VisualElement CreateEmptyState(string message, bool center = false)
        {
            var empty = new VisualElement { name = "alicia-empty-state" };
            empty.AddToClassList("alicia-empty-state");
            if (center)
            {
                empty.AddToClassList("alicia-empty-state--center");
            }

            var label = new Label(message ?? string.Empty);
            label.AddToClassList("alicia-empty-state__label");
            empty.Add(label);
            return empty;
        }

        public static VisualElement CreateRuntimePlaceholder(string message)
        {
            VisualElement placeholder = CreateEmptyState(message, true);
            placeholder.AddToClassList("alicia-runtime-placeholder");
            return placeholder;
        }

        public static AlicizaProgressBar CreateProgressBar(float ratio, string text = null)
        {
            var progress = new AlicizaProgressBar();
            progress.SetValue(ratio, text);
            return progress;
        }

        public static AlicizaFoldoutCard CreateFoldoutCard(string title, bool expanded = false)
        {
            return new AlicizaFoldoutCard(title, expanded);
        }

        public static AlicizaRow AddPropertyRow(
            VisualElement parent,
            string caption,
            SerializedProperty property,
            bool grow = true,
            Type objectType = null,
            bool allowSceneObjects = false)
        {
            if (property == null || parent == null)
            {
                return null;
            }

            VisualElement control;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                {
                    var pill = new AlicizaPillToggle();
                    pill.BindProperty(property);
                    control = pill;
                    break;
                }
                case SerializedPropertyType.Enum:
                {
                    var enumField = new AlicizaEnumField(property);
                    control = enumField;
                    break;
                }
                case SerializedPropertyType.Integer:
                {
                    var intField = new IntegerField { label = string.Empty };
                    intField.AddToClassList("alicia-control");
                    intField.BindProperty(property);
                    control = intField;
                    break;
                }
                case SerializedPropertyType.Float:
                {
                    var floatField = new FloatField { label = string.Empty };
                    floatField.AddToClassList("alicia-control");
                    floatField.BindProperty(property);
                    control = floatField;
                    break;
                }
                case SerializedPropertyType.String:
                {
                    var textField = new TextField { label = string.Empty };
                    textField.AddToClassList("alicia-control");
                    textField.BindProperty(property);
                    control = textField;
                    break;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var objectField = new ObjectField { label = string.Empty };
                    objectField.objectType = objectType ?? typeof(UnityEngine.Object);
                    objectField.allowSceneObjects = allowSceneObjects;
                    objectField.AddToClassList("alicia-control");
                    objectField.BindProperty(property);
                    control = objectField;
                    break;
                }
                default:
                {
                    var propertyField = new PropertyField(property) { label = string.Empty };
                    propertyField.AddToClassList("alicia-control");
                    control = propertyField;
                    break;
                }
            }

            control.AddToClassList("alicia-control");
            if (grow)
            {
                control.AddToClassList("alicia-control--grow");
            }

            return AddRow(parent, caption, control);
        }

        public static void RefreshEvery(VisualElement element, Action refresh, long intervalMs = 200)
        {
            if (element == null || refresh == null)
            {
                return;
            }

            element.schedule.Execute(refresh).Every(intervalMs);
        }

        private static string GetToneClass(AlicizaValueTone tone)
        {
            switch (tone)
            {
                case AlicizaValueTone.Muted:
                    return "alicia-tone--muted";
                case AlicizaValueTone.Accent:
                    return "alicia-tone--accent";
                case AlicizaValueTone.Success:
                    return "alicia-tone--success";
                case AlicizaValueTone.Warning:
                    return "alicia-tone--warning";
                case AlicizaValueTone.Danger:
                    return "alicia-tone--danger";
                default:
                    return "alicia-tone--default";
            }
        }
    }
}
