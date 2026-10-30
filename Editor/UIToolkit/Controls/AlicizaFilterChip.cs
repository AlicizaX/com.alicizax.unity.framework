using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public sealed class AlicizaFilterChip : Button
    {

        public bool Selected { get; private set; }

        public AlicizaFilterChip(string text)
        {
            this.text = text ?? string.Empty;
            AddToClassList("alicia-chip");
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            EnableInClassList("alicia-chip--selected", selected);
        }
    }
}
