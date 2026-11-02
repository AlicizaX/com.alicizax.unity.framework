#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolPerformanceTests : PoolFixture
    {
        [UnityTest]
        public IEnumerator ColdGrowthAllocatesAndSteadyStateDoesNot()
        {
            const int count = 10000;
            Use<ColdItem<PoolPerformanceTests>>();
            ColdItem<PoolPerformanceTests> first = null;
            yield return AllocationCapture.Measure("pool-cold-instance", 1,
                () => first = MemoryPool.Acquire<ColdItem<PoolPerformanceTests>>(), sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            MemoryPool.Release(first);
            Action steady = () => RunHotPath<ColdItem<PoolPerformanceTests>>(count);
            yield return AllocationCapture.Measure("pool-steady-first-call", count, steady, _ => { }, true);
            yield return AllocationCapture.Measure("pool-steady", count, steady, sample => Assert.That(sample.Bytes, Is.Zero));
            var items = new ColdItem<PoolPerformanceTests>[4096];
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 4096, 4096, 16, 30f));
            Action burst = () =>
            {
                for (int i = 0; i < items.Length; i++) items[i] = MemoryPool.Acquire<ColdItem<PoolPerformanceTests>>();
                for (int i = 0; i < items.Length; i++) MemoryPool.Release(items[i]);
            };
            yield return AllocationCapture.Measure("pool-growth-4096", items.Length, burst, sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            yield return AllocationCapture.Measure("pool-burst-reuse-4096", items.Length, burst, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(Info<ColdItem<PoolPerformanceTests>>().LeasedCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HotPathCostDoesNotDependOnFreeListSize()
        {
            Use<ColdItem<int>>();
            const int operations = 200000;
            var held = new ColdItem<int>[10000];
            MemoryPool.SetBudget(new MemoryPoolBudget(0, 10000, 10000, 16, 30f));
            RunHotPath<ColdItem<int>>(operations);
            double smallest = 0;
            foreach (int size in new[] { 1, 1024, 10000 })
            {
                for (int i = 0; i < size; i++) held[i] = MemoryPool.Acquire<ColdItem<int>>();
                for (int i = 0; i < size; i++) MemoryPool.Release(held[i]);
                double best = double.MaxValue;
                for (int run = 0; run < 5; run++)
                {
                    var clock = Stopwatch.StartNew();
                    RunHotPath<ColdItem<int>>(operations);
                    clock.Stop();
                    best = Math.Min(best, clock.Elapsed.TotalMilliseconds * 1000000.0 / operations);
                }

                TestContext.WriteLine($"POOL-HOTPATH,free={size},ns/op={best:F2}");
                if (size == 1) smallest = best;
                else Assert.That(best, Is.LessThan(smallest * 4 + 50), "free=" + size);
                Action action = () => RunHotPath<ColdItem<int>>(10000);
                yield return AllocationCapture.Measure("pool-scale-" + size, 10000, action, sample => Assert.That(sample.Bytes, Is.Zero));
                MemoryPool<ColdItem<int>>.Instance.Trim(0);
            }
        }

        [UnityTest]
        public IEnumerator ScheduledTickAndTrimDoNotAllocate()
        {
            Use<ColdItem<long>>();
            MemoryPool.SetBudget(new MemoryPoolBudget(1, 4096, 4096, 16, 30f));
            var items = new ColdItem<long>[4096];
            for (int i = 0; i < items.Length; i++) items[i] = MemoryPool.Acquire<ColdItem<long>>();
            for (int i = 0; i < items.Length; i++) MemoryPool.Release(items[i]);
            Action tick = () =>
            {
                for (int i = 0; i < 10000; i++) Tick(0.001f);
            };
            yield return AllocationCapture.Measure("pool-tick-first", 10000, tick, _ => { });
            yield return AllocationCapture.Measure("pool-tick", 10000, tick, sample => Assert.That(sample.Bytes, Is.Zero));
            for (int i = 0; i < items.Length; i++) items[i] = MemoryPool.Acquire<ColdItem<long>>();
            for (int i = 0; i < items.Length; i++) MemoryPool.Release(items[i]);
            Action trim = () => MemoryPool<ColdItem<long>>.Instance.Trim(0);
            yield return AllocationCapture.Measure("pool-trim-4096", items.Length, trim, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(Info<ColdItem<long>>().FreeCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SequentialDemandDoesNotCreateUnusedObjects()
        {
            Use<ColdItem<Guid>>();
            int created = Info<ColdItem<Guid>>().CreatedCount;
            for (int frame = 0; frame < 120; frame++)
            {
                for (int i = 0; i < 100; i++) MemoryPool.Release(MemoryPool.Acquire<ColdItem<Guid>>());
                Tick(1f / 60f);
            }

            var info = Info<ColdItem<Guid>>();
            TestContext.WriteLine($"POLICY,sequential,created={info.CreatedCount - created},retained={info.FreeCount},target={info.TargetFreeCount}");
            Assert.That(info.CreatedCount - created, Is.EqualTo(1));
            yield return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunHotPath<T>(int count) where T : MemoryObject, new()
        {
            for (int i = 0; i < count; i++) MemoryPool.Release(MemoryPool.Acquire<T>());
        }
    }
}
#endif
