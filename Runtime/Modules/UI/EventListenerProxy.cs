using System;

namespace AlicizaX
{
    /// <summary>
    /// UI 事件订阅代理：视图启用时订阅、停用时退订。视图首次需要事件时从内存池取出，
    /// 视图销毁时归还（归还即退订并清空），销毁重建的窗口复用同一批代理与订阅数组。
    /// 订阅函数按事件类型缓存为静态委托，重订阅不分配。
    /// </summary>
    public sealed class EventListenerProxy : MemoryObject
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
            if (_count == _subscriptions.Length)
                Array.Resize(ref _subscriptions, _count == 0 ? InitialCapacity : _count * 2);
            ref Subscription subscription = ref _subscriptions[_count++];
            subscription.Handler = handler;
            subscription.Subscribe = subscribe;
            subscription.Handle = _suspended ? default : subscribe(handler);
        }

        internal void Suspend()
        {
            if (_suspended) return;
            _suspended = true;
            for (int i = _count - 1; i >= 0; i--)
            {
                ref Subscription subscription = ref _subscriptions[i];
                subscription.Handle.Dispose();
                subscription.Handle = default;
            }
        }

        internal void Resume()
        {
            if (!_suspended) return;
            _suspended = false;
            for (int i = 0; i < _count; i++)
            {
                ref Subscription subscription = ref _subscriptions[i];
                subscription.Handle = subscription.Subscribe(subscription.Handler);
            }
        }

        /// <summary>退订并清空，回到新建时的状态；归还内存池时由池调用。保留订阅数组容量供复用。</summary>
        public override void Clear()
        {
            Suspend();
            Array.Clear(_subscriptions, 0, _count);
            _count = 0;
            _suspended = false;
        }
    }
}
