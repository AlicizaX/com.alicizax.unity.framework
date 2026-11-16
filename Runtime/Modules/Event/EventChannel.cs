using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AlicizaX
{
    internal abstract class EventChannel
    {
        internal static readonly List<EventChannel> Channels = new List<EventChannel>();

        internal readonly Type EventType;

        protected EventChannel(Type eventType)
        {
            EventType = eventType;
            Channels.Add(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearAll()
        {
            for (int i = 0; i < Channels.Count; i++) Channels[i].Clear();
#if UNITY_EDITOR
            EventDebugRegistry.ResetStats();
#endif
        }

        internal abstract void Unsubscribe(int slot, int version);

        internal abstract void Clear();

#if UNITY_EDITOR
        internal EventDebugCounters Counters;

        internal abstract EventDebugSummary CreateDebugSummary();

        internal abstract EventDebugSubscriberInfo[] CreateDebugSubscribers();
#endif
    }

    internal sealed class EventChannel<THandler> : EventChannel where THandler : Delegate
    {
        private const int MinCapacity = 4;

        private struct Slot
        {
            internal int Index;
            internal int Version;
        }

        private readonly HashSet<THandler> _subscribedHandlers = new HashSet<THandler>();
        internal THandler[] Handlers = Array.Empty<THandler>();
        private int[] _handlerSlots = Array.Empty<int>();
        private Slot[] _slots = Array.Empty<Slot>();
        private int _count;
        private int _holeCount;
        private int _publishDepth;
        private int _freeSlot = -1;

        internal EventChannel(Type eventType) : base(eventType)
        {
        }

        internal int SubscriberCount => _count - _holeCount;

        internal EventRuntimeHandle Subscribe(THandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_subscribedHandlers.Add(handler))
            {
                Debug.LogWarning($"Duplicate event handler subscription: {EventType.Name}.{handler.Method.Name}");
                return default;
            }

            if (_count == Handlers.Length) Grow(Math.Max(MinCapacity, Handlers.Length * 2));
            int slot = _freeSlot;
            ref Slot entry = ref _slots[slot];
            _freeSlot = entry.Index;
            entry.Index = _count;
            Handlers[_count] = handler;
            _handlerSlots[_count] = slot;
            _count++;
#if UNITY_EDITOR
            if (EventDebugRegistry.Recording) EventDebugRegistry.RecordSubscribe(this, SubscriberCount, Handlers.Length);
#endif
            return new EventRuntimeHandle(this, slot, entry.Version);
        }

        internal override void Unsubscribe(int slot, int version)
        {
            ref Slot entry = ref _slots[slot];
            if (entry.Version != version) return;
            int index = entry.Index;
            ReleaseSlot(slot);
            _subscribedHandlers.Remove(Handlers[index]);
            if (_publishDepth == 0)
            {
                RemoveAt(index);
            }
            else
            {
                Handlers[index] = null;
                _holeCount++;
            }
#if UNITY_EDITOR
            if (EventDebugRegistry.Recording) EventDebugRegistry.RecordUnsubscribe(this, SubscriberCount, Handlers.Length);
#endif
        }

        internal override void Clear()
        {
            _subscribedHandlers.Clear();
            for (int index = 0; index < _count; index++)
            {
                if (Handlers[index] == null) continue;
                ReleaseSlot(_handlerSlots[index]);
                Handlers[index] = null;
            }

            if (_publishDepth == 0) _count = 0;
            else _holeCount = _count;
#if UNITY_EDITOR
            if (EventDebugRegistry.Recording) EventDebugRegistry.RecordClear(this, SubscriberCount, Handlers.Length);
#endif
        }

        internal void Reserve(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (capacity > Handlers.Length) Grow(capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int BeginPublish()
        {
            _publishDepth++;
#if UNITY_EDITOR
            if (EventDebugRegistry.Recording) EventDebugRegistry.RecordPublish(this);
#endif
            return _count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void EndPublish()
        {
            if (--_publishDepth == 0 && _holeCount != 0) Compact();
        }

        internal void LogHandlerException(Exception exception)
        {
#if UNITY_EDITOR
            if (EventDebugRegistry.Recording) EventDebugRegistry.RecordHandlerException(this, SubscriberCount, Handlers.Length);
#endif
            Debug.LogException(exception);
        }

        private void ReleaseSlot(int slot)
        {
            ref Slot entry = ref _slots[slot];
            entry.Version++;
            entry.Index = _freeSlot;
            _freeSlot = slot;
        }

        private void RemoveAt(int index)
        {
            int last = --_count;
            if (index != last)
            {
                int movedSlot = _handlerSlots[last];
                Handlers[index] = Handlers[last];
                _handlerSlots[index] = movedSlot;
                _slots[movedSlot].Index = index;
            }

            Handlers[last] = null;
        }

        private void Compact()
        {
            int write = 0;
            for (int read = 0; read < _count; read++)
            {
                THandler handler = Handlers[read];
                if (handler == null) continue;
                if (read != write)
                {
                    int slot = _handlerSlots[read];
                    Handlers[write] = handler;
                    _handlerSlots[write] = slot;
                    _slots[slot].Index = write;
                }

                write++;
            }

            Array.Clear(Handlers, write, _count - write);
            _count = write;
            _holeCount = 0;
        }

        private void Grow(int capacity)
        {
            int oldCapacity = Handlers.Length;
            var handlers = new THandler[capacity];
            var handlerSlots = new int[capacity];
            var slots = new Slot[capacity];
            Array.Copy(Handlers, handlers, _count);
            Array.Copy(_handlerSlots, handlerSlots, _count);
            Array.Copy(_slots, slots, oldCapacity);
            for (int slot = capacity - 1; slot >= oldCapacity; slot--)
            {
                slots[slot].Index = _freeSlot;
                _freeSlot = slot;
            }

            Handlers = handlers;
            _handlerSlots = handlerSlots;
            _slots = slots;
            _subscribedHandlers.EnsureCapacity(capacity);
#if UNITY_EDITOR
            if (EventDebugRegistry.Recording) EventDebugRegistry.RecordResize(this, SubscriberCount, capacity);
#endif
        }

#if UNITY_EDITOR
        internal override EventDebugSummary CreateDebugSummary()
            => new EventDebugSummary(EventType, typeof(THandler) == typeof(Action), SubscriberCount, Handlers.Length, Counters);

        internal override EventDebugSubscriberInfo[] CreateDebugSubscribers()
        {
            var subscribers = new EventDebugSubscriberInfo[SubscriberCount];
            int written = 0;
            for (int index = 0; index < _count; index++)
            {
                THandler handler = Handlers[index];
                if (handler == null) continue;
                int slot = _handlerSlots[index];
                object target = handler.Target;
                var unityTarget = target as UnityEngine.Object;
                subscribers[written++] = new EventDebugSubscriberInfo(slot, _slots[slot].Version,
                    handler.Method.DeclaringType?.FullName ?? "<UnknownType>", handler.Method.Name,
                    target?.GetType().FullName ?? "<Static>", unityTarget, target == null,
                    target is UnityEngine.Object && unityTarget == null, typeof(THandler) == typeof(Action),
                    target != null && target.GetType().IsDefined(typeof(CompilerGeneratedAttribute), false),
                    handler.Method.IsDefined(typeof(CompilerGeneratedAttribute), false));
            }

            return subscribers;
        }
#endif
    }
}
