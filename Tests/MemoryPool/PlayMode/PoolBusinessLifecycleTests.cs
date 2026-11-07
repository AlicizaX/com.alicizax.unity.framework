using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX.Resource.Runtime;
using AlicizaX.Resource.Tests;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.MemoryPoolTests
{
    public sealed class PoolBusinessLifecycleTests : PoolFixture
    {
        [UnityTest]
        public IEnumerator SettingDestructionTrimsFreeObjectsAndLaterReturnsAreReused()
        {
            var go = new GameObject("pool-setting-test");
            go.AddComponent<MemoryPoolSetting>();
            var free = MemoryPool.Acquire<PoolItem>();
            var leased = MemoryPool.Acquire<PoolItem>();
            leased.Payload = new object();
            MemoryPool.Release(free);
            UnityEngine.Object.Destroy(go);
            yield return null;
            Assert.That(free.Evictions, Is.EqualTo(1));
            Assert.That(leased.Payload, Is.Not.Null);
            MemoryPool.Release(leased);
            Assert.That(leased.Evictions, Is.Zero);
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(1));
            Assert.That(MemoryPool.Acquire<PoolItem>(), Is.SameAs(leased));
            MemoryPool.Release(leased);
        }

        [UnityTest]
        public IEnumerator DestroyedOwnerReleasesResourcesAndPooledLoadContexts()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            var owner = f.GameObject("owner");
            f.Service.OwnerScope(owner, ResourceScopeSource.Binding, null).Load<TextAsset>("a");
            f.Service.IdleAssetCapacity = 0;
            UnityEngine.Object.Destroy(owner);
            yield return null;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            AssertNoLeasedLoadContexts();
        }

        [UnityTest]
        public IEnumerator ConcurrentLoadsShareOneContextAndCancellationBalancesWaiters()
        {
            using var f = new ResourceFixture(true);
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var operations = new UniTask<AssetLease<TextAsset>>[64];
            using var cancellation = new CancellationTokenSource();
            for (int i = 0; i < operations.Length; i++)
                operations[i] = f.Service.LoadLeaseAsync<TextAsset>("a", (i & 1) == 0 ? cancellation.Token : default);
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            cancellation.Cancel();
            yield return null;
            f.Loader.Complete("a");
            int valid = 0;
            for (int i = 0; i < operations.Length; i++)
            {
                AssetLease<TextAsset> lease = default;
                yield return f.Wait(operations[i], value => lease = value);
                if (lease.IsValid) { valid++; lease.Dispose(); }
            }
            Assert.That(valid, Is.EqualTo(32));
            f.AssertNoReferences("a");
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            AssertNoLeasedLoadContexts();
        }

        [UnityTest]
        public IEnumerator LastCancelledWaiterDisposesHandleAndReturnsContext()
        {
            using var f = new ResourceFixture(true);
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            using var cancellation = new CancellationTokenSource();
            var operation = f.Service.LoadLeaseAsync<TextAsset>("a", cancellation.Token);
            cancellation.Cancel();
            AssetLease<TextAsset> lease = default;
            yield return f.Wait(operation, value => lease = value);
            Assert.That(lease.IsValid, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            AssertNoLeasedLoadContexts();
        }

        [UnityTest]
        public IEnumerator ServiceShutdownDuringPendingLoadReturnsContextAfterWaiterResumes()
        {
            using var f = new ResourceFixture(true);
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var operation = f.Service.LoadLeaseAsync<TextAsset>("a");
            typeof(ResourceService).GetMethod("OnDispose", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(f.Service, null);
            AssetLease<TextAsset> lease = default;
            yield return f.Wait(operation, value => lease = value);
            Assert.That(lease.IsValid, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            AssertNoLeasedLoadContexts();
        }

        [UnityTest]
        public IEnumerator SnapshotOwnershipReleasesNestedMemoryObjects()
        {
            Use<GameObjectPoolSnapshot>();
            Use<GameObjectPoolInstanceSnapshot>();
            var snapshot = MemoryPool.Acquire<GameObjectPoolSnapshot>();
            for (int i = 0; i < 65; i++) snapshot.instances.Add(MemoryPool.Acquire<GameObjectPoolInstanceSnapshot>());
            MemoryPool.Release(snapshot);
            Assert.That(snapshot.InstanceCount, Is.Zero);
            Assert.That(Info<GameObjectPoolInstanceSnapshot>().LeasedCount, Is.Zero);
            Assert.That(Info<GameObjectPoolInstanceSnapshot>().FreeCount, Is.EqualTo(65));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReturnedPayloadIsCollectibleWhilePoolRemainsAlive()
        {
            WeakReference payload = ReturnPayload();
            yield return null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(payload.IsAlive, Is.False);
            Assert.That(Info<PoolItem>().FreeCount, Is.EqualTo(1));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ReturnPayload()
        {
            var item = MemoryPool.Acquire<PoolItem>();
            item.Payload = new byte[1024 * 1024];
            var weak = new WeakReference(item.Payload);
            MemoryPool.Release(item);
            return weak;
        }

        private static void AssertNoLeasedLoadContexts()
        {
            var infos = new MemoryPoolInfo[MemoryPool.Count];
            int count = MemoryPool.GetInfos(infos);
            for (int i = 0; i < count; i++)
            {
                string name = infos[i].Type.Name;
                if (name == "LeaseLoadPromise`1" || name == "ScopeLoadPromise`1" || name == "BindingLoadWaiter")
                    Assert.That(infos[i].LeasedCount, Is.Zero, name);
            }
        }
    }
}
