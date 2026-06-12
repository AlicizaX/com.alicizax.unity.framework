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
        SafePublish,
        Resize,
        Clear,
        MutationRejected,
        HandlerException,
        DeferredMutation,
        Flush
    }

    internal readonly struct EventDebugSummary
    {
        internal readonly Type EventType;
        internal readonly bool Initialized;
        internal readonly int SubscriberCount;
        internal readonly int PeakSubscriberCount;
        internal readonly int Capacity;
        internal readonly int EmptySubscriberCount;
        internal readonly int InSubscriberCount;
        internal readonly long PublishCount;
        internal readonly long SafePublishCount;
        internal readonly long SubscribeCount;
        internal readonly long UnsubscribeCount;
        internal readonly int ResizeCount;
        internal readonly int ClearCount;
        internal readonly long MutationRejectedCount;
        internal readonly long HandlerExceptionCount;
        internal readonly long DeferredMutationCount;
        internal readonly int FlushCount;
        internal readonly int PeakPendingCount;
        internal readonly int LastOperationFrame;
        internal readonly long LastOperationTicksUtc;

        internal EventDebugSummary(
            Type eventType,
            bool initialized,
            int subscriberCount,
            int peakSubscriberCount,
            int capacity,
            int emptySubscriberCount,
            int inSubscriberCount,
            long publishCount,
            long safePublishCount,
            long subscribeCount,
            long unsubscribeCount,
            int resizeCount,
            int clearCount,
            long mutationRejectedCount,
            long handlerExceptionCount,
            long deferredMutationCount,
            int flushCount,
            int peakPendingCount,
            int lastOperationFrame,
            long lastOperationTicksUtc)
        {
            EventType = eventType;
            Initialized = initialized;
            SubscriberCount = subscriberCount;
            PeakSubscriberCount = peakSubscriberCount;
            Capacity = capacity;
            EmptySubscriberCount = emptySubscriberCount;
            InSubscriberCount = inSubscriberCount;
            PublishCount = publishCount;
            SafePublishCount = safePublishCount;
            SubscribeCount = subscribeCount;
            UnsubscribeCount = unsubscribeCount;
            ResizeCount = resizeCount;
            ClearCount = clearCount;
            MutationRejectedCount = mutationRejectedCount;
            HandlerExceptionCount = handlerExceptionCount;
            DeferredMutationCount = deferredMutationCount;
            FlushCount = flushCount;
            PeakPendingCount = peakPendingCount;
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
        internal readonly bool IsParameterless;
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
            bool isParameterless,
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
            IsParameterless = isParameterless;
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
            internal Func<int> PayloadSubscriberCountProvider;
            internal Func<int> PayloadCapacityProvider;
            internal Func<int> InSubscriberCountProvider;
            internal Func<EventDebugSubscriberInfo[]> PayloadSubscribersProvider;
            internal Func<int> EmptySubscriberCountProvider;
            internal Func<int> EmptyCapacityProvider;
            internal Func<EventDebugSubscriberInfo[]> EmptySubscribersProvider;

            internal int PeakSubscriberCount;
            internal long PublishCount;
            internal long SafePublishCount;
            internal long SubscribeCount;
            internal long UnsubscribeCount;
            internal int ResizeCount;
            internal int ClearCount;
            internal long MutationRejectedCount;
            internal long HandlerExceptionCount;
            internal long DeferredMutationCount;
            internal int FlushCount;
            internal int PeakPendingCount;
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
        private static int _detailedHistoryRequestCount;
        private static int _historyWriteIndex;
        private static int _historyCount;

        internal static bool BackgroundFullHistoryEnabled { get; set; }

        internal static bool BenchmarkReleaseLikeMode { get; set; }

        internal static bool DetailedHistoryEnabled => _detailedHistoryRequestCount > 0 || BackgroundFullHistoryEnabled;

        internal static void BeginDetailedHistory()
        {
            _detailedHistoryRequestCount++;
        }

        internal static void EndDetailedHistory()
        {
            if (_detailedHistoryRequestCount > 0)
            {
                _detailedHistoryRequestCount--;
            }
        }

        internal static void RegisterPayloadContainer<T>(
            Func<int> subscriberCountProvider,
            Func<int> capacityProvider,
            Func<int> inSubscriberCountProvider,
            Func<EventDebugSubscriberInfo[]> subscribersProvider)
            where T : struct, IEventArgs
        {
            State state = GetOrCreateState(typeof(T));
            state.PayloadSubscriberCountProvider = subscriberCountProvider;
            state.PayloadCapacityProvider = capacityProvider;
            state.InSubscriberCountProvider = inSubscriberCountProvider;
            state.PayloadSubscribersProvider = subscribersProvider;
            state.PeakSubscriberCount = Math.Max(state.PeakSubscriberCount, subscriberCountProvider());
        }

        internal static void RegisterEmptyContainer<T>(
            Func<int> subscriberCountProvider,
            Func<int> capacityProvider,
            Func<EventDebugSubscriberInfo[]> subscribersProvider)
            where T : struct, IEventArgs
        {
            State state = GetOrCreateState(typeof(T));
            state.EmptySubscriberCountProvider = subscriberCountProvider;
            state.EmptyCapacityProvider = capacityProvider;
            state.EmptySubscribersProvider = subscribersProvider;
            state.PeakSubscriberCount = Math.Max(state.PeakSubscriberCount, subscriberCountProvider());
        }

        internal static void RecordSubscribe<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.SubscribeCount++;
            state.PeakSubscriberCount = Math.Max(state.PeakSubscriberCount, subscriberCount);
            MarkOperation(state, EventDebugOperationKind.Subscribe, subscriberCount, capacity, DetailedHistoryEnabled);
        }

        internal static void RecordUnsubscribe<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.UnsubscribeCount++;
            MarkOperation(state, EventDebugOperationKind.Unsubscribe, subscriberCount, capacity, DetailedHistoryEnabled);
        }

        internal static void RecordPublish<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.PublishCount++;
            if (DetailedHistoryEnabled)
            {
                MarkOperation(state, EventDebugOperationKind.Publish, subscriberCount, capacity, true);
            }
        }

        internal static void RecordSafePublish<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.SafePublishCount++;
            if (DetailedHistoryEnabled)
            {
                MarkOperation(state, EventDebugOperationKind.SafePublish, subscriberCount, capacity, true);
            }
        }

        internal static void RecordResize<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.ResizeCount++;
            MarkOperation(state, EventDebugOperationKind.Resize, subscriberCount, capacity, true);
        }

        internal static void RecordClear<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.ClearCount++;
            MarkOperation(state, EventDebugOperationKind.Clear, subscriberCount, capacity, true);
        }

        internal static void RecordMutationRejected<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.MutationRejectedCount++;
            MarkOperation(state, EventDebugOperationKind.MutationRejected, subscriberCount, capacity, true);
        }

        internal static void RecordHandlerException<T>(int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.HandlerExceptionCount++;
            MarkOperation(state, EventDebugOperationKind.HandlerException, subscriberCount, capacity, true);
        }

        internal static void RecordDeferredMutation<T>(int pendingCount, int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.DeferredMutationCount++;
            state.PeakPendingCount = Math.Max(state.PeakPendingCount, pendingCount);
            MarkOperation(state, EventDebugOperationKind.DeferredMutation, subscriberCount, capacity, DetailedHistoryEnabled);
        }

        internal static void RecordFlush<T>(int flushedCount, int subscriberCount, int capacity) where T : struct, IEventArgs
        {
            State state = GetState<T>();
            state.FlushCount++;
            state.PeakPendingCount = Math.Max(state.PeakPendingCount, flushedCount);
            MarkOperation(state, EventDebugOperationKind.Flush, subscriberCount, capacity, DetailedHistoryEnabled);
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
                subscribers = BuildSubscribers(state);
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
                state.PeakSubscriberCount = GetSubscriberCount(state);
                state.PublishCount = 0;
                state.SafePublishCount = 0;
                state.SubscribeCount = 0;
                state.UnsubscribeCount = 0;
                state.ResizeCount = 0;
                state.ClearCount = 0;
                state.MutationRejectedCount = 0;
                state.HandlerExceptionCount = 0;
                state.DeferredMutationCount = 0;
                state.FlushCount = 0;
                state.PeakPendingCount = 0;
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

        private static State GetOrCreateState(Type eventType)
        {
            if (!_states.TryGetValue(eventType, out State state))
            {
                state = new State(eventType);
                _states.Add(eventType, state);
                _registrationOrder.Add(eventType);
            }

            return state;
        }

        private static EventDebugSummary BuildSummary(State state)
        {
            int subscriberCount = GetSubscriberCount(state);
            int capacity = GetCapacity(state);
            int emptySubscriberCount = state.EmptySubscriberCountProvider?.Invoke() ?? 0;
            int inSubscriberCount = state.InSubscriberCountProvider?.Invoke() ?? 0;

            return new EventDebugSummary(
                state.EventType,
                state.PayloadSubscriberCountProvider != null || state.EmptySubscriberCountProvider != null,
                subscriberCount,
                state.PeakSubscriberCount,
                capacity,
                emptySubscriberCount,
                inSubscriberCount,
                state.PublishCount,
                state.SafePublishCount,
                state.SubscribeCount,
                state.UnsubscribeCount,
                state.ResizeCount,
                state.ClearCount,
                state.MutationRejectedCount,
                state.HandlerExceptionCount,
                state.DeferredMutationCount,
                state.FlushCount,
                state.PeakPendingCount,
                state.LastOperationFrame,
                state.LastOperationTicksUtc);
        }

        private static int GetSubscriberCount(State state)
        {
            return (state.PayloadSubscriberCountProvider?.Invoke() ?? 0) +
                   (state.EmptySubscriberCountProvider?.Invoke() ?? 0);
        }

        private static int GetCapacity(State state)
        {
            return (state.PayloadCapacityProvider?.Invoke() ?? 0) +
                   (state.EmptyCapacityProvider?.Invoke() ?? 0);
        }

        private static EventDebugSubscriberInfo[] BuildSubscribers(State state)
        {
            EventDebugSubscriberInfo[] payloadSubscribers = state.PayloadSubscribersProvider?.Invoke() ?? Array.Empty<EventDebugSubscriberInfo>();
            EventDebugSubscriberInfo[] emptySubscribers = state.EmptySubscribersProvider?.Invoke() ?? Array.Empty<EventDebugSubscriberInfo>();

            if (payloadSubscribers.Length == 0)
            {
                return emptySubscribers;
            }

            if (emptySubscribers.Length == 0)
            {
                return payloadSubscribers;
            }

            EventDebugSubscriberInfo[] subscribers = new EventDebugSubscriberInfo[payloadSubscribers.Length + emptySubscribers.Length];
            Array.Copy(payloadSubscribers, 0, subscribers, 0, payloadSubscribers.Length);
            Array.Copy(emptySubscribers, 0, subscribers, payloadSubscribers.Length, emptySubscribers.Length);
            return subscribers;
        }

        private static void MarkOperation(State state, EventDebugOperationKind kind, int subscriberCount, int capacity, bool recordHistory)
        {
            int frame = Time.frameCount;
            long ticksUtc = DateTime.UtcNow.Ticks;

            state.LastOperationFrame = frame;
            state.LastOperationTicksUtc = ticksUtc;

            if (!recordHistory)
            {
                return;
            }

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
