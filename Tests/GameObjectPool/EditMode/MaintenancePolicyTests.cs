using AlicizaX;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class MaintenancePolicyTests : GameObjectPoolFixture
    {
        [TestCase(1f)]
        [TestCase(1.6f)]
        public void BurstIdleTrimIncludesTheDeadlineAndExcludesTheInstantBeforeIt(float releasedAt)
        {
            Prefab("a");
            Catalog(Entry("a", idle: 1f));
            FillIdle("a", 1);
            var pool = RuntimePool("a");
            int index = Read<int>(pool, "_inactiveHead");
            var page = (System.Array)Read<System.Array>(pool, "_pages").GetValue(index >> 7);
            object slot = page.GetValue(index & 127);
            slot.GetType().GetField("lastReleaseTime").SetValue(slot, releasedAt);
            page.SetValue(slot, index & 127);
            pool.ExecuteMaintenance(releasedAt, false);
            float deadline = pool.NextMaintenanceAt;
            Assert.That(deadline, Is.EqualTo(releasedAt + 1f));
            pool.ExecuteMaintenance(deadline - 0.01f, false);
            Ledger("a", 1, 0, 1, true);
            pool.ExecuteMaintenance(deadline, false);
            Assert.That(pool.TotalCount, Is.Zero, $"At advertised deadline {deadline:R}, elapsed={deadline - releasedAt:R}, idle=1.");
            Ledger("a", 0, 0, 0, false);
        }

        [Test]
        public void FixedTrimBudgetIsHonoredAcrossTicksInsteadOfDrainingInOneTick()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Fixed, minIdle: 0, soft: 64, hard: 64));
            FillIdle("fx/hit", 40);
            Tick();
            Assert.That(DestroyCount("fx/hit"), Is.EqualTo(16));
            Assert.That(RuntimePool("fx/hit").TotalCount, Is.EqualTo(24));
            TickUntil("fx/hit", pool => pool.TotalCount == 0, 8);
            Ledger("fx/hit", 0, 0, 0, false);
            Assert.That(Loader.LiveLeases, Is.Zero);
        }

        [Test]
        public void BurstDoesNotTrimUntilIdleSecondsElapse()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Burst, minIdle: 0, soft: 8, hard: 16, idle: 60f));
            FillIdle("fx/hit", 4);
            Tick();
            Ledger("fx/hit", 4, 0, 4, true);
        }

        [Test]
        public void StickyDoesNotTrimOnTickAndFlushTrimsToMinIdle()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Sticky, minIdle: 2, soft: 8, hard: 16));
            FillIdle("fx/hit", 6);
            Tick();
            Ledger("fx/hit", 6, 0, 6, true);
            Service.Flush("fx/hit");
            TickUntil("fx/hit", pool => pool.TotalCount <= 2, 8);
            Ledger("fx/hit", 2, 0, 2, true);
            Assert.That(RuntimePool("fx/hit").IsPrefabLoaded, Is.True);
        }

        [Test]
        public void FlushAllAndGroupUseLowMemoryPlanNotShutdown()
        {
            Prefab("fx/a");
            Prefab("fx/b");
            Catalog(
                Entry("fx/a", PoolPolicy.Fixed, minIdle: 1, soft: 8, hard: 16, group: "A"),
                Entry("fx/b", PoolPolicy.Fixed, minIdle: 1, soft: 8, hard: 16, group: "B"));
            FillIdle("fx/a", 4);
            FillIdle("fx/b", 4);
            Service.FlushGroup("A");
            TickUntil("fx/a", pool => pool.TotalCount == 1, 8);
            Ledger("fx/a", 1, 0, 1, true);
            Ledger("fx/b", 4, 0, 4, true);
            Service.FlushAll();
            TickUntil("fx/b", pool => pool.TotalCount == 1, 8);
            Ledger("fx/b", 1, 0, 1, true);
        }

        [Test]
        public void OverSoftCapacityTrimsImmediatelyEvenForBurst()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Burst, minIdle: 0, soft: 2, hard: 16, idle: 60f));
            FillIdle("fx/hit", 6);
            Tick();
            Assert.That(RuntimePool("fx/hit").TotalCount, Is.EqualTo(5));
            TickUntil("fx/hit", pool => pool.TotalCount <= 2, 8);
            Ledger("fx/hit", 2, 0, 2, true);
        }

        [Test]
        public void UnloadPrefabFalseKeepsLeaseWhenEmpty()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Fixed, minIdle: 0, soft: 8, hard: 8, unload: false));
            FillIdle("fx/hit", 2);
            Service.Flush("fx/hit");
            TickUntil("fx/hit", pool => pool.TotalCount == 0, 8);
            Ledger("fx/hit", 0, 0, 0, true);
            Assert.That(Loader.LiveLeases, Is.EqualTo(1));
        }

        [Test]
        public void EmptyHeapDisablesTickAndUnknownFlushIsNoOp()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Assert.That(Read<bool>(Service, "_enabled"), Is.False);
            Service.Flush("missing");
            Service.FlushGroup("missing");
            Tick();
            Assert.That(Summary().PoolCount, Is.Zero);
        }

        [Test]
        public void LowMemoryTrimsEveryPoolToMinIdle()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Sticky, minIdle: 1, soft: 8, hard: 16));
            FillIdle("fx/hit", 5);
            RaiseLowMemory();
            TickUntil("fx/hit", pool => pool.TotalCount == 1, 8);
            Ledger("fx/hit", 1, 0, 1, true);
        }

        [Test]
        public void FlushDoesNotDestroyActiveInstances()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Fixed, minIdle: 0, soft: 8, hard: 16));
            Service.LoadPrefab("fx/hit");
            GameObject active = MustSpawn("fx/hit");
            GameObject idleA = MustSpawn("fx/hit");
            GameObject idleB = MustSpawn("fx/hit");
            GameObject idleC = MustSpawn("fx/hit");
            Service.Despawn(idleA);
            Service.Despawn(idleB);
            Service.Despawn(idleC);
            Ledger("fx/hit", 4, 1, 3, true);
            Service.Flush("fx/hit");
            TickUntil("fx/hit", pool => pool.InactiveCount == 0, 8);
            Ledger("fx/hit", 1, 1, 0, true);
            Assert.That(active != null, Is.True);
            Service.Despawn(active);
        }

        [Test]
        public void ShutdownCancelsMaintenanceAndReleasesRuntimePool()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Fixed, minIdle: 0, soft: 8, hard: 16));
            FillIdle("fx/hit", 3);
            Assert.That(Using<RuntimeGameObjectPool>(), Is.EqualTo(1));
            ((IServiceLifecycle)Service).Destroy();
            Service = null;
            Assert.That(Using<RuntimeGameObjectPool>(), Is.Zero);
            Assert.That(Loader.LiveLeases, Is.Zero);
        }
    }
}
