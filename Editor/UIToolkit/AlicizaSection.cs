using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// A non-collapsible inspector section with a static header.
    /// </summary>
    public sealed class AlicizaSection : VisualElement
    {
        /// <summary>
        /// The section header.
        /// </summary>
        public VisualElement Header { get; }

        /// <summary>
        /// The section content.
        /// </summary>
        public VisualElement Body { get; }

        /// <summary>
        /// Creates a static section with the supplied title.
        /// </summary>
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
