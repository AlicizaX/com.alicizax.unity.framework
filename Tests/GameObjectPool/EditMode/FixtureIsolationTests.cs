using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class FixtureIsolationTests : GameObjectPoolFixture
    {
        [Test]
        public void TeardownDoesNotDestroyAnUnrelatedPoolHandle()
        {
            var external = new GameObject("external-pool-handle");
            external.AddComponent<GameObjectPoolHandle>();
            try
            {
                Prefab("owned");
                Catalog(Entry("owned"));
                Service.LoadPrefab("owned");
                MustSpawn("owned");
                TearDownPool();
                Assert.That(external != null, Is.True);
            }
            finally { if (external != null) Object.DestroyImmediate(external); }
        }

        [Test]
        public void WrongAndDuplicatePrefabReleasesCannotHideBehindAnotherLease()
        {
            var first = Prefab("first");
            var second = Prefab("second");
            Loader.LoadPrefab("first");
            Assert.Throws<AssertionException>(() => Loader.UnloadPrefab(second));
            Assert.That(Loader.LiveLeases, Is.EqualTo(1));
            Loader.LoadPrefab("second");
            Loader.UnloadPrefab(first);
            Assert.Throws<AssertionException>(() => Loader.UnloadPrefab(first));
            Assert.That(Loader.LeaseCount(second), Is.EqualTo(1));
            Loader.UnloadPrefab(second);
            Assert.That(Loader.LiveLeases, Is.Zero);
        }

        [UnityTest]
        public IEnumerator IndependentLoadsCanCompleteInReverseOrder()
        {
            var first = Prefab("first");
            var second = Prefab("second");
            Catalog(Entry("first"), Entry("second"));
            Loader.CompleteImmediately = false;
            var a = Service.LoadPrefabAsync("first");
            var b = Service.LoadPrefabAsync("second");
            var firstRequest = Loader.Pending[0];
            var secondRequest = Loader.Pending[1];
            Assert.That(firstRequest.Location, Is.EqualTo("first"));
            Assert.That(secondRequest.Location, Is.EqualTo("second"));
            Loader.Complete(secondRequest);
            yield return Wait(b, value => Assert.That(value, Is.SameAs(second)));
            Assert.That(PrefabLoading("first"), Is.True);
            Assert.That(Loader.LeaseCount(first), Is.Zero);
            Loader.Complete(firstRequest);
            yield return Wait(a, value => Assert.That(value, Is.SameAs(first)));
            Assert.That(Loader.LeaseCount(first), Is.EqualTo(1));
            Assert.That(Loader.LeaseCount(second), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CatalogReplacementCannotAdoptTheOldSameAddressResult()
        {
            var oldAsset = Prefab("a");
            Catalog(Entry("a"));
            Loader.CompleteImmediately = false;
            var oldLoad = Service.LoadPrefabAsync("a");
            var oldRequest = Loader.Pending[0];
            Catalog(Entry("a"));
            var newAsset = Prefab("a");
            var newLoad = Service.LoadPrefabAsync("a");
            var newRequest = Loader.Pending[1];
            Loader.Complete(newRequest);
            yield return Wait(newLoad, value => Assert.That(value, Is.SameAs(newAsset)));
            Loader.Complete(oldRequest);
            yield return WaitCanceled(oldLoad);
            Assert.That(Loader.LeaseCount(oldAsset), Is.Zero);
            Assert.That(Loader.LeaseCount(newAsset), Is.EqualTo(1));
            Assert.That(Service.LoadPrefab("a"), Is.SameAs(newAsset));
        }
    }
}
