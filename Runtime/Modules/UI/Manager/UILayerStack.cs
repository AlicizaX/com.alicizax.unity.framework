using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed class UILayerStack
    {
        private readonly int _layer;
        private UIWindowRecord _bottom;
        private int _nextOrder;

        internal UILayerStack(int layer) => _layer = layer;

        internal int Count { get; private set; }
        internal UIWindowRecord Top { get; private set; }
        internal UIWindowRecord Bottom => _bottom;

        internal void Push(UIWindowRecord record)
        {
            if (_nextOrder >= UIService.WindowsPerLayerLimit) Renumber();
            record.StackBelow = Top;
            if (Top == null) _bottom = record;
            else Top.StackAbove = record;
            Top = record;
            Count++;
            Debug.Assert(Count <= UIService.WindowsPerLayerLimit,
                "[UI] Too many windows in one layer: canvas sorting order overlaps the next layer.");
            AssignOrder(record, _nextOrder++);
        }

        internal void Remove(UIWindowRecord record)
        {
            UIWindowRecord below = record.StackBelow;
            UIWindowRecord above = record.StackAbove;
            if (below == null) _bottom = above;
            else below.StackAbove = above;
            if (above == null)
            {
                Top = below;
                _nextOrder = below == null ? 0 : below.Order + 1;
            }
            else above.StackBelow = below;
            record.StackBelow = null;
            record.StackAbove = null;
            record.Order = -1;
            Count--;
        }

        private void Renumber()
        {
            _nextOrder = 0;
            for (UIWindowRecord record = _bottom; record != null; record = record.StackAbove)
                AssignOrder(record, _nextOrder++);
        }

        private void AssignOrder(UIWindowRecord record, int order)
        {
            record.Order = order;
            record.View.Depth = _layer * UIService.LAYER_DEEP + order * UIService.WINDOW_DEEP;
        }
    }
}
