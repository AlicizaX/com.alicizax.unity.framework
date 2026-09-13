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
    public sealed class ResourceBindingTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [UnityTest]
        public IEnumerator OldMaterialRequestCannotOverrideNewSharedMaterial()
        {
            f.Material("old");
            var asset = f.Material("new");
            var owner = f.Owner();
            var target = owner.gameObject.AddComponent<MeshRenderer>();
            f.Loader.CompleteImmediately = false;
            var old = f.Bindings.BindMaterialInstanceAsync(owner, target, new ResourceKey("old"));
            Assert.That(f.Bindings.BindSharedMaterial(owner, target, new ResourceKey("new")), Is.EqualTo(ResourceBindStatus.Success));
            f.Loader.Complete("old");
            yield return ResourceFixture.Wait(old, status => Assert.That(status, Is.EqualTo(ResourceBindStatus.StaleOwner)));
            Assert.That(target.sharedMaterial, Is.SameAs(asset));
            Assert.That(f.Info("old").BindingRefCount, Is.Zero);
            Assert.That(f.Info("new").BindingRefCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OldSubSpriteCannotOverrideNewSprite()
        {
            f.Sprite("atlas");
            var asset = f.Sprite("new");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            f.Loader.CompleteImmediately = false;
            var old = f.Bindings.BindSubSpriteAsync(owner, image, new ResourceKey("atlas"), "atlas");
            f.Bindings.BindSprite(owner, image, new ResourceKey("new"));
            f.Loader.Complete("atlas");
            yield return ResourceFixture.Wait(old, status => Assert.That(status, Is.EqualTo(ResourceBindStatus.StaleOwner)));
            Assert.That(image.sprite, Is.SameAs(asset));
            Assert.That(f.Info("atlas").BindingRefCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CanceledSlotReuseDoesNotAcceptOldRequest()
        {
            f.Material("old");
            var asset = f.Material("new");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            f.Loader.CompleteImmediately = false;
            using var cts = new CancellationTokenSource();
            var old = f.Bindings.BindImageMaterialAsync(owner, image, new ResourceKey("old"), cancellationToken: cts.Token);
            cts.Cancel();
            yield return ResourceFixture.Wait(old, status => Assert.That(status, Is.EqualTo(ResourceBindStatus.Canceled)));
            var current = f.Bindings.BindImageMaterialAsync(owner, image, new ResourceKey("new"));
            f.Loader.Complete("old");
            f.Loader.Complete("new");
            yield return ResourceFixture.Wait(current, status => Assert.That(status, Is.EqualTo(ResourceBindStatus.Success)));
            Assert.That(image.material, Is.SameAs(asset));
            Assert.That(f.Info("old").RefCountTotal, Is.Zero);
        }

        [Test]
        public void EmptyLocationClearsAndUnregisterReleasesOnlyItsTarget()
        {
            f.Sprite("a");
            var owner = f.Owner();
            var first = owner.gameObject.AddComponent<SpriteRenderer>();
            var second = f.Keep(new GameObject("second")).AddComponent<SpriteRenderer>();
            f.Bindings.BindSprite(owner, first, new ResourceKey("a"));
            f.Bindings.BindSprite(owner, second, new ResourceKey("a"));
            Assert.That(f.Bindings.BindSprite(owner, first, new ResourceKey("")), Is.EqualTo(ResourceBindStatus.Success));
            Assert.That(first.sprite, Is.Null);
            Assert.That(f.Info("a").BindingRefCount, Is.EqualTo(1));
            f.Bindings.UnregisterTarget(owner, second);
            Assert.That(second.sprite, Is.Null);
            Assert.That(f.Info("a").BindingRefCount, Is.Zero);
        }

        [Test]
        public void ApplyCallbackExceptionReleasesLeaseAndPreservesError()
        {
            f.Material("a");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            image.RegisterDirtyMaterialCallback(() => throw new InvalidOperationException("dirty callback"));
            Assert.Throws<AggregateException>(() => f.Bindings.BindImageMaterial(owner, image, new ResourceKey("a")));
            Assert.That(f.Info("a").BindingRefCount, Is.Zero);
        }

        [Test]
        public void OwnerReleaseContinuesAfterOneTargetCallbackThrows()
        {
            f.Material("material");
            f.Sprite("sprite");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            var sprite = f.Keep(new GameObject("sprite")).AddComponent<SpriteRenderer>();
            f.Bindings.BindSprite(owner, sprite, new ResourceKey("sprite"));
            f.Bindings.BindImageMaterial(owner, image, new ResourceKey("material"));
            image.RegisterDirtyMaterialCallback(() => throw new InvalidOperationException("clear callback"));
            Assert.Throws<InvalidOperationException>(() => owner.ReleaseBindings());
            Assert.That(owner.IsRegistered, Is.False);
            Assert.That(sprite.sprite, Is.Null);
            Assert.That(f.Info("material").RefCountTotal, Is.Zero);
            Assert.That(f.Info("sprite").RefCountTotal, Is.Zero);
        }

        [Test]
        public void OwnerReleaseDuringApplyCannotLeaveTransferredLease()
        {
            f.Material("a");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            image.RegisterDirtyMaterialCallback(() => owner.ReleaseBindings());
            Assert.That(f.Bindings.BindImageMaterial(owner, image, new ResourceKey("a")), Is.EqualTo(ResourceBindStatus.StaleOwner));
            Assert.That(owner.IsRegistered, Is.False);
            Assert.That(f.Info("a").BindingRefCount, Is.Zero);
        }

        [Test]
        public void PoolReleaseResetInCallbackCannotRestoreShutdownRegistration()
        {
            var prefab = f.Keep(new GameObject("source"));
            f.Loader.Assets.Add("prefab", prefab);
            var instance = f.Keep(f.Service.LoadGameObject("prefab"));
            var owner = instance.GetComponent<ResourceOwner>();
            var image = instance.AddComponent<Image>();
            f.Material("a");
            f.Bindings.BindImageMaterial(owner, image, new ResourceKey("a"));
            image.RegisterDirtyMaterialCallback(f.Service.ForceUnloadAllAssets);

            Assert.That(owner.ReleaseBindings(), Is.EqualTo(ResourceBindStatus.Success));
            Assert.That(owner.IsRegistered, Is.False, "Reset must not leave the owner registered with the retired binding service.");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Service.LoadAsset<Material>(owner, "a"), Is.SameAs(f.Loader.Assets["a"]));
            Assert.That(f.Info("a").DirectRefCount, Is.EqualTo(1));
        }

        [Test]
        public void ReentrantSpriteReplacementDoesNotApplyOldNativeSize()
        {
            f.Sprite("old");
            var expected = f.Sprite("new");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            var expectedSize = new Vector2(91, 73);
            image.rectTransform.sizeDelta = expectedSize;
            bool reentered = false;
            image.RegisterDirtyMaterialCallback(() =>
            {
                if (reentered) return;
                reentered = true;
                Assert.That(f.Bindings.BindSprite(owner, image, new ResourceKey("new")), Is.EqualTo(ResourceBindStatus.Success));
            });

            Assert.That(f.Bindings.BindSprite(owner, image, new ResourceKey("old"), ResourceBindingOptions.SetNativeSize), Is.EqualTo(ResourceBindStatus.StaleOwner));
            Assert.That(reentered, Is.True);
            Assert.That(image.sprite, Is.SameAs(expected));
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(expectedSize));
            Assert.That(f.Info("old").BindingRefCount, Is.Zero);
            Assert.That(f.Info("new").BindingRefCount, Is.EqualTo(1));
        }

        [Test]
        public void ReentrantTargetTransferKeepsTheNewestOwner()
        {
            f.Material("a");
            f.Material("b");
            var latest = f.Material("c");
            var first = f.Owner();
            var second = f.Owner();
            var third = f.Owner();
            var image = first.gameObject.AddComponent<Image>();
            f.Bindings.BindImageMaterial(first, image, new ResourceKey("a"));
            bool invoked = false;
            image.RegisterDirtyMaterialCallback(() =>
            {
                if (invoked) return;
                invoked = true;
                f.Bindings.BindImageMaterial(third, image, new ResourceKey("c"));
            });
            Assert.That(f.Bindings.BindImageMaterial(second, image, new ResourceKey("b")), Is.EqualTo(ResourceBindStatus.StaleOwner));
            Assert.That(image.material, Is.SameAs(latest));
            Assert.That(f.Info("a").BindingRefCount, Is.Zero);
            Assert.That(f.Info("b").BindingRefCount, Is.Zero);
            Assert.That(f.Info("c").BindingRefCount, Is.EqualTo(1));
            Assert.That(f.Bindings.UnregisterTarget(third, image), Is.EqualTo(ResourceBindStatus.Success));
            Assert.That(image.material, Is.Not.SameAs(latest));
            Assert.That(f.Info("c").BindingRefCount, Is.Zero);
        }
    }
}
