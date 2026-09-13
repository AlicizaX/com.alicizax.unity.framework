using System;
using System.Collections;
using System.Reflection;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceModuleLifecycleTests
    {
        private GameObject root;
        private TextAsset asset;

        [SetUp]
        public void SetUp()
        {
            AppServices.EnsureWorld();
            root = new GameObject("ResourceModuleAudit");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            AppServices.Shutdown();
            if (asset != null) Object.DestroyImmediate(asset);
            if (YooAssets.IsInitialized) YooAssets.Destroy();
        }

        [UnityTest]
        public IEnumerator DestroyComponentUnregistersServiceAndReleasesReferences()
        {
            var component = root.AddComponent<ResourceComponent>();
            var service = (ResourceService)AppServices.Require<IResourceService>();
            var loader = new ControlledLoader();
            asset = new TextAsset("module");
            loader.Assets.Add("module", asset);
            service.Loader = loader;
            var lease = service.LoadLease<TextAsset>("module");
            Object.Destroy(component);
            yield return null;
            Assert.That(AppServices.TryGet<IResourceService>(out _), Is.False);
            Assert.That(lease.IsValid, Is.False);
            Assert.That(loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void RequestedCollectionWaitsForIntervalAndRunsOnce()
        {
            var component = root.AddComponent<ResourceComponent>();
            component.UseSystemUnloadUnusedAssets = false;
            var elapsed = typeof(ResourceComponent).GetField("_lastGCCollectElapseSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
            var pending = typeof(ResourceComponent).GetField("_performGCCollect", BindingFlags.Instance | BindingFlags.NonPublic);
            var update = typeof(ResourceComponent).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            elapsed.SetValue(component, 0f);
            component.ForceUnloadUnusedAssets(true);
            int collections = GC.CollectionCount(GC.MaxGeneration);
            update.Invoke(component, null);
            Assert.That(GC.CollectionCount(GC.MaxGeneration), Is.EqualTo(collections));
            Assert.That(pending.GetValue(component), Is.True);
            elapsed.SetValue(component, float.MaxValue);
            update.Invoke(component, null);
            Assert.That(GC.CollectionCount(GC.MaxGeneration), Is.EqualTo(collections + 1));
            Assert.That(pending.GetValue(component), Is.False);
            update.Invoke(component, null);
            Assert.That(GC.CollectionCount(GC.MaxGeneration), Is.EqualTo(collections + 1));
        }

        [Test]
        public void ServiceReplacementUsesExistingGlobalBackend()
        {
            root.AddComponent<ResourceComponent>();
            AppServices.App.Unregister<IResourceService>();
            var replacement = AppServices.App.Register<IResourceService>(new ResourceService());
            Assert.DoesNotThrow(() => replacement.Initialize());
            Assert.That(replacement.BindingService, Is.Not.Null);
        }

#if UNITY_EDITOR
        [Test]
        public void DisableCallbackAfterBackendQuitCannotLoadSprite()
        {
            root.AddComponent<ResourceComponent>();
            var service = AppServices.Require<IResourceService>();
            var image = root.AddComponent<Image>();
            var probe = root.AddComponent<ResourceDisableLoadProbe>();
            probe.Target = image;
            QuitBackend();
            Assert.That(AppServices.Require<IResourceService>(), Is.SameAs(service));
            root.SetActive(false);
            Assert.That(probe.Calls, Is.EqualTo(1));
            Assert.That(probe.Error, Is.Null, probe.Error?.ToString());
            Assert.That(root.GetComponent<ResourceOwner>(), Is.Null);
            Assert.That(service.BindingService.GetOwnerInfos(null, 0, 0), Is.Zero);
            Assert.That(service.GetAssetInfos(null, 0, 0), Is.Zero);
        }

        [UnityTest]
        public IEnumerator ShortcutOverloadsAfterBackendQuitLeaveNoOwnerOrRecords()
        {
            root.AddComponent<ResourceComponent>();
            var service = AppServices.Require<IResourceService>();
            var image = root.AddComponent<Image>();
            var spriteObject = new GameObject("sprite-target");
            spriteObject.transform.SetParent(root.transform);
            var sprite = spriteObject.AddComponent<SpriteRenderer>();
            var meshObject = new GameObject("mesh-target");
            meshObject.transform.SetParent(root.transform);
            var mesh = meshObject.AddComponent<MeshRenderer>();
            QuitBackend();
            image.SetSprite("unavailable");
            image.SetSprite("unavailable", ResourceBindingOptions.SetNativeSize);
            sprite.SetSprite("unavailable");
            sprite.SetSprite("unavailable", ResourceBindingOptions.None);
            image.SetSubSprite("unavailable", "sprite");
            image.SetSubSprite("unavailable", "sprite", ResourceBindingOptions.SetNativeSize);
            sprite.SetSubSprite("unavailable", "sprite");
            sprite.SetSubSprite("unavailable", "sprite", ResourceBindingOptions.None);
            image.SetMaterial("unavailable");
            image.SetMaterial("unavailable", ResourceBindingOptions.None, true);
            sprite.SetMaterial("unavailable");
            sprite.SetMaterial("unavailable", ResourceBindingOptions.None, true);
            mesh.SetMaterial("unavailable");
            mesh.SetMaterial("unavailable", ResourceBindingOptions.None, false, true);
            mesh.SetSharedMaterial("unavailable");
            mesh.SetSharedMaterial("unavailable", ResourceBindingOptions.None, true);
            yield return null;
            Assert.That(root.GetComponentsInChildren<ResourceOwner>(true), Is.Empty);
            Assert.That(service.BindingService.GetOwnerInfos(null, 0, 0), Is.Zero);
            Assert.That(service.BindingService.GetBindingInfos(null, 0, 0), Is.Zero);
            Assert.That(service.GetAssetInfos(null, 0, 0), Is.Zero);
        }

        private static void QuitBackend()
        {
            var driver = (GameObject)typeof(YooAssets).GetField("s_driver", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            driver.SendMessage("OnApplicationQuit");
            Assert.That(YooAssets.IsInitialized, Is.False);
        }
#endif
    }

    public sealed class ResourceDisableLoadProbe : MonoBehaviour
    {
        internal Image Target;
        internal int Calls;
        internal Exception Error;

        private void OnDisable()
        {
            Calls++;
            try { Target.SetSprite("unavailable"); }
            catch (Exception exception) { Error = exception; }
        }
    }
}
