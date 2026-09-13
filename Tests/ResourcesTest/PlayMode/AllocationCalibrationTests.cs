using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class AllocationCalibrationTests
    {
        [UnityTest]
        public IEnumerator AllocationCounterMeasuresKnownAllocation()
        {
#if UNITY_EDITOR
            yield return AllocationCapture.Measure("calibration", 1, () => GC.KeepAlive(new byte[4096]), sample =>
            {
                Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(4096));
                Assert.That(sample.Allocations, Is.EqualTo(1));
            });
#else
            Assert.Fail("This calibration requires the Unity Editor profiler.");
            yield break;
#endif
        }
    }
}
