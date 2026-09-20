using System;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    public static partial class EventBus
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe<T>(Action handler) where T : struct, IEmptyEventArgs
            => EventContainer<T, Action>.Subscribe(handler);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe<T>(InEventHandler<T> handler) where T : struct, IPayloadEventArgs
            => EventContainer<T, InEventHandler<T>>.Subscribe(handler);

        public static int GetPayloadSubscriberCount<T>() where T : struct, IPayloadEventArgs
            => EventContainer<T, InEventHandler<T>>.SubscriberCount;

        public static int GetEmptySubscriberCount<T>() where T : struct, IEmptyEventArgs
            => EventContainer<T, Action>.SubscriberCount;

        public static void ReservePayloadCapacity<T>(int capacity) where T : struct, IPayloadEventArgs
            => EventContainer<T, InEventHandler<T>>.ReserveCapacity(capacity);

        public static void ReserveEmptyCapacity<T>(int capacity) where T : struct, IEmptyEventArgs
            => EventContainer<T, Action>.ReserveCapacity(capacity);

        public static void ClearPayload<T>() where T : struct, IPayloadEventArgs
            => EventContainer<T, InEventHandler<T>>.Clear();

        public static void ClearEmpty<T>() where T : struct, IEmptyEventArgs
            => EventContainer<T, Action>.Clear();
    }
}
