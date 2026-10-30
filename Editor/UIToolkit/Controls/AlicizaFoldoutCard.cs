using System;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaFoldoutCard : VisualElement
    {
        private readonly Label _chevron;

        public VisualElement Header { get; }

        public VisualElement Body { get; }

        public VisualElement Actions { get; }

        public Label Title { get; }

        public Label Meta { get; }

        public bool Expanded { get; private set; }

        public event Action<bool> ExpandedChanged;

        public AlicizaFoldoutCard(string title, bool expanded = false)
        {
            AddToClassList("alicia-card");

            Header = new VisualElement { name = "alicia-card-header" };
            Header.AddToClassList("alicia-card__header");

            var toggleArea = new VisualElement { name = "alicia-card-toggle" };
            toggleArea.AddToClassList("alicia-card__toggle");

            _chevron = new Label { pickingMode = PickingMode.Ignore };
            _chevron.AddToClassList("alicia-card__chevron");
            toggleArea.Add(_chevron);

            Title = new Label(title ?? string.Empty) { pickingMode = PickingMode.Ignore };
            Title.AddToClassList("alicia-card__title");
            Title.tooltip = title ?? string.Empty;
            toggleArea.Add(Title);

            Meta = new Label { pickingMode = PickingMode.Ignore };
            Meta.AddToClassList("alicia-card__meta");
            toggleArea.Add(Meta);

            Header.Add(toggleArea);

            Actions = new VisualElement { name = "alicia-card-actions" };
            Actions.AddToClassList("alicia-card__actions");
            Header.Add(Actions);

            Body = new VisualElement { name = "alicia-card-body" };
            Body.AddToClassList("alicia-card__body");

            toggleArea.RegisterCallback<PointerDownEvent>(OnTogglePointerDown);

            Add(Header);
            Add(Body);

            SetExpanded(expanded, false);
        }

        public void SetExpanded(bool expanded, bool notify = true)
        {
            if (Expanded == expanded && notify)
            {
                return;
            }

            Expanded = expanded;
            _chevron.text = expanded ? "▾" : "▸";
            Body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("alicia-card--expanded", expanded);

            if (notify)
            {
                ExpandedChanged?.Invoke(expanded);
            }
        }

        private void OnTogglePointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            SetExpanded(!Expanded);
            evt.StopPropagation();
        }
    }
}
