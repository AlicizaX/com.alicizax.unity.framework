using System.Collections.Generic;
using System.Text.RegularExpressions;
using AlicizaX;
using AlicizaX.Resource.Runtime;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class GameObjectPoolResourcePathTests
    {
        private ResourceFixture _resources;
        private GameObjectPoolService _service;
        private Transform _container;

        [SetUp]
        public void SetUp()
        {
            MemoryPoolRegistry.InitializeMainThread();
            MemoryPool<RuntimeGameObjectPool>.ClearAll();
            MemoryPool<GameObjectPoolSnapshot>.ClearAll();
            MemoryPool<GameObjectPoolInstanceSnapshot>.ClearAll();
            _resources = new ResourceFixture();
            AppServices.EnsureWorld();
            AppServices.App.Register<IResourceService>(_resources.Service);
            _container = new GameObject("gop-resource-container").transform;
            _service = new GameObjectPoolService(_container);
            ((IServiceLifecycle)_service).Initialize(null, null);
            var prefab = _resources.Keep(new GameObject("fx_hit"));
            _resources.Loader.Assets["fx/hit"] = prefab;
            var config = ScriptableObject.CreateInstance<PoolConfigScriptableObject>();
            _resources.Keep(config);
            config.entries = new List<PoolEntry>
            {
                GameObjectPoolFixture.Entry("fx/hit", minIdle: 0, soft: 4, hard: 4)
            };
            _resources.Loader.Assets["pool-config"] = config;
            _resources.Text("not-a-config");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_service != null)
                    ((IServiceLifecycle)_service).Destroy();
                Assert.That(GameObjectPoolFixture.Using<RuntimeGameObjectPool>(), Is.Zero);
            }
            finally
            {
                _service = null;
                if (_container != null)
                    UnityEngine.Object.DestroyImmediate(_container.gameObject);
                AppServices.Shutdown();
                _resources.Dispose();
                MemoryPool<RuntimeGameObjectPool>.ClearAll();
                MemoryPool<GameObjectPoolSnapshot>.ClearAll();
                MemoryPool<GameObjectPoolInstanceSnapshot>.ClearAll();
            }
        }

        [Test]
        public void LoadCatalogFromResourcePathThenFlushUnloadsYooLease()
        {
            _service.LoadCatalog("pool-config");
            Assert.That(_service.LoadPrefab("fx/hit"), Is.Not.Null);
            Assert.That(_resources.Info("fx/hit").DirectRefCount, Is.EqualTo(1));
            GameObject instance = _service.Spawn("fx/hit");
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.GetComponent<GameObjectPoolHandle>(), Is.Not.Null);
            _service.Despawn(instance);
            _service.Flush("fx/hit");
            for (int i = 0; i < 8; i++)
                ((IServiceTickable)_service).Tick(0f);
            GameObjectPoolSummarySnapshot summary = ((IGameObjectPoolDebugService)_service).GetDebugSummary();
            Assert.That(summary.TotalInstanceCount, Is.Zero);
            Assert.That(summary.LoadedPrefabCount, Is.Zero);
            _resources.AssertNoReferences("fx/hit");
            _resources.Service.UnloadUnusedAssets(true);
            Assert.That(_resources.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void LoadCatalogWrongAssetTypeClearsPreviousPools()
        {
            _service.LoadCatalog("pool-config");
            Assert.That(_service.LoadPrefab("fx/hit"), Is.Not.Null);
            _service.LoadCatalog("not-a-config");
            LogAssert.Expect(LogType.Error, new Regex("not in PoolConfig"));
            Assert.That(_service.Spawn("fx/hit"), Is.Null);
            Assert.That(((IGameObjectPoolDebugService)_service).GetDebugSummary().PoolCount, Is.Zero);
            _resources.Service.UnloadUnusedAssets(true);
            Assert.That(_resources.Loader.LiveHandles, Is.Zero);
        }
    }
}
