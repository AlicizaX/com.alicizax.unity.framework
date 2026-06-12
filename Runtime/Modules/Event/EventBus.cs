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
        public static EventRuntimeHandle Subscribe<T>(Action handler) where T : struct, IEventArgs
        {
            return EmptyEventContainer<T>.Subscribe(handler);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe<T>(InEventHandler<T> handler) where T : struct, IEventArgs
        {
            return EventContainer<T>.Subscribe(handler);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish<T>(in T evt) where T : struct, IEventArgs
        {
            EventContainer<T>.Publish(in evt);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish<T>() where T : struct, IEventArgs
        {
            EmptyEventContainer<T>.Publish();
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SafePublish<T>(in T evt) where T : struct, IEventArgs
        {
            EventContainer<T>.SafePublish(in evt);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SafePublish<T>() where T : struct, IEventArgs
        {
            EmptyEventContainer<T>.SafePublish();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSubscriberCount<T>() where T : struct, IEventArgs
        {
            return EventContainer<T>.SubscriberCount + EmptyEventContainer<T>.SubscriberCount;
        }

        public static void EnsureCapacity<T>(int capacity) where T : struct, IEventArgs
        {
            EventContainer<T>.EnsureCapacity(capacity);
            EmptyEventContainer<T>.EnsureCapacity(capacity);
        }

        public static void Clear<T>() where T : struct, IEventArgs
        {
            EventContainer<T>.Clear();
            EmptyEventContainer<T>.Clear();
        }
    }
}
