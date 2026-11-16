using System;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    public static class EventBus
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe<T>(Action handler) where T : struct, IEmptyEventArgs
            => EventContainer<T, Action>.Channel.Subscribe(handler);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe<T>(InEventHandler<T> handler) where T : struct, IPayloadEventArgs
            => EventContainer<T, InEventHandler<T>>.Channel.Subscribe(handler);

        public static void Publish<T>() where T : struct, IEmptyEventArgs
        {
            EventChannel<Action> channel = EventContainer<T, Action>.Channel;
            int count = channel.BeginPublish();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Action handler = channel.Handlers[i];
                    if (handler == null) continue;
                    try
                    {
                        handler();
                    }
                    catch (Exception exception)
                    {
                        channel.LogHandlerException(exception);
                    }
                }
            }
            finally
            {
                channel.EndPublish();
            }
        }

        public static void Publish<T>(in T payload) where T : struct, IPayloadEventArgs
        {
            EventChannel<InEventHandler<T>> channel = EventContainer<T, InEventHandler<T>>.Channel;
            int count = channel.BeginPublish();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    InEventHandler<T> handler = channel.Handlers[i];
                    if (handler == null) continue;
                    try
                    {
                        handler(in payload);
                    }
                    catch (Exception exception)
                    {
                        channel.LogHandlerException(exception);
                    }
                }
            }
            finally
            {
                channel.EndPublish();
            }
        }

        public static int GetPayloadSubscriberCount<T>() where T : struct, IPayloadEventArgs
            => EventContainer<T, InEventHandler<T>>.Channel.SubscriberCount;

        public static int GetEmptySubscriberCount<T>() where T : struct, IEmptyEventArgs
            => EventContainer<T, Action>.Channel.SubscriberCount;

        public static void ReservePayloadCapacity<T>(int capacity) where T : struct, IPayloadEventArgs
            => EventContainer<T, InEventHandler<T>>.Channel.Reserve(capacity);

        public static void ReserveEmptyCapacity<T>(int capacity) where T : struct, IEmptyEventArgs
            => EventContainer<T, Action>.Channel.Reserve(capacity);

        public static void ClearPayload<T>() where T : struct, IPayloadEventArgs
            => EventContainer<T, InEventHandler<T>>.Channel.Clear();

        public static void ClearEmpty<T>() where T : struct, IEmptyEventArgs
            => EventContainer<T, Action>.Channel.Clear();
    }
}
