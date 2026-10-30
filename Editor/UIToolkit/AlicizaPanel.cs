using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaPanel : VisualElement
    {

        public VisualElement Header { get; }

        public VisualElement Body { get; }

        public AlicizaPanel(string title)
        {
            AddToClassList("alicia-panel");

            Body = new VisualElement { name = "alicia-panel-body" };
            Body.AddToClassList("alicia-panel__body");

            if (string.IsNullOrEmpty(title))
            {
                AddToClassList("alicia-panel--untitled");
                Header = null;
                Add(Body);
                return;
            }

            Header = new VisualElement { name = "alicia-panel-header" };
            Header.AddToClassList("alicia-panel__header");

            var accent = new VisualElement { name = "alicia-panel-accent", pickingMode = PickingMode.Ignore };
            accent.AddToClassList("alicia-panel__accent");
            Header.Add(accent);

            var titleLabel = new Label(title) { name = "alicia-panel-title" };
            titleLabel.AddToClassList("alicia-panel__title");
            Header.Add(titleLabel);

            Add(Header);
            Add(Body);
        }
    }
}
