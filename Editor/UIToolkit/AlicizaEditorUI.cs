using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// Factory helpers for building inspectors with the AlicizaX UI Toolkit theme.
    /// </summary>
    public static class AlicizaEditorUI
    {
        /// <summary>
        /// Width of the caption column used by <see cref="AlicizaRow"/>.
        /// </summary>
        public const float RowCaptionWidth = 132f;

        /// <summary>
        /// Creates a themed inspector root. The Inspector window handles scrolling.
        /// </summary>
        public static VisualElement CreateInspectorRoot()
        {
            var root = new VisualElement { name = "alicia-inspector-root" };
            AlicizaEditorTheme.Apply(root);
            return root;
        }

        /// <summary>
        /// Creates a titled panel.
        /// </summary>
        public static AlicizaPanel CreatePanel(string title)
        {
            return new AlicizaPanel(title);
        }

        /// <summary>
        /// Creates a caption/content row.
        /// </summary>
        public static AlicizaRow CreateRow(string caption)
        {
            return new AlicizaRow(caption);
        }

        /// <summary>
        /// Creates a caption/content row, adds <paramref name="content"/> to it, and appends the row to <paramref name="parent"/>.
        /// Set <paramref name="alignTop"/> for layouts whose content is taller than one line, such as list editors.
        /// </summary>
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

        /// <summary>
        /// Creates a foldout styled as an inspector section and returns it. The caller adds the section to a parent.
        /// </summary>
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

        /// <summary>
        /// Creates a non-collapsible section with a static header. Use this when the content should always be visible.
        /// </summary>
        public static AlicizaSection CreateStaticSection(string title)
        {
            return new AlicizaSection(title);
        }

        /// <summary>
        /// Creates a muted inline hint label.
        /// </summary>
        public static Label CreateHint(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("alicia-hint");
            return label;
        }

        /// <summary>
        /// Creates a compact square button used for add/remove actions.
        /// </summary>
        public static Button CreateIconButton(string text, string tooltip = null)
        {
            var button = new Button { text = text ?? string.Empty, tooltip = tooltip };
            button.AddToClassList("alicia-icon-button");
            return button;
        }

        /// <summary>
        /// Creates a compact outlined text button, used for secondary actions such as "Add".
        /// </summary>
        public static Button CreateTextButton(string text, string tooltip = null)
        {
            var button = new Button { text = text ?? string.Empty, tooltip = tooltip };
            button.AddToClassList("alicia-text-button");
            return button;
        }
    }
}
