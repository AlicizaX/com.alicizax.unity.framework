using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    internal readonly struct UITransitionWaiter
    {
        internal readonly Action<object> Continuation;
        internal readonly object State;

        internal UITransitionWaiter(Action<object> continuation, object state)
        {
            Continuation = continuation;
            State = state;
        }
    }

    internal readonly struct UITransitionWaiters
    {
        internal readonly UITransitionWaiter[] Items;
        internal readonly int Count;

        internal UITransitionWaiters(UITransitionWaiter[] items, int count)
        {
            Items = items;
            Count = count;
        }
    }

    internal sealed class UITransitionGate : IUniTaskSource
    {
        private const int InitialWaiterCapacity = 2;

        private UITransitionWaiter[] _waiters = Array.Empty<UITransitionWaiter>();
        private UITransitionWaiter[] _spareWaiters;
        private int _waiterCount;
        private short _token;
        private bool _pending;

        internal UniTask Task => new UniTask(this, _token);

        internal UITransitionWaiters Restart()
        {
            UITransitionWaiters waiters = TakeWaiters();
            _token++;
            _pending = true;
            return waiters;
        }

        internal UITransitionWaiters Settle()
        {
            UITransitionWaiters waiters = TakeWaiters();
            _token++;
            _pending = false;
            return waiters;
        }

        internal void Release(in UITransitionWaiters waiters)
        {
            if (waiters.Count == 0) return;
            UITransitionWaiter[] items = waiters.Items;
            for (int i = 0; i < waiters.Count; i++)
            {
                UITransitionWaiter waiter = items[i];
                items[i] = default;
                try { waiter.Continuation(waiter.State); }
                catch (Exception error) { Log.Exception(error); }
            }
            if (_waiters.Length == 0) _waiters = items;
            else _spareWaiters ??= items;
        }

        private UITransitionWaiters TakeWaiters()
        {
            if (_waiterCount == 0) return default;
            var waiters = new UITransitionWaiters(_waiters, _waiterCount);
            _waiters = _spareWaiters ?? Array.Empty<UITransitionWaiter>();
            _spareWaiters = null;
            _waiterCount = 0;
            return waiters;
        }

        public UniTaskStatus GetStatus(short token) =>
            _pending && token == _token ? UniTaskStatus.Pending : UniTaskStatus.Succeeded;

        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            if (GetStatus(token) != UniTaskStatus.Pending)
            {
                continuation(state);
                return;
            }
            if (_waiterCount == _waiters.Length)
                Array.Resize(ref _waiters, _waiterCount == 0 ? InitialWaiterCapacity : _waiterCount * 2);
            _waiters[_waiterCount++] = new UITransitionWaiter(continuation, state);
        }

        public void GetResult(short token) { }

        public UniTaskStatus UnsafeGetStatus() => _pending ? UniTaskStatus.Pending : UniTaskStatus.Succeeded;

#if UNITY_EDITOR
        internal int WaiterCapacity => _waiters.Length;
#endif
    }
}
