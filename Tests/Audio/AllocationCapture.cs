#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace AlicizaX.Audio.Tests
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

        private sealed class ProfilerSession : IDisposable
        {
            private readonly bool callstacks = Profiler.enableAllocationCallstacks;
            private readonly bool enabled = ProfilerDriver.enabled;
            private readonly bool editor = ProfilerDriver.profileEditor;
            private readonly bool cpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
            private readonly bool memory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            private readonly int budget = Profiler.maxUsedMemory;
            private bool disposed;

            internal ProfilerSession(bool trace)
            {
                Profiler.maxUsedMemory = MaxProfilerBytes;
                Profiler.enableAllocationCallstacks = trace;
                ProfilerDriver.profileEditor = !UnityEngine.Application.isPlaying;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                ProfilerDriver.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = enabled;
                Profiler.enableAllocationCallstacks = callstacks;
                ProfilerDriver.profileEditor = editor;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, cpu);
                ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, memory);
                Profiler.maxUsedMemory = budget;
            }
        }

        internal static IEnumerator MeasureFrames(string name, IEnumerator operation, Action<AllocationSample> verify, bool traceAllocations = false)
        {
            using var session = new ProfilerSession(true);
            yield return Measure("async-recorder-ready", 1, () => { }, sample => Assert.That(sample.Bytes, Is.Zero));
            string start = "AudioAudit.Start." + (++sequence);
            string finish = "AudioAudit.Finish." + sequence;
            yield return null;
            int first = ProfilerDriver.lastFrameIndex;
            Profiler.BeginSample(start);
            Profiler.EndSample();
            try
            {
                while (operation.MoveNext()) yield return operation.Current;
            }
            finally { (operation as IDisposable)?.Dispose(); }
            Profiler.BeginSample(finish);
            Profiler.EndSample();
            int from = -1, to = -1;
            double deadline = UnityEditor.EditorApplication.timeSinceStartup + 5;
            while (to < 0 && UnityEditor.EditorApplication.timeSinceStartup < deadline)
            {
                InternalEditorUtility.RepaintAllViews();
                yield return null;
                for (int frame = Math.Max(first, ProfilerDriver.firstFrameIndex); frame <= ProfilerDriver.lastFrameIndex; frame++)
                {
                    using var data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                    if (!data.valid) continue;
                    int beginId = data.GetMarkerId(start), endId = data.GetMarkerId(finish);
                    for (int i = 0; i < data.sampleCount; i++)
                    {
                        int marker = data.GetSampleMarkerId(i);
                        if (marker == beginId) from = frame;
                        if (marker == endId) to = frame;
                    }
                }
            }
            Assert.That(from, Is.GreaterThanOrEqualTo(0), "Missing cross-frame start marker.");
            Assert.That(to, Is.GreaterThan(from), "The operation must actually suspend across frames.");
            long bytes = 0, backendBytes = 0, unclassified = 0, continuationBytes = 0;
            int allocations = 0;
            var stack = new List<ulong>();
            for (int frame = from; frame <= to; frame++)
            {
                for (int thread = 0; ; thread++)
                {
                    using var data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!data.valid) break;
                    int alloc = data.GetMarkerId("GC.Alloc");
                    for (int i = 0; i < data.sampleCount; i++)
                    {
                        if (data.GetSampleMarkerId(i) != alloc) continue;
                        long size = data.GetSampleMetadataAsLong(i, 0);
                        stack.Clear();
                        data.GetSampleCallstack(i, stack);
                        bool owned = false, backend = false;
                        foreach (ulong address in stack)
                        {
                            string method = data.ResolveMethodInfo(address).methodName;
                            if (string.IsNullOrEmpty(method)) continue;
                            owned |= method.StartsWith("AlicizaX.Framework.Runtime.dll!", StringComparison.Ordinal) ||
                                method.StartsWith("UniTask.Runtime.dll!", StringComparison.Ordinal) || method.Contains("AudioAsyncAllocationProbe");
                            backend |= method.StartsWith("YooAsset.dll!", StringComparison.Ordinal) || method.Contains("ControlledLoader.");
                        }
                        if (!owned && !backend) { unclassified += size; continue; }
                        if (traceAllocations && owned && !backend)
                        {
                            TestContext.WriteLine($"ASYNC_ALLOC,{name},{frame - from},{size}");
                            foreach (ulong address in stack)
                            {
                                var method = data.ResolveMethodInfo(address);
                                if (!string.IsNullOrEmpty(method.methodName))
                                    TestContext.WriteLine($"ASYNC_STACK,{method.methodName},{method.sourceFileName},{method.sourceFileLine}");
                            }
                        }
                        bytes += size;
                        allocations++;
                        if (backend) backendBytes += size;
                        if (frame > from) continuationBytes += size;
                    }
                }
            }
            session.Dispose();
            TestContext.WriteLine($"ASYNC_GC,{name},{to - from + 1},{bytes},{backendBytes},{continuationBytes},{unclassified},{allocations}");
            verify(new AllocationSample(bytes, backendBytes, allocations, 0));
        }

        internal static IEnumerator Measure(string name, int iterations, Action action, Action<AllocationSample> verify, bool traceAllocations = false)
        {
            using var session = new ProfilerSession(traceAllocations);
            string marker = "ResourceAudit.Measure." + (++sequence);
            var clock = new Stopwatch();
            // Compile the measurement delegate without invoking or prewarming the operation.
            RuntimeHelpers.PrepareDelegate(action);
            action.Method.MethodHandle.GetFunctionPointer();
            yield return null;
            int firstFrame = ProfilerDriver.lastFrameIndex;
            Profiler.BeginSample(marker);
            try
            {
                clock.Start();
                action();
            }
            finally
            {
                clock.Stop();
                Profiler.EndSample();
            }
            bool found = false;
            long bytes = 0;
            long backendBytes = 0;
            int allocations = 0;
            double deadline = UnityEditor.EditorApplication.timeSinceStartup + 5;
            while (!found && UnityEditor.EditorApplication.timeSinceStartup < deadline)
            {
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
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
            session.Dispose();
            verify(new AllocationSample(bytes, backendBytes, allocations, clock.Elapsed.TotalMilliseconds));
        }
    }
}
#endif
