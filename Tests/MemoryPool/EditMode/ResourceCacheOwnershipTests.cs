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
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            replacement.Dispose();
            f.AssertNoReferences("a");
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void LeaseScopeAndBindingReferencesReleaseIndependently()
        {
            using var f = new ResourceFixture();
            var sprite = f.Sprite("sprite");
            var loads = f.Service.CreateScope();
            var bindings = f.Service.CreateScope();
            var target = f.GameObject().AddComponent<SpriteRenderer>();
            var lease = f.Service.LoadLease<Sprite>("sprite");
            loads.Load<Sprite>("sprite");
            target.SetSprite(bindings, "sprite");
            Assert.That(f.Info("sprite").RefCount, Is.EqualTo(3));
            lease.Dispose();
            loads.Dispose();
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            Assert.That(target.sprite, Is.SameAs(sprite));
            bindings.Dispose();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ScopeDeduplicatesRepeatedLoadsAndReleasesAllOwnedResources()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Text("b");
            var scope = f.Service.CreateScope();
            for (int i = 0; i < 1000; i++) scope.Load<TextAsset>("a");
            scope.Load<TextAsset>("b");
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            Assert.That(f.Loader.Loads, Is.EqualTo(2));
            f.Service.IdleAssetCapacity = 0;
            scope.Dispose();
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
            f.AssertReleased("b");
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Info("c").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Loader.Loads, Is.EqualTo(3));
        }

        [Test]
        public void ExpiryBudgetEventuallyReleasesAllIdleHandles()
        {
            using var f = new ResourceFixture();
            f.Service.IdleAssetCapacity = 128;
            f.Service.IdleAssetExpireTime = 0;
            f.Service.IdleReleaseCountPerTick = 16;
            for (int i = 0; i < 65; i++)
            {
                string key = i.ToString();
                f.Text(key);
                f.Service.LoadLease<TextAsset>(key).Dispose();
            }
            f.Tick(1f);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(49));
            for (int i = 0; i < 4; i++) f.Tick(1f);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ReactivatedAssetLeavesTheIdleListAndExpiryUsesTheCurrentTtl()
        {
            using var f = new ResourceFixture();
            f.Text("long"); f.Text("short"); f.Text("reactivated");
            f.Service.IdleAssetExpireTime = 100;
            f.Service.LoadLease<TextAsset>("long").Dispose();
            f.Service.LoadLease<TextAsset>("short").Dispose();
            f.Service.LoadLease<TextAsset>("reactivated").Dispose();
            var held = f.Service.LoadLease<TextAsset>("reactivated");
            f.Tick(50f);
            Assert.That(f.Info("long").State, Is.EqualTo(ResourceAssetState.Idle));
            f.Service.IdleAssetExpireTime = 10;
            f.Tick(1f);
            f.AssertReleased("long");
            f.AssertReleased("short");
            Assert.That(held.IsValid, Is.True);
            held.Dispose();
            Assert.That(f.Info("reactivated").State, Is.EqualTo(ResourceAssetState.Idle));
            f.Tick(200f);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void FailedBackendLoadDoesNotLeavePendingReferencesOrKeys()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Loader.ThrowOnLoad = true;
            for (int i = 0; i < 20; i++)
                Assert.That(Assert.Throws<InvalidOperationException>(() => f.Service.LoadLease<TextAsset>("a")).Message, Is.EqualTo("controlled load failure"));
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Service.GetStats().LoadingCount, Is.Zero);
            f.Loader.ThrowOnLoad = false;
            var lease = f.Service.LoadLease<TextAsset>("a");
            Assert.That(lease.IsValid, Is.True);
            Assert.That(f.Service.GetStats().WaiterCount, Is.Zero);
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            lease.Dispose();
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }
    }
}
