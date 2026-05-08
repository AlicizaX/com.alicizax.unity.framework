#if UNITY_EDITOR
using System;
using System.Diagnostics;
using Cysharp.Text;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AlicizaX
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/MemoryPool Benchmark")]
    public sealed class MemoryPoolBenchmark : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private int objectCount = 10000;
        [SerializeField] private int loopCount = 100000;
        [SerializeField] private int adaptiveFrameCount = 420;
        [SerializeField] private int burstSize = 4096;
        [SerializeField] private int extremeBurstSize = 32768;
        [SerializeField] private int waveCount = 24;
        [SerializeField] private int multiTypeCount = 2048;
        [SerializeField] private bool logEachCase = true;
        [SerializeField] private bool logMemoryDelta = true;
        [SerializeField] private int maxCapturedLogChars = 128 * 1024;

        private static readonly ProfilerMarker s_TotalMarker = new ProfilerMarker("MemoryPoolBenchmark.Total");
        private static readonly ProfilerMarker s_SimpleMarker = new ProfilerMarker("MemoryPoolBenchmark.Simple");
        private static readonly ProfilerMarker s_AcquireReleaseMarker = new ProfilerMarker("MemoryPoolBenchmark.AcquireRelease");
        private static readonly ProfilerMarker s_AdaptiveMarker = new ProfilerMarker("MemoryPoolBenchmark.AdaptivePolicy");
        private static readonly ProfilerMarker s_ExtremeMarker = new ProfilerMarker("MemoryPoolBenchmark.Extreme");
        private static readonly ProfilerMarker s_InfoMarker = new ProfilerMarker("MemoryPoolBenchmark.InfoBuffer");
        private static readonly ProfilerMarker s_CompactMarker = new ProfilerMarker("MemoryPoolBenchmark.Compact");

        private readonly Stopwatch m_Stopwatch = new Stopwatch();
        private Utf16ValueStringBuilder m_LogBuilder;
        private bool m_LogBuilderCreated;
        private int m_FailCount;
        private int m_CaseCount;
        private long m_CaseAllocBefore;
        private long m_CaseAllocAfter;
        private BenchmarkMemory[] m_Buffer;
        private SimpleMemory[] m_SimpleBuffer;
        private BenchmarkMemoryA[] m_BufferA;
        private BenchmarkMemoryB[] m_BufferB;
        private BenchmarkMemoryC[] m_BufferC;
        private MemoryPoolInfo[] m_InfoBuffer;

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
        }

        private void Start()
        {
            if (runOnStart)
                RunAll();
        }

        [ContextMenu("Run MemoryPool Benchmark")]
        public void RunAll()
        {
            ClearCapturedConsoleOutput();
            m_FailCount = 0;
            m_CaseCount = 0;
            int maxBuffer = Math.Max(Math.Max(objectCount, burstSize), extremeBurstSize);
            EnsureBuffer(maxBuffer);
            EnsureSimpleBuffer(64);
            EnsureTypedBuffers(Math.Max(multiTypeCount, burstSize));

            using (s_TotalMarker.Auto())
            {
                RunCase("Simple Acquire/Release", RunSimpleAcquireRelease);
                RunCase("Simple Reuse Identity", RunSimpleReuseIdentity);
                RunCase("Simple Capacity Learning", RunSimpleCapacityLearning);
                RunCase("Acquire/Release Hot Loop", RunAcquireReleaseHotLoop);
                RunCase("Interleaved Acquire Release", RunInterleavedAcquireRelease);
                RunCase("Generic API Hot Loop", RunGenericApiHotLoop);
                RunCase("Facade Generic Release Hot Loop", RunFacadeGenericReleaseHotLoop);
                RunCase("Facade Acquire Direct Release", RunFacadeAcquireDirectRelease);
                RunCase("Direct Acquire Facade Release", RunDirectAcquireFacadeRelease);
                RunCase("Adaptive Burst Fill", RunAdaptiveBurstFill);
                RunCase("Idle Budget Release", RunIdleBudgetRelease);
                RunCase("Wave Burst Anti Thrash", RunWaveBurstAntiThrash);
                RunCase("Extreme Single Burst", RunExtremeSingleBurst);
                RunCase("Extreme Hard Capacity Overflow", RunExtremeHardCapacityOverflow);
                RunCase("Multi Type Active Queue", RunMultiTypeActiveQueue);
                RunCase("ClearAll Unschedule", RunClearAllUnschedule);
                RunCase("ClearAll Active Queue Reset", RunClearAllActiveQueueReset);
                RunCase("Type API Cold Path", RunTypeApiColdPath);
                RunCase("Cached Handle Hot Path", RunCachedHandleHotPath);
                RunCase("Info Buffer No Alloc", RunInfoBufferNoAlloc);
                RunCase("Explicit Compact", RunExplicitCompact);
            }

            Debug.Log(BuildLog("MemoryPool benchmark finished. cases=", m_CaseCount, ", fails=", m_FailCount));
        }

        [ContextMenu("Copy Captured Console Output")]
        public void CopyCapturedConsoleOutput()
        {
            EnsureLogBuilder();
            string text = m_LogBuilder.ToString();
            GUIUtility.systemCopyBuffer = text;
            Debug.Log(BuildLog("MemoryPoolBenchmark copied console output chars=", text.Length, ", max=", maxCapturedLogChars));
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

            if (!logEachCase)
                return;

            if (logMemoryDelta)
                Debug.Log(BuildLog("[MemoryPoolBenchmark] ", caseName, " ms=", m_Stopwatch.Elapsed.TotalMilliseconds, " gcAlloc=", m_CaseAllocAfter - m_CaseAllocBefore));
            else
                Debug.Log(BuildLog("[MemoryPoolBenchmark] ", caseName, " ms=", m_Stopwatch.Elapsed.TotalMilliseconds));
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

        private void RunSimpleAcquireRelease()
        {
            using (s_SimpleMarker.Auto())
            {
                MemoryPool<SimpleMemory>.ClearAll();
                MemoryPool<SimpleMemory>.Prewarm(1);

                RestartCaseMeasure();
                SimpleMemory item = MemoryPool<SimpleMemory>.Acquire();
                item.Value = 7;
                MemoryPool<SimpleMemory>.Release(item);
                StopCaseMeasure();

                AssertEqual(item.Value, 0, "simple release did not clear object");
                MemoryPool<SimpleMemory>.ClearAll();
            }
        }

        private void RunSimpleReuseIdentity()
        {
            using (s_SimpleMarker.Auto())
            {
                MemoryPool<SimpleMemory>.ClearAll();
                MemoryPool<SimpleMemory>.Prewarm(1);
                SimpleMemory first = MemoryPool<SimpleMemory>.Acquire();
                MemoryPool<SimpleMemory>.Release(first);

                RestartCaseMeasure();
                SimpleMemory second = MemoryPool<SimpleMemory>.Acquire();
                StopCaseMeasure();

                AssertTrue(ReferenceEquals(first, second), "simple reuse did not return same instance");
                MemoryPool<SimpleMemory>.Release(second);
                MemoryPool<SimpleMemory>.ClearAll();
            }
        }

        private void RunSimpleCapacityLearning()
        {
            using (s_SimpleMarker.Auto())
            {
                MemoryPool<SimpleMemory>.ClearAll();
                MemoryPool<SimpleMemory>.SetCapacity(64, 256);
                for (int i = 0; i < 48; i++)
                    m_SimpleBuffer[i] = null;

                RestartCaseMeasure();
                for (int i = 0; i < 48; i++)
                    m_SimpleBuffer[i] = MemoryPool<SimpleMemory>.Acquire();
                StopCaseMeasure();

                for (int i = 0; i < 48; i++)
                {
                    MemoryPool<SimpleMemory>.Release(m_SimpleBuffer[i]);
                    m_SimpleBuffer[i] = null;
                }

                MemoryPool<SimpleMemory>.ClearAll();
            }
        }

        private void RunAcquireReleaseHotLoop()
        {
            using (s_AcquireReleaseMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(objectCount, objectCount << 2);
                MemoryPool<BenchmarkMemory>.Prewarm(objectCount);

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkMemory item = MemoryPool<BenchmarkMemory>.Acquire();
                    item.Value = i;
                    MemoryPool<BenchmarkMemory>.Release(item);
                }
                StopCaseMeasure();

                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunInterleavedAcquireRelease()
        {
            using (s_AcquireReleaseMarker.Auto())
            {
                int count = Math.Min(objectCount, m_Buffer.Length);
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(count, count << 2);
                MemoryPool<BenchmarkMemory>.Prewarm(count >> 1);

                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                {
                    BenchmarkMemory item = MemoryPool<BenchmarkMemory>.Acquire();
                    item.Value = i;
                    if ((i & 1) == 0)
                    {
                        MemoryPool<BenchmarkMemory>.Release(item);
                    }
                    else
                    {
                        m_Buffer[i] = item;
                    }
                }

                for (int i = 1; i < count; i += 2)
                {
                    MemoryPool<BenchmarkMemory>.Release(m_Buffer[i]);
                    m_Buffer[i] = null;
                }
                StopCaseMeasure();

                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunGenericApiHotLoop()
        {
            using (s_AcquireReleaseMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(objectCount, objectCount << 2);
                MemoryPool<BenchmarkMemory>.Prewarm(objectCount);

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkMemory item = MemoryPool.Acquire<BenchmarkMemory>();
                    MemoryPool.Release(item);
                }
                StopCaseMeasure();

                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }


        private void RunFacadeGenericReleaseHotLoop()
        {
            using (s_AcquireReleaseMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(objectCount, objectCount << 2);
                MemoryPool<BenchmarkMemory>.Prewarm(objectCount);

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkMemory item = MemoryPool.Acquire<BenchmarkMemory>();
                    MemoryPool.Release<BenchmarkMemory>(item);
                }
                StopCaseMeasure();

                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }


        private void RunFacadeAcquireDirectRelease()
        {
            using (s_AcquireReleaseMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(objectCount, objectCount << 2);
                MemoryPool<BenchmarkMemory>.Prewarm(objectCount);

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkMemory item = MemoryPool.Acquire<BenchmarkMemory>();
                    MemoryPool<BenchmarkMemory>.Release(item);
                }
                StopCaseMeasure();

                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunDirectAcquireFacadeRelease()
        {
            using (s_AcquireReleaseMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(objectCount, objectCount << 2);
                MemoryPool<BenchmarkMemory>.Prewarm(objectCount);

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkMemory item = MemoryPool<BenchmarkMemory>.Acquire();
                    MemoryPool.Release<BenchmarkMemory>(item);
                }
                StopCaseMeasure();

                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunAdaptiveBurstFill()
        {
            using (s_AdaptiveMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(Math.Max(64, burstSize >> 1), burstSize << 1);

                RestartCaseMeasure();
                for (int i = 0; i < burstSize; i++)
                    m_Buffer[i] = MemoryPool<BenchmarkMemory>.Acquire();

                for (int i = 0; i < burstSize; i++)
                {
                    MemoryPool<BenchmarkMemory>.Release(m_Buffer[i]);
                    m_Buffer[i] = null;
                }

                for (int frame = 0; frame < adaptiveFrameCount; frame++)
                    MemoryPoolRegistry.TickAll(frame);
                StopCaseMeasure();

                MemoryPoolInfo info = GetBenchmarkInfo(typeof(BenchmarkMemory));
                AssertTrue(info.UnusedCount > 0, "adaptive fill did not keep reserve");
                AssertTrue(info.PoolArrayLength >= info.UnusedCount, "pool array smaller than unused count");
                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunIdleBudgetRelease()
        {
            using (s_AdaptiveMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(burstSize, burstSize << 1);
                MemoryPool<BenchmarkMemory>.Prewarm(burstSize);

                RestartCaseMeasure();
                for (int frame = 0; frame < adaptiveFrameCount + 360; frame++)
                    MemoryPoolRegistry.TickAll(frame + 10000);
                StopCaseMeasure();

                MemoryPoolInfo info = GetBenchmarkInfo(typeof(BenchmarkMemory));
                AssertTrue(info.UnusedCount < burstSize, "idle release did not reduce unused objects");
                AssertTrue(info.PoolArrayLength >= burstSize, "idle release should not compact backing array");
                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunWaveBurstAntiThrash()
        {
            using (s_ExtremeMarker.Auto())
            {
                int count = Math.Min(burstSize, m_Buffer.Length);
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(count, count << 1);

                RestartCaseMeasure();
                for (int wave = 0; wave < waveCount; wave++)
                {
                    int waveSize = (wave & 1) == 0 ? count : count >> 2;
                    for (int i = 0; i < waveSize; i++)
                        m_Buffer[i] = MemoryPool<BenchmarkMemory>.Acquire();

                    for (int i = 0; i < waveSize; i++)
                    {
                        MemoryPool<BenchmarkMemory>.Release(m_Buffer[i]);
                        m_Buffer[i] = null;
                    }

                    for (int frame = 0; frame < 12; frame++)
                        MemoryPoolRegistry.TickAll(20000 + wave * 16 + frame);
                }
                StopCaseMeasure();

                MemoryPoolInfo info = GetBenchmarkInfo(typeof(BenchmarkMemory));
                AssertTrue(info.PoolArrayLength >= count, "wave burst backing array shrank unexpectedly");
                AssertTrue(info.UnusedCount > 0, "wave burst failed to retain reserve");
                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunExtremeSingleBurst()
        {
            using (s_ExtremeMarker.Auto())
            {
                int count = Math.Min(extremeBurstSize, m_Buffer.Length);
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(Math.Max(128, count >> 2), count);

                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                    m_Buffer[i] = MemoryPool<BenchmarkMemory>.Acquire();

                for (int i = 0; i < count; i++)
                {
                    MemoryPool<BenchmarkMemory>.Release(m_Buffer[i]);
                    m_Buffer[i] = null;
                }
                StopCaseMeasure();

                MemoryPoolInfo info = GetBenchmarkInfo(typeof(BenchmarkMemory));
                AssertTrue(info.UnusedCount == count, "extreme single burst did not keep released objects under hard cap");
                AssertTrue(info.PoolArrayLength >= count, "extreme single burst did not grow backing array");
                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunExtremeHardCapacityOverflow()
        {
            using (s_ExtremeMarker.Auto())
            {
                int count = Math.Min(burstSize, m_Buffer.Length);
                int hardCapacity = Math.Max(32, count >> 3);
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(hardCapacity >> 1, hardCapacity);

                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                    m_Buffer[i] = MemoryPool<BenchmarkMemory>.Acquire();

                for (int i = 0; i < count; i++)
                {
                    MemoryPool<BenchmarkMemory>.Release(m_Buffer[i]);
                    m_Buffer[i] = null;
                }
                StopCaseMeasure();

                MemoryPoolInfo info = GetBenchmarkInfo(typeof(BenchmarkMemory));
                AssertTrue(info.UnusedCount == hardCapacity, "hard capacity overflow retained more than hard cap");
                AssertTrue(info.PoolArrayLength == hardCapacity, "hard capacity overflow grew array past hard cap");
                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunMultiTypeActiveQueue()
        {
            using (s_ExtremeMarker.Auto())
            {
                int count = Math.Min(multiTypeCount, m_BufferA.Length);
                MemoryPool<BenchmarkMemoryA>.ClearAll();
                MemoryPool<BenchmarkMemoryB>.ClearAll();
                MemoryPool<BenchmarkMemoryC>.ClearAll();
                MemoryPool<BenchmarkMemoryA>.SetCapacity(count, count << 1);
                MemoryPool<BenchmarkMemoryB>.SetCapacity(count, count << 1);
                MemoryPool<BenchmarkMemoryC>.SetCapacity(count, count << 1);

                RestartCaseMeasure();
                for (int i = 0; i < count; i++)
                {
                    m_BufferA[i] = MemoryPool<BenchmarkMemoryA>.Acquire();
                    m_BufferB[i] = MemoryPool<BenchmarkMemoryB>.Acquire();
                    m_BufferC[i] = MemoryPool<BenchmarkMemoryC>.Acquire();
                }

                for (int i = 0; i < count; i++)
                {
                    MemoryPool<BenchmarkMemoryA>.Release(m_BufferA[i]);
                    MemoryPool<BenchmarkMemoryB>.Release(m_BufferB[i]);
                    MemoryPool<BenchmarkMemoryC>.Release(m_BufferC[i]);
                    m_BufferA[i] = null;
                    m_BufferB[i] = null;
                    m_BufferC[i] = null;
                }

                for (int frame = 0; frame < adaptiveFrameCount; frame++)
                    MemoryPoolRegistry.TickAll(30000 + frame);
                StopCaseMeasure();

                AssertTrue(GetBenchmarkInfo(typeof(BenchmarkMemoryA)).UnusedCount > 0, "type A did not tick");
                AssertTrue(GetBenchmarkInfo(typeof(BenchmarkMemoryB)).UnusedCount > 0, "type B did not tick");
                AssertTrue(GetBenchmarkInfo(typeof(BenchmarkMemoryC)).UnusedCount > 0, "type C did not tick");
                MemoryPool<BenchmarkMemoryA>.ClearAll();
                MemoryPool<BenchmarkMemoryB>.ClearAll();
                MemoryPool<BenchmarkMemoryC>.ClearAll();
            }
        }


        private void RunClearAllUnschedule()
        {
            using (s_ExtremeMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(burstSize, burstSize << 1);
                MemoryPool<BenchmarkMemory>.Prewarm(burstSize);
                MemoryPoolRegistry.TickAll(39000);
                MemoryPool<BenchmarkMemory>.ClearAll();

                RestartCaseMeasure();
                for (int frame = 0; frame < adaptiveFrameCount; frame++)
                    MemoryPoolRegistry.TickAll(39001 + frame);
                StopCaseMeasure();

                MemoryPoolInfo info = GetBenchmarkInfo(typeof(BenchmarkMemory));
                AssertTrue(info.UnusedCount == 0, "clear all should unschedule single pool tick");
                AssertTrue(info.PoolArrayLength == 0, "clear all should keep backing array empty until reused");
            }
        }

        private void RunClearAllActiveQueueReset()
        {
            using (s_ExtremeMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(burstSize, burstSize << 1);
                MemoryPool<BenchmarkMemory>.Prewarm(burstSize);
                MemoryPoolRegistry.TickAll(40000);
                MemoryPoolRegistry.ClearAll();

                RestartCaseMeasure();
                MemoryPool<BenchmarkMemory>.Prewarm(16);
                MemoryPoolRegistry.TickAll(40001);
                StopCaseMeasure();

                MemoryPoolInfo info = GetBenchmarkInfo(typeof(BenchmarkMemory));
                AssertTrue(info.UnusedCount >= 16, "clear all active queue reset blocked reschedule");
                AssertTrue(info.PoolArrayLength >= 16, "clear all active queue reset did not regrow backing array");
                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunTypeApiColdPath()
        {
            using (s_ExtremeMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.Prewarm(objectCount);
                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkMemory item = MemoryPool.Acquire<BenchmarkMemory>();
                    MemoryPool.Release<BenchmarkMemory>(item);
                }
                StopCaseMeasure();

                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }


        private void RunCachedHandleHotPath()
        {
            using (s_ExtremeMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.Prewarm(objectCount);
                MemoryPoolHandle handle = MemoryPool.GetHandle(typeof(BenchmarkMemory));
                AssertTrue(handle.IsValid, "cached handle is invalid");

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    IMemory item = handle.Acquire();
                    handle.Release(item);
                }
                StopCaseMeasure();

                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private void RunInfoBufferNoAlloc()
        {
            using (s_InfoMarker.Auto())
            {
                EnsureInfoBuffer(Math.Max(1, MemoryPool.Count));

                RestartCaseMeasure();
                int count = MemoryPool.GetAllMemoryPoolInfos(m_InfoBuffer);
                StopCaseMeasure();

                AssertTrue(count <= m_InfoBuffer.Length, "info count exceeded buffer length");
            }
        }

        private void RunExplicitCompact()
        {
            using (s_CompactMarker.Auto())
            {
                MemoryPool<BenchmarkMemory>.ClearAll();
                MemoryPool<BenchmarkMemory>.SetCapacity(objectCount, objectCount << 2);
                MemoryPool<BenchmarkMemory>.Prewarm(objectCount);
                MemoryPool<BenchmarkMemory>.Shrink(8);

                RestartCaseMeasure();
                MemoryPool<BenchmarkMemory>.Compact();
                StopCaseMeasure();

                MemoryPoolInfo info = GetBenchmarkInfo(typeof(BenchmarkMemory));
                AssertTrue(info.PoolArrayLength <= 8, "compact did not shrink backing array");
                MemoryPool<BenchmarkMemory>.ClearAll();
            }
        }

        private MemoryPoolInfo GetBenchmarkInfo(Type targetType)
        {
            EnsureInfoBuffer(Math.Max(1, MemoryPool.Count));
            int count = MemoryPool.GetAllMemoryPoolInfos(m_InfoBuffer);
            for (int i = 0; i < count; i++)
            {
                if (m_InfoBuffer[i].Type == targetType)
                    return m_InfoBuffer[i];
            }

            return default;
        }

        private void EnsureBuffer(int count)
        {
            if (m_Buffer == null || m_Buffer.Length < count)
                m_Buffer = new BenchmarkMemory[count];
        }

        private void EnsureSimpleBuffer(int count)
        {
            if (m_SimpleBuffer == null || m_SimpleBuffer.Length < count)
                m_SimpleBuffer = new SimpleMemory[count];
        }

        private void EnsureTypedBuffers(int count)
        {
            if (m_BufferA == null || m_BufferA.Length < count)
                m_BufferA = new BenchmarkMemoryA[count];
            if (m_BufferB == null || m_BufferB.Length < count)
                m_BufferB = new BenchmarkMemoryB[count];
            if (m_BufferC == null || m_BufferC.Length < count)
                m_BufferC = new BenchmarkMemoryC[count];
        }

        private void EnsureInfoBuffer(int count)
        {
            if (m_InfoBuffer == null || m_InfoBuffer.Length < count)
                m_InfoBuffer = new MemoryPoolInfo[count];
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

        private sealed class SimpleMemory : IMemory
        {
            public int Value;

            public void Clear()
            {
                Value = 0;
            }
        }

        private sealed class BenchmarkMemory : IMemory
        {
            public int Value;

            public void Clear()
            {
                Value = 0;
            }
        }

        private sealed class BenchmarkMemoryA : IMemory
        {
            public int Value;

            public void Clear()
            {
                Value = 0;
            }
        }

        private sealed class BenchmarkMemoryB : IMemory
        {
            public int Value;

            public void Clear()
            {
                Value = 0;
            }
        }

        private sealed class BenchmarkMemoryC : IMemory
        {
            public int Value;

            public void Clear()
            {
                Value = 0;
            }
        }
    }
}
#endif

