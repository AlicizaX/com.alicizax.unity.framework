using System.Collections;
using AlicizaX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class GameObjectPoolPerformanceTests : GameObjectPoolFixture
    {
        [UnityTest]
        public IEnumerator SteadySpawnDespawnHitPathHasNoPerOperationAllocation()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", minIdle: 1, soft: 8, hard: 16));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            Service.Despawn(instance);
            yield return AllocationCapture.Measure("first-hit-spawn-despawn", 1, () =>
            {
                instance = Service.Spawn("fx/hit");
                Service.Despawn(instance);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("steady-hit-spawn-despawn", 10000, () =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    instance = Service.Spawn("fx/hit");
                    Service.Despawn(instance);
                }
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Ledger("fx/hit", 1, 0, 1, true);
        }

        [UnityTest]
        public IEnumerator TickWithNoDueWorkAndCachedCatalogResolveDoNotAllocate()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/**", minIdle: 1, soft: 8, hard: 16));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            Service.Despawn(instance);
            Assert.That(Service.Spawn("fx/hit"), Is.SameAs(instance));
            Service.Despawn(instance);
            Tick();
            yield return AllocationCapture.Measure("cached-resolve-spawn", 1000, () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    instance = Service.Spawn("fx/hit");
                    Service.Despawn(instance);
                }
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            yield return AllocationCapture.Measure("idle-tick", 16, () =>
            {
                for (int i = 0; i < 16; i++)
                    Tick();
            }, sample => Assert.That(sample.Bytes, Is.Zero));
        }
    }
}
