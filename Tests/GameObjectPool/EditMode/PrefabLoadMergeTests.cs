using System;
using System.Collections;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class PrefabLoadMergeTests : GameObjectPoolFixture
    {
        [UnityTest]
        public IEnumerator SyncLoadDoesNotJoinInFlightAsyncLoad()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Loader.CompleteImmediately = false;
            UniTask<GameObject> pending = Service.LoadPrefabAsync("fx/hit");
            Assert.That(PrefabLoading("fx/hit"), Is.True);
            Assert.That(Service.LoadPrefab("fx/hit"), Is.Null);
            Assert.That(Service.Spawn("fx/hit"), Is.Null);
            Assert.That(Loader.LoadCalls, Is.Zero);
            Loader.CompletePending();
            GameObject loaded = null;
            yield return Wait(pending, value => loaded = value);
            Assert.That(loaded, Is.SameAs(Loader.Prefabs["fx/hit"]));
            Ledger("fx/hit", 0, 0, 0, true);
        }

        [UnityTest]
        public IEnumerator ConcurrentAsyncLoadsShareOneBackendCall()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Loader.CompleteImmediately = false;
            UniTask<GameObject> first = Service.LoadPrefabAsync("fx/hit");
            UniTask<GameObject> second = Service.LoadPrefabAsync("fx/hit");
            Assert.That(Loader.AsyncLoadCalls, Is.EqualTo(1));
            Assert.That(PrefabLoading("fx/hit"), Is.True);
            Loader.CompletePending();
            GameObject a = null, b = null;
            yield return Wait(first, value => a = value);
            yield return Wait(second, value => b = value);
            Assert.That(a, Is.SameAs(b));
            Assert.That(Loader.LiveLeases, Is.EqualTo(1));
            Ledger("fx/hit", 0, 0, 0, true);
        }

        [UnityTest]
        public IEnumerator AsyncFailureCompletesWaitersWithNullAndLeavesNoLease()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Loader.CompleteImmediately = false;
            UniTask<GameObject> first = Service.LoadPrefabAsync("fx/hit");
            UniTask<GameObject> spawn = Service.SpawnAsync("fx/hit");
            Loader.FailPending();
            GameObject loaded = Loader.Prefabs["fx/hit"];
            GameObject spawned = loaded;
            yield return Wait(first, value => loaded = value);
            yield return Wait(spawn, value => spawned = value);
            Assert.That(loaded, Is.Null);
            Assert.That(spawned, Is.Null);
            Assert.That(Loader.LiveLeases, Is.Zero);
            Ledger("fx/hit", 0, 0, 0, false);
            Assert.That(PrefabLoading("fx/hit"), Is.False);
        }

        [UnityTest]
        public IEnumerator CancelledWaiterDoesNotAbortSharedLoad()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Loader.CompleteImmediately = false;
            var cts = new CancellationTokenSource();
            UniTask<GameObject> cancelled = Service.LoadPrefabAsync("fx/hit", cts.Token);
            UniTask<GameObject> kept = Service.LoadPrefabAsync("fx/hit");
            cts.Cancel();
            yield return WaitCanceled(cancelled);
            Loader.CompletePending();
            GameObject prefab = null;
            yield return Wait(kept, value => prefab = value);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(Loader.LiveLeases, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LateResultAfterShutdownUnloadsPrefab()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Loader.CompleteImmediately = false;
            UniTask<GameObject> pending = Service.LoadPrefabAsync("fx/hit");
            int version = LoadVersion("fx/hit");
            ((IServiceLifecycle)Service).Destroy();
            Service = null;
            Loader.CompletePending();
            yield return null;
            yield return WaitCanceled(pending);
            Assert.That(Loader.UnloadCalls, Is.EqualTo(1));
            Assert.That(Loader.LiveLeases, Is.Zero);
            Assert.That(version, Is.GreaterThanOrEqualTo(0));
        }

        [UnityTest]
        public IEnumerator LoadCatalogDuringInFlightLoadDropsLateResult()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Loader.CompleteImmediately = false;
            UniTask<GameObject> pending = Service.LoadPrefabAsync("fx/hit");
            Catalog(Entry("fx/other"));
            Loader.CompletePending();
            yield return null;
            yield return WaitCanceled(pending);
            Assert.That(Loader.UnloadCalls, Is.EqualTo(1));
            Assert.That(RuntimePool("fx/hit"), Is.Null);
        }

        [UnityTest]
        public IEnumerator WarmupCreatesIdleUpToHardCapacityAndYields()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", minIdle: 0, soft: 5, hard: 5));
            yield return Wait(Service.WarmupAsync("fx/hit", 20));
            Ledger("fx/hit", 5, 0, 5, true);
            yield return Wait(Service.WarmupAsync("fx/hit", 2));
            Ledger("fx/hit", 5, 0, 5, true);
        }

        [UnityTest]
        public IEnumerator WarmupZeroAndNegativeDoNotLoadOrCreateInstances()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", minIdle: 0, soft: 5, hard: 5));
            yield return Wait(Service.WarmupAsync("fx/hit", 0));
            Ledger("fx/hit", 0, 0, 0, false);
            yield return Wait(Service.WarmupAsync("fx/hit", -8));
            Ledger("fx/hit", 0, 0, 0, false);
            Assert.That(Loader.LoadCalls + Loader.AsyncLoadCalls, Is.Zero);
            Assert.That(Loader.LiveLeases, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SpawnAsyncLoadsThenReturnsInstance()
        {
            Prefab("fx/hit", rootMarker: true, childMarker: true);
            Catalog(Entry("fx/hit"));
            GameObject instance = null;
            yield return Wait(Service.SpawnAsync("fx/hit"), value => instance = value);
            Assert.That(instance, Is.Not.Null);
            Ledger("fx/hit", 1, 1, 0, true);
            Service.Despawn(instance);
            RootMarker marker = null;
            yield return Wait(Service.SpawnAsync<RootMarker>("fx/hit"), value => marker = value);
            Assert.That(marker, Is.Not.Null);
            ChildOnlyMarker child = null;
            yield return Wait(Service.SpawnAsync<ChildOnlyMarker>("fx/hit"), value => child = value);
            Assert.That(child, Is.Null);
            Ledger("fx/hit", 2, 1, 1, true);
            Service.Despawn(marker.gameObject);
        }

        [UnityTest]
        public IEnumerator AsyncExceptionIsSwallowedAndWaitersSeeNull()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Loader.ThrowOnLoad = true;
            GameObject result = Loader.Prefabs["fx/hit"];
            yield return Wait(Service.LoadPrefabAsync("fx/hit"), value => result = value);
            Assert.That(result, Is.Null);
            Ledger("fx/hit", 0, 0, 0, false);
            Assert.That(PrefabLoading("fx/hit"), Is.False);
        }
    }
}
