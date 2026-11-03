using System;

namespace AlicizaX
{
    public class EventListenerProxy : MemoryObject
    {
        private const int InitialCapacity = 4;

        private delegate EventRuntimeHandle SubscribeHandler(Delegate handler);

        private struct Subscription
        {
            internal Delegate Handler;
            internal SubscribeHandler Subscribe;
            internal EventRuntimeHandle Handle;
        }

        private static class EmptySubscriber<T> where T : struct, IEmptyEventArgs
        {
            internal static readonly SubscribeHandler Subscribe = SubscribeToBus;
            private static EventRuntimeHandle SubscribeToBus(Delegate handler) => EventBus.Subscribe<T>((Action)handler);
        }

        private static class PayloadSubscriber<T> where T : struct, IPayloadEventArgs
        {
            internal static readonly SubscribeHandler Subscribe = SubscribeToBus;
            private static EventRuntimeHandle SubscribeToBus(Delegate handler) => EventBus.Subscribe<T>((InEventHandler<T>)handler);
        }

        private Subscription[] _subscriptions = Array.Empty<Subscription>();
        private int _count;
        private bool _suspended;

        public void AddUIEvent<T>(Action handler) where T : struct, IEmptyEventArgs =>
            AddSubscription(handler, EmptySubscriber<T>.Subscribe);

        public void AddUIEvent<T>(InEventHandler<T> handler) where T : struct, IPayloadEventArgs =>
            AddSubscription(handler, PayloadSubscriber<T>.Subscribe);

        private void AddSubscription(Delegate handler, SubscribeHandler subscribe)
        {
            EventRuntimeHandle handle = _suspended ? default : subscribe(handler);
            if (_count == _subscriptions.Length)
                Array.Resize(ref _subscriptions, _count == 0 ? InitialCapacity : _count * 2);
            ref Subscription subscription = ref _subscriptions[_count++];
            subscription.Handler = handler;
            subscription.Subscribe = subscribe;
            subscription.Handle = handle;
        }

#if UNITY_EDITOR
        internal bool IsSuspended => _suspended;
        internal int SubscriptionCount => _count;
        internal int SubscriptionCapacity => _subscriptions.Length;
#endif

        internal void Suspend()
        {
            _suspended = true;
            for (int i = _count - 1; i >= 0; i--) _subscriptions[i].Handle.Dispose();
        }

        internal void Resume()
        {
            _suspended = false;
            for (int i = 0; i < _count; i++)
            {
                ref Subscription subscription = ref _subscriptions[i];
                subscription.Handle = subscription.Subscribe(subscription.Handler);
            }
        }

        public override void Clear()
        {
            Suspend();
            Array.Clear(_subscriptions, 0, _count);
            _count = 0;
            _suspended = false;
        }
    }
}
