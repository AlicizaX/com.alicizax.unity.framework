#if UNITY_EDITOR
using System;
using System.Diagnostics;
using Cysharp.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace AlicizaX
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Event Benchmark")]
    public sealed class EventBenchmark : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private int subscriberCount = 10000;
        [SerializeField] private int loopCount = 100000;
        [SerializeField] private int publishLoopCount = 10000;
        [SerializeField] private int prewarmCapacity = 10000;
        [SerializeField] private bool releaseLikeMode = true;
        [SerializeField] private bool collectBeforeEachCase = true;
        [SerializeField] private bool logEachCase = true;
        [SerializeField] private bool logMemoryDelta = true;
        [SerializeField] private Text text;
        [SerializeField] private int maxCapturedLogChars = 128 * 1024;

        private static readonly ProfilerMarker s_TotalMarker = new ProfilerMarker("EventBenchmark.Total");
        private static readonly ProfilerMarker s_PayloadMarker = new ProfilerMarker("EventBenchmark.Payload");
        private static readonly ProfilerMarker s_EmptyMarker = new ProfilerMarker("EventBenchmark.Empty");

        private readonly Stopwatch m_Stopwatch = new Stopwatch();
        private Utf16ValueStringBuilder m_LogBuilder;
        private bool m_LogBuilderCreated;
        private int m_FailCount;
        private int m_CaseCount;
        private long m_CaseAllocBefore;
        private long m_CaseAllocAfter;
        private long m_CaseMemoryBefore;
        private long m_CaseMemoryAfter;
        private int m_Gen0Before;
        private int m_Gen1Before;
        private int m_Gen2Before;
        private int m_Gen0After;
        private int m_Gen1After;
        private int m_Gen2After;
        private EventRuntimeHandle[] m_Handles;
        private Action[] m_EmptyHandlers;
        private InEventHandler<BenchmarkPayloadEvent>[] m_PayloadHandlers;
        private EmptyListener[] m_EmptyListeners;
        private PayloadListener[] m_PayloadListeners;

        private void OnEnable()
        {
            ClearCapturedConsoleOutput();
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            m_LogBuilder.Dispose();
            m_LogBuilderCreated = false;
            ClearBenchmarkEvents();
        }

        private void Start()
        {
            if (runOnStart)
                RunAll();
        }

        [ContextMenu("Run Event Benchmark")]
        public void RunAll()
        {
            bool previousReleaseLikeMode = EventDebugRegistry.BenchmarkReleaseLikeMode;
            EventDebugRegistry.BenchmarkReleaseLikeMode = releaseLikeMode;

            ClearCapturedConsoleOutput();
            ClearBenchmarkEvents();
            EnsureBuffers(Math.Max(subscriberCount, loopCount));
            EventBus.EnsureCapacity<BenchmarkPayloadEvent>(Math.Max(prewarmCapacity, subscriberCount));
            EventBus.EnsureCapacity<BenchmarkEmptyEvent>(Math.Max(prewarmCapacity, subscriberCount));
            m_FailCount = 0;
            m_CaseCount = 0;

            try
            {
                using (s_TotalMarker.Auto())
                {
                    RunCase("Payload Subscribe", RunPayloadSubscribe, subscriberCount, ClearPayloadEvent, ClearPayloadEvent);
                    RunCase("Payload Unsubscribe", RunPayloadUnsubscribe, subscriberCount, EnsurePayloadSubscribers, ClearPayloadEvent);
                    RunCase("Payload Subscribe Unsubscribe Loop", RunPayloadSubscribeUnsubscribeLoop, loopCount, ClearPayloadEvent, ClearPayloadEvent);
                    RunCase("Payload Publish", RunPayloadPublish, publishLoopCount, EnsurePayloadSubscribers, ClearPayloadEvent, (long)publishLoopCount * subscriberCount);
                    RunCase("Empty Subscribe", RunEmptySubscribe, subscriberCount, ClearEmptyEvent, ClearEmptyEvent);
                    RunCase("Empty Unsubscribe", RunEmptyUnsubscribe, subscriberCount, EnsureEmptySubscribers, ClearEmptyEvent);
                    RunCase("Empty Subscribe Unsubscribe Loop", RunEmptySubscribeUnsubscribeLoop, loopCount, ClearEmptyEvent, ClearEmptyEvent);
                    RunCase("Empty Publish", RunEmptyPublish, publishLoopCount, EnsureEmptySubscribers, ClearEmptyEvent, (long)publishLoopCount * subscriberCount);
                }

                ClearBenchmarkEvents();
                Debug.Log(BuildLog("Event benchmark finished. releaseLikeMode=", releaseLikeMode, ", cases=", m_CaseCount, ", fails=", m_FailCount));
            }
            finally
            {
                ClearBenchmarkEvents();
                EventDebugRegistry.BenchmarkReleaseLikeMode = previousReleaseLikeMode;
            }
        }

        [ContextMenu("Copy Captured Console Output")]
        public void CopyCapturedConsoleOutput()
        {
            EnsureLogBuilder();
            string text = m_LogBuilder.ToString();
            GUIUtility.systemCopyBuffer = text;
            Debug.Log(BuildLog("EventBenchmark copied console output chars=", text.Length, ", max=", maxCapturedLogChars));
        }

        [ContextMenu("Clear Captured Console Output")]
        public void ClearCapturedConsoleOutput()
        {
            m_LogBuilder.Dispose();
            m_LogBuilder = ZString.CreateStringBuilder();
            m_LogBuilderCreated = true;
        }

        private void EnsureLogBuilder()
        {
            if (!m_LogBuilderCreated)
            {
                m_LogBuilder = ZString.CreateStringBuilder();
                m_LogBuilderCreated = true;
            }
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            EnsureLogBuilder();
            if (m_LogBuilder.Length >= maxCapturedLogChars)
                return;

            m_LogBuilder.Append('[');
            m_LogBuilder.Append(type);
            m_LogBuilder.Append("] ");
            m_LogBuilder.Append(condition);
            m_LogBuilder.AppendLine();

            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                m_LogBuilder.Append(stackTrace);
                m_LogBuilder.AppendLine();
            }
        }

        private void RunCase(string caseName, Action action, long operationCount, Action setup = null, Action cleanup = null, long callbackCount = 0)
        {
            m_CaseCount++;
            setup?.Invoke();

            if (collectBeforeEachCase)
                ForceCollect();

            m_CaseMemoryBefore = GC.GetTotalMemory(false);
            m_CaseAllocBefore = GetAllocatedBytesForCurrentThread();
            m_Gen0Before = GC.CollectionCount(0);
            m_Gen1Before = GC.CollectionCount(1);
            m_Gen2Before = GC.CollectionCount(2);
            bool profilerWasEnabled = Profiler.enabled;
            Profiler.enabled = true;
            Profiler.BeginSample(caseName);
            try
            {
                m_Stopwatch.Restart();
                action();
            }
            finally
            {
                if (m_Stopwatch.IsRunning)
                    m_Stopwatch.Stop();

                Profiler.EndSample();
                Profiler.enabled = profilerWasEnabled;
            }

            m_CaseAllocAfter = GetAllocatedBytesForCurrentThread();
            m_CaseMemoryAfter = GC.GetTotalMemory(false);
            m_Gen0After = GC.CollectionCount(0);
            m_Gen1After = GC.CollectionCount(1);
            m_Gen2After = GC.CollectionCount(2);

            if (logEachCase)
                LogCaseResult(caseName, operationCount, callbackCount);

            cleanup?.Invoke();
        }

        private long GetAllocatedBytesForCurrentThread()
        {
            return logMemoryDelta ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        }

        private void LogCaseResult(string caseName, long operationCount, long callbackCount)
        {
            double totalMs = m_Stopwatch.Elapsed.TotalMilliseconds;
            double perOperationNs = operationCount > 0 ? totalMs * 1000000.0d / operationCount : 0d;
            string summary = BuildLog("[EventBenchmark] ", caseName, " Total time: ", totalMs, "ms");
            Debug.Log(summary);
            Debug.Log(BuildLog("[EventBenchmark] ", caseName, " Per event: ", perOperationNs, "ns"));

            if (callbackCount > 0)
            {
                double perCallbackNs = totalMs * 1000000.0d / callbackCount;
                Debug.Log(BuildLog("[EventBenchmark] ", caseName, " Per callback: ", perCallbackNs, "ns"));
            }

            if (logMemoryDelta)
            {
                Debug.Log(BuildLog(
                    "[EventBenchmark] ", caseName,
                    " GC threadAlloc: ", m_CaseAllocAfter - m_CaseAllocBefore,
                    " bytes, totalMemoryDelta: ", m_CaseMemoryAfter - m_CaseMemoryBefore,
                    " bytes, gc0: ", m_Gen0After - m_Gen0Before,
                    ", gc1: ", m_Gen1After - m_Gen1Before,
                    ", gc2: ", m_Gen2After - m_Gen2Before));
            }

            if (text != null)
                text.text = summary;
        }

        private void ForceCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void RunPayloadSubscribe()
        {
            using (s_PayloadMarker.Auto())
            {
                for (int i = 0; i < subscriberCount; i++)
                    m_Handles[i] = EventBus.Subscribe<BenchmarkPayloadEvent>(m_PayloadHandlers[i]);

                AssertEqual(EventBus.GetSubscriberCount<BenchmarkPayloadEvent>(), subscriberCount, "payload subscribe count mismatch");
            }
        }

        private void RunPayloadUnsubscribe()
        {
            using (s_PayloadMarker.Auto())
            {
                for (int i = subscriberCount - 1; i >= 0; i--)
                {
                    m_Handles[i].Dispose();
                    m_Handles[i] = default;
                }

                AssertEqual(EventBus.GetSubscriberCount<BenchmarkPayloadEvent>(), 0, "payload unsubscribe count mismatch");
            }
        }

        private void RunPayloadSubscribeUnsubscribeLoop()
        {
            using (s_PayloadMarker.Auto())
            {
                for (int i = 0; i < loopCount; i++)
                {
                    EventRuntimeHandle handle = EventBus.Subscribe<BenchmarkPayloadEvent>(m_PayloadHandlers[i % m_PayloadHandlers.Length]);
                    handle.Dispose();
                }

                AssertEqual(EventBus.GetSubscriberCount<BenchmarkPayloadEvent>(), 0, "payload subscribe unsubscribe loop count mismatch");
            }
        }

        private void RunPayloadPublish()
        {
            using (s_PayloadMarker.Auto())
            {
                BenchmarkPayloadEvent evt = new BenchmarkPayloadEvent(1);
                for (int i = 0; i < publishLoopCount; i++)
                    EventBus.Publish(in evt);

                AssertEqual(EventBus.GetSubscriberCount<BenchmarkPayloadEvent>(), subscriberCount, "payload publish subscriber count mismatch");
            }
        }

        private void RunEmptySubscribe()
        {
            using (s_EmptyMarker.Auto())
            {
                for (int i = 0; i < subscriberCount; i++)
                    m_Handles[i] = EventBus.Subscribe<BenchmarkEmptyEvent>(m_EmptyHandlers[i]);

                AssertEqual(EventBus.GetSubscriberCount<BenchmarkEmptyEvent>(), subscriberCount, "empty subscribe count mismatch");
            }
        }

        private void RunEmptyUnsubscribe()
        {
            using (s_EmptyMarker.Auto())
            {
                for (int i = subscriberCount - 1; i >= 0; i--)
                {
                    m_Handles[i].Dispose();
                    m_Handles[i] = default;
                }

                AssertEqual(EventBus.GetSubscriberCount<BenchmarkEmptyEvent>(), 0, "empty unsubscribe count mismatch");
            }
        }

        private void RunEmptySubscribeUnsubscribeLoop()
        {
            using (s_EmptyMarker.Auto())
            {
                for (int i = 0; i < loopCount; i++)
                {
                    EventRuntimeHandle handle = EventBus.Subscribe<BenchmarkEmptyEvent>(m_EmptyHandlers[i % m_EmptyHandlers.Length]);
                    handle.Dispose();
                }

                AssertEqual(EventBus.GetSubscriberCount<BenchmarkEmptyEvent>(), 0, "empty subscribe unsubscribe loop count mismatch");
            }
        }

        private void RunEmptyPublish()
        {
            using (s_EmptyMarker.Auto())
            {
                for (int i = 0; i < publishLoopCount; i++)
                    EventBus.Publish<BenchmarkEmptyEvent>();

                AssertEqual(EventBus.GetSubscriberCount<BenchmarkEmptyEvent>(), subscriberCount, "empty publish subscriber count mismatch");
            }
        }

        private void EnsurePayloadSubscribers()
        {
            EventBus.Clear<BenchmarkPayloadEvent>();
            for (int i = 0; i < subscriberCount; i++)
                m_Handles[i] = EventBus.Subscribe<BenchmarkPayloadEvent>(m_PayloadHandlers[i]);
        }

        private void EnsureEmptySubscribers()
        {
            EventBus.Clear<BenchmarkEmptyEvent>();
            for (int i = 0; i < subscriberCount; i++)
                m_Handles[i] = EventBus.Subscribe<BenchmarkEmptyEvent>(m_EmptyHandlers[i]);
        }

        private void ClearBenchmarkEvents()
        {
            EventBus.Clear<BenchmarkPayloadEvent>();
            EventBus.Clear<BenchmarkEmptyEvent>();
        }

        private void ClearPayloadEvent()
        {
            EventBus.Clear<BenchmarkPayloadEvent>();
        }

        private void ClearEmptyEvent()
        {
            EventBus.Clear<BenchmarkEmptyEvent>();
        }

        private void EnsureBuffers(int count)
        {
            if (m_Handles == null || m_Handles.Length < count)
                m_Handles = new EventRuntimeHandle[count];

            int handlerCount = Math.Max(1, subscriberCount);
            if (m_EmptyHandlers != null && m_EmptyHandlers.Length >= handlerCount &&
                m_PayloadHandlers != null && m_PayloadHandlers.Length >= handlerCount &&
                m_EmptyListeners != null && m_EmptyListeners.Length >= handlerCount &&
                m_PayloadListeners != null && m_PayloadListeners.Length >= handlerCount)
                return;

            m_EmptyHandlers = new Action[handlerCount];
            m_PayloadHandlers = new InEventHandler<BenchmarkPayloadEvent>[handlerCount];
            m_EmptyListeners = new EmptyListener[handlerCount];
            m_PayloadListeners = new PayloadListener[handlerCount];
            for (int i = 0; i < handlerCount; i++)
            {
                m_EmptyListeners[i] = new EmptyListener();
                m_PayloadListeners[i] = new PayloadListener();
                m_EmptyHandlers[i] = m_EmptyListeners[i].OnEvent;
                m_PayloadHandlers[i] = m_PayloadListeners[i].OnEvent;
            }
        }

        private void AssertEqual(int actual, int expected, string message)
        {
            if (actual == expected)
                return;

            m_FailCount++;
            Debug.LogError(BuildLog(message, " actual=", actual, ", expected=", expected));
        }

        private static string BuildLog(params object[] values)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                for (int i = 0; i < values.Length; i++)
                    builder.Append(values[i]);
                return builder.ToString();
            }
        }

        private readonly struct BenchmarkPayloadEvent : IEventArgs
        {
            public readonly int Value;

            public BenchmarkPayloadEvent(int value)
            {
                Value = value;
            }
        }

        private readonly struct BenchmarkEmptyEvent : IEventArgs
        {
        }

        private sealed class EmptyListener
        {
            private int m_InvokeCount;

            public void OnEvent()
            {
                m_InvokeCount++;
            }
        }

        private sealed class PayloadListener
        {
            private int m_InvokeCount;

            public void OnEvent(in BenchmarkPayloadEvent evt)
            {
                m_InvokeCount += evt.Value;
            }
        }
    }
}
#endif
