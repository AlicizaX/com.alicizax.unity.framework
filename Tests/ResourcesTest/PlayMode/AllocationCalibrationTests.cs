#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class AllocationCalibrationTests
    {
        [UnityTest]
        public IEnumerator ThrowingWorkloadRestoresProfilerAndAllowsTheNextMeasurement()
        {
            bool enabled = ProfilerDriver.enabled;
            bool stacks = Profiler.enableAllocationCallstacks;
            bool editor = ProfilerDriver.profileEditor;
            bool cpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
            bool memory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
            int budget = Profiler.maxUsedMemory;
            var cause = new InvalidOperationException("workload failure");
            var measurement = AllocationCapture.Measure("expected-failure", 1, () => throw cause,
                _ => Assert.Fail("A failed workload cannot produce a valid measurement."));
            try
            {
                Assert.That(measurement.MoveNext(), Is.True);
                yield return measurement.Current;
                Assert.That(Assert.Throws<InvalidOperationException>(() => measurement.MoveNext()), Is.SameAs(cause));
                Assert.That(ProfilerDriver.enabled, Is.EqualTo(enabled));
                Assert.That(Profiler.enableAllocationCallstacks, Is.EqualTo(stacks));
                Assert.That(ProfilerDriver.profileEditor, Is.EqualTo(editor));
                Assert.That(ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU), Is.EqualTo(cpu));
                Assert.That(ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory), Is.EqualTo(memory));
                Assert.That(Profiler.maxUsedMemory, Is.EqualTo(budget));
            }
            finally { (measurement as IDisposable)?.Dispose(); }
            yield return AllocationCapture.Measure("after-failure-calibration", 1, () => GC.KeepAlive(new byte[4096]),
                sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(4096)));
        }

        [UnityTest]
        public IEnumerator AllocationCounterMeasuresKnownAllocation()
        {
            yield return AllocationCapture.Measure("calibration", 1, () => GC.KeepAlive(new byte[4096]), sample =>
            {
                Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(4096));
                Assert.That(sample.Allocations, Is.EqualTo(1));
            });
        }
    }
}
#endif
