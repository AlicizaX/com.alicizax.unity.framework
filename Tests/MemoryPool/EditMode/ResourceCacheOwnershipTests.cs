using System;
using AlicizaX.Resource.Runtime;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class ResourceCacheOwnershipTests : PoolFixture
    {
        [Test]
        public void CopiedLeaseAndStaleHandleCannotDoubleDecrementReferences()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            var first = f.Service.LoadLease<TextAsset>("a");
            var copy = first;
            first.Dispose();
            var replacement = f.Service.LoadLease<TextAsset>("a");
            copy.Dispose();
            Assert.That(replacement.IsValid, Is.True);
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
            replacement.Dispose();
            Assert.That(f.Info("a").RefCountTotal, Is.Zero);
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void MixedDirectLegacyAndBindingReferencesReleaseIndependently()
        {
            using var f = new ResourceFixture();
            var sprite = f.Sprite("sprite");
            var owner = f.Owner();
            var target = owner.gameObject.AddComponent<SpriteRenderer>();
            var direct = f.Service.LoadLease<Sprite>("sprite");
            var legacy = f.Service.LoadAsset<Sprite>("sprite");
            f.Bindings.BindSprite(owner, target, new ResourceKey("sprite", assetType: typeof(Sprite)));
            Assert.That(f.Info("sprite").RefCountTotal, Is.EqualTo(3));
            direct.Dispose();
            f.Service.UnloadAsset(legacy);
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            Assert.That(target.sprite, Is.SameAs(sprite));
            f.Bindings.ReleaseOwner(owner);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void OwnerDeduplicatesRepeatedLoadsAndReleasesAllOwnedResources()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Text("b");
            var owner = f.Owner();
            for (int i = 0; i < 1000; i++) f.Service.LoadAsset<TextAsset>(owner, "a");
            f.Service.LoadAsset<TextAsset>(owner, "b");
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
            Assert.That(f.Loader.Loads, Is.EqualTo(2));
            f.Service.IdleAssetCapacity = 0;
            f.Bindings.ReleaseOwner(owner);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(32)]
        public void IdleCapacityBoundsHandlesWithoutEvictingActiveLeases(int capacity)
        {
            using var f = new ResourceFixture();
            f.Service.IdleAssetCapacity = capacity;
            f.Text("active");
            var active = f.Service.LoadLease<TextAsset>("active");
            for (int i = 0; i < 100; i++)
            {
                string name = "asset-" + i;
                f.Text(name);
                f.Service.LoadLease<TextAsset>(name).Dispose();
                Assert.That(f.Loader.LiveHandles, Is.LessThanOrEqualTo(capacity + 1));
            }
            Assert.That(active.IsValid, Is.True);
            active.Dispose();
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void CapacityEvictsTheLeastRecentlyIdleAsset()
        {
            using var f = new ResourceFixture();
            f.Service.IdleAssetCapacity = 2;
            f.Text("a"); f.Text("b"); f.Text("c");
            f.Service.LoadLease<TextAsset>("a").Dispose();
            f.Service.LoadLease<TextAsset>("b").Dispose();
            f.Service.LoadLease<TextAsset>("a").Dispose();
            f.Service.LoadLease<TextAsset>("c").Dispose();
            Assert.That(f.Info("b").HandleValid, Is.False);
            Assert.That(f.Info("a").HandleValid, Is.True);
            Assert.That(f.Info("c").HandleValid, Is.True);
            Assert.That(f.Loader.Loads, Is.EqualTo(3));
        }

        [Test]
        public void ExpiryBudgetAndCursorEventuallyReleaseAllIdleHandles()
        {
            using var f = new ResourceFixture();
            f.Service.IdleAssetCapacity = 128;
            f.Service.IdleAssetExpireTime = 0;
            for (int i = 0; i < 65; i++)
            {
                string key = i.ToString();
                f.Text(key);
                f.Service.LoadLease<TextAsset>(key).Dispose();
            }
            f.Service.ProcessResourceMaintenance(Time.unscaledTime + 1, 16);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(49));
            for (int i = 0; i < 4; i++) f.Service.ProcessResourceMaintenance(Time.unscaledTime + 1, 16);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ExpiryCursorHandlesDifferentDeadlinesAndReactivation()
        {
            using var f = new ResourceFixture();
            f.Text("long"); f.Text("short"); f.Text("reactivated");
            f.Service.IdleAssetExpireTime = 100;
            f.Service.LoadLease<TextAsset>("long").Dispose();
            f.Service.IdleAssetExpireTime = 0;
            f.Service.LoadLease<TextAsset>("short").Dispose();
            f.Service.LoadLease<TextAsset>("reactivated").Dispose();
            var held = f.Service.LoadLease<TextAsset>("reactivated");
            for (int i = 0; i < 3; i++) f.Service.ProcessResourceMaintenance(Time.unscaledTime + 1, 1);
            Assert.That(f.Info("long").HandleValid, Is.True);
            Assert.That(f.Info("short").HandleValid, Is.False);
            Assert.That(held.IsValid, Is.True);
            held.Dispose();
            f.Service.ProcessResourceMaintenance(Time.unscaledTime + 200, 16);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void FailedBackendLoadDoesNotLeavePendingReferencesOrKeys()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Loader.ThrowOnLoad = true;
            for (int i = 0; i < 20; i++) Assert.Catch(() => f.Service.LoadLease<TextAsset>("a"));
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.Loader.ThrowOnLoad = false;
            var lease = f.Service.LoadLease<TextAsset>("a");
            Assert.That(lease.IsValid, Is.True);
            Assert.That(f.Info("a").PendingRefCount, Is.Zero);
            lease.Dispose();
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }
    }
}
