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
            public override void Clear()
            {
                // 业务 Release() 失败也不能泄漏底层资源：包装对象归还 MemoryPool 时会执行
                // Clear()，这里才是最终释放点。
                Lease.Dispose();
                base.Clear();
                Lease = default;
                ThrowOnRelease = false;
            }
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
        public void BusinessReleaseFailureDetachesAndClearReleasesTheLease()
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
                Assert.That(pool.Count, Is.Zero, "a failed release must not retain a zombie slot");
                Assert.That(pool.Spawn(), Is.Null);
                Assert.That(Info<ResourceWrapper>().UsingCount, Is.Zero);
                Assert.That(f.Loader.LiveHandles, Is.Zero, "Clear() releases the underlying resource lease");
            }
            finally
            {
                wrapper.ThrowOnRelease = false;
                ((IServiceLifecycle)service).Destroy();
            }
        }
    }
}
