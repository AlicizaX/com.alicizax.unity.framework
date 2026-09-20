using System;
using System.Text.RegularExpressions;
using AlicizaX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class InstanceLedgerHandleTests : GameObjectPoolFixture
    {
        [Test]
        public void SpawnHitUsesInactiveTailAndTrimUsesInactiveHead()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Fixed, minIdle: 0, soft: 8, hard: 16));
            Service.LoadPrefab("fx/hit");
            GameObject first = MustSpawn("fx/hit");
            GameObject second = MustSpawn("fx/hit");
            GameObject third = MustSpawn("fx/hit");
            Service.Despawn(first);
            Service.Despawn(second);
            Service.Despawn(third);
            Assert.That(MustSpawn("fx/hit"), Is.SameAs(third));
            Service.Despawn(third);
            int destroyed = DestroyCount("fx/hit");
            Service.Flush("fx/hit");
            TickUntil("fx/hit", pool => pool.TotalCount == 0, 8);
            Assert.That(DestroyCount("fx/hit"), Is.GreaterThan(destroyed));
            Ledger("fx/hit", 0, 0, 0, false);
        }

        [Test]
        public void DespawnHandleAndGameObjectAreTheSameReleasePath()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            var handle = instance.GetComponent<GameObjectPoolHandle>();
            Service.Despawn(handle);
            Ledger("fx/hit", 1, 0, 1, true);
            Assert.That(DespawnStat("fx/hit"), Is.EqualTo(1));
            instance = MustSpawn("fx/hit");
            Service.Despawn(instance);
            Ledger("fx/hit", 1, 0, 1, true);
            Assert.That(DespawnStat("fx/hit"), Is.EqualTo(2));
        }

        [Test]
        public void StaleHandleReleaseFailsAndUnhandledDespawnDestroys()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            var handle = instance.GetComponent<GameObjectPoolHandle>();
            Service.Despawn(instance);
            Assert.That(handle.TryRelease(), Is.False);
            Ledger("fx/hit", 1, 0, 1, true);
            var foreign = Keep(new GameObject("foreign"));
            LogAssert.Expect(LogType.Warning, new Regex("not a pooled instance"));
            Service.Despawn(foreign);
            Service.Despawn((GameObject)null);
            Service.Despawn((GameObjectPoolHandle)null);
        }

        [Test]
        public void OnSpawnExceptionDoesNotLeaveActiveLedgerOrThrowToCallerWithoutObject()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            var probe = instance.GetComponent<PoolableProbe>();
            Service.Despawn(instance);
            probe.ThrowSpawn = true;
            Assert.Throws<InvalidOperationException>(() => Service.Spawn("fx/hit"));
            Ledger("fx/hit", 1, 0, 1, true);
            Assert.That(instance.activeSelf, Is.False);
            Assert.That(instance.transform.parent, Is.EqualTo(GroupRoot(PoolEntry.DefaultGroup)));
            probe.ThrowSpawn = false;
            Assert.That(MustSpawn("fx/hit"), Is.SameAs(instance));
            Assert.That(instance.activeSelf, Is.True);
        }

        [Test]
        public void OnDespawnExceptionStillParksInactive()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            var probe = instance.GetComponent<PoolableProbe>();
            probe.ThrowDespawn = true;
            Assert.Throws<InvalidOperationException>(() => Service.Despawn(instance));
            Ledger("fx/hit", 1, 0, 1, true);
            Assert.That(instance.activeSelf, Is.False);
            probe.ThrowDespawn = false;
            Assert.That(MustSpawn("fx/hit"), Is.SameAs(instance));
        }

        [Test]
        public void OnPooledDestroyExceptionDoesNotAbortTrimOrLeaveLiveLease()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit", PoolPolicy.Fixed, minIdle: 0, soft: 8, hard: 8));
            Service.LoadPrefab("fx/hit");
            GameObject first = MustSpawn("fx/hit");
            GameObject second = MustSpawn("fx/hit");
            first.GetComponent<PoolableProbe>().ThrowDestroy = true;
            second.GetComponent<PoolableProbe>().ThrowDestroy = true;
            Service.Despawn(first);
            Service.Despawn(second);
            LogAssert.Expect(LogType.Exception, new Regex("destroy failure"));
            LogAssert.Expect(LogType.Exception, new Regex("destroy failure"));
            Service.Flush("fx/hit");
            TickUntil("fx/hit", pool => pool.TotalCount == 0, 8);
            Ledger("fx/hit", 0, 0, 0, false);
            Assert.That(Loader.LiveLeases, Is.Zero);
        }

        [Test]
        public void OnPooledDestroyExceptionDuringShutdownStillReleasesRuntimePool()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            instance.GetComponent<PoolableProbe>().ThrowDestroy = true;
            LogAssert.Expect(LogType.Exception, new Regex("destroy failure"));
            ((IServiceLifecycle)Service).Destroy();
            Service = null;
            Assert.That(Using<RuntimeGameObjectPool>(), Is.Zero);
            Assert.That(Loader.LiveLeases, Is.Zero);
        }

        [Test]
        public void ThousandInstancesKeepPagedIdleLinksAndLedger()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", minIdle: 0, soft: 1024, hard: 1024));
            FillIdle("fx/hit", 1024);
            Assert.That(ExpandCount("fx/hit"), Is.EqualTo(1024));
            for (int i = 0; i < 1024; i++)
            {
                GameObject instance = MustSpawn("fx/hit");
                Service.Despawn(instance);
            }

            Ledger("fx/hit", 1024, 0, 1024, true);
            Assert.That(HitCount("fx/hit"), Is.EqualTo(1024));
        }

        [Test]
        public void GenerationIncrementsPerCreateAndBindMatchesSlot()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", PoolPolicy.Fixed, minIdle: 0, soft: 8, hard: 16));
            Service.LoadPrefab("fx/hit");
            GameObject first = MustSpawn("fx/hit");
            uint generation = first.GetComponent<GameObjectPoolHandle>().Generation;
            Assert.That(generation, Is.EqualTo(1u));
            Service.Despawn(first);
            Service.Flush("fx/hit");
            Assert.That(Service.LoadPrefab("fx/hit"), Is.Not.Null);
            GameObject second = MustSpawn("fx/hit");
            Assert.That(second.GetComponent<GameObjectPoolHandle>().Generation, Is.EqualTo(2u));
            Assert.That(second.GetComponent<GameObjectPoolHandle>().SlotIndex, Is.GreaterThanOrEqualTo(0));
            Service.Despawn(second);
        }

        [Test]
        public void PagedSlotsSurvivePastOnePageWithoutLosingIdleLinks()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", minIdle: 0, soft: 256, hard: 256));
            FillIdle("fx/hit", 130);
            Assert.That(ExpandCount("fx/hit"), Is.EqualTo(130));
            for (int i = 0; i < 130; i++)
            {
                GameObject instance = MustSpawn("fx/hit");
                Assert.That(instance, Is.Not.Null);
                Service.Despawn(instance);
            }
            Ledger("fx/hit", 130, 0, 130, true);
        }

        [Test]
        public void ParentIsAppliedOnSpawnAndClearedOnDespawn()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            var parent = Keep(new GameObject("parent")).transform;
            GameObject instance = MustSpawn("fx/hit", parent);
            Assert.That(instance.transform.parent, Is.EqualTo(parent));
            Service.Despawn(instance);
            Assert.That(instance.transform.parent, Is.EqualTo(GroupRoot(PoolEntry.DefaultGroup)));
            Assert.That(instance.activeSelf, Is.False);
        }

        [Test]
        public void SpawnContextCarriesLocationGroupParentAndFrame()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit", group: "Combat"));
            Service.LoadPrefab("fx/hit");
            var parent = Keep(new GameObject("ctx")).transform;
            GameObject instance = MustSpawn("fx/hit", parent);
            var probe = instance.GetComponent<PoolableProbe>();
            Assert.That(probe.LastContext.Location, Is.EqualTo("fx/hit"));
            Assert.That(probe.LastContext.Group, Is.EqualTo("Combat"));
            Assert.That(probe.LastContext.Parent, Is.EqualTo(parent));
            Assert.That(probe.LastContext.SpawnFrame, Is.EqualTo((uint)Time.frameCount));
            Service.Despawn(instance);
        }

        [Test]
        public void DebugSnapshotsReleaseMemoryPoolLeases()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            MustSpawn("fx/hit");
            var snapshots = new GameObjectPoolSnapshot[4];
            int count = DebugService.GetDebugSnapshots(snapshots);
            Assert.That(count, Is.EqualTo(1));
            DebugService.FillDebugInstances(snapshots[0]);
            Assert.That(snapshots[0].InstanceCount, Is.EqualTo(1));
            DebugService.GetDebugSnapshots(snapshots);
            Assert.That(Using<GameObjectPoolSnapshot>(), Is.EqualTo(1));
        }
    }
}
