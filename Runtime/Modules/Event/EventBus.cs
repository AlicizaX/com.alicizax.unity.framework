 using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

namespace AlicizaX
{
    public static class EventBus
    {
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe<T>(Action handler) where T : struct, IEmptyEventArgs
        {
            return EmptyEventContainer<T>.Subscribe(handler);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe<T>(InEventHandler<T> handler) where T : struct, IPayloadEventArgs
        {
            return EventContainer<T>.Subscribe(handler);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish<T>(in T evt) where T : struct, IPayloadEventArgs
        {
            EventContainer<T>.Publish(in evt);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish<T>() where T : struct, IEmptyEventArgs
        {
            EmptyEventContainer<T>.Publish();
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SafePublish<T>(in T evt) where T : struct, IPayloadEventArgs
        {
            EventContainer<T>.SafePublish(in evt);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SafePublish<T>() where T : struct, IEmptyEventArgs
        {
            EmptyEventContainer<T>.SafePublish();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPayloadSubscriberCount<T>() where T : struct, IPayloadEventArgs
        {
            return EventContainer<T>.SubscriberCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetEmptySubscriberCount<T>() where T : struct, IEmptyEventArgs
        {
            return EmptyEventContainer<T>.SubscriberCount;
        }

        public static void EnsurePayloadCapacity<T>(int capacity) where T : struct, IPayloadEventArgs
        {
            EventContainer<T>.EnsureCapacity(capacity);
        }

        public static void EnsureEmptyCapacity<T>(int capacity) where T : struct, IEmptyEventArgs
        {
            EmptyEventContainer<T>.EnsureCapacity(capacity);
        }

        public static void ClearPayload<T>() where T : struct, IPayloadEventArgs
        {
            EventContainer<T>.Clear();
        }

        public static void ClearEmpty<T>() where T : struct, IEmptyEventArgs
        {
            EmptyEventContainer<T>.Clear();
        }
    }
}
