using System;
using System.Collections;
using System.Threading;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceRegressionTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [Test]
        public void SharedLeaseAndCopiedDisposeKeepExactReferences()
        {
            f.Text("a");
            var first = f.Service.LoadLease<TextAsset>("a");
            var copy = first;
            var second = f.Service.LoadLease<TextAsset>("a");
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(2));
            first.Dispose();
            copy.Dispose();
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
            second.Dispose();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void OwnerReleasesThroughItsRegisteringService()
        {
            f.Sprite("a");
            var owner = f.Owner();
            var target = owner.gameObject.AddComponent<SpriteRenderer>();
            f.Bindings.BindSprite(owner, target, new ResourceKey("a"));
            owner.ReleaseBindings();
            f.AssertNoReferences("a");
            Assert.That(target.sprite, Is.Null);
        }

        [Test]
        public void ImageMaterialUnbindClearsAppliedMaterial()
        {
            var asset = f.Material("a");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            f.Bindings.BindImageMaterial(owner, image, new ResourceKey("a"));
            f.Bindings.ReleaseOwner(owner);
            Assert.That(image.material, Is.Not.SameAs(asset));
            f.AssertNoReferences("a");
        }

        [Test]
        public void SharedAndInstanceMaterialUseOnePhysicalSlot()
        {
            f.Material("a");
            f.Material("b");
            var owner = f.Owner();
            var renderer = owner.gameObject.AddComponent<MeshRenderer>();
            f.Bindings.BindSharedMaterial(owner, renderer, new ResourceKey("a"));
            f.Bindings.BindMaterialInstance(owner, renderer, new ResourceKey("b"));
            f.AssertNoReferences("a");
            Assert.That(f.Info("b").BindingRefCount, Is.EqualTo(1));
        }

        [Test]
        public void BackendExceptionDoesNotLeaveLoadingRecord()
        {
            f.Text("a");
            f.Loader.ThrowOnLoad = true;
            Assert.Throws<InvalidOperationException>(() => f.Service.LoadLease<TextAsset>("a"));
            var field = typeof(ResourceService).GetField("_assetLoadingOperationByKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var map = field.GetValue(f.Service);
            Assert.That((int)map.GetType().GetProperty("Count").GetValue(map), Is.Zero);
        }

        [UnityTest]
        public IEnumerator FirstCallerCancellationDoesNotStrandSharedRequest()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            using var cts = new CancellationTokenSource();
            var first = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            var second = f.Service.LoadLeaseAsync<TextAsset>("a");
            cts.Cancel();
            yield return ResourceFixture.Wait(first, lease => Assert.That(lease.IsValid, Is.False));
            f.Loader.Complete("a");
            yield return ResourceFixture.Wait(second, lease => { Assert.That(lease.IsValid, Is.True); lease.Dispose(); });
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void CallbackExceptionReturnsItsReference()
        {
            f.Text("a");
            var task = f.Service.LoadAsset<TextAsset>("a", _ => throw new InvalidOperationException("callback"));
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            f.AssertNoReferences("a");
        }

        [Test]
        public void ZeroTtlReleasedAfterMaintenanceInSameTick()
        {
            f.Text("a");
            f.Service.IdleAssetExpireTime = 0;
            var lease = f.Service.LoadLease<TextAsset>("a");
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 16);
            lease.Dispose();
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 16);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void UniqueKeyChurnMaintainsExactCount(int count)
        {
            var map = new ResourceUlongIntMap();
            map.ReserveCapacity(16);
            for (ulong i = 1; i <= (ulong)count; i++)
            {
                map.Set(i, 1);
                Assert.That(map.Remove(i), Is.True);
            }
            Assert.That(map.Count, Is.Zero);
        }

        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void CachedLeaseChurnMaintainsOneHeldReference(int count)
        {
            f.Text("a");
            var held = f.Service.LoadLease<TextAsset>("a");
            for (int i = 0; i < count; i++) f.Service.LoadLease<TextAsset>("a").Dispose();
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
            held.Dispose();
        }
    }
}
