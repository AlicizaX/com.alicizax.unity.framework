#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace AlicizaX.Resource.Tests
{
    internal readonly struct AllocationSample
    {
        internal readonly long Bytes;
        internal readonly long BackendBytes;
        internal readonly int Allocations;
        internal readonly double Milliseconds;

        internal AllocationSample(long bytes, long backendBytes, int allocations, double milliseconds)
        {
            Bytes = bytes;
            BackendBytes = backendBytes;
            Allocations = allocations;
            Milliseconds = milliseconds;
        }
    }

    internal static class AllocationCapture
    {
        private const int MaxProfilerBytes = 256 * 1024 * 1024;
        private static int sequence;

        internal static IEnumerator Measure(string name, int iterations, Action action, Action<AllocationSample> verify, bool traceAllocations = false)
        {
            bool wasCallstacks = Profiler.enableAllocationCallstacks;
            bool wasEnabled = ProfilerDriver.enabled;
            bool wasEditor = ProfilerDriver.profileEditor;
            bool wasCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
            bool wasMemory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            int previousBudget = Profiler.maxUsedMemory;
            Profiler.maxUsedMemory = MaxProfilerBytes;
            Profiler.enableAllocationCallstacks = traceAllocations;
            ProfilerDriver.profileEditor = !UnityEngine.Application.isPlaying;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.enabled = true;
            string marker = "ResourceAudit.Measure." + (++sequence);
            var clock = new Stopwatch();
            try
            {
                yield return null;
                int firstFrame = ProfilerDriver.lastFrameIndex;
                Profiler.BeginSample(marker);
                try { clock.Start(); action(); }
                finally { clock.Stop(); Profiler.EndSample(); }
                bool found = false;
                long bytes = 0;
                long backendBytes = 0;
                int allocations = 0;
                double deadline = UnityEditor.EditorApplication.timeSinceStartup + 5;
                while (!found && UnityEditor.EditorApplication.timeSinceStartup < deadline)
                {
                    InternalEditorUtility.RepaintAllViews();
                    yield return null;
                    int lastFrame = ProfilerDriver.lastFrameIndex;
                    for (int frame = Math.Max(firstFrame, ProfilerDriver.firstFrameIndex); frame <= lastFrame && !found; frame++)
                    {
                        using var data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                        if (!data.valid) continue;
                        int markerId = data.GetMarkerId(marker);
                        int allocationId = data.GetMarkerId("GC.Alloc");
                        int backendId = data.GetMarkerId("ResourceAudit.Backend");
                        for (int i = 0; i < data.sampleCount; i++)
                        {
                            if (data.GetSampleMarkerId(i) != markerId) continue;
                            found = true;
                            int end = i + data.GetSampleChildrenCountRecursive(i);
                            int backendEnd = -1;
                            for (int child = i + 1; child <= end; child++)
                            {
                                int id = data.GetSampleMarkerId(child);
                                if (id == backendId) backendEnd = child + data.GetSampleChildrenCountRecursive(child);
                                if (id != allocationId) continue;
                                long size = data.GetSampleMetadataAsLong(child, 0);
                                bytes += size;
                                allocations++;
                                if (child <= backendEnd) backendBytes += size;
                                if (traceAllocations)
                                {
                                    var stack = new List<ulong>();
                                    data.GetSampleCallstack(child, stack);
                                    TestContext.WriteLine($"ALLOC,{name},{size}");
                                    bool classified = child <= backendEnd;
                                    foreach (ulong address in stack)
                                    {
                                        var method = data.ResolveMethodInfo(address);
                                        if (string.IsNullOrEmpty(method.methodName)) continue;
                                        if (!classified && method.methodName.StartsWith("YooAsset.dll!", StringComparison.Ordinal))
                                        {
                                            backendBytes += size;
                                            classified = true;
                                        }
                                        else if (method.methodName.StartsWith("AlicizaX.Framework.", StringComparison.Ordinal))
                                            classified = true;
                                        TestContext.WriteLine($"STACK,{method.methodName},{method.sourceFileName},{method.sourceFileLine}");
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
                if (!found)
                {
                    TestContext.WriteLine($"PROFILER,first={firstFrame},last={ProfilerDriver.lastFrameIndex},connected={ProfilerDriver.connectedProfiler}");
                    foreach (int profiler in ProfilerDriver.GetAvailableProfilers())
                        TestContext.WriteLine($"PROFILER-TARGET,{profiler},{ProfilerDriver.GetConnectionIdentifier(profiler)}");
                }
                Assert.That(found, Is.True, "Profiler did not capture the workload marker.");
                TestContext.WriteLine($"PERF,{name},{iterations},{clock.Elapsed.TotalMilliseconds:F4},{bytes},{backendBytes},{allocations}");
                verify(new AllocationSample(bytes, backendBytes, allocations, clock.Elapsed.TotalMilliseconds));
            }
            finally
            {
                ProfilerDriver.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = wasEnabled;
                Profiler.enableAllocationCallstacks = wasCallstacks;
                ProfilerDriver.profileEditor = wasEditor;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, wasCpu);
                ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, wasMemory);
                Profiler.maxUsedMemory = previousBudget;
            }
        }
    }
}
#endif
