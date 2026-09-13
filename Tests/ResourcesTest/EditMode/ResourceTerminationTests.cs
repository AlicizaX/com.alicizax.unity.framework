using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceTerminationTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [TestCase(1)]
        [TestCase(82319)]
        [TestCase(938011)]
        public void IndexMixedOperationsMatchDictionary(int seed)
        {
            var map = new ResourceUlongIntMap();
            var expected = new Dictionary<ulong, int>();
            var random = new System.Random(seed);
            for (int i = 0; i < 50000; i++)
            {
                ulong key = ((ulong)random.Next(512) << 32) | 0x0101;
                switch (random.Next(3))
                {
                    case 0: map.Set(key, i); expected[key] = i; break;
                    case 1: Assert.That(map.Remove(key), Is.EqualTo(expected.Remove(key))); break;
                    default:
                        Assert.That(map.TryGetValue(key, out int value), Is.EqualTo(expected.TryGetValue(key, out int other)));
                        Assert.That(value, Is.EqualTo(other));
                        break;
                }
                Assert.That(map.Count, Is.EqualTo(expected.Count));
            }
            foreach (var pair in expected)
            {
                Assert.That(map.TryGetValue(pair.Key, out int value), Is.True);
                Assert.That(value, Is.EqualTo(pair.Value));
            }
        }

        [UnityTest]
        public IEnumerator SyncJoinCompletesExistingAsyncWithoutDuplicatingStorage()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var task = f.Service.LoadLeaseAsync<TextAsset>("a");
            var sync = f.Service.LoadLease<TextAsset>("a");
            Assert.That(sync.IsValid, Is.True);
            yield return ResourceFixture.Wait(task, lease =>
            {
                Assert.That(lease.Asset, Is.SameAs(sync.Asset));
                Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(2));
                lease.Dispose();
            });
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            sync.Dispose();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProgressExceptionTerminatesRequestAndReleasesHandle(bool complete)
        {
            f.Text("a");
            f.Loader.CompleteImmediately = complete;
            bool delivered = false;
            var callbacks = new LoadAssetCallbacks((_, _, _, _) => delivered = true,
                (_, _, _) => throw new InvalidOperationException("progress"));
            var task = f.Service.LoadAssetAsync("a", typeof(TextAsset), 0, callbacks, null);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.That(delivered, Is.False);
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Info("a").RefCountTotal, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CompletionCallbackResetCannotDeliverInvalidLease()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            bool delivered = false;
            var callbacks = new LoadAssetCallbacks((_, _, _, _) => delivered = true,
                (_, progress, _) => { if (progress == 1) f.Service.ForceUnloadAllAssets(); });
            var task = f.Service.LoadAssetAsync("a", typeof(TextAsset), 0, callbacks, null);
            f.Loader.Complete("a");
            yield return task.ToCoroutine();
            Assert.That(delivered, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator OwnerDestroyedBeforeCompletionRejectsLateResult()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var owner = f.Owner(false);
            var task = f.Service.LoadAssetAsync<TextAsset>(owner, "a");
            UnityEngine.Object.DestroyImmediate(owner.gameObject);
            f.Loader.Complete("a");
            yield return ResourceFixture.Wait(task, asset => Assert.That(asset, Is.Null));
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 64);
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CancellationAfterBackendCompletionBeforeDeliveryWins()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            using var cts = new CancellationTokenSource();
            var task = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            f.Loader.Complete("a");
            cts.Cancel();
            yield return ResourceFixture.Wait(task, lease => Assert.That(lease.IsValid, Is.False));
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ShutdownTerminatesEveryWaitingKind()
        {
            f.Text("a");
            f.Material("m");
            f.Loader.CompleteImmediately = false;
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<UnityEngine.UI.Image>();
            var direct = f.Service.LoadLeaseAsync<TextAsset>("a");
            var owned = f.Service.LoadAssetAsync<TextAsset>(owner, "a");
            var binding = f.Bindings.BindImageMaterialAsync(owner, image, new ResourceKey("m"));
            typeof(ResourceService).GetMethod("OnDestroyService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(f.Service, null);
            f.Loader.Complete("a");
            f.Loader.Complete("m");
            yield return ResourceFixture.Wait(direct, lease => Assert.That(lease.IsValid, Is.False));
            yield return ResourceFixture.Wait(owned, asset => Assert.That(asset, Is.Null));
            yield return ResourceFixture.Wait(binding, status => Assert.That(status, Is.EqualTo(ResourceBindStatus.StaleOwner)));
            Assert.That(owner.IsRegistered, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void UnavailableBackendRejectsCacheHitsAndNewLoads()
        {
            f.Text("cached");
            f.Text("new");
            using var held = f.Service.LoadLease<TextAsset>("cached");
            var owner = f.Owner();
            int loads = f.Loader.Loads;
            f.Loader.IsAvailable = false;
            Assert.That(f.Service.LoadLease<TextAsset>("cached").IsValid, Is.False);
            Assert.That(f.Service.LoadLease<TextAsset>("new").IsValid, Is.False);
            Assert.That(f.Service.LoadLeaseAsync<TextAsset>("cached").GetAwaiter().GetResult().IsValid, Is.False);
            Assert.That(f.Service.LoadLeaseAsync<TextAsset>("new").GetAwaiter().GetResult().IsValid, Is.False);
            Assert.That(f.Service.LoadAsset<TextAsset>(owner, "cached"), Is.Null);
            Assert.That(f.Service.LoadAssetAsync<TextAsset>(owner, "new").GetAwaiter().GetResult(), Is.Null);
            Assert.That(owner.IsRegistered, Is.False);
            Assert.That(f.Loader.Loads, Is.EqualTo(loads));
            Assert.That(f.Info("cached").DirectRefCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BackendShutdownTerminatesPendingWaitersBeforeLateCompletion()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var first = f.Service.LoadLeaseAsync<TextAsset>("a");
            var second = f.Service.LoadLeaseAsync<TextAsset>("a");
            f.Loader.IsAvailable = false;
            yield return ResourceFixture.Wait(first, lease => Assert.That(lease.IsValid, Is.False));
            yield return ResourceFixture.Wait(second, lease => Assert.That(lease.IsValid, Is.False));
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.Loader.Complete("a");
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Info("a").RefCountTotal, Is.Zero);
        }

        [Test]
        public void UnavailableBackendStillAllowsForceCleanup()
        {
            f.Text("a");
            var lease = f.Service.LoadLease<TextAsset>("a");
            f.Loader.IsAvailable = false;
            f.Service.ForceUnloadAllAssets();
            Assert.That(lease.IsValid, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Info("a").RefCountTotal, Is.Zero);
            lease.Dispose();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ResourceServiceLookupPreservesScopePriorityAndUnregistration()
        {
            using var scene = new ResourceFixture();
            AppServices.EnsureWorld();
            try
            {
                AppServices.App.Register<IResourceService>(f.Service);
                AppServices.Scene.Register<IResourceService>(scene.Service);
                Assert.That(AppServices.TryGet<IResourceService>(out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(scene.Service));
                Assert.That(AppServices.App.Require<IResourceService>(), Is.SameAs(f.Service));
                Assert.That(AppServices.Scene.Unregister<IResourceService>(), Is.True);
                Assert.That(AppServices.TryGet<IResourceService>(out resolved), Is.True);
                Assert.That(resolved, Is.SameAs(f.Service));
                Assert.That(AppServices.App.Unregister<IResourceService>(), Is.True);
                Assert.That(AppServices.TryGet<IResourceService>(out _), Is.False);
            }
            finally
            {
                AppServices.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator BackendShutdownInCompletionCallbackPreventsDeliveryAndReleasesPendingReference()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            f.Service.IdleAssetCapacity = 0;
            bool delivered = false;
            var callbacks = new LoadAssetCallbacks((_, _, _, _) => delivered = true,
                (_, progress, _) => { if (progress == 1) f.Loader.IsAvailable = false; });
            var task = f.Service.LoadAssetAsync("a", typeof(TextAsset), 0, callbacks, null);
            f.Loader.Complete("a");
            yield return task.ToCoroutine();
            Assert.That(delivered, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Info("a").RefCountTotal, Is.Zero);
        }
    }
}
