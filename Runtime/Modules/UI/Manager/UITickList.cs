namespace AlicizaX.UI.Runtime
{
    internal sealed class UITickNode
    {
        internal readonly UIBase View;
        internal UITickNode Previous;
        internal UITickNode Next;
        internal uint LinkedTick;

        internal UITickNode(UIBase view) => View = view;
    }

    internal sealed class UITickList
    {
        private UITickNode _first;
        private UITickNode _last;
        private UITickNode _cursor;

        internal int Count { get; private set; }
        internal UITickNode First => _first;

        internal void Link(UITickNode node, uint tick)
        {
            node.LinkedTick = tick;
            node.Previous = _last;
            if (_last == null) _first = node;
            else _last.Next = node;
            _last = node;
            Count++;
        }

        internal void Unlink(UITickNode node)
        {
            if (_cursor == node) _cursor = node.Next;
            if (node.Previous == null) _first = node.Next;
            else node.Previous.Next = node.Next;
            if (node.Next == null) _last = node.Previous;
            else node.Next.Previous = node.Previous;
            node.Previous = null;
            node.Next = null;
            Count--;
        }

        internal void BeginTraversal() => _cursor = _first;

        internal UITickNode MoveNext(uint tick)
        {
            while (_cursor != null)
            {
                UITickNode node = _cursor;
                _cursor = node.Next;
                if (node.LinkedTick != tick) return node;
            }
            return null;
        }
    }
}
