using System.Collections;
using AlicizaX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class PoolableCallbackTests : GameObjectPoolFixture
    {
        [Test]
        public void CallbacksFireOncePerTransitionInOrder()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit", PoolPolicy.Fixed, minIdle: 0, soft: 8, hard: 8));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            var probe = instance.GetComponent<PoolableProbe>();
            Service.Despawn(instance);
            Assert.That(MustSpawn("fx/hit"), Is.SameAs(instance));
            Service.Despawn(instance);
            Assert.That(probe.Calls, Is.EqualTo(new[] { "spawn", "despawn", "spawn", "despawn" }));
            Service.Flush("fx/hit");
            TickUntil("fx/hit", pool => pool.TotalCount == 0, 8);
            Assert.That(probe.Calls, Is.EqualTo(new[] { "spawn", "despawn", "spawn", "despawn", "destroy" }));
            Assert.That(new[] { probe.Spawns, probe.Despawns, probe.Destroys }, Is.EqualTo(new[] { 2, 2, 1 }));
        }

        [Test]
        public void ChildPoolablesIncludingInactiveOnesGetCallbacksWithoutTakingOverVisibility()
        {
            Prefab("fx/hit", childPoolables: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            var parent = Keep(new GameObject("parent")).transform;
            GameObject instance = MustSpawn("fx/hit", parent);
            PoolableProbe[] probes = instance.GetComponentsInChildren<PoolableProbe>(true);
            Assert.That(probes.Length, Is.EqualTo(2));
            Assert.That(instance.TryGetComponent(out PoolableProbe _), Is.False);
            Service.Despawn(instance);
            Ledger("fx/hit", 1, 0, 1, true);
            Assert.That(instance.activeSelf, Is.False);
            Assert.That(instance.transform.parent, Is.EqualTo(GroupRoot(PoolEntry.DefaultGroup)));
            foreach (PoolableProbe probe in probes)
            {
                Assert.That(probe.Calls, Is.EqualTo(new[] { "spawn", "despawn" }), probe.name);
                Assert.That(probe.OwnerActiveAtSpawn, Is.True, probe.name);
                Assert.That(probe.OwnerParentAtSpawn, Is.EqualTo(parent), probe.name);
                Assert.That(probe.OwnerActiveAtDespawn, Is.True, probe.name);
                Assert.That(probe.OwnerParentAtDespawn, Is.EqualTo(parent), probe.name);
            }

            Assert.That(MustSpawn("fx/hit", parent), Is.SameAs(instance));
            Assert.That(instance.activeSelf, Is.True);
            Service.Despawn(instance);
        }

        [Test]
        public void RootPoolableTakesOverVisibilityAndKeepsInstanceActive()
        {
            Prefab("fx/hit", poolable: true, childPoolables: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            var parent = Keep(new GameObject("parent")).transform;
            GameObject instance = MustSpawn("fx/hit", parent);
            var probe = instance.GetComponent<PoolableProbe>();
            Assert.That(probe.OwnerActiveAtSpawn, Is.True);
            Assert.That(probe.OwnerParentAtSpawn, Is.EqualTo(parent));
            Service.Despawn(instance);
            Ledger("fx/hit", 1, 0, 1, true);
            Assert.That(probe.OwnerActiveAtDespawn, Is.True);
            Assert.That(probe.OwnerParentAtDespawn, Is.EqualTo(parent));
            Assert.That(instance.activeSelf, Is.True);
            Assert.That(instance.transform.parent, Is.EqualTo(GroupRoot(PoolEntry.DefaultGroup)));
            Assert.That(MustSpawn("fx/hit", parent), Is.SameAs(instance));
            Assert.That(instance.activeSelf, Is.True);
            Assert.That(instance.transform.parent, Is.EqualTo(parent));
            foreach (PoolableProbe each in instance.GetComponentsInChildren<PoolableProbe>(true))
                Assert.That(each.Calls, Is.EqualTo(new[] { "spawn", "despawn", "spawn" }), each.name);
            Service.Despawn(instance);
        }

        [Test]
        public void TakenOverInstanceDeactivatedByItsOwnerIsReactivatedOnSpawn()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            GameObject instance = MustSpawn("fx/hit");
            Service.Despawn(instance);
            instance.SetActive(false);
            Assert.That(MustSpawn("fx/hit"), Is.SameAs(instance));
            Assert.That(instance.activeSelf, Is.True);
            Assert.That(instance.GetComponent<PoolableProbe>().OwnerActiveAtSpawn, Is.True);
            Service.Despawn(instance);
            Assert.That(instance.activeSelf, Is.True);
        }

        [Test]
        public void DestroyedParentDoesNotTakeTakenOverIdleInstance()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            var parent = Keep(new GameObject("parent")).transform;
            GameObject instance = MustSpawn("fx/hit", parent);
            Service.Despawn(instance);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
            Assert.That(instance != null, Is.True);
            Ledger("fx/hit", 1, 0, 1, true);
            Assert.That(MustSpawn("fx/hit"), Is.SameAs(instance));
            Service.Despawn(instance);
        }

        [UnityTest]
        public IEnumerator WarmedTakenOverInstancesStayInactiveUntilFirstSpawn()
        {
            Prefab("fx/hit", poolable: true);
            Catalog(Entry("fx/hit"));
            yield return Wait(Service.WarmupAsync("fx/hit", 2));
            Ledger("fx/hit", 2, 0, 2, true);
            Transform root = GroupRoot(PoolEntry.DefaultGroup);
            Assert.That(root.childCount, Is.EqualTo(2));
            for (int i = 0; i < root.childCount; i++)
                Assert.That(root.GetChild(i).gameObject.activeSelf, Is.False);
            GameObject instance = MustSpawn("fx/hit");
            Assert.That(instance.activeSelf, Is.True);
            Service.Despawn(instance);
            Assert.That(instance.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator PoolableHitPathsDoNotAllocate()
        {
            Prefab("fx/owned").AddComponent<CountingPoolable>();
            GameObject managed = Prefab("fx/managed");
            var child = new GameObject("child");
            child.transform.SetParent(managed.transform, false);
            child.AddComponent<CountingPoolable>();
            Catalog(Entry("fx/owned"), Entry("fx/managed"));
            foreach (string location in new[] { "fx/owned", "fx/managed" })
            {
                Service.LoadPrefab(location);
                GameObject instance = MustSpawn(location);
                Service.Despawn(instance);
                var counter = instance.GetComponentInChildren<CountingPoolable>(true);
                yield return AllocationCapture.Measure("poolable-hit-" + location, 10000, () =>
                {
                    for (int i = 0; i < 10000; i++)
                        Service.Despawn(Service.Spawn(location));
                }, sample => Assert.That(sample.Bytes, Is.Zero, location));
                Assert.That(counter.Spawns, Is.EqualTo(10001), location);
                Assert.That(counter.Despawns, Is.EqualTo(10001), location);
                Ledger(location, 1, 0, 1, true);
            }
        }
    }
}
