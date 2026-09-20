using System;
using System.Collections;
using System.Threading;
using AlicizaX.ObjectPool;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceWorkflowTests
    {
        private ResourceFixture f;
        [SetUp]
        public void SetUp()
        {
            f = new ResourceFixture();
            AppServices.EnsureWorld();
            AppServices.App.Register<IResourceService>(f.Service);
        }

        [TearDown]
        public void TearDown()
        {
            AppServices.Shutdown();
            f.Dispose();
            AwakeResourceProbe.OnAwake = null;
        }

        [UnityTest]
        public IEnumerator EveryShortcutOverloadReplacesAndClearsItsPhysicalProperty()
        {
            var sprite = f.Sprite("s");
            var material = f.Material("m");
            var image = f.Owner().gameObject.AddComponent<Image>();
            var sr = f.Owner().gameObject.AddComponent<SpriteRenderer>();
            var mr = f.Owner().gameObject.AddComponent<MeshRenderer>();
            image.SetSprite("s", true);
            image.SetSprite("s", ResourceBindingOptions.SetNativeSize);
            Assert.That(image.sprite, Is.SameAs(sprite));
            sr.SetSprite("s");
            sr.SetSprite("s", ResourceBindingOptions.None);
            image.SetSubSprite("s", "s", true);
            image.SetSubSprite("s", "s", ResourceBindingOptions.SetNativeSize);
            sr.SetSubSprite("s", "s");
            sr.SetSubSprite("s", "s", ResourceBindingOptions.None);
            image.SetMaterial("m");
            image.SetMaterial("m", ResourceBindingOptions.None, true);
            sr.SetMaterial("m");
            sr.SetMaterial("m", ResourceBindingOptions.None, true);
            mr.SetMaterial("m");
            var oldInstance = mr.sharedMaterial;
            mr.SetMaterial("m", ResourceBindingOptions.None, true, true);
            var nextInstance = mr.sharedMaterial;
            Assert.That(nextInstance, Is.Not.SameAs(oldInstance));
            mr.SetSharedMaterial("m");
            mr.SetSharedMaterial("m", ResourceBindingOptions.None, true);
            mr.SetMaterial("m", false, true);
            mr.SetMaterial("m", ResourceBindingOptions.None, false, false);
            yield return null;
            Assert.That(oldInstance == null, Is.True);
            Assert.That(nextInstance == null, Is.True);
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(sr.sprite, Is.SameAs(sprite));
            Assert.That(image.material, Is.SameAs(material));
            Assert.That(sr.sharedMaterial, Is.SameAs(material));
            Assert.That(mr.sharedMaterial, Is.SameAs(material));
            Assert.That(f.Info("m").BindingRefCount, Is.EqualTo(3));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            image.SetSprite("", cancellationToken: canceled.Token);
            Assert.That(image.sprite, Is.SameAs(sprite));
            image.SetSprite("");
            sr.SetSprite("");
            image.SetMaterial("");
            sr.SetMaterial("");
            mr.SetSharedMaterial("");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(image.sprite, Is.Null);
            Assert.That(sr.sprite, Is.Null);
            Assert.That(image.material, Is.Not.SameAs(material));
            Assert.That(mr.sharedMaterial, Is.Null);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PooledPrefabKeepsSourceAndReleasesAssignedResources()
        {
            var source = f.Keep(new GameObject("prefab-source"));
            source.AddComponent<Image>();
            f.Loader.Assets.Add("prefab", source);
            f.Material("material");
            f.Service.IdleAssetCapacity = 0;
            var instance = f.Keep(f.Service.LoadGameObject("prefab"));
            var owner = instance.GetComponent<ResourceOwner>();
            var pools = AppServices.App.Register<IObjectPoolService>(new ObjectPoolService());
            var pool = pools.GetOrCreatePool<PooledResourceView>();
            var entry = MemoryPool.Acquire<PooledResourceView>();
            entry.SetTarget(instance);
            Assert.That(pool.Register(entry, false), Is.True);
            for (int i = 0; i < 64; i++)
            {
                Assert.That(pool.Spawn(), Is.SameAs(entry));
                instance.GetComponent<Image>().SetMaterial("material");
                pool.Unspawn(entry);
                f.AssertNoReferences("material");
                Assert.That(f.Info("prefab").DirectRefCount, Is.EqualTo(1));
                Assert.That(owner.IsRegistered, Is.True);
            }
            pool.ReleaseAllUnused();
            yield return null;
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            Assert.That(instance == null, Is.True);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PrefabAwakeResetRollsBackInstanceAndSourceLease()
        {
            var source = f.Keep(new GameObject("reentrant-prefab"));
            source.AddComponent<AwakeResourceProbe>();
            f.Loader.Assets.Add("prefab", source);
            AwakeResourceProbe.OnAwake = f.Service.ForceUnloadAllAssets;
            var instance = f.Service.LoadGameObject("prefab");
            Assert.That(instance, Is.Null);
            yield return null;
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(Object.FindObjectsOfType<AwakeResourceProbe>().Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TargetDestroyedWhileOwnerSurvivesCancelsPendingBinding()
        {
            f.Material("m");
            f.Loader.CompleteImmediately = false;
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            var pending = f.Bindings.BindImageMaterialAsync(owner, image, new ResourceKey("m"));
            Object.Destroy(image);
            yield return null;
            f.Loader.Complete("m");
            yield return ResourceFixture.Wait(pending, status => Assert.That(status, Is.EqualTo(ResourceBindStatus.StaleOwner)));
            Assert.That(owner != null, Is.True);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ShortcutAvailabilityGateAllocations()
        {
            f.Sprite("sprite");
            string location = "sprite";
            var target = f.Keep(new GameObject("availability-target")).AddComponent<Image>();
            f.Loader.IsAvailable = false;
            bool canLoad = true;
            yield return AllocationCapture.Measure("availability-state-first-use", 1,
                () => canLoad = f.Service.CanLoadResources, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(canLoad, Is.False);
            yield return AllocationCapture.Measure("shortcut-unavailable-first-use", 1,
                () => target.SetSprite(location), _ => { }, true);
            yield return AllocationCapture.Measure("shortcut-unavailable", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) target.SetSprite(location);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(target.GetComponent<ResourceOwner>(), Is.Null);
            Assert.That(f.Loader.Loads, Is.Zero);
            f.Loader.IsAvailable = true;
            target.SetSprite(location);
            yield return AllocationCapture.Measure("shortcut-cached", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) target.SetSprite(location);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(f.Info(location).BindingRefCount, Is.EqualTo(1));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
        }
#endif

        public sealed class PooledResourceView : ObjectBase<GameObject>
        {
            public void SetTarget(GameObject value) => Initialize(value);
            protected internal override void OnSpawn() => Target.SetActive(true);
            protected internal override void OnUnspawn()
            {
                ResourceOwner.ReleaseBindingsInHierarchy(Target);
                Target.SetActive(false);
            }
            protected internal override void Release(bool isShutdown) => Object.Destroy(Target);
        }
    }

    public sealed class AwakeResourceProbe : MonoBehaviour
    {
        internal static Action OnAwake;
        private void Awake() => OnAwake?.Invoke();
    }
}
