using AlicizaX.Resource.Runtime;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class BusinessPoolOwnershipTests : PoolFixture
    {
        public sealed class LeaseWrapper : MemoryObject
        {
            public ResourceAssetLease<TextAsset> Lease;

            public override void Clear()
            {
                Lease.Dispose();
                Lease = default;
            }
        }

        public sealed class CachedLeaseWrapper : MemoryObject, IPoolEvictable
        {
            public ResourceAssetLease<TextAsset> Lease;
            public int Uses;

            public override void Clear()
            {
                Uses++;
            }

            public void OnEvict()
            {
                Lease.Dispose();
                Lease = default;
            }
        }

        [Test]
        public void WrapperReleasesItsLeaseWhenReturned()
        {
            Use<LeaseWrapper>();
            using var f = new ResourceFixture();
            f.Text("a");
            f.Service.IdleAssetCapacity = 0;
            var wrapper = MemoryPool.Acquire<LeaseWrapper>();
            wrapper.Lease = f.Service.LoadLease<TextAsset>("a");
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            MemoryPool.Release(wrapper);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(wrapper.Lease.IsValid, Is.False);
            Assert.That(Info<LeaseWrapper>().LeasedCount, Is.Zero);
        }

        [Test]
        public void CachedWrapperKeepsItsLeaseAcrossReuseUntilEviction()
        {
            Use<CachedLeaseWrapper>();
            using var f = new ResourceFixture();
            f.Text("a");
            f.Service.IdleAssetCapacity = 0;
            var wrapper = MemoryPool.Acquire<CachedLeaseWrapper>();
            wrapper.Lease = f.Service.LoadLease<TextAsset>("a");
            MemoryPool.Release(wrapper);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            Assert.That(MemoryPool.Acquire<CachedLeaseWrapper>(), Is.SameAs(wrapper));
            Assert.That(wrapper.Lease.IsValid, Is.True);
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            MemoryPool.Release(wrapper);
            MemoryPool<CachedLeaseWrapper>.Instance.Trim(0);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(wrapper.Uses, Is.EqualTo(2));
        }
    }
}
