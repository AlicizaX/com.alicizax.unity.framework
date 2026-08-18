#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using AlicizaX;
using Cysharp.Text;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AlicizaX.Timer.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Timer Benchmark")]
    public sealed class TimerBenchmark : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool includeFireCases = true;
        [SerializeField] private int timerCount = 10000;
        [SerializeField] private int loopCount = 100000;
        [SerializeField] private int fireTimerCount = 1024;
        [SerializeField] private int burstFireCount = 4096;
        [SerializeField] private int tickLoopCount = 1024;
        [SerializeField] private float controlDuration = 10f;
        [SerializeField] private float fireDelay = 0.001f;
        [SerializeField] private float fireWaitSeconds = 0.05f;
        [SerializeField] private float burstFireDelay = 0.001f;
        [SerializeField] private bool logEachCase = true;
        [SerializeField] private bool logMemoryDelta = true;
        [SerializeField] private int maxCapturedLogChars = 128 * 1024;

        private static readonly ProfilerMarker s_TotalMarker = new ProfilerMarker("TimerBenchmark.Total");
        private static readonly ProfilerMarker s_AddRemoveMarker = new ProfilerMarker("TimerBenchmark.AddRemove");
        private static readonly ProfilerMarker s_ControlMarker = new ProfilerMarker("TimerBenchmark.Control");
        private static readonly ProfilerMarker s_WheelMarker = new ProfilerMarker("TimerBenchmark.Wheel");
        private static readonly ProfilerMarker s_DebugMarker = new ProfilerMarker("TimerBenchmark.Debug");
        private static readonly ProfilerMarker s_BurstMarker = new ProfilerMarker("TimerBenchmark.BurstTick");

        private static readonly TimerHandlerNoArgs s_NoOpHandler = OnNoOp;
        private static readonly TimerHandlerNoArgs s_CountHandler = OnCount;
        private static readonly TimerHandlerNoArgs s_RemoveSelfHandler = OnRemoveSelf;
        private static readonly Action<BenchmarkArg> s_GenericHandler = OnGeneric;
        private static readonly Action<BenchmarkArg> s_GenericCountHandler = OnGenericCount;

        private static int s_CallbackCount;
        private static ITimerService s_CallbackService;
        private static ulong s_CallbackHandle;

        private readonly Stopwatch m_Stopwatch = new Stopwatch();
        private readonly BenchmarkArg m_GenericArg = new BenchmarkArg();
        private readonly float[] m_WheelDelays =
        {
            0.001f, 0.05f, 0.2f, 1f, 10f, 60f, 300f, 3600f
        };

        private Utf16ValueStringBuilder m_LogBuilder;
        private ITimerService m_Service;
        private ITimerDebugService m_DebugService;
        private IServiceTickable m_Tickable;
        private ulong[] m_Handles;
        private TimerDebugInfo[] m_InfoBuffer;
        private Coroutine m_Routine;
        private int m_FailCount;
        private int m_CaseCount;
        private bool m_LogBuilderCreated;
        private bool m_Running;
        private long m_CaseAllocBefore;
        private long m_CaseAllocAfter;

        private void OnEnable()
        {
            ClearCapturedConsoleOutput();
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            m_Running = false;
            ClearAllTimers();
            m_LogBuilder.Dispose();
            m_LogBuilderCreated = false;
        }

        private void Start()
        {
            if (runOnStart)
                RunAll();
        }

        [ContextMenu("Run Timer Benchmark")]
        public void RunAll()
        {
            if (m_Running)
                return;

            if (m_Routine != null)
                StopCoroutine(m_Routine);

            m_Routine = StartCoroutine(RunAllRoutine());
        }

        [ContextMenu("Copy Captured Console Output")]
        public void CopyCapturedConsoleOutput()
        {
            EnsureLogBuilder();
            string text = m_LogBuilder.ToString();
            GUIUtility.systemCopyBuffer = text;
            Debug.Log(BuildLog("TimerBenchmark copied console output chars=", text.Length, ", max=", maxCapturedLogChars));
        }

        [ContextMenu("Clear Captured Console Output")]
        public void ClearCapturedConsoleOutput()
        {
            m_LogBuilder.Dispose();
            m_LogBuilder = ZString.CreateStringBuilder();
            m_LogBuilderCreated = true;
        }

        private IEnumerator RunAllRoutine()
        {
            m_Running = true;
            ClearCapturedConsoleOutput();
            EnsureService();
            if (m_Service == null)
            {
                m_Running = false;
                m_Routine = null;
                yield break;
            }

            m_FailCount = 0;
            m_CaseCount = 0;
            EnsureHandleBuffer(Math.Max(Math.Max(Math.Max(timerCount, fireTimerCount), burstFireCount), 8));
            EnsureInfoBuffer(Math.Max(Math.Max(timerCount, fireTimerCount), burstFireCount) + 16);
            ClearAllTimers();

            using (s_TotalMarker.Auto())
            {
                RunCase("Add/Remove OneShot Hot Loop", RunAddRemoveOneShotHotLoop);
                RunCase("Add/Remove Loop Hot Loop", RunAddRemoveLoopHotLoop);
                RunCase("Generic Add/Remove Hot Loop", RunGenericAddRemoveHotLoop);
                RunCase("Add Unscaled OneShot", RunAddUnscaledOneShot);
                RunCase("Stop/Resume", RunStopResume);
                RunCase("Restart", RunRestart);
                RunCase("GetLeftTime/IsRunning", RunQueryHotLoop);
                RunCase("Mixed Delay Wheel Insert", RunMixedDelayWheelInsert);
                RunCase("Page Growth", RunPageGrowth);
                RunCase("Invalid Handle Guards", RunInvalidHandleGuards);
                RunCase("Null Callback Guard", RunNullCallbackGuard);
                RunCase("Tick Idle", RunTickIdle);
                RunCase("Tick With Pending Timers", RunTickWithPendingTimers);
                RunCase("GetStatistics", RunGetStatistics);
                RunCase("GetAllTimers Buffer", RunGetAllTimersBuffer);
                RunCase("Handle Reuse After Remove", RunHandleReuseAfterRemove);
            }

            if (includeFireCases)
            {
                if (Application.isPlaying)
                {
                    yield return RunFireCase("Fire OneShot", RunFireOneShot);
                    yield return RunFireCase("Fire Loop Callbacks", RunFireLoopCallbacks);
                    yield return RunFireCase("Generic Fire", RunGenericFire);
                    yield return RunFireCase("Remove During Callback", RunRemoveDuringCallback);
                    yield return RunFireCase("Burst Same-Tick OneShot", RunBurstSameTickOneShot);
                    yield return RunFireCase("Burst Same-Tick Loop", RunBurstSameTickLoop);
                }
                else
                {
                    Debug.Log("TimerBenchmark skipped fire cases outside Play Mode.");
                }
            }

            ClearAllTimers();
            Debug.Log(BuildLog("Timer benchmark finished. cases=", m_CaseCount, ", fails=", m_FailCount));
            m_Running = false;
            m_Routine = null;
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

        private void EnsureService()
        {
            if (m_Service != null)
                return;

            if (AppServices.HasWorld && AppServices.App.TryGet(out m_Service))
            {
                BindServiceInterfaces();
                return;
            }

            if (!AppServices.HasWorld && GetComponent<AppServiceRoot>() == null)
                gameObject.AddComponent<BenchmarkAppRoot>();

            if (GetComponent<TimerComponent>() == null)
                gameObject.AddComponent<TimerComponent>();

            if (!AppServices.HasWorld || !AppServices.App.TryGet(out m_Service))
            {
                Debug.LogError("TimerBenchmark requires TimerComponent registration.");
                return;
            }

            BindServiceInterfaces();
        }

        private void BindServiceInterfaces()
        {
            m_DebugService = m_Service as ITimerDebugService;
            m_Tickable = m_Service as IServiceTickable;
            if (m_DebugService == null)
                Debug.LogError("TimerBenchmark requires ITimerDebugService.");
            if (m_Tickable == null)
                Debug.LogError("TimerBenchmark requires IServiceTickable.");
        }

        private void RunCase(string caseName, Action action)
        {
            m_CaseCount++;
            m_CaseAllocBefore = GetAllocatedBytesForCurrentThread();
            m_CaseAllocAfter = m_CaseAllocBefore;
            m_Stopwatch.Restart();
            action();
            if (m_Stopwatch.IsRunning)
            {
                m_Stopwatch.Stop();
                m_CaseAllocAfter = GetAllocatedBytesForCurrentThread();
            }

            LogCase(caseName);
        }

        private IEnumerator RunFireCase(string caseName, Func<IEnumerator> action)
        {
            m_CaseCount++;
            m_CaseAllocBefore = GetAllocatedBytesForCurrentThread();
            m_CaseAllocAfter = m_CaseAllocBefore;
            m_Stopwatch.Restart();
            yield return action();
            if (m_Stopwatch.IsRunning)
            {
                m_Stopwatch.Stop();
                m_CaseAllocAfter = GetAllocatedBytesForCurrentThread();
            }

            LogCase(caseName);
        }

        private void LogCase(string caseName)
        {
            if (!logEachCase)
                return;

            if (logMemoryDelta)
                Debug.Log(BuildLog("[TimerBenchmark] ", caseName, " ms=", m_Stopwatch.Elapsed.TotalMilliseconds, " gcAlloc=", m_CaseAllocAfter - m_CaseAllocBefore));
            else
                Debug.Log(BuildLog("[TimerBenchmark] ", caseName, " ms=", m_Stopwatch.Elapsed.TotalMilliseconds));
        }

        private void RestartCaseMeasure()
        {
            m_CaseAllocBefore = GetAllocatedBytesForCurrentThread();
            m_CaseAllocAfter = m_CaseAllocBefore;
            m_Stopwatch.Restart();
        }

        private void StopCaseMeasure()
        {
            m_Stopwatch.Stop();
            m_CaseAllocAfter = GetAllocatedBytesForCurrentThread();
        }

        private long GetAllocatedBytesForCurrentThread()
        {
            return logMemoryDelta ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        }

        private void RunAddRemoveOneShotHotLoop()
        {
            using (s_AddRemoveMarker.Auto())
            {
                ClearAllTimers();
                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    ulong handle = m_Service.AddTimer(s_NoOpHandler, controlDuration);
                    AssertNotZero(handle, "one-shot add returned invalid handle");
                    m_Service.RemoveTimer(handle);
                }

                StopCaseMeasure();
                AssertActiveCount(0, "one-shot add/remove left active timers");
            }
        }

        private void RunAddRemoveLoopHotLoop()
        {
            using (s_AddRemoveMarker.Auto())
            {
                ClearAllTimers();
                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    ulong handle = m_Service.AddTimer(s_NoOpHandler, controlDuration, true);
                    AssertNotZero(handle, "loop add returned invalid handle");
                    m_Service.RemoveTimer(handle);
                }

                StopCaseMeasure();
                AssertActiveCount(0, "loop add/remove left active timers");
            }
        }

        private void RunGenericAddRemoveHotLoop()
        {
            using (s_AddRemoveMarker.Auto())
            {
                ClearAllTimers();
                m_GenericArg.Value = 0;
                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    ulong handle = m_Service.AddTimer(s_GenericHandler, m_GenericArg, controlDuration);
                    AssertNotZero(handle, "generic add returned invalid handle");
                    m_Service.RemoveTimer(handle);
                }

                StopCaseMeasure();
                AssertActiveCount(0, "generic add/remove left active timers");
            }
        }

        private void RunAddUnscaledOneShot()
        {
            using (s_AddRemoveMarker.Auto())
            {
                int count = Math.Min(timerCount, m_Handles.Length);
                ClearAllTimers();
                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                {
                    m_Handles[i] = m_Service.AddTimer(s_NoOpHandler, controlDuration, false, true);
                    AssertNotZero(m_Handles[i], "unscaled add returned invalid handle");
                    AssertTrue(m_Service.IsRunning(m_Handles[i]), "unscaled timer is not running");
                }

                AssertActiveCount(count, "unscaled add active count mismatch");
                for (int i = 0; i < count; i++)
                    m_Service.RemoveTimer(m_Handles[i]);
                StopCaseMeasure();
                AssertActiveCount(0, "unscaled remove left active timers");
            }
        }

        private void RunStopResume()
        {
            using (s_ControlMarker.Auto())
            {
                ClearAllTimers();
                ulong handle = m_Service.AddTimer(s_NoOpHandler, controlDuration);
                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    m_Service.Stop(handle);
                    m_Service.Resume(handle);
                }

                StopCaseMeasure();
                AssertTrue(m_Service.IsRunning(handle), "timer is not running after stop/resume");
                AssertTrue(m_Service.GetLeftTime(handle) > 0f, "timer left time was cleared by stop/resume");
                m_Service.RemoveTimer(handle);
            }
        }

        private void RunRestart()
        {
            using (s_ControlMarker.Auto())
            {
                ClearAllTimers();
                ulong handle = m_Service.AddTimer(s_NoOpHandler, controlDuration);
                m_Service.Stop(handle);
                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                    m_Service.Restart(handle);
                StopCaseMeasure();

                AssertTrue(m_Service.IsRunning(handle), "timer is not running after restart");
                float leftTime = m_Service.GetLeftTime(handle);
                AssertTrue(leftTime > controlDuration * 0.5f, "restart did not restore remaining time");
                m_Service.RemoveTimer(handle);
            }
        }

        private void RunQueryHotLoop()
        {
            using (s_ControlMarker.Auto())
            {
                ClearAllTimers();
                ulong handle = m_Service.AddTimer(s_NoOpHandler, controlDuration);
                bool running = false;
                float leftTime = 0f;
                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    running = m_Service.IsRunning(handle);
                    leftTime = m_Service.GetLeftTime(handle);
                }

                StopCaseMeasure();
                AssertTrue(running, "query hot loop saw timer as not running");
                AssertTrue(leftTime > 0f, "query hot loop saw zero left time");
                m_Service.RemoveTimer(handle);
            }
        }

        private void RunMixedDelayWheelInsert()
        {
            using (s_WheelMarker.Auto())
            {
                int count = Math.Min(timerCount, m_Handles.Length);
                ClearAllTimers();
                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                {
                    float delay = m_WheelDelays[i & (m_WheelDelays.Length - 1)];
                    bool isLoop = (i & 1) == 0;
                    bool isUnscaled = (i & 2) == 0;
                    m_Handles[i] = m_Service.AddTimer(s_NoOpHandler, delay, isLoop, isUnscaled);
                    AssertNotZero(m_Handles[i], "mixed wheel add returned invalid handle");
                }

                StopCaseMeasure();
                AssertActiveCount(count, "mixed wheel insert active count mismatch");
                for (int i = 0; i < count; i++)
                    m_Service.RemoveTimer(m_Handles[i]);
                AssertActiveCount(0, "mixed wheel remove left active timers");
            }
        }

        private void RunPageGrowth()
        {
            using (s_WheelMarker.Auto())
            {
                int count = Math.Min(timerCount, m_Handles.Length);
                ClearAllTimers();
                GetStats(out _, out int capacityBefore, out _, out _);
                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                    m_Handles[i] = m_Service.AddTimer(s_NoOpHandler, controlDuration);
                StopCaseMeasure();

                GetStats(out int activeCount, out int capacityAfter, out int peakActiveCount, out int freeCount);
                AssertEqual(activeCount, count, "page growth active count mismatch");
                AssertTrue(capacityAfter >= count, "page growth did not expand capacity");
                AssertTrue(capacityAfter >= capacityBefore, "page growth shrunk capacity");
                AssertTrue(peakActiveCount >= count, "page growth peak active count mismatch");
                AssertTrue(freeCount >= 0, "page growth free count is invalid");

                for (int i = 0; i < count; i++)
                    m_Service.RemoveTimer(m_Handles[i]);
                AssertActiveCount(0, "page growth remove left active timers");
            }
        }

        private void RunInvalidHandleGuards()
        {
            using (s_ControlMarker.Auto())
            {
                ClearAllTimers();
                ulong staleHandle = m_Service.AddTimer(s_NoOpHandler, controlDuration);
                m_Service.RemoveTimer(staleHandle);

                RestartCaseMeasure();
                m_Service.Stop(0UL);
                m_Service.Resume(0UL);
                m_Service.Restart(0UL);
                m_Service.RemoveTimer(0UL);
                m_Service.Stop(staleHandle);
                m_Service.Resume(staleHandle);
                m_Service.Restart(staleHandle);
                m_Service.RemoveTimer(staleHandle);
                bool running = m_Service.IsRunning(staleHandle);
                float leftTime = m_Service.GetLeftTime(staleHandle);
                StopCaseMeasure();

                AssertTrue(!running, "stale handle was reported as running");
                AssertTrue(leftTime == 0f, "stale handle returned leftover time");
                AssertActiveCount(0, "invalid handle guards created timers");
            }
        }

        private void RunNullCallbackGuard()
        {
            ClearAllTimers();
            RestartCaseMeasure();
            ulong noArgsHandle = m_Service.AddTimer(null, controlDuration);
            ulong genericHandle = m_Service.AddTimer<BenchmarkArg>(null, m_GenericArg, controlDuration);
            StopCaseMeasure();

            AssertEqual((int)noArgsHandle, 0, "null no-args callback produced a handle");
            AssertEqual((int)genericHandle, 0, "null generic callback produced a handle");
            AssertActiveCount(0, "null callback created an active timer");
        }

        private void RunTickIdle()
        {
            using (s_WheelMarker.Auto())
            {
                ClearAllTimers();
                RestartCaseMeasure();
                TickService(tickLoopCount);
                StopCaseMeasure();
                AssertActiveCount(0, "idle tick created timers");
            }
        }

        private void RunTickWithPendingTimers()
        {
            using (s_WheelMarker.Auto())
            {
                int count = Math.Min(timerCount, m_Handles.Length);
                ClearAllTimers();
                for (int i = 0; i < count; i++)
                    m_Handles[i] = m_Service.AddTimer(s_NoOpHandler, 3600f, (i & 1) == 0, (i & 2) == 0);

                RestartCaseMeasure();
                TickService(tickLoopCount);
                StopCaseMeasure();

                AssertActiveCount(count, "pending tick changed active timer count");
                for (int i = 0; i < count; i++)
                    m_Service.RemoveTimer(m_Handles[i]);
            }
        }

        private void RunGetStatistics()
        {
            using (s_DebugMarker.Auto())
            {
                int count = Math.Min(64, m_Handles.Length);
                ClearAllTimers();
                for (int i = 0; i < count; i++)
                    m_Handles[i] = m_Service.AddTimer(s_NoOpHandler, controlDuration);

                RestartCaseMeasure();
                int activeCount = 0;
                int poolCapacity = 0;
                int peakActiveCount = 0;
                int freeCount = 0;
                for (int i = 0; i < loopCount; i++)
                    m_DebugService.GetStatistics(out activeCount, out poolCapacity, out peakActiveCount, out freeCount);
                StopCaseMeasure();

                AssertEqual(activeCount, count, "statistics active count mismatch");
                AssertTrue(poolCapacity >= count, "statistics capacity smaller than active count");
                AssertTrue(peakActiveCount >= count, "statistics peak smaller than active count");
                AssertTrue(freeCount == poolCapacity - count, "statistics free count mismatch");

                for (int i = 0; i < count; i++)
                    m_Service.RemoveTimer(m_Handles[i]);
            }
        }

        private void RunGetAllTimersBuffer()
        {
            using (s_DebugMarker.Auto())
            {
                int count = Math.Min(64, m_Handles.Length);
                ClearAllTimers();
                for (int i = 0; i < count; i++)
                    m_Handles[i] = m_Service.AddTimer(s_NoOpHandler, controlDuration, i == 0, i == 1);

                EnsureInfoBuffer(count);
                RestartCaseMeasure();
                int filled = 0;
                for (int i = 0; i < loopCount; i++)
                    filled = m_DebugService.GetAllTimers(m_InfoBuffer);
                StopCaseMeasure();

                AssertEqual(filled, count, "debug buffer fill count mismatch");
                AssertEqual(m_DebugService.GetAllTimers(null), 0, "null debug buffer should return 0");
                AssertEqual(m_DebugService.GetAllTimers(Array.Empty<TimerDebugInfo>()), 0, "empty debug buffer should return 0");

                bool foundRunning = false;
                bool foundLoop = false;
                bool foundUnscaled = false;
                for (int i = 0; i < filled; i++)
                {
                    if ((m_InfoBuffer[i].Flags & TimerDebugFlags.Running) != 0)
                        foundRunning = true;
                    if ((m_InfoBuffer[i].Flags & TimerDebugFlags.Loop) != 0)
                        foundLoop = true;
                    if ((m_InfoBuffer[i].Flags & TimerDebugFlags.Unscaled) != 0)
                        foundUnscaled = true;
                }

                AssertTrue(foundRunning, "debug buffer missed running flag");
                AssertTrue(foundLoop, "debug buffer missed loop flag");
                AssertTrue(foundUnscaled, "debug buffer missed unscaled flag");

                for (int i = 0; i < count; i++)
                    m_Service.RemoveTimer(m_Handles[i]);
            }
        }

        private void RunHandleReuseAfterRemove()
        {
            ClearAllTimers();
            ulong first = m_Service.AddTimer(s_NoOpHandler, controlDuration);
            m_Service.RemoveTimer(first);
            ulong second = m_Service.AddTimer(s_NoOpHandler, controlDuration);
            AssertTrue(first != 0UL && second != 0UL, "handle reuse produced an invalid handle");
            AssertTrue(first != second, "removed handle was reused without version change");
            AssertTrue(!m_Service.IsRunning(first), "old handle stayed valid after reuse");
            AssertTrue(m_Service.IsRunning(second), "new handle is not running");
            m_Service.RemoveTimer(second);
        }

        private IEnumerator RunFireOneShot()
        {
            int count = Math.Min(fireTimerCount, m_Handles.Length);
            ClearAllTimers();
            s_CallbackCount = 0;
            for (int i = 0; i < count; i++)
                m_Handles[i] = m_Service.AddTimer(s_CountHandler, fireDelay);

            RestartCaseMeasure();
            yield return WaitForFire();
            StopCaseMeasure();

            AssertEqual(s_CallbackCount, count, "one-shot fire callback count mismatch");
            AssertActiveCount(0, "one-shot fire left active timers");
        }

        private IEnumerator RunFireLoopCallbacks()
        {
            int count = Math.Min(fireTimerCount, m_Handles.Length);
            ClearAllTimers();
            s_CallbackCount = 0;
            for (int i = 0; i < count; i++)
                m_Handles[i] = m_Service.AddTimer(s_CountHandler, fireDelay, true);

            RestartCaseMeasure();
            yield return WaitForFire();
            StopCaseMeasure();

            AssertTrue(s_CallbackCount >= count, "loop fire did not invoke callbacks");
            AssertActiveCount(count, "loop fire changed active timer count");
            for (int i = 0; i < count; i++)
                m_Service.RemoveTimer(m_Handles[i]);
            AssertActiveCount(0, "loop fire remove left active timers");
        }

        private IEnumerator RunGenericFire()
        {
            int count = Math.Min(fireTimerCount, m_Handles.Length);
            ClearAllTimers();
            m_GenericArg.Value = 0;
            for (int i = 0; i < count; i++)
                m_Handles[i] = m_Service.AddTimer(s_GenericCountHandler, m_GenericArg, fireDelay);

            RestartCaseMeasure();
            yield return WaitForFire();
            StopCaseMeasure();

            AssertEqual(m_GenericArg.Value, count, "generic fire callback count mismatch");
            AssertActiveCount(0, "generic fire left active timers");
        }

        private IEnumerator RunRemoveDuringCallback()
        {
            ClearAllTimers();
            s_CallbackService = m_Service;
            s_CallbackHandle = m_Service.AddTimer(s_RemoveSelfHandler, fireDelay, true);
            AssertNotZero(s_CallbackHandle, "remove-during-callback add returned invalid handle");

            RestartCaseMeasure();
            yield return WaitForFire();
            StopCaseMeasure();

            AssertTrue(!m_Service.IsRunning(s_CallbackHandle), "self-removed timer is still running");
            AssertActiveCount(0, "self-removed timer stayed active");
            s_CallbackService = null;
            s_CallbackHandle = 0UL;
        }

        private IEnumerator RunBurstSameTickOneShot()
        {
            yield return RunIsolatedBurstTick("oneshot", false);
        }

        private IEnumerator RunBurstSameTickLoop()
        {
            yield return RunIsolatedBurstTick("loop", true);
        }

        private IEnumerator RunIsolatedBurstTick(string label, bool isLoop)
        {
            int count = Math.Max(1, Math.Min(burstFireCount, m_Handles.Length));
            float delay = burstFireDelay > 0.001f ? burstFireDelay : 0.001f;
            TimerService isolated = new TimerService(count);
            ITimerService service = isolated;
            ITimerDebugService debug = isolated;
            IServiceTickable tickable = isolated;
            s_CallbackCount = 0;

            for (int i = 0; i < count; i++)
            {
                m_Handles[i] = service.AddTimer(s_CountHandler, delay, isLoop, true);
                AssertNotZero(m_Handles[i], "burst add returned invalid handle");
            }

            debug.GetStatistics(out int setupActive, out _, out _, out _);
            AssertEqual(setupActive, count, "burst setup active count mismatch");

            float waitSeconds = delay + 0.004f;
            float endTime = Time.unscaledTime + waitSeconds;
            while (Time.unscaledTime < endTime)
                yield return null;

            RestartCaseMeasure();
            using (s_BurstMarker.Auto())
                tickable.Tick(0f);
            StopCaseMeasure();

            if (isLoop)
            {
                AssertEqual(s_CallbackCount, count, "burst loop tick callback count mismatch");
                debug.GetStatistics(out int activeAfter, out _, out _, out _);
                AssertEqual(activeAfter, count, "burst loop tick changed active count");
                for (int i = 0; i < count; i++)
                    service.RemoveTimer(m_Handles[i]);
            }
            else
            {
                AssertEqual(s_CallbackCount, count, "burst oneshot tick callback count mismatch");
                debug.GetStatistics(out int activeAfter, out _, out _, out _);
                AssertEqual(activeAfter, 0, "burst oneshot tick left active timers");
            }

            debug.GetStatistics(out int leftover, out _, out _, out _);
            AssertEqual(leftover, 0, "burst leftover timers after cleanup");
            Debug.Log(BuildLog("[TimerBenchmark] Burst ", label, " count=", count, " callbacks=", s_CallbackCount));
        }

        private IEnumerator WaitForFire()
        {
            float endTime = Time.unscaledTime + Mathf.Max(0.02f, fireWaitSeconds);
            while (Time.unscaledTime < endTime)
                yield return null;
        }

        private void TickService(int times)
        {
            for (int i = 0; i < times; i++)
                m_Tickable.Tick(0f);
        }

        private void ClearAllTimers()
        {
            if (m_Service == null || m_DebugService == null)
                return;

            EnsureInfoBuffer(16);
            while (true)
            {
                int count = m_DebugService.GetAllTimers(m_InfoBuffer);
                if (count <= 0)
                    break;

                for (int i = 0; i < count; i++)
                    m_Service.RemoveTimer(m_InfoBuffer[i].TimerHandle);

                if (count < m_InfoBuffer.Length)
                    break;

                EnsureInfoBuffer(m_InfoBuffer.Length << 1);
            }
        }

        private void GetStats(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount)
        {
            m_DebugService.GetStatistics(out activeCount, out poolCapacity, out peakActiveCount, out freeCount);
        }

        private void EnsureHandleBuffer(int count)
        {
            if (m_Handles == null || m_Handles.Length < count)
                m_Handles = new ulong[count];
        }

        private void EnsureInfoBuffer(int count)
        {
            if (m_InfoBuffer == null || m_InfoBuffer.Length < count)
                m_InfoBuffer = new TimerDebugInfo[count];
        }

        private void AssertActiveCount(int expected, string message)
        {
            GetStats(out int activeCount, out _, out _, out _);
            AssertEqual(activeCount, expected, message);
        }

        private void AssertNotZero(ulong handle, string message)
        {
            if (handle != 0UL)
                return;

            m_FailCount++;
            Debug.LogError(message);
        }

        private void AssertTrue(bool value, string message)
        {
            if (value)
                return;

            m_FailCount++;
            Debug.LogError(message);
        }

        private void AssertEqual(int actual, int expected, string message)
        {
            if (actual == expected)
                return;

            m_FailCount++;
            Debug.LogError(BuildLog(message, " actual=", actual, ", expected=", expected));
        }

        private static void OnNoOp()
        {
        }

        private static void OnCount()
        {
            s_CallbackCount++;
        }

        private static void OnRemoveSelf()
        {
            if (s_CallbackService != null)
                s_CallbackService.RemoveTimer(s_CallbackHandle);
        }

        private static void OnGeneric(BenchmarkArg arg)
        {
        }

        private static void OnGenericCount(BenchmarkArg arg)
        {
            arg.Value++;
        }

        private static string BuildLog(object a, string b, object c, string d, object e)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                builder.Append(a);
                builder.Append(b);
                builder.Append(c);
                builder.Append(d);
                builder.Append(e);
                return builder.ToString();
            }
        }

        private static string BuildLog(string a, object b, string c, object d)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                builder.Append(a);
                builder.Append(b);
                builder.Append(c);
                builder.Append(d);
                return builder.ToString();
            }
        }

        private static string BuildLog(string a, object b, string c, object d, string e, object f)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                builder.Append(a);
                builder.Append(b);
                builder.Append(c);
                builder.Append(d);
                builder.Append(e);
                builder.Append(f);
                return builder.ToString();
            }
        }

        private sealed class BenchmarkArg
        {
            public int Value;
        }

        private sealed class BenchmarkAppRoot : AppServiceRoot
        {
        }
    }
}

#endif
