#if UNITY_EDITOR
using System;
using System.Collections;
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
    }
}
#endif
