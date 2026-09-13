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
    public sealed class ResourceOwnershipTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [Test]
        public void RegisterForReturnsOnlyAnOwnerRegisteredWithTheRequestedService()
        {
            var target = f.Keep(new GameObject("registered-target")).transform;
            var owner = ResourceOwner.RegisterFor(target, f.Bindings);
            Assert.That(owner, Is.Not.Null);
            Assert.That(owner.BindingService, Is.SameAs(f.Bindings));
            Assert.That(ResourceOwner.RegisterFor(target, f.Bindings), Is.SameAs(owner));
            using var other = new ResourceFixture();
            Assert.That(ResourceOwner.RegisterFor(target, other.Bindings), Is.Null);
            Assert.That(owner.BindingService, Is.SameAs(f.Bindings));
            Assert.That(ResourceOwner.RegisterFor(null, f.Bindings), Is.Null);
            Assert.Throws<ArgumentNullException>(() => ResourceOwner.RegisterFor(target, null));
            owner.ReleaseBindings();
            Assert.That(ResourceOwner.RegisterFor(target, other.Bindings), Is.SameAs(owner));
            Assert.That(owner.BindingService, Is.SameAs(other.Bindings));
        }

        [Test]
        public void OwnerAssetChainsRemainIndependentAcrossReuseAndReset()
        {
            const int count = 300;
            var first = f.Owner();
            var second = f.Owner();
            for (int i = 0; i < count; i++)
            {
                string key = "owned-" + i;
                var asset = f.Text(key);
                Assert.That(f.Service.LoadAsset<TextAsset>(first, key), Is.SameAs(asset));
                Assert.That(f.Service.LoadAsset<TextAsset>(first, key), Is.SameAs(asset));
                Assert.That(f.Service.LoadAsset<TextAsset>(second, key), Is.SameAs(asset));
                Assert.That(f.Info(key).DirectRefCount, Is.EqualTo(2));
            }
            first.ReleaseBindings();
            for (int i = 0; i < count; i++) Assert.That(f.Info("owned-" + i).DirectRefCount, Is.EqualTo(1));
            for (int i = count - 1; i >= 0; i--) f.Service.LoadAsset<TextAsset>(first, "owned-" + i);
            second.ReleaseBindings();
            for (int i = 0; i < count; i++) Assert.That(f.Info("owned-" + i).DirectRefCount, Is.EqualTo(1));
            f.Service.ForceUnloadAllAssets();
            Assert.That(first.IsRegistered, Is.False);
            Assert.That(second.IsRegistered, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.Service.LoadAsset<TextAsset>(second, "owned-0");
            first.ReleaseBindings();
            Assert.That(f.Info("owned-0").DirectRefCount, Is.EqualTo(1));
            second.ReleaseBindings();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SharedAsyncOwnerLoadsDeduplicateAfterPartialCancellation()
        {
            var expected = f.Text("shared-owned");
            var first = f.Owner();
            var second = f.Owner();
            f.Loader.CompleteImmediately = false;
            using var canceled = new CancellationTokenSource();
            var abandoned = f.Service.LoadAssetAsync<TextAsset>(first, "shared-owned", canceled.Token);
            var sameOwner = f.Service.LoadAssetAsync<TextAsset>(first, "shared-owned");
            var duplicate = f.Service.LoadAssetAsync<TextAsset>(first, "shared-owned");
            var otherOwner = f.Service.LoadAssetAsync<TextAsset>(second, "shared-owned");
            canceled.Cancel();
            f.Loader.Complete("shared-owned");
            yield return ResourceFixture.Wait(abandoned, asset => Assert.That(asset, Is.Null));
            yield return ResourceFixture.Wait(sameOwner, asset => Assert.That(asset, Is.SameAs(expected)));
            yield return ResourceFixture.Wait(duplicate, asset => Assert.That(asset, Is.SameAs(expected)));
            yield return ResourceFixture.Wait(otherOwner, asset => Assert.That(asset, Is.SameAs(expected)));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Info("shared-owned").DirectRefCount, Is.EqualTo(2));
            Assert.That(f.Info("shared-owned").PendingRefCount, Is.Zero);
            first.ReleaseBindings();
            second.ReleaseBindings();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ResetDuringOwnerCleanupCannotCorruptReusedAssetChains()
        {
            f.Text("owned");
            f.Material("material");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            f.Service.LoadAsset<TextAsset>(owner, "owned");
            f.Bindings.BindImageMaterial(owner, image, new ResourceKey("material"));
            image.RegisterDirtyMaterialCallback(() => f.Service.ForceUnloadAllAssets());
            owner.ReleaseBindings();
            Assert.That(owner.IsRegistered, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.Service.LoadAsset<TextAsset>(owner, "owned");
            Assert.That(f.Info("owned").DirectRefCount, Is.EqualTo(1));
            owner.ReleaseBindings();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void OwnerLoadsDeduplicateAndMultipleOwnersRetainIndependently()
        {
            var asset = f.Text("a");
            var first = f.Owner();
            var second = f.Owner();
            Assert.That(f.Service.LoadAsset<TextAsset>(first, "a"), Is.SameAs(asset));
            Assert.That(f.Service.LoadAsset<TextAsset>(first, "a"), Is.SameAs(asset));
            Assert.That(f.Service.LoadAsset<TextAsset>(second, "a"), Is.SameAs(asset));
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(2));
            first.ReleaseBindings();
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
            second.ReleaseBindings();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ResetInvalidatesLeaseValueAndCannotReleaseReplacement()
        {
            f.Text("a");
            var old = f.Service.LoadLease<TextAsset>("a");
            f.Service.ForceUnloadAllAssets();
            var replacement = f.Service.LoadLease<TextAsset>("a");
            Assert.That(old.IsValid, Is.False);
            Assert.That(old.Asset, Is.Null);
            old.Dispose();
            Assert.That(replacement.IsValid, Is.True);
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
            replacement.Dispose();
        }

        [Test]
        public void PreCanceledResolvedKeyDoesNotAcquire()
        {
            f.Text("a");
            var lease = f.Service.LoadLease<TextAsset>("a");
            var key = new ResourceKey(f.Info("a").LoadKeyId, 0);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.That(f.Service.AcquireDirectAsync(key, cts.Token).GetAwaiter().GetResult().IsValid, Is.False);
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
            lease.Dispose();
        }

        [Test]
        public void AssetFactoryAndTypedConvenienceShareRecord()
        {
            f.Sprite("a");
            var first = f.Service.LoadLease<Sprite>(ResourceKey.Asset<Sprite>("a"));
            var second = f.Service.LoadLease<Sprite>("a");
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(2));
            first.Dispose();
            second.Dispose();
        }

        [Test]
        public void CapacityEvictsOldestIdleAndProtectsActiveResources()
        {
            f.Service.IdleAssetCapacity = 2;
            foreach (string key in new[] { "held", "a", "b", "c" }) f.Text(key);
            var held = f.Service.LoadLease<TextAsset>("held");
            foreach (string key in new[] { "a", "b", "c" }) f.Service.LoadLease<TextAsset>(key).Dispose();
            Assert.That(f.Info("a").HandleValid, Is.False);
            Assert.That(f.Info("b").HandleValid, Is.True);
            Assert.That(f.Info("c").HandleValid, Is.True);
            Assert.That(held.IsValid, Is.True);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(3));
            held.Dispose();
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(2));
        }

        [Test]
        public void ZeroCapacityTransfersBeforeEviction()
        {
            f.Service.IdleAssetCapacity = 0;
            f.Text("a");
            var lease = f.Service.LoadLease<TextAsset>("a");
            Assert.That(lease.IsValid, Is.True);
            lease.Dispose();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ExpiryBudgetAndChangingTtlDoNotReleaseEarlyOrStarve()
        {
            f.Text("long");
            f.Text("short");
            f.Service.IdleAssetExpireTime = 600;
            f.Service.LoadLease<TextAsset>("long").Dispose();
            f.Service.IdleAssetExpireTime = 0;
            f.Service.LoadLease<TextAsset>("short").Dispose();
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 1);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(2));
            f.Service.ProcessResourceMaintenance(Time.unscaledTime, 1);
            Assert.That(f.Info("short").HandleValid, Is.False);
            Assert.That(f.Info("long").HandleValid, Is.True);
            f.Service.ProcessResourceMaintenance(Time.unscaledTime + 599, 1);
            Assert.That(f.Info("long").HandleValid, Is.True);
            f.Service.ProcessResourceMaintenance(Time.unscaledTime + 601, 1);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FollowerCancellationAndLateResultReleaseExactlyOnce()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var first = f.Service.LoadLeaseAsync<TextAsset>("a");
            using var cts = new CancellationTokenSource();
            var canceled = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            cts.Cancel();
            yield return ResourceFixture.Wait(canceled, lease => Assert.That(lease.IsValid, Is.False));
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            f.Loader.Complete("a");
            yield return ResourceFixture.Wait(first, lease => { Assert.That(lease.IsValid, Is.True); lease.Dispose(); });
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AllCallersCancelBeforeLateCompletion()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            using var cts = new CancellationTokenSource();
            var first = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            var second = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            cts.Cancel();
            yield return ResourceFixture.Wait(first, lease => Assert.That(lease.IsValid, Is.False));
            yield return ResourceFixture.Wait(second, lease => Assert.That(lease.IsValid, Is.False));
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.Loader.Complete("a");
            yield return null;
            Assert.That(f.Info("a").RefCountTotal, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SharedCompletionRemainsPinnedDuringOtherCallersCleanup()
        {
            f.Text("a");
            f.Service.IdleAssetCapacity = 0;
            f.Loader.CompleteImmediately = false;
            var first = f.Service.LoadLeaseAsync<TextAsset>("a");
            var second = f.Service.LoadLeaseAsync<TextAsset>("a");
            f.Loader.Complete("a");
            yield return ResourceFixture.Wait(first, lease => { lease.Dispose(); f.Service.UnloadUnusedAssets(true); });
            yield return ResourceFixture.Wait(second, lease => { Assert.That(lease.IsValid, Is.True); lease.Dispose(); });
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FailureEndsAllWaitersAndRetryStartsNewOperation()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var first = f.Service.LoadLeaseAsync<TextAsset>("a");
            var second = f.Service.LoadLeaseAsync<TextAsset>("a");
            f.Loader.Complete("a", false);
            yield return ResourceFixture.Wait(first, lease => Assert.That(lease.IsValid, Is.False));
            yield return ResourceFixture.Wait(second, lease => Assert.That(lease.IsValid, Is.False));
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.Loader.Providers.Remove("a");
            f.Loader.CompleteImmediately = true;
            var retry = f.Service.LoadLease<TextAsset>("a");
            Assert.That(retry.IsValid, Is.True);
            Assert.That(f.Loader.Loads, Is.EqualTo(2));
            retry.Dispose();
        }

        [UnityTest]
        public IEnumerator ResetLateCompletionCannotTouchReplacementOperation()
        {
            f.Text("a");
            f.Text("b");
            f.Loader.CompleteImmediately = false;
            var old = f.Service.LoadLeaseAsync<TextAsset>("a");
            f.Service.ForceUnloadAllAssets();
            var current = f.Service.LoadLeaseAsync<TextAsset>("b");
            f.Loader.Complete("a");
            yield return ResourceFixture.Wait(old, lease => Assert.That(lease.IsValid, Is.False));
            f.Loader.Complete("b");
            yield return ResourceFixture.Wait(current, lease => { Assert.That(lease.IsValid, Is.True); lease.Dispose(); });
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator OwnerReleaseAndReuseRejectsOldRequest()
        {
            f.Text("a");
            f.Text("b");
            var owner = f.Owner(false);
            f.Loader.CompleteImmediately = false;
            var old = f.Service.LoadAssetAsync<TextAsset>(owner, "a");
            owner.ReleaseBindings();
            var current = f.Service.LoadAssetAsync<TextAsset>(owner, "b");
            f.Loader.Complete("b");
            yield return ResourceFixture.Wait(current, asset => Assert.That(asset, Is.SameAs(f.Loader.Assets["b"])));
            f.Loader.Complete("a");
            yield return ResourceFixture.Wait(old, asset => Assert.That(asset, Is.Null));
            Assert.That(f.Info("a").DirectRefCount, Is.Zero);
            Assert.That(f.Info("b").DirectRefCount, Is.EqualTo(1));
        }

        [Test]
        public void CallbackThatAlreadyReleasedCannotReleaseAnotherCallerOnException()
        {
            f.Text("a");
            var held = f.Service.LoadAsset<TextAsset>("a");
            var callback = f.Service.LoadAsset<TextAsset>("a", asset =>
            {
                f.Service.UnloadAsset(asset);
                throw new InvalidOperationException("after release");
            });
            Assert.Throws<InvalidOperationException>(() => callback.GetAwaiter().GetResult());
            Assert.That(f.Info("a").LegacyDirectRefCount, Is.EqualTo(1));
            f.Service.UnloadAsset(held);
        }

        [Test]
        public void CallbackRollbackRemovesItsExactLeaseUnderReentrantLoad()
        {
            f.Text("a");
            TextAsset nested = null;
            var task = f.Service.LoadAsset<TextAsset>("a", _ =>
            {
                nested = f.Service.LoadAsset<TextAsset>("a");
                throw new InvalidOperationException("after nested load");
            });
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.That(f.Info("a").LegacyDirectRefCount, Is.EqualTo(1));
            f.Service.UnloadAsset(nested);
            Assert.That(f.Info("a").RefCountTotal, Is.Zero);
        }

        [Test]
        public void ForeignOwnerIsRejectedUntilExplicitRelease()
        {
            using var other = new ResourceFixture();
            var owner = f.Owner();
            Assert.That(f.Bindings.RegisterOwner(owner), Is.EqualTo(ResourceBindStatus.Success));
            Assert.That(other.Bindings.RegisterOwner(owner), Is.EqualTo(ResourceBindStatus.StaleOwner));
            owner.ReleaseBindings();
            Assert.That(other.Bindings.RegisterOwner(owner), Is.EqualTo(ResourceBindStatus.Success));
            owner.ReleaseBindings();
        }
    }
}
