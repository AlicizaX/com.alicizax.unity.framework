using System;
using AlicizaX.ObjectPool;
using AlicizaX.Resource.Runtime;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class BusinessPoolOwnershipTests : PoolFixture
    {
        public sealed class ResourceWrapper : ObjectBase
        {
            public ResourceAssetLease<TextAsset> Lease;
            public bool ThrowOnRelease;
            public void Bind(ResourceAssetLease<TextAsset> lease) { Lease = lease; Initialize(lease.Asset); }
            protected internal override void Release(bool isShutdown)
            {
                if (ThrowOnRelease) throw new InvalidOperationException("business release");
                Lease.Dispose();
            }
            public override void Clear() { base.Clear(); Lease = default; ThrowOnRelease = false; }
        }

        [Test]
        public void SharedObjectPoolReferencesRetainResourceUntilTheLastUnspawn()
        {
            Use<ResourceWrapper>();
            using var f = new ResourceFixture();
            f.Text("a");
            f.Service.IdleAssetCapacity = 0;
            var service = new ObjectPoolService();
            ((IServiceLifecycle)service).Initialize(null, null);
            try
            {
                var pool = service.GetOrCreatePool<ResourceWrapper>(new ObjectPoolCreateOptions(allowMultiSpawn: true));
                var wrapper = MemoryPool<ResourceWrapper>.Acquire();
                wrapper.Bind(f.Service.LoadLease<TextAsset>("a"));
                Assert.That(pool.Register(wrapper, false), Is.True);
                Assert.That(pool.Spawn(), Is.SameAs(wrapper));
                Assert.That(pool.Spawn(), Is.SameAs(wrapper));
                pool.Unspawn(wrapper);
                pool.ReleaseAllUnused();
                Assert.That(wrapper.Lease.IsValid, Is.True);
                pool.Unspawn(wrapper);
                pool.ReleaseAllUnused();
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                Assert.That(Info<ResourceWrapper>().UsingCount, Is.Zero);
            }
            finally { ((IServiceLifecycle)service).Destroy(); }
        }

        [Test]
        public void BusinessReleaseFailureOrphansTheUnusedEntryUntilShutdown()
        {
            Use<ResourceWrapper>();
            using var f = new ResourceFixture();
            f.Text("a");
            f.Service.IdleAssetCapacity = 0;
            var service = new ObjectPoolService();
            ((IServiceLifecycle)service).Initialize(null, null);
            var wrapper = MemoryPool<ResourceWrapper>.Acquire();
            wrapper.Bind(f.Service.LoadLease<TextAsset>("a"));
            var pool = service.GetOrCreatePool<ResourceWrapper>();
            Assert.That(pool.Register(wrapper, false), Is.True);
            wrapper.ThrowOnRelease = true;
            try
            {
                Assert.Throws<InvalidOperationException>(() => pool.ReleaseAllUnused());
                Assert.That(pool.Count, Is.Zero);
                pool.ReleaseAllUnused();
                Assert.That(Info<ResourceWrapper>().UsingCount, Is.EqualTo(1));
                Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
                TestContext.WriteLine($"OWNERSHIP-LIMIT,business-release-failed,object-pool-count={pool.Count},memory-leases={Info<ResourceWrapper>().UsingCount}");
            }
            finally
            {
                wrapper.ThrowOnRelease = false;
                ((IServiceLifecycle)service).Destroy();
            }
            Assert.That(Info<ResourceWrapper>().UsingCount, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }
    }
}
