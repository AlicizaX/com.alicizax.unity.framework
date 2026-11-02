using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceFixtureTests
    {
        [Test]
        public void MissingRecordCannotMasqueradeAsAReleasedRecord()
        {
            using var f = new ResourceFixture();
            Assert.Throws<AssertionException>(() => f.Info("missing"));
            Assert.That(f.TryFindInfo("missing", out _), Is.False);
            f.Text("held");
            using var lease = f.Service.LoadLease<TextAsset>("held");
            Assert.Throws<AssertionException>(() => f.AssertNoReferences("held"));
            Assert.Throws<AssertionException>(() => f.AssertReleased("held"));
        }

        [Test]
        public void SameLocationKeepsPackageTypeAndSubAssetRecordsIndependent()
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
            var scope = f.Service.CreateScope();
            var target = f.GameObject().AddComponent<SpriteRenderer>();
            target.SetSubSprite(scope, "same", sprite.name, ResourceBindingOptions.Synchronous);
            Assert.That(defaultLease.Asset, Is.SameAs(text));
            Assert.That(otherLease.Asset, Is.SameAs(otherText));
            Assert.That(objectLease.Asset, Is.SameAs(text));
            Assert.That(spriteLease.Asset, Is.SameAs(sprite));
            Assert.That(target.sprite, Is.SameAs(sprite));
            Assert.That(f.Loader.Requests.Count, Is.EqualTo(5));
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(5));
            Assert.Throws<AssertionException>(() => f.Info("same"));
            Assert.Throws<AssertionException>(() => f.Loader.RequestFor("same"));
            Assert.That(f.TryFindInfo("same", out var subInfo, typeof(Sprite), true), Is.True);
            Assert.That(subInfo.RefCount, Is.EqualTo(1));
            defaultLease.Dispose();
            f.Service.UnloadUnusedAssets(true);
            Assert.That(otherLease.IsValid && objectLease.IsValid && spriteLease.IsValid, Is.True);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(4));
            otherLease.Dispose();
            objectLease.Dispose();
            spriteLease.Dispose();
            scope.Dispose();
            f.Service.UnloadUnusedAssets(true);
            f.AssertReleased("same");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void ResultRequiresACompletedTask()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var task = f.Service.LoadLeaseAsync<TextAsset>("a");
            Assert.Throws<AssertionException>(() => f.Result(task));
            f.Loader.Complete("a");
            f.Tick();
            f.Result(task).Dispose();
        }

        [Test]
        public void TickAdvancesServiceTime()
        {
            using var f = new ResourceFixture();
            f.Service.IdleAssetExpireTime = 3f;
            f.Text("a");
            f.Service.LoadLease<TextAsset>("a").Dispose();
            f.Tick(2f);
            Assert.That(f.Info("a").IdleSeconds, Is.EqualTo(2f));
            f.Tick(1f);
            f.AssertReleased("a");
        }

        [Test]
        public void DisposeReportsLeakedBackendHandles()
        {
            var f = new ResourceFixture();
            f.Text("a");
            var leaked = f.Loader.LoadAsset(f.Service.DefaultPackageName, "a", typeof(TextAsset), true);
            Assert.Throws<AssertionException>(() => f.Dispose());
            Assert.That(leaked.IsValid, Is.False, "Dispose still releases handles after reporting the leak.");
        }
    }
}
