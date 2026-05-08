#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX
{
    internal enum EventDebugOperationKind : byte
    {
        Subscribe,
        Unsubscribe,
        Publish,
        Resize,
        Clear,
        MutationRejected
    }

    internal readonly struct EventDebugSummary
    {
        internal readonly Type EventType;
        internal readonly bool Initialized;
        internal readonly int SubscriberCount;
        internal readonly int PeakSubscriberCount;
        internal readonly int Capacity;
        internal readonly int ValueSubscriberCount;
        internal readonly int InSubscriberCount;
        internal readonly long PublishCount;
        internal readonly long SubscribeCount;
        internal readonly long UnsubscribeCount;
        internal readonly int ResizeCount;
        internal readonly int ClearCount;
        internal readonly long MutationRejectedCount;
        internal readonly int LastOperationFrame;
        internal readonly long LastOperationTicksUtc;

        internal EventDebugSummary(
            Type eventType,
            bool initialized,
            int subscriberCount,
            int peakSubscriberCount,
            int capacity,
            int valueSubscriberCount,
            int inSubscriberCount,
            long publishCount,
            long subscribeCount,
            long unsubscribeCount,
            int resizeCount,
            int clearCount,
            long mutationRejectedCount,
            int lastOperationFrame,
            long lastOperationTicksUtc)
        {
            EventType = eventType;
            Initialized = initialized;
            SubscriberCount = subscriberCount;
            PeakSubscriberCount = peakSubscriberCount;
            Capacity = capacity;
            ValueSubscriberCount = valueSubscriberCount;
            InSubscriberCount = inSubscriberCount;
            PublishCount = publishCount;
            SubscribeCount = subscribeCount;
            UnsubscribeCount = unsubscribeCount;
            ResizeCount = resizeCount;
            ClearCount = clearCount;
            MutationRejectedCount = mutationRejectedCount;
            LastOperationFrame = lastOperationFrame;
            LastOperationTicksUtc = lastOperationTicksUtc;
        }
    }

    internal readonly struct EventDebugSubscriberInfo
    {
        internal readonly int HandlerIndex;
        internal readonly int Version;
        internal readonly string DeclaringTypeName;
        internal readonly string MethodName;
        internal readonly string TargetTypeName;
        internal readonly UnityEngine.Object UnityTarget;
        internal readonly bool IsStatic;
        internal readonly bool IsUnityObjectDestroyed;
        internal readonly bool UsesInParameter;
        internal readonly bool IsCompilerGeneratedTarget;
        internal readonly bool IsCompilerGeneratedMethod;

        internal EventDebugSubscriberInfo(
            int handlerIndex,
            int version,
            string declaringTypeName,
            string methodName,
            string targetTypeName,
            UnityEngine.Object unityTarget,
            bool isStatic,
            bool isUnityObjectDestroyed,
            bool usesInParameter,
            bool isCompilerGeneratedTarget,
            bool isCompilerGeneratedMethod)
        {
            HandlerIndex = handlerIndex;
            Version = version;
            DeclaringTypeName = declaringTypeName;
            MethodName = methodName;
            TargetTypeName = targetTypeName;
            UnityTarget = unityTarget;
            IsStatic = isStatic;
            IsUnityObjectDestroyed = isUnityObjectDestroyed;
            UsesInParameter = usesInParameter;
            IsCompilerGeneratedTarget = isCompilerGeneratedTarget;
            IsCompilerGeneratedMethod = isCompilerGeneratedMethod;
        }
    }

    internal readonly struct EventDebugOperationRecord
    {
        internal readonly Type EventType;
        internal readonly EventDebugOperationKind OperationKind;
        internal readonly int Frame;
        internal readonly long TicksUtc;
        internal readonly int SubscriberCount;
        internal readonly int Capacity;

        internal EventDebugOperationRecord(
            Type eventType,
            EventDebugOperationKind operationKind,
            int frame,
            long ticksUtc,
            int subscriberCount,
            int capacity)
        {
            EventType = eventType;
            OperationKind = operationKind;
            Frame = frame;
            TicksUtc = ticksUtc;
            SubscriberCount = subscriberCount;
            Capacity = capacity;
        }
    }

    internal static class EventDebugRegistry
    {
        private const int HistoryCapacity = 128;

        private sealed class State
        {
            internal readonly Type EventType;
            internal Func<int> SubscriberCountProvider;
            internal Func<int> CapacityProvider;
            internal Func<int> ValueSubscriberCountProvider;
            internal Func<int> InSubscriberCountProvider;
            internal Func<EventDebugSubscriberInfo[]> SubscribersProvider;

            internal int PeakSubscriberCount;
            internal long PublishCount;
            internal long SubscribeCount;
            internal long UnsubscribeCount;
            internal int ResizeCount;
            internal int ClearCount;
            internal long MutationRejectedCount;
            internal int LastOperationFrame;
            internal long LastOperationTicksUtc;

            internal State(Type eventType)
            {
                EventType = eventType;
            }
        }

        private static readonly Dictionary<Type, State> _states = new();
        private static readonly List<Type> _registrationOrder = new();
        private static readonly EventDebugOperationRecord[] _history = new EventDebugOperationRecord[HistoryCapacity];
        private static int _historyWriteIndex;
        private static int _historyCount;

        internal static void RegisterContainer<T>(
            Func<int> subscriberCountProvider,
            Func<int> capacityProvider,
            Func<int> valueSubscriberCountProvider,
            Func<int> inSubscriberCountProvider,
            Func<EventDebugSubscriberInfo[]> subscribersProvider)
            where T : struct, IEventArgs
        {
            Type eventType = typeof(T);
            if (!_states.TryGetValue(eventType, out State state))
            {
                state = new State(eventType);
                _states.Add(eventType, state);
                _registrationOrder.Add(eventType);
            }

            state.SubscriberCountProvider = subscriberCountProvider;
            state.CapacityProvider = capacityProvider;
            state.ValueSubscriberCountProvider = valueSubscriberCountProvider;
            state.InSubscriberCountProvider = inSubscriberCountProvider;
            state.SubscribersProvider = subscribersProvider;
            state.PeakSubscriberCount = Math.Max(state.PeakSubscriberCount, subscriberCountProvider());
        }

        internal static void RecordSubscribe<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.SubscribeCount++;
            state.PeakSubscriberCount = Math.Max(state.PeakSubscriberCount, subscriberCount);
            MarkOperation(state, EventDebugOperationKind.Subscribe, subscriberCount, capacity);
        }

        internal static void RecordUnsubscribe<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.UnsubscribeCount++;
            MarkOperation(state, EventDebugOperationKind.Unsubscribe, subscriberCount, capacity);
        }

        internal static void RecordPublish<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.PublishCount++;
            MarkOperation(state, EventDebugOperationKind.Publish, subscriberCount, capacity);
        }

        internal static void RecordResize<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.ResizeCount++;
            MarkOperation(state, EventDebugOperationKind.Resize, subscriberCount, capacity);
        }

        internal static void RecordClear<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.ClearCount++;
            MarkOperation(state, EventDebugOperationKind.Clear, subscriberCount, capacity);
        }

        internal static void RecordMutationRejected<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.MutationRejectedCount++;
            MarkOperation(state, EventDebugOperationKind.MutationRejected, subscriberCount, capacity);
        }

        internal static EventDebugSummary[] GetSummaries()
        {
            int count = _registrationOrder.Count;
            EventDebugSummary[] summaries = new EventDebugSummary[count];
            for (int i = 0; i < count; i++)
            {
                summaries[i] = BuildSummary(_states[_registrationOrder[i]]);
            }

            return summaries;
        }

        internal static bool TryGetDetails(Type eventType, out EventDebugSummary summary, out EventDebugSubscriberInfo[] subscribers)
        {
            if (_states.TryGetValue(eventType, out State state))
            {
                summary = BuildSummary(state);
                subscribers = state.SubscribersProvider?.Invoke() ?? Array.Empty<EventDebugSubscriberInfo>();
                return true;
            }

            summary = default;
            subscribers = Array.Empty<EventDebugSubscriberInfo>();
            return false;
        }

        internal static EventDebugOperationRecord[] GetRecentOperations()
        {
            EventDebugOperationRecord[] result = new EventDebugOperationRecord[_historyCount];
            for (int i = 0; i < _historyCount; i++)
            {
                int index = (_historyWriteIndex - 1 - i + _history.Length) % _history.Length;
                result[i] = _history[index];
            }

            return result;
        }

        internal static void ResetStats()
        {
            foreach (Type eventType in _registrationOrder)
            {
                State state = _states[eventType];
                state.PeakSubscriberCount = state.SubscriberCountProvider?.Invoke() ?? 0;
                state.PublishCount = 0;
                state.SubscribeCount = 0;
                state.UnsubscribeCount = 0;
                state.ResizeCount = 0;
                state.ClearCount = 0;
                state.MutationRejectedCount = 0;
                state.LastOperationFrame = 0;
                state.LastOperationTicksUtc = 0;
            }

            _historyWriteIndex = 0;
            _historyCount = 0;
        }

        private static State GetState<T>() where T : struct, IEventArgs
        {
            Type eventType = typeof(T);
            if (_states.TryGetValue(eventType, out State state))
            {
                return state;
            }

            throw new InvalidOperationException($"Event debug state is not registered for {eventType.FullName}.");
        }

        private static EventDebugSummary BuildSummary(State state)
        {
            int subscriberCount = state.SubscriberCountProvider?.Invoke() ?? 0;
            int capacity = state.CapacityProvider?.Invoke() ?? 0;
            int valueSubscriberCount = state.ValueSubscriberCountProvider?.Invoke() ?? 0;
            int inSubscriberCount = state.InSubscriberCountProvider?.Invoke() ?? 0;

            return new EventDebugSummary(
                state.EventType,
                state.SubscriberCountProvider != null,
                subscriberCount,
                state.PeakSubscriberCount,
                capacity,
                valueSubscriberCount,
                inSubscriberCount,
                state.PublishCount,
                state.SubscribeCount,
                state.UnsubscribeCount,
                state.ResizeCount,
                state.ClearCount,
                state.MutationRejectedCount,
                state.LastOperationFrame,
                state.LastOperationTicksUtc);
        }

        private static void MarkOperation(State state, EventDebugOperationKind kind, int subscriberCount, int capacity)
        {
            int frame = Time.frameCount;
            long ticksUtc = DateTime.UtcNow.Ticks;

            state.LastOperationFrame = frame;
            state.LastOperationTicksUtc = ticksUtc;

            _history[_historyWriteIndex] = new EventDebugOperationRecord(
                state.EventType,
                kind,
                frame,
                ticksUtc,
                subscriberCount,
                capacity);

            _historyWriteIndex = (_historyWriteIndex + 1) % _history.Length;
            if (_historyCount < _history.Length)
            {
                _historyCount++;
            }
        }
    }
}
#endif
