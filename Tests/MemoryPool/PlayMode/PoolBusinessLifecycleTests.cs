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
        public IEnumerator SettingDestructionRetiresLeasesThatReturnLater()
        {
            var go = new GameObject("pool-setting-test");
            go.AddComponent<MemoryPoolSetting>();
            var item = MemoryPool<PoolItem>.Acquire();
            item.Payload = new object();
            UnityEngine.Object.Destroy(go);
            yield return null;
            Assert.That(item.Payload, Is.Not.Null);
            MemoryPool.Release(item);
            Assert.That(item.Evictions, Is.EqualTo(1));
            Assert.That(Info<PoolItem>().PageCapacity, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DestroyedOwnerReleasesResourcesAndPooledLoadContexts()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            var owner = f.Owner();
            f.Service.LoadAsset<TextAsset>(owner, "a");
            f.Service.IdleAssetCapacity = 0;
            UnityEngine.Object.Destroy(owner.gameObject);
            yield return null;
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 16);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ConcurrentLoadsShareOneContextAndCancellationBalancesWaiters()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var operations = new UniTask<ResourceAssetLease<TextAsset>>[64];
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
                ResourceAssetLease<TextAsset> lease = default;
                yield return ResourceFixture.Wait(operations[i], value => lease = value);
                if (lease.IsValid) { valid++; lease.Dispose(); }
            }
            Assert.That(valid, Is.EqualTo(32));
            Assert.That(f.Info("a").RefCountTotal, Is.Zero);
            f.Service.IdleAssetCapacity = 0;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            AssertNoLeasedLoadContexts();
        }

        [UnityTest]
        public IEnumerator LastCancelledWaiterDisposesHandleAndReturnsContext()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            using var cancellation = new CancellationTokenSource();
            var operation = f.Service.LoadLeaseAsync<TextAsset>("a", cancellation.Token);
            cancellation.Cancel();
            ResourceAssetLease<TextAsset> lease = default;
            yield return ResourceFixture.Wait(operation, value => lease = value);
            Assert.That(lease.IsValid, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            AssertNoLeasedLoadContexts();
        }

        [UnityTest]
        public IEnumerator ServiceShutdownDuringPendingLoadReturnsContextAfterWaiterResumes()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var operation = f.Service.LoadLeaseAsync<TextAsset>("a");
            typeof(ResourceService).GetMethod("OnDestroyService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(f.Service, null);
            ResourceAssetLease<TextAsset> lease = default;
            yield return ResourceFixture.Wait(operation, value => lease = value);
            Assert.That(lease.IsValid, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            AssertNoLeasedLoadContexts();
        }

        [UnityTest]
        public IEnumerator SnapshotOwnershipReleasesNestedMemoryObjects()
        {
            var snapshot = MemoryPool<GameObjectPoolSnapshot>.Acquire();
            for (int i = 0; i < 65; i++) snapshot.instances.Add(MemoryPool<GameObjectPoolInstanceSnapshot>.Acquire());
            MemoryPool.Release(snapshot);
            Assert.That(snapshot.InstanceCount, Is.Zero);
            MemoryPoolInfo info = default;
            MemoryPool<GameObjectPoolInstanceSnapshot>.GetInfo(ref info);
            Assert.That(info.UsingCount, Is.Zero);
            MemoryPool<GameObjectPoolSnapshot>.ClearAll();
            MemoryPool<GameObjectPoolInstanceSnapshot>.ClearAll();
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
            Assert.That(Info<PoolItem>().UnusedCount, Is.EqualTo(1));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ReturnPayload()
        {
            var item = MemoryPool<PoolItem>.Acquire();
            item.Payload = new byte[1024 * 1024];
            var weak = new WeakReference(item.Payload);
            MemoryPool.Release(item);
            return weak;
        }

        private static void AssertNoLeasedLoadContexts()
        {
            var infos = new MemoryPoolInfo[MemoryPool.Count];
            MemoryPool.GetAllMemoryPoolInfos(infos);
            foreach (var info in infos)
                if (info.Type.Name == "LoadingOperationState") Assert.That(info.UsingCount, Is.Zero);
        }
    }
}
