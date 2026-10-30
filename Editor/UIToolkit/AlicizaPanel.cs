using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// A titled panel with a fixed-height header and a padded body.
    /// </summary>
    public sealed class AlicizaPanel : VisualElement
    {
        /// <summary>
        /// The panel header element. Add custom controls here when a title alone is not enough.
        /// </summary>
        public VisualElement Header { get; }

        /// <summary>
        /// The panel body. Add rows and sections here.
        /// </summary>
        public VisualElement Body { get; }

        /// <summary>
        /// Creates a panel with the supplied header title.
        /// </summary>
        public AlicizaPanel(string title)
        {
            AddToClassList("alicia-panel");

            Header = new VisualElement { name = "alicia-panel-header" };
            Header.AddToClassList("alicia-panel__header");

            var accent = new VisualElement { name = "alicia-panel-accent", pickingMode = PickingMode.Ignore };
            accent.AddToClassList("alicia-panel__accent");
            Header.Add(accent);

            var titleLabel = new Label(title ?? string.Empty) { name = "alicia-panel-title" };
            titleLabel.AddToClassList("alicia-panel__title");
            Header.Add(titleLabel);

            Body = new VisualElement { name = "alicia-panel-body" };
            Body.AddToClassList("alicia-panel__body");

            Add(Header);
            Add(Body);
        }
    }
}
