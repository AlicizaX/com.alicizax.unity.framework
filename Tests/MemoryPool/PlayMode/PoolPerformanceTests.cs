#if UNITY_EDITOR
using System;
using System.Collections;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolPerformanceTests : PoolFixture
    {
        [UnityTest]
        public IEnumerator ColdGrowthAndStablePathsWithoutPrewarming()
        {
            const int count = 10000;
            Use<ColdItem<PoolPerformanceTests>>();
            ColdItem<PoolPerformanceTests> first = null;
            yield return AllocationCapture.Measure("pool-cold-instance", 1,
                () => first = MemoryPool<ColdItem<PoolPerformanceTests>>.Acquire(), sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            MemoryPool.Release(first);
            Action generic = () =>
            {
                for (int i = 0; i < count; i++) MemoryPool.Release(MemoryPool<ColdItem<PoolPerformanceTests>>.Acquire());
            };
            yield return AllocationCapture.Measure("pool-generic-first-call", count, generic, _ => { }, true);
            yield return AllocationCapture.Measure("pool-generic", count, generic, sample => Assert.That(sample.Bytes, Is.Zero));
            var handle = MemoryPool.GetHandle(typeof(ColdItem<PoolPerformanceTests>));
            Action cachedHandle = () =>
            {
                for (int i = 0; i < count; i++) handle.Release(handle.Acquire());
            };
            yield return AllocationCapture.Measure("pool-handle-first-call", count, cachedHandle, _ => { }, true);
            yield return AllocationCapture.Measure("pool-handle", count, cachedHandle, sample => Assert.That(sample.Bytes, Is.Zero));
            Type type = typeof(ColdItem<PoolPerformanceTests>);
            Action dynamicType = () =>
            {
                for (int i = 0; i < count; i++) MemoryPool.Release(MemoryPool.Acquire(type));
            };
            yield return AllocationCapture.Measure("pool-type-first-call", count, dynamicType, _ => { }, true);
            yield return AllocationCapture.Measure("pool-type", count, dynamicType, sample => Assert.That(sample.Bytes, Is.Zero));
            var items = new ColdItem<PoolPerformanceTests>[4096];
            MemoryPool<ColdItem<PoolPerformanceTests>>.SetCapacity(4096, 4096);
            Action burst = () =>
            {
                for (int i = 0; i < items.Length; i++) items[i] = MemoryPool<ColdItem<PoolPerformanceTests>>.Acquire();
                for (int i = 0; i < items.Length; i++) MemoryPool.Release(items[i]);
            };
            yield return AllocationCapture.Measure("pool-growth-4096", items.Length, burst, sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            yield return AllocationCapture.Measure("pool-burst-reuse-4096", items.Length, burst, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(Info<ColdItem<PoolPerformanceTests>>().UsingCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SequentialDemandDoesNotCreateUnusedObjectsInMaintenance()
        {
            Use<ColdItem<Guid>>();
            for (int frame = 0; frame < 120; frame++)
            {
                for (int i = 0; i < 100; i++) MemoryPool.Release(MemoryPool<ColdItem<Guid>>.Acquire());
                Tick();
            }
            var info = Info<ColdItem<Guid>>();
            TestContext.WriteLine($"POLICY,sequential,created={info.CreateCount},retained={info.UnusedCount},target={info.TargetFreeReserve}");
            Assert.That(info.CreateCount, Is.EqualTo(1));
            yield return null;
        }
    }
}
#endif
