using System.Text.RegularExpressions;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceBindingTests
    {
        private ResourceFixture f;
        private ResourceScope scope;

        [SetUp]
        public void SetUp()
        {
            f = new ResourceFixture();
            scope = f.Service.CreateScope("bindings");
        }

        [TearDown] public void TearDown() => f.Dispose();

        private SpriteRenderer SpriteTarget() => f.GameObject().AddComponent<SpriteRenderer>();

        [Test]
        public void CachedAssetAppliesInTheSameCall()
        {
            var sprite = f.Sprite("s");
            using var held = f.Service.LoadLease<Sprite>("s");
            var target = SpriteTarget();
            target.SetSprite(scope, "s");
            Assert.That(target.sprite, Is.SameAs(sprite));
            Assert.That(f.Info("s").RefCount, Is.EqualTo(2));
            Assert.That(f.Service.GetStats().BindingReferenceCount, Is.EqualTo(1));
            f.AssertInvariants();
        }

        [Test]
        public void MissAppliesWhenTheLoadCompletes()
        {
            var sprite = f.Sprite("s");
            var target = SpriteTarget();
            target.SetSprite(scope, f.Service.Key<Sprite>("s"));
            Assert.That(target.sprite, Is.Null);
            Assert.That(f.Info("s").State, Is.EqualTo(ResourceAssetState.Loading));
            f.Tick();
            Assert.That(target.sprite, Is.SameAs(sprite));
            Assert.That(f.Info("s").RefCount, Is.EqualTo(1));
            f.AssertInvariants();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LatestBindingWinsRegardlessOfCompletionOrder(bool latestFirst)
        {
            f.Sprite("a");
            var b = f.Sprite("b");
            f.Loader.CompleteImmediately = false;
            var target = SpriteTarget();
            target.SetSprite(scope, "a");
            target.SetSprite(scope, "b");
            f.Loader.Complete(latestFirst ? "b" : "a");
            f.Tick();
            f.Loader.Complete(latestFirst ? "a" : "b");
            f.Tick();
            Assert.That(target.sprite, Is.SameAs(b));
            Assert.That(f.Info("a").RefCount, Is.Zero);
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Info("b").RefCount, Is.EqualTo(1));
            f.AssertInvariants();
        }

        [Test]
        public void ReplacingReleasesThePreviousAsset()
        {
            f.Sprite("a");
            var b = f.Sprite("b");
            var target = SpriteTarget();
            target.SetSprite(scope, "a", ResourceBindingOptions.Synchronous);
            target.SetSprite(scope, "b", ResourceBindingOptions.Synchronous);
            Assert.That(target.sprite, Is.SameAs(b));
            Assert.That(f.Info("a").RefCount, Is.Zero);
            target.SetSprite(scope, "b");
            Assert.That(f.Info("b").RefCount, Is.EqualTo(1), "Rebinding the same asset keeps one reference.");
            f.AssertInvariants();
        }

        [Test]
        public void EmptyLocationClearsOnlyThePropertyItApplied()
        {
            f.Sprite("a");
            var foreign = f.Sprite("foreign");
            var target = SpriteTarget();
            var other = SpriteTarget();
            target.SetSprite(scope, "a", ResourceBindingOptions.Synchronous);
            other.SetSprite(scope, "a", ResourceBindingOptions.Synchronous);
            other.sprite = foreign;
            target.SetSprite(scope, "");
            other.SetSprite(scope, (string)null);
            Assert.That(target.sprite, Is.Null);
            Assert.That(other.sprite, Is.SameAs(foreign));
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            Assert.That(f.Info("a").RefCount, Is.Zero);
            target.SetSprite(scope, "");
            Assert.That(f.Service.Bindings.Count, Is.Zero, "Clearing an unbound target creates nothing.");
            f.AssertInvariants();
        }

        [Test]
        public void SynchronousOptionAppliesAMissImmediately()
        {
            var sprite = f.Sprite("s");
            f.Loader.CompleteImmediately = false;
            var target = SpriteTarget();
            target.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            Assert.That(target.sprite, Is.SameAs(sprite));
            Assert.That(f.Loader.SynchronousLoads, Is.EqualTo(1));
            f.AssertInvariants();
        }

        [Test]
        public void SynchronousBindingJoinsAPendingAsyncBinding()
        {
            var sprite = f.Sprite("s");
            f.Loader.CompleteImmediately = false;
            var first = SpriteTarget();
            var second = SpriteTarget();
            first.SetSprite(scope, "s");
            second.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            Assert.That(first.sprite, Is.SameAs(sprite));
            Assert.That(second.sprite, Is.SameAs(sprite));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Loader.WaitCalls, Is.EqualTo(1));
            Assert.That(f.Info("s").RefCount, Is.EqualTo(2));
            f.AssertInvariants();
        }

        [Test]
        public void MaterialInstancesAreDestroyedOnReplaceSwitchAndClear()
        {
            var material = f.Material("m");
            var renderer = f.GameObject().AddComponent<MeshRenderer>();
            renderer.SetMaterialInstance(scope, "m", ResourceBindingOptions.Synchronous);
            var first = renderer.sharedMaterial;
            Assert.That(first, Is.Not.SameAs(material));
            Assert.That(first.shader, Is.SameAs(material.shader));
            renderer.SetMaterialInstance(scope, "m");
            var second = renderer.sharedMaterial;
            Assert.That(first == null, Is.True);
            Assert.That(second, Is.Not.SameAs(material));
            renderer.SetSharedMaterial(scope, "m");
            Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            Assert.That(second == null, Is.True);
            Assert.That(f.Service.Bindings.Count, Is.EqualTo(1), "Shared and instance material share one binding slot.");
            renderer.SetMaterialInstance(scope, "m");
            var third = renderer.sharedMaterial;
            renderer.SetSharedMaterial(scope, "");
            Assert.That(third == null, Is.True);
            Assert.That(renderer.sharedMaterial, Is.Null);
            Assert.That(f.Info("m").RefCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void ImageSpriteAndMaterialUseIndependentSlots()
        {
            var sprite = f.Sprite("s");
            var material = f.Material("m");
            var image = f.GameObject().AddComponent<Image>();
            image.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            image.SetMaterial(scope, "m", ResourceBindingOptions.Synchronous);
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(image.material, Is.SameAs(material));
            Assert.That(f.Service.Bindings.Count, Is.EqualTo(2));
            image.SetMaterial(scope, "");
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(f.Service.Bindings.Count, Is.EqualTo(1));
            f.AssertInvariants();
        }

        [Test]
        public void SetNativeSizeResizesTheImage()
        {
            f.Sprite("s");
            var image = f.GameObject().AddComponent<Image>();
            image.rectTransform.sizeDelta = new Vector2(100f, 100f);
            image.SetSprite(scope, "s", ResourceBindingOptions.Synchronous | ResourceBindingOptions.SetNativeSize);
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(4f, 4f)));
        }

        [Test]
        public void SubSpriteBindsByNameAndMissingNameKeepsThePreviousSprite()
        {
            var sprite = f.Sprite("atlas");
            var target = SpriteTarget();
            target.SetSubSprite(scope, "atlas", "atlas", ResourceBindingOptions.Synchronous);
            Assert.That(target.sprite, Is.SameAs(sprite));
            Assert.That(f.Info("atlas", typeof(Sprite), true).RefCount, Is.EqualTo(1));
            LogAssert.Expect(LogType.Warning, new Regex("Resource binding sprite 'missing' not found"));
            target.SetSubSprite(scope, "atlas", "missing", ResourceBindingOptions.Synchronous);
            Assert.That(target.sprite, Is.SameAs(sprite));
            Assert.That(f.Info("atlas", typeof(Sprite), true).RefCount, Is.EqualTo(1));
            f.AssertInvariants();
        }

        [Test]
        public void ScopeDisposeClearsPropertiesAndReleasesReferences()
        {
            f.Sprite("s");
            var first = SpriteTarget();
            var second = SpriteTarget();
            first.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            second.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            scope.Dispose();
            Assert.That(first.sprite, Is.Null);
            Assert.That(second.sprite, Is.Null);
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            Assert.That(f.Info("s").RefCount, Is.Zero);
            first.SetSprite(scope, "s");
            Assert.That(first.sprite, Is.Null, "A disposed scope cannot bind.");
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void RebindingIntoAnotherScopeMovesOwnership()
        {
            var sprite = f.Sprite("s");
            var target = SpriteTarget();
            var other = f.Service.CreateScope("other");
            target.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            target.SetSprite(other, "s");
            Assert.That(f.Service.Bindings.CountScopeBindings(scope.Index), Is.Zero);
            Assert.That(f.Service.Bindings.CountScopeBindings(other.Index), Is.EqualTo(1));
            scope.Dispose();
            Assert.That(target.sprite, Is.SameAs(sprite));
            other.Dispose();
            Assert.That(target.sprite, Is.Null);
            f.AssertInvariants();
        }

        [Test]
        public void ScopeDisposedDuringLoadDropsThePendingBinding()
        {
            f.Sprite("s");
            f.Loader.CompleteImmediately = false;
            var target = SpriteTarget();
            target.SetSprite(scope, "s");
            scope.Dispose();
            f.Loader.Complete("s");
            f.Tick();
            Assert.That(target.sprite, Is.Null);
            Assert.That(f.Info("s").RefCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void DestroyedTargetsAreReleasedIncludingPendingLoads()
        {
            f.Sprite("s");
            var bound = SpriteTarget();
            bound.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            f.Loader.CompleteImmediately = false;
            f.Sprite("p");
            var pending = SpriteTarget();
            pending.SetSprite(scope, "p");
            Object.DestroyImmediate(bound.gameObject);
            Object.DestroyImmediate(pending.gameObject);
            f.Loader.Complete("p");
            f.Tick();
            Assert.That(f.Info("s").RefCount, Is.Zero);
            Assert.That(f.Info("p").RefCount, Is.Zero);
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void ForceUnloadAllClearsBoundProperties()
        {
            f.Sprite("s");
            var target = SpriteTarget();
            target.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            f.Service.ForceUnloadAllAssets();
            Assert.That(target.sprite, Is.Null);
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            target.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            Assert.That(target.sprite, Is.Not.Null, "Scopes stay usable after a reset.");
            f.AssertInvariants();
        }

        [Test]
        public void ShutdownRejectsBindings()
        {
            f.Sprite("s");
            var target = SpriteTarget();
            f.Service.Shutdown();
            target.SetSprite(f.Service.App, "s", ResourceBindingOptions.Synchronous);
            Assert.That(target.sprite, Is.Null);
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            Assert.That(f.Loader.Loads, Is.Zero);
        }

        [Test]
        public void FailedLoadLeavesTheBindingUnapplied()
        {
            f.Sprite("s");
            f.Loader.CompleteImmediately = false;
            var target = SpriteTarget();
            target.SetSprite(scope, "s");
            f.Loader.Complete("s", false);
            LogAssert.Expect(LogType.Warning, new Regex("Resource load failed"));
            f.Tick();
            Assert.That(target.sprite, Is.Null);
            Assert.That(f.Service.GetStats().BindingReferenceCount, Is.Zero);
            f.Loader.CompleteImmediately = true;
            target.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            Assert.That(target.sprite, Is.Not.Null);
            f.AssertInvariants();
        }
    }
}
