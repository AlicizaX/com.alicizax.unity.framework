using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

namespace AlicizaX
{
    /// <summary>
    /// 游戏事件代理：UI 层订阅 -> Proxy 自动回收。
    /// </summary>
    public class EventListenerProxy : MemoryObject
    {
        private readonly List<EventRuntimeHandle> _eventHandles = new();


        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddUIEvent<T>(Action handler) where T : struct, IEventArgs
        {
            EventRuntimeHandle handle = EmptyEventContainer<T>.Subscribe(handler);
            _eventHandles.Add(handle);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddUIEvent<T>(InEventHandler<T> handler) where T : struct, IEventArgs
        {
            EventRuntimeHandle handle = EventContainer<T>.Subscribe(handler);
            _eventHandles.Add(handle);
        }


        public override void Clear()
        {
            for (int i = _eventHandles.Count - 1; i >= 0; i--)
            {
                _eventHandles[i].Dispose();
            }

            _eventHandles.Clear();
        }
    }
}
