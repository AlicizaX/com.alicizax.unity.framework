using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public abstract class AsyncLifetimeCases : GameObjectPoolFixture
    {
        [TestCase("spawn", false)]
        [TestCase("spawn", true)]
        [TestCase("load", false)]
        [TestCase("load", true)]
        [TestCase("warmup", false)]
        [TestCase("warmup", true)]
        public void AlreadyCanceledRequestsDoNotLoadOrCreate(string operation, bool loaded)
        {
            Prefab("fx/a");
            Catalog(Entry("fx/a", PoolPolicy.Fixed));
            if (loaded) Service.LoadPrefab("fx/a");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            UniTask task = operation == "spawn"
                ? Service.SpawnAsync("fx/a", cancellationToken: cancellation.Token).AsUniTask()
                : operation == "load"
                    ? Service.LoadPrefabAsync("fx/a", cancellation.Token).AsUniTask()
                    : Service.WarmupAsync("fx/a", 1, cancellation.Token);
            var awaiter = task.GetAwaiter();
            Assert.That(awaiter.IsCompleted, Is.True);
            Assert.Throws<OperationCanceledException>(() => awaiter.GetResult());
            Assert.That(Loader.AsyncLoadCalls, Is.Zero);
            Ledger("fx/a", 0, 0, 0, loaded);
        }

        [UnityTest]
        public IEnumerator WarmupCannotCreateInReusedPoolAfterCatalogReload()
        {
            Prefab("fx/a");
            Prefab("fx/b");
            Catalog(Entry("fx/a", PoolPolicy.Sticky, soft: 64, hard: 64));
            Service.LoadPrefab("fx/a");
            UniTask warmup = Service.WarmupAsync("fx/a", 64);
            RuntimeGameObjectPool original = RuntimePool("fx/a");
            Assert.That(original.TotalCount, Is.InRange(1, 8));
            Catalog(Entry("fx/b", PoolPolicy.Sticky, soft: 64, hard: 64));
            Service.LoadPrefab("fx/b");
            Assert.That(RuntimePool("fx/b"), Is.SameAs(original), "Exercise wrapper reuse.");
            yield return Canceled(warmup);
            Ledger("fx/b", 0, 0, 0, true);
            Assert.That(Loader.LiveLeases, Is.EqualTo(1));
            Assert.That(Loader.UnloadCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator WarmupStopsAfterServiceShutdown()
        {
            Prefab("fx/a");
            Catalog(Entry("fx/a", soft: 64, hard: 64));
            UniTask warmup = Service.WarmupAsync("fx/a", 64);
            Assert.That(RuntimePool("fx/a").TotalCount, Is.InRange(1, 8));
            ((IServiceLifecycle)Service).Destroy();
            Service = null;
            yield return Canceled(warmup);
            Assert.That(Loader.LiveLeases, Is.Zero);
            Assert.That(Using<RuntimeGameObjectPool>(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator CanceledWarmupSchedulesItsCreatedIdleInstances()
        {
            Prefab("fx/a");
            Catalog(Entry("fx/a", PoolPolicy.Fixed, soft: 64, hard: 64));
            using var cancellation = new CancellationTokenSource();
            UniTask warmup = Service.WarmupAsync("fx/a", 64, cancellation.Token);
            int created = RuntimePool("fx/a").TotalCount;
            Assert.That(created, Is.InRange(1, 8));
            cancellation.Cancel();
            yield return Canceled(warmup);
            Ledger("fx/a", created, 0, created, true);
            Assert.That(MaintenanceCount(), Is.EqualTo(1));
            TickUntil("fx/a", pool => pool.TotalCount == 0);
            Assert.That(Loader.LiveLeases, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MixedWaitersShareLoadAndCancelOnlyTheSpawnWaiter()
        {
            Prefab("fx/a");
            Catalog(Entry("fx/a", PoolPolicy.Sticky, soft: 8, hard: 8));
            Loader.CompleteImmediately = false;
            using var cancellation = new CancellationTokenSource();
            UniTask<GameObject> spawn = Service.SpawnAsync("fx/a", cancellationToken: cancellation.Token);
            UniTask<GameObject> load = Service.LoadPrefabAsync("fx/a");
            UniTask warmup = Service.WarmupAsync("fx/a", 3);
            cancellation.Cancel();
            yield return WaitCanceled(spawn);
            Assert.That(Loader.AsyncLoadCalls, Is.EqualTo(1));
            Loader.CompletePending();
            yield return Wait(load, value => Assert.That(value, Is.SameAs(Loader.Prefabs["fx/a"])));
            yield return Wait(warmup);
            Ledger("fx/a", 3, 0, 3, true);
            Assert.That(Loader.LiveLeases, Is.EqualTo(1));
        }

        private static IEnumerator Canceled(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 180; frame++) yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "Canceled operation did not terminate.");
            Assert.Throws<OperationCanceledException>(() => awaiter.GetResult());
        }
    }
}
