using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// A single inspector row with a fixed-width caption and a flexible content area.
    /// </summary>
    public sealed class AlicizaRow : VisualElement
    {
        /// <summary>
        /// The caption label on the left side of the row.
        /// </summary>
        public Label Caption { get; }

        /// <summary>
        /// The content area on the right side of the row.
        /// </summary>
        public VisualElement Content { get; }

        /// <summary>
        /// Creates a row with the supplied caption.
        /// </summary>
        public AlicizaRow(string caption)
        {
            AddToClassList("alicia-row");

            Caption = new Label(caption ?? string.Empty) { name = "alicia-row-caption" };
            Caption.AddToClassList("alicia-row__caption");

            Content = new VisualElement { name = "alicia-row-content" };
            Content.AddToClassList("alicia-row__content");

            Add(Caption);
            Add(Content);
        }
    }
}
