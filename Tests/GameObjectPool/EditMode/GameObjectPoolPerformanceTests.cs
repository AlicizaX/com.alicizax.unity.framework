using System;
using System.Collections;
using System.Diagnostics;
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

        [UnityTest]
        public IEnumerator HitPathCostDoesNotDependOnIdleCount()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Sticky, minIdle: 0, soft: 2048, hard: 2048));
            double smallest = 0;
            foreach (int idle in new[] { 1, 2048 })
            {
                FillIdle("fx/hit", idle);
                Action hitPath = () =>
                {
                    for (int i = 0; i < 2000; i++) Service.Despawn(Service.Spawn("fx/hit"));
                };
                hitPath();
                double best = double.MaxValue;
                for (int run = 0; run < 5; run++)
                {
                    var clock = Stopwatch.StartNew();
                    hitPath();
                    clock.Stop();
                    best = Math.Min(best, clock.Elapsed.TotalMilliseconds * 1000000.0 / 2000);
                }

                TestContext.WriteLine($"GOP-HOTPATH,idle={idle},ns/op={best:F0}");
                if (idle == 1) smallest = best;
                else Assert.That(best, Is.LessThan(smallest * 3 + 2000), "idle=" + idle);
                yield return AllocationCapture.Measure("hit-path-idle-" + idle, 2000, hitPath, sample => Assert.That(sample.Bytes, Is.Zero));
            }
        }
    }
}
