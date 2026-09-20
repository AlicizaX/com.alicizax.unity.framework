#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace AlicizaX.EventTests
{
    public readonly struct AllocationSample
    {
        public readonly long Bytes;
        public readonly long ServiceBytes;
        public readonly int Allocations;
        public readonly double Milliseconds;
        public readonly int Gen0Collections;
        public readonly int Gen1Collections;
        public readonly int Gen2Collections;
        internal AllocationSample(long bytes, long serviceBytes, int allocations, double milliseconds, int gen0, int gen1, int gen2)
        { Bytes = bytes; ServiceBytes = serviceBytes; Allocations = allocations; Milliseconds = milliseconds; Gen0Collections = gen0; Gen1Collections = gen1; Gen2Collections = gen2; }
    }

    public static class AllocationCapture
    {
        private const int MaxProfilerBytes = 256 * 1024 * 1024;
        private static int sequence;

        public static IEnumerator Measure(string name, int iterations, Action action, Action<AllocationSample> verify, bool trace = false)
        {
            bool enabled = ProfilerDriver.enabled, editor = ProfilerDriver.profileEditor;
            bool cpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU), memory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            bool stacks = Profiler.enableAllocationCallstacks;
            int previousBudget = Profiler.maxUsedMemory;
            try
            {
                Profiler.maxUsedMemory = MaxProfilerBytes;
                ProfilerDriver.profileEditor = !UnityEngine.Application.isPlaying;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
                Profiler.enableAllocationCallstacks = trace;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = true;
                string marker = "EventAudit.Measure." + (++sequence);
                var watch = new Stopwatch();
                RuntimeHelpers.PrepareDelegate(action);
                action.Method.MethodHandle.GetFunctionPointer();
                yield return null;
                int first = ProfilerDriver.lastFrameIndex;
                int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
                Profiler.BeginSample(marker);
                try { watch.Start(); action(); }
                finally { watch.Stop(); Profiler.EndSample(); }
                gen0 = GC.CollectionCount(0) - gen0;
                gen1 = GC.CollectionCount(1) - gen1;
                gen2 = GC.CollectionCount(2) - gen2;
                bool found = false;
                long bytes = 0;
                long serviceBytes = 0;
                int allocations = 0;
                double deadline = EditorApplication.timeSinceStartup + 5;
                while (!found && EditorApplication.timeSinceStartup < deadline)
                {
                    InternalEditorUtility.RepaintAllViews();
                    yield return null;
                    for (int frame = Math.Max(first, ProfilerDriver.firstFrameIndex); frame <= ProfilerDriver.lastFrameIndex && !found; frame++)
                    {
                        using var data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                        if (!data.valid) continue;
                        int markerId = data.GetMarkerId(marker), allocId = data.GetMarkerId("GC.Alloc"), serviceId = data.GetMarkerId("EventAudit.Service");
                        for (int i = 0; i < data.sampleCount; i++)
                        {
                            if (data.GetSampleMarkerId(i) != markerId) continue;
                            found = true;
                            int end = i + data.GetSampleChildrenCountRecursive(i);
                            int serviceEnd = -1;
                            for (int child = i + 1; child <= end; child++)
                            {
                                if (data.GetSampleMarkerId(child) == serviceId) serviceEnd = child + data.GetSampleChildrenCountRecursive(child);
                                if (data.GetSampleMarkerId(child) != allocId) continue;
                                long size = data.GetSampleMetadataAsLong(child, 0);
                                bytes += size;
                                if (child <= serviceEnd) serviceBytes += size;
                                allocations++;
                                if (!trace) continue;
                                TestContext.WriteLine($"ALLOC,{name},{size}");
                                var stack = new List<ulong>();
                                data.GetSampleCallstack(child, stack);
                                foreach (ulong address in stack)
                                {
                                    var method = data.ResolveMethodInfo(address);
                                    if (!string.IsNullOrEmpty(method.methodName)) TestContext.WriteLine($"STACK,{method.methodName},{method.sourceFileName},{method.sourceFileLine}");
                                }
                            }
                            break;
                        }
                    }
                }
                Assert.That(found, Is.True, "Profiler must capture the operation marker");
                TestContext.WriteLine($"PERF,{name},{iterations},{watch.Elapsed.TotalMilliseconds:F4},{bytes},{allocations},service-marker-bytes,{serviceBytes}");
                TestContext.WriteLine($"GC_COLLECTIONS,{name},{gen0},{gen1},{gen2}");
                verify(new AllocationSample(bytes, serviceBytes, allocations, watch.Elapsed.TotalMilliseconds, gen0, gen1, gen2));
            }
            finally
            {
                ProfilerDriver.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = enabled;
                ProfilerDriver.profileEditor = editor;
                Profiler.enableAllocationCallstacks = stacks;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, cpu);
                ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, memory);
                Profiler.maxUsedMemory = previousBudget;
            }
        }
    }
}
#endif
