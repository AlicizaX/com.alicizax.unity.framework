using System;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    internal static class UnsubscribeRegistry
    {
        private static Action<int, int>[] _handlers = new Action<int, int>[32];
        private static int _nextId = 1; // 0表示无效

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Register(Action<int, int> handler)
        {
            int id = _nextId++;
            if (id >= _handlers.Length)
            {
                Array.Resize(ref _handlers, _handlers.Length * 2);
            }

            _handlers[id] = handler;
            return id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Invoke(int id, int index, int version)
        {
            _handlers[id]?.Invoke(index, version);
        }
    }
}
