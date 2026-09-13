#if UNITY_EDITOR
using System;
using System.Collections;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourcePerformanceTests
    {
        [UnityTest]
        public IEnumerator IndexChurnAllocations()
        {
            long allocated = 0;
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                var map = new ResourceUlongIntMap();
                map.ReserveCapacity(16);
                int removed = 0;
                yield return AllocationCapture.Measure("index-churn", count, () =>
                {
                    for (ulong i = 1; i <= (ulong)count; i++)
                    {
                        map.Set(i, 1);
                        if (map.Remove(i)) removed++;
                    }
                }, sample => allocated += sample.Bytes);
                Assert.That(removed, Is.EqualTo(count));
                Assert.That(map.Count, Is.Zero);
            }
            Assert.That(allocated, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CachedLeaseAndBindingAllocations()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Sprite("s");
            string location = "a";
            string package = f.Service.DefaultPackageName;
            var held = f.Service.LoadLease<TextAsset>(location);
            var owner = f.Owner();
            var target = owner.gameObject.AddComponent<SpriteRenderer>();
            var key = new ResourceKey("s", assetType: typeof(Sprite));
            f.Bindings.BindSprite(owner, target, key);
            yield return AllocationCapture.Measure("cached-async-first-use", 1,
                () => f.Service.LoadLeaseAsync<TextAsset>(location, default, package).GetAwaiter().GetResult().Dispose(), _ => { });
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                yield return AllocationCapture.Measure("cached-lease", count, () =>
                {
                    for (int i = 0; i < count; i++) f.Service.LoadLease<TextAsset>(location, package).Dispose();
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("cached-async-lease", count, () =>
                {
                    for (int i = 0; i < count; i++) f.Service.LoadLeaseAsync<TextAsset>(location, default, package).GetAwaiter().GetResult().Dispose();
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("binding-replace", count, () =>
                {
                    for (int i = 0; i < count; i++) f.Bindings.BindSprite(owner, target, key);
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                yield return AllocationCapture.Measure("binding-unregister", count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        f.Bindings.UnregisterTarget(owner, target);
                        f.Bindings.BindSprite(owner, target, key);
                    }
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
                Assert.That(f.Info("s").BindingRefCount, Is.EqualTo(1));
            }
            held.Dispose();
            f.Bindings.ReleaseOwner(owner);
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FirstUseAndCapacityGrowth()
        {
            ResourceFixture f = null;
            try
            {
                yield return AllocationCapture.Measure("fixture-first-use", 1, () => f = new ResourceFixture(), _ => { });
                f.Text("first");
                var firstKey = ResourceKey.Asset<TextAsset>("first");
                ResourceAssetLease<TextAsset> lease = default;
                yield return AllocationCapture.Measure("load-first-use", 1, () => lease = f.Service.LoadLease<TextAsset>(firstKey), _ => { });
                lease.Dispose();
                f.Text("second");
                var secondKey = ResourceKey.Asset<TextAsset>("second");
                yield return AllocationCapture.Measure("load-new-key", 1, () => lease = f.Service.LoadLease<TextAsset>(secondKey), _ => { });
                lease.Dispose();
                var owner = f.Owner();
                f.Sprite("sprite");
                var target = owner.gameObject.AddComponent<SpriteRenderer>();
                var key = new ResourceKey("sprite", assetType: typeof(Sprite));
                yield return AllocationCapture.Measure("binding-first-owner", 1,
                    () => f.Bindings.BindSprite(owner, target, key), _ => { });
                var other = f.Owner();
                var otherTarget = other.gameObject.AddComponent<SpriteRenderer>();
                yield return AllocationCapture.Measure("binding-new-owner", 1,
                    () => f.Bindings.BindSprite(other, otherTarget, key),
                    sample => Assert.That(sample.Bytes, Is.Zero));
                var leases = new ResourceAssetLease<TextAsset>[1024];
                foreach (int count in new[] { 16, 256, 1024 })
                {
                    yield return AllocationCapture.Measure("lease-capacity-growth", count, () =>
                    {
                        for (int i = 0; i < count; i++) leases[i] = f.Service.LoadLease<TextAsset>(firstKey);
                        for (int i = 0; i < count; i++) leases[i].Dispose();
                    }, _ => { });
                }
                f.Service.UnloadUnusedAssets(true);
                Assert.That(f.Info("first").RefCountTotal, Is.Zero);
            }
            finally { f?.Dispose(); }
        }
    }
}
#endif
