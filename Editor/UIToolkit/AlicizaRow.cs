using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaRow : VisualElement
    {

        public Label Caption { get; }

        public VisualElement Content { get; }

        public AlicizaRow(string caption)
        {
            AddToClassList("alicia-row");

            Caption = new Label(caption ?? string.Empty) { name = "alicia-row-caption" };
            Caption.AddToClassList("alicia-row__caption");
            Caption.tooltip = caption ?? string.Empty;

            Content = new VisualElement { name = "alicia-row-content" };
            Content.AddToClassList("alicia-row__content");

            Add(Caption);
            Add(Content);
        }
    }
}
