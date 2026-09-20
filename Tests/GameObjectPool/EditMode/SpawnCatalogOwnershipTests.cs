using System.Collections;
using System.Text.RegularExpressions;
using AlicizaX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class SpawnCatalogOwnershipTests : GameObjectPoolFixture
    {
        [Test]
        public void UnregisteredLocationReturnsNullAndDoesNotCreatePool()
        {
            Prefab("fx/hit");
            LogAssert.Expect(LogType.Error, new Regex("not in PoolConfig"));
            Assert.That(Service.Spawn("fx/hit"), Is.Null);
            Assert.That(RuntimePool("fx/hit"), Is.Null);
            Assert.That(Summary().PoolCount, Is.Zero);
            Assert.That(Service.LoadPrefab("fx/hit"), Is.Null);
            Assert.That(Loader.LoadCalls, Is.Zero);
        }

        [Test]
        public void EmptyAndWhitespaceLocationReturnsNullWithoutWarning()
        {
            Catalog(Entry("fx/hit"));
            Assert.That(Service.Spawn(null), Is.Null);
            Assert.That(Service.Spawn(""), Is.Null);
            Assert.That(Service.Spawn("   "), Is.Null);
            Assert.That(Summary().PoolCount, Is.Zero);
        }

        [Test]
        public void SyncSpawnDoesNotLoadPrefabAndLoadThenSpawnCreatesInstance()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", hard: 4));
            Assert.That(Service.Spawn("fx/hit"), Is.Null);
            Assert.That(Loader.LoadCalls, Is.Zero);
            Assert.That(RuntimePool("fx/hit"), Is.Not.Null);
            Ledger("fx/hit", 0, 0, 0, false);
            Assert.That(Service.LoadPrefab("fx/hit"), Is.SameAs(Loader.Prefabs["fx/hit"]));
            Assert.That(Loader.LoadCalls, Is.EqualTo(1));
            GameObject first = MustSpawn("fx/hit");
            Assert.That(first.GetComponent<GameObjectPoolHandle>(), Is.Not.Null);
            Assert.That(first.activeSelf, Is.True);
            Ledger("fx/hit", 1, 1, 0, true);
            Assert.That(MissCount("fx/hit"), Is.EqualTo(1));
            Service.Despawn(first);
            Ledger("fx/hit", 1, 0, 1, true);
            GameObject hit = MustSpawn("fx/hit");
            Assert.That(hit, Is.SameAs(first));
            Assert.That(HitCount("fx/hit"), Is.EqualTo(1));
            Assert.That(Loader.LoadCalls, Is.EqualTo(1));
        }

        [Test]
        public void SpawnNormalizesLocationAndDoesNotCreateImplicitPool()
        {
            Prefab("UI/Window");
            Catalog(Entry("UI/Window"));
            Service.LoadPrefab(" Assets/Bundles/UI/Window.prefab ");
            GameObject instance = MustSpawn(@"UI\Window.prefab");
            Assert.That(RuntimePool("UI/Window"), Is.Not.Null);
            Assert.That(RuntimePool("UI/Window").Location, Is.EqualTo("UI/Window"));
            Service.Despawn(instance);
        }

        [Test]
        public void SpawnGenericUsesRootGetComponentOnly()
        {
            Prefab("fx/hit", rootMarker: true, childMarker: true);
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            RootMarker root = Service.Spawn<RootMarker>("fx/hit");
            Assert.That(root, Is.Not.Null);
            Assert.That(Service.Spawn<ChildOnlyMarker>("fx/hit"), Is.Null);
            Ledger("fx/hit", 2, 1, 1, true);
            GameObject missing = MustSpawn("fx/hit");
            Assert.That(missing.GetComponentInChildren<ChildOnlyMarker>(true), Is.Not.Null);
            Assert.That(missing, Is.Not.SameAs(root.gameObject));
            Service.Despawn(root.gameObject);
            Service.Despawn(missing);
        }

        [Test]
        public void TrySpawnWritesFalseWhenNull()
        {
            Catalog(Entry("fx/hit"));
            Assert.That(Service.TrySpawn("fx/hit", null, out GameObject instance), Is.False);
            Assert.That(instance, Is.Null);
            Prefab("fx/hit");
            Service.LoadPrefab("fx/hit");
            Assert.That(Service.TrySpawn("fx/hit", null, out instance), Is.True);
            Assert.That(instance, Is.Not.Null);
            Service.Despawn(instance);
        }

        [Test]
        public void HardCapacityRejectsMissButIdleHitStillWorks()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit", minIdle: 0, soft: 1, hard: 1));
            Service.LoadPrefab("fx/hit");
            GameObject first = MustSpawn("fx/hit");
            LogAssert.Expect(LogType.Warning, new Regex("HardCapacity reached"));
            Assert.That(Service.Spawn("fx/hit"), Is.Null);
            Ledger("fx/hit", 1, 1, 0, true);
            Service.Despawn(first);
            Ledger("fx/hit", 1, 0, 1, true);
            Assert.That(MustSpawn("fx/hit"), Is.SameAs(first));
        }

        [Test]
        public void FailedLoaderLeavesPrefabUnloadedAndSpawnNull()
        {
            Catalog(Entry("fx/hit"));
            Assert.That(Service.LoadPrefab("fx/hit"), Is.Null);
            Assert.That(Loader.LoadCalls, Is.EqualTo(1));
            Ledger("fx/hit", 0, 0, 0, false);
            Assert.That(Service.Spawn("fx/hit"), Is.Null);
            Loader.ThrowOnLoad = true;
            Assert.Throws<System.InvalidOperationException>(() => Service.LoadPrefab("fx/hit"));
            Ledger("fx/hit", 0, 0, 0, false);
        }

        [UnityTest]
        public IEnumerator LoadCatalogClearsPoolsAndGroupRoots()
        {
            Prefab("fx/a");
            Prefab("fx/b");
            Catalog(Entry("fx/a", group: "A"), Entry("fx/b", group: "B"));
            Service.LoadPrefab("fx/a");
            MustSpawn("fx/a");
            Assert.That(GroupRoot("A"), Is.Not.Null);
            Catalog(Entry("fx/b", group: "B"));
            Assert.That(RuntimePool("fx/a"), Is.Null);
            Assert.That(Summary().PoolCount, Is.Zero);
            Assert.That(Using<RuntimeGameObjectPool>(), Is.Zero);
            yield return null;
            Assert.That(GroupRoot("A"), Is.Null);
        }

        [Test]
        public void TwoLocationsKeepIndependentPoolsAndIdleHits()
        {
            Prefab("fx/a");
            Prefab("fx/b");
            Catalog(Entry("fx/a", hard: 8), Entry("fx/b", hard: 8));
            Assert.That(Service.LoadPrefab("fx/a"), Is.Not.Null);
            Assert.That(Service.LoadPrefab("fx/b"), Is.Not.Null);
            GameObject a = MustSpawn("fx/a");
            GameObject b = MustSpawn("fx/b");
            Ledger("fx/a", 1, 1, 0, true);
            Ledger("fx/b", 1, 1, 0, true);
            Service.Despawn(a);
            Service.Despawn(b);
            Assert.That(MustSpawn("fx/a"), Is.SameAs(a));
            Assert.That(MustSpawn("fx/b"), Is.SameAs(b));
            Assert.That(RuntimePool("fx/a"), Is.Not.SameAs(RuntimePool("fx/b")));
            Service.Despawn(a);
            Service.Despawn(b);
        }

        [Test]
        public void NullCatalogIsEmptyAndDoesNotKeepPreviousRules()
        {
            Prefab("fx/hit");
            Catalog(Entry("fx/hit"));
            Service.LoadPrefab("fx/hit");
            MustSpawn("fx/hit");
            Service.LoadCatalog((PoolConfigScriptableObject)null);
            LogAssert.Expect(LogType.Error, new Regex("not in PoolConfig"));
            Assert.That(Service.Spawn("fx/hit"), Is.Null);
            Assert.That(Summary().PoolCount, Is.Zero);
        }
    }
}
