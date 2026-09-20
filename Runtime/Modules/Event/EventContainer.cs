using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AlicizaX
{
    internal static class EventContainer<TEvent, THandler> where TEvent : struct where THandler : Delegate
    {
        private enum OperationKind : byte { Subscribe, Unsubscribe, Clear, ReserveCapacity }
        private struct Operation
        {
            internal OperationKind Kind;
            internal int Slot;
            internal int Version;
            internal int Capacity;
        }

        private sealed class HandlerComparer : IEqualityComparer<THandler>
        {
            public bool Equals(THandler x, THandler y) => x.Equals(y);
            public int GetHashCode(THandler handler)
                => unchecked(handler.GetHashCode() * 397) ^ (handler.Target == null ? 0 : RuntimeHelpers.GetHashCode(handler.Target));
        }

        private static readonly int TypeId;
        private static readonly Dictionary<THandler, int> Subscriptions;
        private static THandler[] _callbacks;
        private static int[] _versions;
        private static bool[] _cancelled;
        private static int[] _freeSlots;
        private static int[] _packedIndices;
        private static THandler[] _packedCallbacks;
        private static int[] _packedSlots;
        private static Operation[] _pending;
        private static int _packedCount;
        private static int _freeCount;
        private static int _pendingCount;
        private static int _publishDepth;
        private static int _version;

        static EventContainer()
        {
            EventArgsGuard.Validate<TEvent>();
            int capacity = EventInitialSize<TEvent>.Size;
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(EventInitialSize<TEvent>.Size));
            _callbacks = new THandler[capacity];
            _versions = new int[capacity];
            _cancelled = new bool[capacity];
            _freeSlots = new int[capacity];
            _packedIndices = new int[capacity];
            _packedCallbacks = new THandler[capacity];
            _packedSlots = new int[capacity];
            _pending = new Operation[Math.Max(4, capacity)];
            Subscriptions = new Dictionary<THandler, int>(capacity, new HandlerComparer());
            for (int i = 0; i < capacity; i++) _freeSlots[i] = i;
            _freeCount = capacity;
            TypeId = UnsubscribeRegistry.Register(Unsubscribe);
#if UNITY_EDITOR
            EventDebugRegistry.RegisterContainer<TEvent>(typeof(THandler) == typeof(Action),
                () => _packedCount, () => _callbacks.Length, GetDebugSubscribers);
#endif
        }

        internal static int SubscriberCount => _packedCount;

        internal static EventRuntimeHandle Subscribe(THandler callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (Subscriptions.ContainsKey(callback))
            {
                Debug.LogWarning($"Duplicate event handler subscription: {typeof(TEvent).Name}.{callback.Method.Name}");
                return default;
            }
            if (_freeCount == 0) GrowSlots(_callbacks.Length == 0 ? 4 : checked(_callbacks.Length * 2), true);
            int slot = _freeSlots[--_freeCount];
            int version = unchecked(++_version);
            if (version == 0) version = unchecked(++_version);
            _callbacks[slot] = callback;
            _versions[slot] = version;
            _cancelled[slot] = false;
            _packedIndices[slot] = -1;
            Subscriptions.Add(callback, slot);
            if (_publishDepth != 0) Enqueue(OperationKind.Subscribe, slot, version);
            else AddPacked(slot);
            return new EventRuntimeHandle(TypeId, slot, version);
        }

        private static void Unsubscribe(int slot, int version)
        {
            if ((uint)slot >= (uint)_versions.Length || version == 0 || _versions[slot] != version || _cancelled[slot]) return;
            _cancelled[slot] = true;
            int index = _packedIndices[slot];
            if (index >= 0) _packedCallbacks[index] = null;
            THandler callback = _callbacks[slot];
            if (Subscriptions.TryGetValue(callback, out int owner) && owner == slot) Subscriptions.Remove(callback);
            if (_publishDepth != 0) Enqueue(OperationKind.Unsubscribe, slot, version);
            else RemoveSlot(slot);
        }

        private static void AddPacked(int slot)
        {
            if (_packedCount == _packedCallbacks.Length) GrowPacked(_callbacks.Length);
            int index = _packedCount++;
            _packedCallbacks[index] = _callbacks[slot];
            _packedSlots[index] = slot;
            _packedIndices[slot] = index;
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode) EventDebugRegistry.RecordSubscribe<TEvent>(_packedCount, _callbacks.Length);
#endif
        }

        private static void RemoveSlot(int slot)
        {
            int index = _packedIndices[slot];
            if (index >= 0)
            {
                int last = --_packedCount;
                if (index != last)
                {
                    _packedCallbacks[index] = _packedCallbacks[last];
                    int movedSlot = _packedSlots[last];
                    _packedSlots[index] = movedSlot;
                    _packedIndices[movedSlot] = index;
                }
                _packedCallbacks[last] = null;
                _packedSlots[last] = 0;
            }
            ReleaseSlot(slot);
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode) EventDebugRegistry.RecordUnsubscribe<TEvent>(_packedCount, _callbacks.Length);
#endif
        }

        private static void ReleaseSlot(int slot)
        {
            _callbacks[slot] = null;
            _versions[slot] = 0;
            _cancelled[slot] = false;
            _packedIndices[slot] = -1;
            _freeSlots[_freeCount++] = slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int BeginPublish(out THandler[] callbacks)
        {
            _publishDepth++;
            callbacks = _packedCallbacks;
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode) EventDebugRegistry.RecordPublish<TEvent>(_packedCount, _callbacks.Length);
#endif
            return _packedCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void EndPublish()
        {
            if (--_publishDepth == 0) FlushPending();
        }

        internal static void RecordHandlerException(Exception exception)
        {
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode) EventDebugRegistry.RecordHandlerException<TEvent>(_packedCount, _callbacks.Length);
#endif
            Debug.LogException(exception);
        }

        internal static void ReserveCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (_publishDepth != 0) Enqueue(OperationKind.ReserveCapacity, capacity: capacity);
            else ReserveStorage(capacity);
        }

        private static void ReserveStorage(int capacity)
        {
            if (capacity > _callbacks.Length) GrowSlots(capacity, false);
            if (capacity > _packedCallbacks.Length) GrowPacked(capacity);
        }

        private static void GrowSlots(int capacity, bool warn)
        {
            int oldCapacity = _callbacks.Length;
            Array.Resize(ref _callbacks, capacity);
            Array.Resize(ref _versions, capacity);
            Array.Resize(ref _cancelled, capacity);
            Array.Resize(ref _packedIndices, capacity);
            Array.Resize(ref _freeSlots, capacity);
            Subscriptions.EnsureCapacity(capacity);
            for (int i = capacity - 1; i >= oldCapacity; i--) _freeSlots[_freeCount++] = i;
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordResize<TEvent>(_packedCount, capacity);
                if (warn) Debug.LogWarning($"EventContainer<{typeof(TEvent).Name}> capacity grew to {capacity}. Use Prewarm({capacity}) or reserve capacity.");
            }
#endif
        }

        private static void GrowPacked(int capacity)
        {
            Array.Resize(ref _packedCallbacks, capacity);
            Array.Resize(ref _packedSlots, capacity);
        }

        internal static void Clear()
        {
            Subscriptions.Clear();
            if (_publishDepth != 0) Enqueue(OperationKind.Clear);
            else ClearPacked();
        }

        private static void ClearPacked()
        {
            for (int i = 0; i < _packedCount; i++)
            {
                ReleaseSlot(_packedSlots[i]);
                _packedCallbacks[i] = null;
                _packedSlots[i] = 0;
            }
            _packedCount = 0;
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode) EventDebugRegistry.RecordClear<TEvent>(_packedCount, _callbacks.Length);
#endif
        }

        private static void Enqueue(OperationKind kind, int slot = 0, int version = 0, int capacity = 0)
        {
            if (_pendingCount == _pending.Length) Array.Resize(ref _pending, checked(_pending.Length * 2));
            _pending[_pendingCount++] = new Operation { Kind = kind, Slot = slot, Version = version, Capacity = capacity };
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode) EventDebugRegistry.RecordDeferredMutation<TEvent>(_pendingCount, _packedCount, _callbacks.Length);
#endif
        }

        private static void FlushPending()
        {
            int count = _pendingCount;
            if (count == 0) return;
            for (int i = 0; i < count; i++)
            {
                Operation operation = _pending[i];
                switch (operation.Kind)
                {
                    case OperationKind.Subscribe:
                        AddPacked(operation.Slot);
                        break;
                    case OperationKind.Unsubscribe:
                        if (_versions[operation.Slot] == operation.Version) RemoveSlot(operation.Slot);
                        break;
                    case OperationKind.Clear:
                        ClearPacked();
                        break;
                    case OperationKind.ReserveCapacity:
                        ReserveStorage(operation.Capacity);
                        break;
                }
                _pending[i] = default;
            }
            _pendingCount = 0;
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode) EventDebugRegistry.RecordFlush<TEvent>(count, _packedCount, _callbacks.Length);
#endif
        }

#if UNITY_EDITOR
        private static EventDebugSubscriberInfo[] GetDebugSubscribers()
        {
            if (_packedCount == 0) return Array.Empty<EventDebugSubscriberInfo>();
            var subscribers = new EventDebugSubscriberInfo[_packedCount];
            for (int i = 0; i < subscribers.Length; i++)
            {
                int slot = _packedSlots[i];
                THandler callback = _callbacks[slot];
                object target = callback.Target;
                var unityTarget = target as UnityEngine.Object;
                subscribers[i] = new EventDebugSubscriberInfo(slot, _versions[slot],
                    callback.Method.DeclaringType?.FullName ?? "<UnknownType>", callback.Method.Name,
                    target?.GetType().FullName ?? "<Static>", unityTarget, target == null,
                    target is UnityEngine.Object && unityTarget == null, typeof(THandler) == typeof(Action),
                    target != null && target.GetType().IsDefined(typeof(CompilerGeneratedAttribute), false),
                    callback.Method.IsDefined(typeof(CompilerGeneratedAttribute), false));
            }
            return subscribers;
        }
#endif
    }
}
