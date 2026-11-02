using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceModuleLifecycleTests
    {
        private sealed class ProbeProcedure : ProcedureBase
        {
            internal ResourceScope Scope => Res;
        }

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

        private ResourceService AddComponent(out ControlledLoader loader)
        {
            root.AddComponent<ResourceComponent>();
            var service = (ResourceService)AppServices.Require<IResourceService>();
            loader = new ControlledLoader { DefaultPackageName = service.DefaultPackageName };
            asset = new TextAsset("module");
            loader.Assets.Add("module", asset);
            service.Loader = loader;
            return service;
        }

        [Test]
        public void ComponentRegistersOneServiceUnderBothContracts()
        {
            var service = AddComponent(out _);
            Assert.That(AppServices.Require<IResourcePackageService>(), Is.SameAs(service));
            Assert.That(service.App.IsAlive, Is.True);
        }

        [UnityTest]
        public IEnumerator DestroyingTheComponentUnregistersAndReleasesEverything()
        {
            var service = AddComponent(out var loader);
            var lease = service.LoadLease<TextAsset>("module");
            var scope = service.CreateScope();
            scope.Load<TextAsset>("module");
            Object.Destroy(root.GetComponent<ResourceComponent>());
            yield return null;
            Assert.That(AppServices.TryGet<IResourceService>(out _), Is.False);
            Assert.That(AppServices.TryGet<IResourcePackageService>(out _), Is.False);
            Assert.That(lease.IsValid, Is.False);
            Assert.That(scope.IsAlive, Is.False);
            Assert.That(loader.LiveHandles, Is.Zero);
            Assert.That(service.CanLoadResources, Is.False);
        }

        [Test]
        public void WorldTickCompletesPendingLoads()
        {
            var service = AddComponent(out var loader);
            loader.CompleteImmediately = false;
            var task = service.LoadLeaseAsync<TextAsset>("module");
            loader.Complete("module");
            Assert.That(task.GetAwaiter().IsCompleted, Is.False);
            AppServices.EnsureWorld().Tick(0.016f);
            Assert.That(task.GetAwaiter().IsCompleted, Is.True);
            var lease = task.GetAwaiter().GetResult();
            Assert.That(lease.Asset, Is.SameAs(asset));
            lease.Dispose();
        }

        [Test]
        public void ComponentSettingsForwardToTheService()
        {
            var service = AddComponent(out _);
            var component = root.GetComponent<ResourceComponent>();
            component.ExpireProcessCountPerFrame = 5;
            component.IdleAssetCapacity = 7;
            component.IdleAssetExpireTime = 9f;
            Assert.That(service.IdleReleaseCountPerTick, Is.EqualTo(5));
            Assert.That(service.IdleAssetCapacity, Is.EqualTo(7));
            Assert.That(service.IdleAssetExpireTime, Is.EqualTo(9f));
        }

        [Test]
        public void LowMemoryRequestsForcedUnloadAndCollection()
        {
            var service = AddComponent(out _);
            var component = root.GetComponent<ResourceComponent>();
            var force = typeof(ResourceComponent).GetField("_forceUnloadUnusedAssets", BindingFlags.Instance | BindingFlags.NonPublic);
            var collect = typeof(ResourceComponent).GetField("_performGCCollect", BindingFlags.Instance | BindingFlags.NonPublic);
            LogAssert.Expect(LogType.Warning, new Regex("Low memory"));
            service.OnLowMemory();
            Assert.That(force.GetValue(component), Is.True);
            Assert.That(collect.GetValue(component), Is.True);
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
        public void ProcedureLeaveDisposesItsScope()
        {
            var service = AddComponent(out var loader);
            var procedure = new ProbeProcedure();
            var first = procedure.Scope;
            Assert.That(first.IsAlive, Is.True);
            Assert.That(procedure.Scope.Generation, Is.EqualTo(first.Generation));
            first.Load<TextAsset>("module");
            procedure.Leave();
            Assert.That(first.IsAlive, Is.False);
            Assert.That(service.GetStats().ReferenceCount, Is.Zero);
            var second = procedure.Scope;
            Assert.That(second.IsAlive, Is.True, "Re-entering creates a fresh scope.");
            procedure.Leave();
            Assert.That(loader.Loads, Is.EqualTo(1));
        }

        [Test]
        public void ReplacementServiceInitializesAgainstTheExistingBackend()
        {
            root.AddComponent<ResourceComponent>();
            var original = AppServices.Require<IResourceService>();
            AppServices.App.Unregister(original);
            var replacement = new ResourceService();
            AppServices.App.Register<IResourceService, IResourcePackageService>(replacement);
            Assert.DoesNotThrow(() => replacement.Initialize());
            Assert.That(AppServices.Require<IResourcePackageService>(), Is.SameAs(replacement));
        }
    }
}
