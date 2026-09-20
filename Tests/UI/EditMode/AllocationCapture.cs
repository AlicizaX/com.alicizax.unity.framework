#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace AlicizaX.UI.Tests
{
    internal readonly struct AllocationSample
    {
        internal readonly long Bytes;
        internal readonly int Allocations;
        internal readonly double Milliseconds;
        internal AllocationSample(long bytes, int allocations, double milliseconds)
        {
            Bytes = bytes; Allocations = allocations; Milliseconds = milliseconds;
        }
    }

    internal static class AllocationCapture
    {
        private const int MaxProfilerBytes = 256 * 1024 * 1024;
        private static int sequence;
        internal static IEnumerator Measure(string name, int operations, Action action, Action<AllocationSample> verify)
        {
            bool enabled = ProfilerDriver.enabled, editor = ProfilerDriver.profileEditor;
            bool cpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU), memory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            int previousBudget = Profiler.maxUsedMemory;
            string marker = "UIRefactor.Measure." + (++sequence);
            var clock = new Stopwatch();
            RuntimeHelpers.PrepareDelegate(action);
            action.Method.MethodHandle.GetFunctionPointer();
            Profiler.maxUsedMemory = MaxProfilerBytes;
            ProfilerDriver.profileEditor = !UnityEngine.Application.isPlaying;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, true);
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.enabled = true;
            try
            {
                yield return null;
                int first = ProfilerDriver.lastFrameIndex;
                Profiler.BeginSample(marker);
                try { clock.Start(); action(); }
                finally { clock.Stop(); Profiler.EndSample(); }
                bool found = false;
                long bytes = 0;
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
                        int id = data.GetMarkerId(marker), gc = data.GetMarkerId("GC.Alloc");
                        for (int i = 0; i < data.sampleCount; i++)
                        {
                            if (data.GetSampleMarkerId(i) != id) continue;
                            found = true;
                            int end = i + data.GetSampleChildrenCountRecursive(i);
                            for (int child = i + 1; child <= end; child++)
                            {
                                if (data.GetSampleMarkerId(child) != gc) continue;
                                bytes += data.GetSampleMetadataAsLong(child, 0);
                                allocations++;
                            }
                            break;
                        }
                    }
                }
                Assert.That(found, Is.True, "Profiler did not capture the workload marker.");
                TestContext.WriteLine($"PERF,{name},{operations},{clock.Elapsed.TotalMilliseconds:F6},{bytes},{allocations}");
                verify(new AllocationSample(bytes, allocations, clock.Elapsed.TotalMilliseconds));
            }
            finally
            {
                ProfilerDriver.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = enabled;
                ProfilerDriver.profileEditor = editor;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, cpu);
                ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, memory);
                Profiler.maxUsedMemory = previousBudget;
            }
        }
    }
}
#endif
