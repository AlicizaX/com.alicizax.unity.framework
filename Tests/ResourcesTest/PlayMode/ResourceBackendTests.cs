#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceBackendTests
    {
        private ResourceService service;
        private ResourcePackage package;
        private string folder;
        private string packageName;
        private ScriptableObject settings;
        private object previousSettings;
        private FieldInfo settingsField;
        private GameObject root;
        private bool initializedGlobal;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            packageName = "ResourceAudit" + Guid.NewGuid().ToString("N");
            folder = "Assets/" + packageName;
            AssetDatabase.CreateFolder("Assets", packageName);
            File.WriteAllText(folder + "/text.txt", "resource-audit");
            var texture = new Texture2D(4, 4);
            File.WriteAllBytes(folder + "/sprite.png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.Refresh();
            var importer = (TextureImporter)AssetImporter.GetAtPath(folder + "/sprite.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
            AssetDatabase.CreateAsset(new Material(Shader.Find("UI/Default")), folder + "/material.mat");
            var prefab = new GameObject("prefab");
            prefab.AddComponent<ResourceOwner>();
            PrefabUtility.SaveAsPrefabAsset(prefab, folder + "/prefab.prefab");
            Object.DestroyImmediate(prefab);
            AssetDatabase.SaveAssets();

            var editorAssembly = Assembly.Load("YooAsset.Editor");
            var settingsType = editorAssembly.GetType("YooAsset.Editor.BundleCollectorSetting", true);
            var dataType = editorAssembly.GetType("YooAsset.Editor.BundleCollectorSettingData", true);
            settingsField = dataType.GetField("s_setting", BindingFlags.NonPublic | BindingFlags.Static);
            previousSettings = settingsField.GetValue(null);
            settings = ScriptableObject.CreateInstance(settingsType);
            object collectorPackage = Activator.CreateInstance(editorAssembly.GetType("YooAsset.Editor.BundleCollectorPackage", true));
            collectorPackage.GetType().GetField("PackageName").SetValue(collectorPackage, packageName);
            object group = Activator.CreateInstance(editorAssembly.GetType("YooAsset.Editor.BundleCollectorGroup", true));
            group.GetType().GetField("GroupName").SetValue(group, "Audit");
            object collector = Activator.CreateInstance(editorAssembly.GetType("YooAsset.Editor.BundleCollector", true));
            collector.GetType().GetField("CollectPath").SetValue(collector, folder);
            ((IList)group.GetType().GetField("Collectors").GetValue(group)).Add(collector);
            ((IList)collectorPackage.GetType().GetField("Groups").GetValue(collectorPackage)).Add(group);
            ((IList)settingsType.GetField("Packages").GetValue(settings)).Add(collectorPackage);
            settingsField.SetValue(null, settings);

            AppServices.EnsureWorld();
            service = new ResourceService { DefaultPackageName = packageName, PlayMode = EPlayMode.EditorSimulateMode };
            AppServices.App.Register<IResourceService>(service);
            initializedGlobal = !YooAssets.IsInitialized;
            service.Initialize();
            yield return ResourceFixture.Wait(service.InitPackageAsync(), success => Assert.That(success, Is.True));
            package = YooAssets.GetPackage(packageName);
            var version = package.RequestPackageVersionAsync();
            yield return version;
            Assert.That(version.Status, Is.EqualTo(EOperationStatus.Succeeded), version.Error);
            var manifest = service.LoadPackageManifestAsync(version.PackageVersion);
            yield return manifest;
            Assert.That(manifest.Status, Is.EqualTo(EOperationStatus.Succeeded), manifest.Error);
            root = new GameObject("BackendAuditRoot");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            AppServices.Shutdown();
            if (package != null)
            {
                var destroy = package.DestroyPackageAsync();
                yield return destroy;
                Assert.That(destroy.Status, Is.EqualTo(EOperationStatus.Succeeded), destroy.Error);
                YooAssets.RemovePackage(packageName);
            }
            if (initializedGlobal && YooAssets.IsInitialized) YooAssets.Destroy();
            if (settingsField != null) settingsField.SetValue(null, previousSettings);
            if (settings != null) Object.DestroyImmediate(settings);
            if (folder != null) Assert.That(AssetDatabase.DeleteAsset(folder), Is.True);
        }

        [UnityTest]
        public IEnumerator SharedRealLoadCancellationAndCacheReleaseConverge()
        {
            string location = folder + "/text.txt";
            using var cts = new CancellationTokenSource();
            var canceled = service.LoadLeaseAsync<TextAsset>(location, cts.Token);
            var surviving = service.LoadLeaseAsync<TextAsset>(location);
            cts.Cancel();
            yield return ResourceFixture.Wait(canceled, lease => Assert.That(lease.IsValid, Is.False));
            ResourceAssetLease<TextAsset> held = default;
            yield return ResourceFixture.Wait(surviving, lease => held = lease);
            Assert.That(held.Asset.text, Is.EqualTo("resource-audit"));
            var cached = service.LoadLease<TextAsset>(location);
            Assert.That(cached.Asset, Is.SameAs(held.Asset));
            Assert.That(References(), Is.EqualTo(2));
            Assert.That(BackendCount("_providerDict"), Is.EqualTo(1));
            held.Dispose();
            cached.Dispose();
            Assert.That(References(), Is.Zero);
            Assert.That(BackendCount("_providerDict"), Is.EqualTo(1));
            service.UnloadUnusedAssets(true);
            yield return WaitForBackendRelease();
        }

        [UnityTest]
        public IEnumerator SynchronousSubAssetLoadUsesTheSubAssetProvider()
        {
            var key = new ResourceKey(folder + "/sprite.png", assetType: typeof(Sprite), assetKind: ResourceAssetKind.SubAssets);
            var handle = service.AcquireDirect(key);
            Assert.That(handle.IsValid, Is.True);
            Assert.That(service.TryGetSubSpriteAsset(handle, "sprite", out var sprite), Is.True);
            Assert.That(sprite, Is.Not.Null);
            service.Release(handle);
            service.UnloadUnusedAssets(true);
            yield return WaitForBackendRelease();
        }

        [UnityTest]
        public IEnumerator InactivePrefabAndMaterialInstanceReleaseOnDestroy()
        {
            root.SetActive(false);
            var instance = service.LoadGameObject(folder + "/prefab.prefab", root.transform);
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.activeInHierarchy, Is.False);
            var owner = instance.GetComponent<ResourceOwner>();
            var renderer = instance.AddComponent<MeshRenderer>();
            var bindings = service.BindingService;
            Assert.That(bindings.BindMaterialInstance(owner, renderer, new ResourceKey(folder + "/material.mat")), Is.EqualTo(ResourceBindStatus.Success));
            var runtime = renderer.sharedMaterial;
            Assert.That(runtime, Is.Not.SameAs(AssetDatabase.LoadAssetAtPath<Material>(folder + "/material.mat")));
            Assert.That(bindings.BindSharedMaterial(owner, renderer, new ResourceKey(folder + "/material.mat")), Is.EqualTo(ResourceBindStatus.Success));
            yield return null;
            Assert.That(runtime == null, Is.True);
            Assert.That(References(), Is.EqualTo(2));
            Object.Destroy(root);
            yield return null;
            service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            Assert.That(References(), Is.Zero);
            service.UnloadUnusedAssets(true);
            yield return WaitForBackendRelease();
        }

        [UnityTest]
        public IEnumerator ForceResetRejectsLoadsUntilBackendFinishes()
        {
            string location = folder + "/text.txt";
            var pending = service.LoadLeaseAsync<TextAsset>(location);
            service.ForceUnloadAllAssets();
            var duringReset = service.LoadLease<TextAsset>(location);
            Assert.That(duringReset.IsValid, Is.False);
            yield return ResourceFixture.Wait(pending, lease => Assert.That(lease.IsValid, Is.False));
            yield return WaitForBackendRelease();
            service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            var next = service.LoadLease<TextAsset>(location);
            Assert.That(next.IsValid, Is.True);
            next.Dispose();
            service.UnloadUnusedAssets(true);
            yield return WaitForBackendRelease();
        }

        [UnityTest]
        public IEnumerator ResetCallbackExceptionStillStartsPhysicalUnload()
        {
            var owner = root.AddComponent<ResourceOwner>();
            var image = root.AddComponent<Image>();
            Assert.That(service.BindingService.BindImageMaterial(owner, image, new ResourceKey(folder + "/material.mat")), Is.EqualTo(ResourceBindStatus.Success));
            UnityEngine.Events.UnityAction callback = () => throw new InvalidOperationException("reset clear failure");
            image.RegisterDirtyMaterialCallback(callback);
            try
            {
                var error = Assert.Throws<InvalidOperationException>(() => service.ForceUnloadAllAssets());
                Assert.That(error.Message, Is.EqualTo("reset clear failure"));
                Assert.That(References(), Is.Zero);
                Assert.That(owner.IsRegistered, Is.False);
                Assert.That(service.CanLoadResources, Is.False, "Reset must remain blocked until the backend unload completes, including after callback failure.");
                yield return WaitForBackendRelease();
                service.ProcessResourceMaintenance(Time.unscaledTime, 64);
                Assert.That(service.CanLoadResources, Is.True);
            }
            finally { image.UnregisterDirtyMaterialCallback(callback); }
        }

        [UnityTest]
        public IEnumerator DestroyedServiceCannotInitializePackages()
        {
            var cachedPackage = package;
            AppServices.App.Unregister(service);
            bool result = true;
            yield return ResourceFixture.Wait(service.InitPackageAsync(), success => result = success);
            Assert.That(result, Is.False, "The destroyed resource service cannot accept package initialization.");
            Assert.That(service.CanLoadResources, Is.False);
            Assert.That(YooAssets.GetPackage(packageName), Is.SameAs(cachedPackage));
        }

        [UnityTest]
        public IEnumerator ServiceShutdownRequestsPhysicalBackendRelease()
        {
            var held = service.LoadLease<TextAsset>(folder + "/text.txt");
            Assert.That(held.IsValid, Is.True);
            AppServices.App.Unregister(service);
            Assert.That(held.IsValid, Is.False);
            yield return WaitForBackendRelease();
        }

        private int References()
        {
            var infos = new ResourceAssetInfo[service.GetAssetInfos(null, 0, 0)];
            service.GetAssetInfos(infos, 0, infos.Length);
            int result = 0;
            foreach (var info in infos) result += info.RefCountTotal;
            return result;
        }

        [UnityTest]
        public IEnumerator RealPrefabAndRuntimeMaterialStressConverge()
        {
            foreach (int count in new[] { 32, 256, 1024 })
            {
                var instances = new GameObject[count];
                var materials = new Material[count];
                string prefab = folder + "/prefab.prefab";
                var material = new ResourceKey(folder + "/material.mat");
                for (int i = 0; i < count; i++)
                {
                    instances[i] = service.LoadGameObject(prefab, root.transform);
                    var renderer = instances[i].AddComponent<MeshRenderer>();
                    var owner = instances[i].GetComponent<ResourceOwner>();
                    Assert.That(service.BindingService.BindMaterialInstance(owner, renderer, material), Is.EqualTo(ResourceBindStatus.Success));
                    materials[i] = renderer.sharedMaterial;
                }
                Assert.That(References(), Is.EqualTo(count * 2));
                Assert.That(BackendCount("_providerDict"), Is.EqualTo(2));
                for (int i = 0; i < count; i++) Object.Destroy(instances[i]);
                yield return null;
                yield return null;
                service.ProcessResourceMaintenance(Time.unscaledTime, 64);
                Assert.That(References(), Is.Zero);
                for (int i = 0; i < count; i++) Assert.That(materials[i] == null, Is.True);
                service.UnloadUnusedAssets(true);
                yield return WaitForBackendRelease();
                TestContext.WriteLine($"REAL-STATE,{count},providers=0,bundles=0,refs=0,runtime-materials=0,managed={GC.GetTotalMemory(false)},unity={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()}");
            }
        }

        [UnityTest]
        public IEnumerator MetadataCacheUsesNoResourceReferencesAndInvalidatesOnManifestUpdate()
        {
            string location = folder + "/text.txt";
            string packageName = service.DefaultPackageName;
            var info = service.GetAssetInfo(location);
            Assert.That(info.IsValid, Is.True);
            yield return AllocationCapture.Measure("metadata-cache-hit", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) service.GetAssetInfo(location, packageName);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(References(), Is.Zero);
            Assert.That(BackendCount("_providerDict"), Is.Zero);
            var version = package.RequestPackageVersionAsync();
            yield return version;
            yield return service.LoadPackageManifestAsync(version.PackageVersion);
            Assert.That(service.GetAssetInfo(location), Is.Not.SameAs(info));
        }

        private int BackendCount(string field)
        {
            object manager = typeof(ResourcePackage).GetField("_resourceManager", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(package);
            return ((IDictionary)manager.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager)).Count;
        }

        private IEnumerator WaitForBackendRelease()
        {
            for (int frame = 0; frame < 180 && (BackendCount("_providerDict") > 0 || BackendCount("_bundleLoaderDict") > 0); frame++)
                yield return null;
            Assert.That(BackendCount("_providerDict"), Is.Zero);
            Assert.That(BackendCount("_bundleLoaderDict"), Is.Zero);
        }
    }
}
#endif
