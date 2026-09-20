using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX;
using AlicizaX.Resource.Runtime;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class YooAssetPrefabLoaderLeaseTests
    {
        private ResourceFixture _resources;
        private YooAssetPrefabLoader _loader;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            MemoryPoolRegistry.InitializeMainThread();
            _resources = new ResourceFixture();
            AppServices.EnsureWorld();
            AppServices.App.Register<IResourceService>(_resources.Service);
            _prefab = _resources.Keep(new GameObject("yoo-prefab"));
            _resources.Loader.Assets["fx/hit"] = _prefab;
            _loader = new YooAssetPrefabLoader();
        }

        [TearDown]
        public void TearDown()
        {
            AppServices.Shutdown();
            _resources.Dispose();
        }

        [Test]
        public void MultipleLoadsStackLeasesByUnityInstanceIdAndUnloadPopsLast()
        {
            GameObject first = _loader.LoadPrefab("fx/hit");
            GameObject second = _loader.LoadPrefab("fx/hit");
            Assert.That(first, Is.SameAs(_prefab));
            Assert.That(second, Is.SameAs(_prefab));
            Assert.That(_resources.Info("fx/hit").DirectRefCount, Is.EqualTo(2));
            Assert.That(LeaseCount(_prefab), Is.EqualTo(2));
            _loader.UnloadPrefab(_prefab);
            Assert.That(_resources.Info("fx/hit").DirectRefCount, Is.EqualTo(1));
            Assert.That(LeaseCount(_prefab), Is.EqualTo(1));
            _loader.UnloadPrefab(_prefab);
            _resources.AssertNoReferences("fx/hit");
            Assert.That(LeaseCount(_prefab), Is.Zero);
            _resources.Service.UnloadUnusedAssets(true);
            Assert.That(_resources.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void UnloadNullAndUnregisteredAreNoOp()
        {
            _loader.UnloadPrefab(null);
            var foreign = _resources.Keep(new GameObject("foreign"));
            _loader.UnloadPrefab(foreign);
            Assert.That(_resources.Loader.Loads, Is.Zero);
            Assert.That(_resources.Loader.LiveHandles, Is.Zero);
            Assert.That(LeaseMapCount(), Is.Zero);
        }

        [Test]
        public void InvalidLeaseIsNotRetained()
        {
            var texture = _resources.Keep(new Texture2D(1, 1));
            _resources.Loader.Assets["fx/wrong"] = texture;
            Assert.That(_loader.LoadPrefab("fx/wrong"), Is.Null);
            Assert.That(LeaseMapCount(), Is.Zero);
            _resources.AssertNoReferences("fx/wrong");
            _resources.Service.UnloadUnusedAssets(true);
            Assert.That(_resources.Loader.LiveHandles, Is.Zero);
        }

        private int LeaseCount(GameObject prefab)
        {
            var leases = (Dictionary<ulong, List<ResourceAssetLease<GameObject>>>)typeof(YooAssetPrefabLoader)
                .GetField("_leases", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_loader);
            ulong id = UnityObjectId.Get(prefab);
            return leases.TryGetValue(id, out List<ResourceAssetLease<GameObject>> stack) ? stack.Count : 0;
        }

        private int LeaseMapCount()
        {
            var leases = (IDictionary)typeof(YooAssetPrefabLoader)
                .GetField("_leases", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_loader);
            return leases.Count;
        }
    }
}
