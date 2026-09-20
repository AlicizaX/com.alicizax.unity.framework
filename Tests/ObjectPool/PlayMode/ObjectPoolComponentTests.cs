using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class PoolTestRoot : AppServiceRoot { }

    public sealed class ObjectPoolComponentTests : ObjectPoolFixture
    {
        private GameObject root, first, second;
        [UnityTearDown]
        public IEnumerator CleanupScene()
        {
            if (first != null) UnityEngine.Object.Destroy(first);
            if (second != null) UnityEngine.Object.Destroy(second);
            if (root != null) UnityEngine.Object.Destroy(root);
            yield return null;
            AppServices.Shutdown();
        }

        [UnityTest]
        public IEnumerator DuplicateComponentDoesNotAllocateOrOwnAnExistingService()
        {
            Assert.That(AppServices.HasWorld, Is.False);
            root = new GameObject("pool-root");
            root.AddComponent<PoolTestRoot>();
            first = new GameObject("pool-component-owner");
            var owner = first.AddComponent<ObjectPoolComponent>();
            var registered = AppServices.App.Require<IObjectPoolService>();
            registered.GetOrCreatePool<PoolObject>();
            Assert.That(owner.Count, Is.EqualTo(1));
            second = new GameObject("pool-component-duplicate");
            var duplicate = second.AddComponent<ObjectPoolComponent>();
            Assert.That(AppServices.App.Require<IObjectPoolService>(), Is.SameAs(registered));
            Assert.That(Read<IObjectPoolService>(duplicate, "_mObjectPoolService"), Is.Null);
            UnityEngine.Object.Destroy(second);
            yield return null;
            Assert.That(AppServices.App.Require<IObjectPoolService>(), Is.SameAs(registered));
            UnityEngine.Object.Destroy(first);
            yield return null;
            Assert.That(AppServices.App.TryGet<IObjectPoolService>(out _), Is.False);
            Assert.That(registered.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ComponentDestructionDoesNotUnregisterAReplacementService()
        {
            AppServices.EnsureWorld();
            first = new GameObject("pool-component-replaced");
            first.AddComponent<ObjectPoolComponent>();
            var original = AppServices.App.Require<IObjectPoolService>();
            Assert.That(AppServices.App.Unregister(original), Is.True);
            var replacement = AppServices.App.Register<IObjectPoolService>(new ObjectPoolService());
            UnityEngine.Object.Destroy(first);
            yield return null;
            Assert.That(AppServices.App.Require<IObjectPoolService>(), Is.SameAs(replacement));
            replacement.GetOrCreatePool<PoolObject>();
        }

        [UnityTest]
        public IEnumerator SceneUnloadDestroysTheOwningComponentAndAllPoolEntries()
        {
            AppServices.EnsureWorld();
            var scene = SceneManager.CreateScene("object-pool-scene");
            first = new GameObject("pool-scene-owner");
            SceneManager.MoveGameObjectToScene(first, scene);
            first.AddComponent<ObjectPoolComponent>();
            var service = AppServices.App.Require<IObjectPoolService>();
            var pool = service.GetOrCreatePool<PoolObject>();
            var obj = Item(locked: true);
            var probe = obj.Probe;
            pool.Register(obj, true);
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(first == null, Is.True);
            Assert.That(AppServices.App.TryGet<IObjectPoolService>(out _), Is.False);
            Assert.That(probe.Shutdowns, Is.EqualTo(1));
            Assert.That(Using<PoolObject>(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator DisabledRootFreezesMaintenanceAndResumeHonorsTheFrameBudget()
        {
            root = new GameObject("pool-maintenance-root");
            var driver = root.AddComponent<PoolTestRoot>();
            first = new GameObject("pool-maintenance-component");
            first.AddComponent<ObjectPoolComponent>();
            var pool = AppServices.App.Require<IObjectPoolService>().GetOrCreatePool<PoolObject>();
            for (int i = 0; i < 25; i++) pool.Register(Item(), false);
            driver.enabled = false;
            pool.Capacity = 0;
            yield return null;
            yield return null;
            Assert.That(pool.Count, Is.EqualTo(25));
            driver.enabled = true;
            yield return null;
            Assert.That(pool.Count, Is.EqualTo(17));
            yield return null;
            Assert.That(pool.Count, Is.EqualTo(9));
            yield return null;
            Assert.That(pool.Count, Is.EqualTo(1));
            yield return null;
            Assert.That(pool.Count, Is.Zero);
        }

        [Test]
        public void RootExecutionOrderPrecedesObjectPoolRegistration()
        {
            Assert.That(typeof(AppServiceRoot).GetCustomAttribute<DefaultExecutionOrder>().order, Is.EqualTo(-32000));
            Assert.That(typeof(ObjectPoolComponent).GetCustomAttribute<DefaultExecutionOrder>().order, Is.EqualTo(-900));
            Assert.That(typeof(ObjectPoolComponent).GetCustomAttribute<DisallowMultipleComponent>(), Is.Not.Null);
        }
    }
}
