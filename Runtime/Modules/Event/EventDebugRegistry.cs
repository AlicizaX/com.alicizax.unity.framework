#if UNITY_EDITOR
using System;
using UnityEngine;

namespace AlicizaX
{
    internal enum EventDebugOperationKind : byte
    {
        Subscribe,
        Unsubscribe,
        Resize,
        Clear,
        HandlerException
    }

    internal struct EventDebugCounters
    {
        internal long PublishCount;
        internal long SubscribeCount;
        internal long UnsubscribeCount;
        internal long HandlerExceptionCount;
        internal int ResizeCount;
        internal int ClearCount;
        internal int PeakSubscriberCount;
        internal int LastOperationFrame;
        internal long LastOperationTicksUtc;
    }

    internal readonly struct EventDebugSummary
    {
        internal readonly Type EventType;
        internal readonly bool Parameterless;
        internal readonly int SubscriberCount;
        internal readonly int Capacity;
        internal readonly EventDebugCounters Counters;

        internal EventDebugSummary(Type eventType, bool parameterless, int subscriberCount, int capacity, EventDebugCounters counters)
        {
            EventType = eventType;
            Parameterless = parameterless;
            SubscriberCount = subscriberCount;
            Capacity = capacity;
            Counters = counters;
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

        private static readonly EventDebugOperationRecord[] _history = new EventDebugOperationRecord[HistoryCapacity];
        private static int _recordingRequestCount;
        private static int _historyWriteIndex;
        private static int _historyCount;

        internal static bool Recording => _recordingRequestCount > 0;

        internal static void BeginRecording()
        {
            _recordingRequestCount++;
        }

        internal static void EndRecording()
        {
            _recordingRequestCount--;
        }

        internal static void RecordSubscribe(EventChannel channel, int subscriberCount, int capacity)
        {
            channel.Counters.SubscribeCount++;
            channel.Counters.PeakSubscriberCount = Math.Max(channel.Counters.PeakSubscriberCount, subscriberCount);
            Record(channel, EventDebugOperationKind.Subscribe, subscriberCount, capacity);
        }

        internal static void RecordUnsubscribe(EventChannel channel, int subscriberCount, int capacity)
        {
            channel.Counters.UnsubscribeCount++;
            Record(channel, EventDebugOperationKind.Unsubscribe, subscriberCount, capacity);
        }

        internal static void RecordPublish(EventChannel channel)
        {
            channel.Counters.PublishCount++;
        }

        internal static void RecordResize(EventChannel channel, int subscriberCount, int capacity)
        {
            channel.Counters.ResizeCount++;
            Record(channel, EventDebugOperationKind.Resize, subscriberCount, capacity);
        }

        internal static void RecordClear(EventChannel channel, int subscriberCount, int capacity)
        {
            channel.Counters.ClearCount++;
            Record(channel, EventDebugOperationKind.Clear, subscriberCount, capacity);
        }

        internal static void RecordHandlerException(EventChannel channel, int subscriberCount, int capacity)
        {
            channel.Counters.HandlerExceptionCount++;
            Record(channel, EventDebugOperationKind.HandlerException, subscriberCount, capacity);
        }

        internal static EventDebugSummary[] GetSummaries()
        {
            var summaries = new EventDebugSummary[EventChannel.Channels.Count];
            for (int i = 0; i < summaries.Length; i++)
            {
                summaries[i] = EventChannel.Channels[i].CreateDebugSummary();
            }

            return summaries;
        }

        internal static bool TryGetDetails(Type eventType, out EventDebugSummary summary, out EventDebugSubscriberInfo[] subscribers)
        {
            foreach (EventChannel channel in EventChannel.Channels)
            {
                if (channel.EventType != eventType) continue;
                summary = channel.CreateDebugSummary();
                subscribers = channel.CreateDebugSubscribers();
                return true;
            }

            summary = default;
            subscribers = Array.Empty<EventDebugSubscriberInfo>();
            return false;
        }

        internal static EventDebugOperationRecord[] GetRecentOperations()
        {
            var result = new EventDebugOperationRecord[_historyCount];
            for (int i = 0; i < _historyCount; i++)
            {
                result[i] = _history[(_historyWriteIndex - 1 - i + HistoryCapacity) % HistoryCapacity];
            }

            return result;
        }

        internal static void ResetStats()
        {
            foreach (EventChannel channel in EventChannel.Channels)
            {
                channel.Counters = default;
                channel.Counters.PeakSubscriberCount = channel.CreateDebugSummary().SubscriberCount;
            }

            _historyWriteIndex = 0;
            _historyCount = 0;
        }

        private static void Record(EventChannel channel, EventDebugOperationKind kind, int subscriberCount, int capacity)
        {
            int frame = Time.frameCount;
            long ticksUtc = DateTime.UtcNow.Ticks;
            channel.Counters.LastOperationFrame = frame;
            channel.Counters.LastOperationTicksUtc = ticksUtc;
            _history[_historyWriteIndex] = new EventDebugOperationRecord(channel.EventType, kind, frame, ticksUtc, subscriberCount, capacity);
            _historyWriteIndex = (_historyWriteIndex + 1) % HistoryCapacity;
            if (_historyCount < HistoryCapacity) _historyCount++;
        }
    }
}
#endif
