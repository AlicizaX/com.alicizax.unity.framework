using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceFixtureTests
    {
        [Test]
        public void MissingRecordCannotMasqueradeAsAZeroReferenceRecord()
        {
            using var f = new ResourceFixture();
            Assert.Throws<AssertionException>(() => f.Info("missing"));
            Assert.That(f.TryFindInfo("missing", out _), Is.False);
            f.AssertUnloaded("missing");
            f.Text("held");
            using var lease = f.Service.LoadLease<TextAsset>("held");
            Assert.Throws<AssertionException>(() => f.AssertNoReferences("held"));
            Assert.Throws<AssertionException>(() => f.AssertUnloaded("held"));
        }

        [Test]
        public void SameLocationKeepsPackageTypeAndSubAssetOwnershipIndependent()
        {
            using var f = new ResourceFixture();
            string package = f.Service.DefaultPackageName;
            var text = f.Text("same");
            var otherText = f.Keep(new TextAsset("other-package"));
            var sprite = f.Sprite("sprite-source");
            f.Loader.KeyedAssets[("Other", "same", typeof(TextAsset), false)] = otherText;
            f.Loader.KeyedAssets[(package, "same", typeof(Sprite), false)] = sprite;
            f.Loader.KeyedAssets[(package, "same", typeof(Sprite), true)] = sprite;
            var defaultLease = f.Service.LoadLease<TextAsset>("same");
            var otherLease = f.Service.LoadLease<TextAsset>("same", "Other");
            var objectLease = f.Service.LoadLease<Object>("same");
            var spriteLease = f.Service.LoadLease<Sprite>("same");
            var sub = f.Service.AcquireDirect(new ResourceKey("same", assetType: typeof(Sprite), assetKind: ResourceAssetKind.SubAssets));
            Assert.That(defaultLease.Asset, Is.SameAs(text));
            Assert.That(otherLease.Asset, Is.SameAs(otherText));
            Assert.That(objectLease.Asset, Is.SameAs(text));
            Assert.That(spriteLease.Asset, Is.SameAs(sprite));
            Assert.That(f.Service.TryGetSubSpriteAsset(sub, sprite.name, out var subSprite), Is.True);
            Assert.That(subSprite, Is.SameAs(sprite));
            Assert.That(f.Loader.Requests.Count, Is.EqualTo(5));
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(5));
            Assert.Throws<AssertionException>(() => f.Info("same"));
            Assert.Throws<AssertionException>(() => f.Loader.RequestFor("same"));
            Assert.That(f.TryFindInfo("same", out var otherInfo, "Other", typeof(TextAsset)), Is.True);
            Assert.That(otherInfo.DirectRefCount, Is.EqualTo(1));
            defaultLease.Dispose();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(otherLease.IsValid && objectLease.IsValid && spriteLease.IsValid && sub.IsValid, Is.True);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(4));
            otherLease.Dispose(); objectLease.Dispose(); spriteLease.Dispose(); f.Service.Release(sub);
            f.Service.UnloadUnusedAssets(true);
            f.AssertUnloaded("same");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void LateCompletionRetainsItsOriginalAssetAndCannotCompleteTheNewGeneration()
        {
            using var f = new ResourceFixture();
            var oldAsset = f.Text("a");
            f.Loader.CompleteImmediately = false;
            var oldHandle = f.Loader.LoadAsset(f.Service.DefaultPackageName, "a", typeof(TextAsset), false, 0);
            var oldRequest = f.Loader.RequestFor("a");
            oldHandle.Dispose();
            var newAsset = f.Keep(new TextAsset("new"));
            f.Loader.Assets["a"] = newAsset;
            using var newHandle = f.Loader.LoadAsset(f.Service.DefaultPackageName, "a", typeof(TextAsset), false, 0);
            var newRequest = f.Loader.RequestFor("a");
            Assert.That(newRequest, Is.Not.SameAs(oldRequest));
            f.Loader.Complete(oldRequest);
            Assert.That(oldRequest.Asset, Is.SameAs(oldAsset));
            Assert.That(newHandle.IsDone, Is.False);
            f.Loader.Complete(newRequest);
            Assert.That(newHandle.AssetObject, Is.SameAs(newAsset));
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
        }
    }
}
