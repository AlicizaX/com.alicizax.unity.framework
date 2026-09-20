#if UNITY_EDITOR
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using AlicizaX.Resource.Runtime;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolScalePerformanceTests : PoolFixture
    {
        [UnityTest]
        public IEnumerator NewTypeMaterializationIsMeasuredSeparately()
        {
            Type type = typeof(ColdItem<PoolScalePerformanceTests>);
            MemoryObject item = null;
            yield return AllocationCapture.Measure("pool-new-type-and-instance", 1,
                () => item = MemoryPool.Acquire(type), sample => Assert.That(sample.Bytes, Is.GreaterThan(0)));
            MemoryPool.Release(item);
            MemoryPool.RemoveAll(type);
        }

        [UnityTest]
        public IEnumerator PoolSizeDoesNotIntroduceHotPathAllocations()
        {
            Use<ColdItem<int>>();
            const int operations = 10000;
            var held = new ColdItem<int>[10000];
            foreach (int size in new[] { 1, 32, 1024, 10000 })
            {
                MemoryPool<ColdItem<int>>.SetCapacity(size, size);
                for (int i = 0; i < size; i++) held[i] = MemoryPool<ColdItem<int>>.Acquire();
                for (int i = 0; i < size; i++) MemoryPool.Release(held[i]);
                Action action = () => RunHotPath<ColdItem<int>>(operations);
                yield return AllocationCapture.Measure("pool-scale-first-" + size, operations, action, _ => { });
                yield return AllocationCapture.Measure("pool-scale-" + size, operations, action, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(Info<ColdItem<int>>().UsingCount, Is.Zero);
                MemoryPool<ColdItem<int>>.ClearAll();
                Assert.That(Info<ColdItem<int>>().PageCapacity, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator TenThousandConcurrentLeasesMeasureGrowthReuseAndTrim()
        {
            Use<ColdItem<long>>();
            const int count = 10000;
            var items = new ColdItem<long>[count];
            MemoryPool<ColdItem<long>>.SetCapacity(count, count);
            Action burst = () =>
            {
                for (int i = 0; i < count; i++) items[i] = MemoryPool<ColdItem<long>>.Acquire();
                for (int i = 0; i < count; i++) MemoryPool.Release(items[i]);
            };
            yield return AllocationCapture.Measure("pool-extreme-growth", count, burst, sample => Assert.That(sample.Bytes, Is.GreaterThan(count * 16)));
            yield return AllocationCapture.Measure("pool-extreme-reuse", count, burst, sample => Assert.That(sample.Bytes, Is.Zero));
            Action trim = () => MemoryPool<ColdItem<long>>.TrimNativeMetadata();
            yield return AllocationCapture.Measure("pool-extreme-trim-first", count, trim, _ => { });
            Assert.That(Info<ColdItem<long>>().PageCapacity, Is.Zero);
            burst();
            yield return AllocationCapture.Measure("pool-extreme-trim", count, trim, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(Info<ColdItem<long>>().PageCapacity, Is.Zero);
            Assert.That(Info<ColdItem<long>>().UsingCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CachedResourcesAndBoundedMaintenanceHaveMeasuredAllocationCosts()
        {
            using var f = new ResourceFixture();
            f.Text("hit");
            var key = ResourceKey.Asset<TextAsset>("hit");
            ResourceAssetLease<TextAsset> lease = default;
            yield return AllocationCapture.Measure("resource-cold-load", 1, () => lease = f.Service.LoadLease<TextAsset>(key), _ => { }, true);
            lease.Dispose();
            Action sync = () =>
            {
                for (int i = 0; i < 10000; i++) f.Service.LoadLease<TextAsset>(key).Dispose();
            };
            Action async = () =>
            {
                for (int i = 0; i < 10000; i++) f.Service.LoadLeaseAsync<TextAsset>(key).GetAwaiter().GetResult().Dispose();
            };
            yield return AllocationCapture.Measure("resource-cache-sync-first", 10000, sync, _ => { });
            yield return AllocationCapture.Measure("resource-cache-sync", 10000, sync, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("resource-cache-async-first", 10000, async, _ => { }, true);
            yield return AllocationCapture.Measure("resource-cache-async", 10000, async, sample => Assert.That(sample.Bytes, Is.Zero));
            f.Service.IdleAssetCapacity = 4096;
            f.Service.IdleAssetExpireTime = 3600;
            int loaded = 1;
            foreach (int size in new[] { 16, 256, 4096 })
            {
                while (loaded < size)
                {
                    string name = "maintenance-" + loaded++;
                    f.Text(name);
                    f.Service.LoadLease<TextAsset>(name).Dispose();
                }
                Action maintain = () =>
                {
                    for (int i = 0; i < 10000; i++) f.Service.ProcessResourceMaintenance(0, 16);
                };
                yield return AllocationCapture.Measure("resource-maintenance-first-" + size, 10000, maintain, _ => { });
                yield return AllocationCapture.Measure("resource-maintenance-" + size, 10000, maintain, sample => Assert.That(sample.Bytes, Is.Zero));
            }
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunHotPath<T>(int count) where T : MemoryObject, new()
        {
            for (int i = 0; i < count; i++) MemoryPool<T>.Release(MemoryPool<T>.Acquire());
        }

        [UnityTest]
        public IEnumerator ScheduledPoolMaintenanceDoesNotAllocateOnSuccessfulTicks()
        {
            MemoryPool.ShortDecayStartFrames = int.MaxValue;
            MemoryPool.LongDecayStartFrames = int.MaxValue;
            MemoryPool.ZeroFreeReserveStartFrames = int.MaxValue;
            MemoryPool.UnscheduleIdleFrames = int.MaxValue;
            MemoryPool.AutoTrimNativeMetadataFrames = -1;
            MemoryPool.Release(MemoryPool<PoolItem>.Acquire());
            Action maintain = () => Tick(10000);
            yield return AllocationCapture.Measure("pool-maintenance-first", 10000, maintain, _ => { });
            yield return AllocationCapture.Measure("pool-maintenance", 10000, maintain, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(MemoryPool.GetHandle(typeof(PoolItem)).Inner.ActiveIndex, Is.GreaterThanOrEqualTo(0));
        }
    }
}
#endif
