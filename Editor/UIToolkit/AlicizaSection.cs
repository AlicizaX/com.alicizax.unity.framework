using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaSection : VisualElement
    {

        public VisualElement Header { get; }

        public VisualElement Body { get; }

        public AlicizaSection(string title)
        {
            AddToClassList("alicia-section");

            Header = new VisualElement { name = "alicia-section-header" };
            Header.AddToClassList("alicia-section__header");

            var titleLabel = new Label(title ?? string.Empty);
            titleLabel.AddToClassList("alicia-section__title");
            Header.Add(titleLabel);

            Body = new VisualElement { name = "alicia-section-body" };
            Body.AddToClassList("alicia-section__content");

            Add(Header);
            Add(Body);
        }
    }
}
