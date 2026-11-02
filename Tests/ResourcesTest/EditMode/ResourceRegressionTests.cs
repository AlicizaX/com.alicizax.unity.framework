using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceRegressionTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [Test]
        public void KeysInternByPackageLocationTypeAndSubAssets()
        {
            var s = f.Service;
            int text = s.KeyId<TextAsset>("a", "");
            Assert.That(s.KeyId<TextAsset>("a", s.DefaultPackageName), Is.EqualTo(text));
            Assert.That(s.KeyId<TextAsset>(new string('a', 1), ""), Is.EqualTo(text));
            Assert.That(s.Key<TextAsset>("a").Id, Is.EqualTo(text));
            int[] ids = { text, s.KeyId<TextAsset>("a", "Other"), s.KeyId<Sprite>("a", ""), s.SubAssetsKeyId("a", ""), s.KeyId<TextAsset>("b", "") };
            Assert.That(ids, Is.Unique);
            Assert.That(s.AssetCount, Is.EqualTo(ids.Length));
            Assert.That(f.Loader.Loads, Is.Zero, "Interning a key never touches the backend.");
            Assert.That(default(AssetRef<TextAsset>).IsValid, Is.False);
            Assert.That(s.Key<TextAsset>("a").IsValid, Is.True);
        }

        [Test]
        public void AssetRefAndStringLoadsResolveToTheSameRecord()
        {
            var asset = f.Text("a");
            var key = f.Service.Key<TextAsset>("a");
            using var byKey = f.Service.LoadLease(key);
            using var byString = f.Service.LoadLease<TextAsset>("a");
            Assert.That(byKey.Asset, Is.SameAs(asset));
            Assert.That(byString.Asset, Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(2));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
        }

        [Test]
        public void ReleasedAssetStaysIdleAndReacquiresWithoutBackendLoad()
        {
            var asset = f.Text("a");
            f.Service.LoadLease<TextAsset>("a").Dispose();
            var info = f.Info("a");
            Assert.That(info.State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(info.RefCount, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            using var again = f.Service.LoadLease<TextAsset>("a");
            Assert.That(again.Asset, Is.SameAs(asset));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Active));
            f.AssertInvariants();
        }

        [Test]
        public void IdleCapacityEvictsOldestFirst()
        {
            f.Service.IdleAssetCapacity = 2;
            foreach (string key in new[] { "a", "b", "c" })
            {
                f.Text(key);
                f.Service.LoadLease<TextAsset>(key).Dispose();
            }
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Released));
            Assert.That(f.Info("b").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Info("c").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(2));
            f.Service.LoadLease<TextAsset>("b").Dispose();
            f.Text("d");
            f.Service.LoadLease<TextAsset>("d").Dispose();
            Assert.That(f.Info("c").State, Is.EqualTo(ResourceAssetState.Released), "Reacquiring b moved it behind c.");
            Assert.That(f.Info("b").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Info("d").State, Is.EqualTo(ResourceAssetState.Idle));
            f.AssertInvariants();
        }

        [Test]
        public void ShrinkingCapacityReleasesOldestIdleAssets()
        {
            foreach (string key in new[] { "a", "b", "c" })
            {
                f.Text(key);
                f.Service.LoadLease<TextAsset>(key).Dispose();
            }
            f.Service.IdleAssetCapacity = 1;
            f.AssertReleased("a");
            f.AssertReleased("b");
            Assert.That(f.Info("c").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            f.AssertInvariants();
        }

        [Test]
        public void ZeroCapacityReleasesOnLastReferenceAndSyncLoadStillReturnsTheAsset()
        {
            var asset = f.Text("a");
            f.Service.IdleAssetCapacity = 0;
            var lease = f.Service.LoadLease<TextAsset>("a");
            Assert.That(lease.IsValid, Is.True);
            Assert.That(lease.Asset, Is.SameAs(asset));
            lease.Dispose();
            f.AssertReleased("a");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            var scope = f.Service.CreateScope();
            Assert.That(scope.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            scope.Dispose();
            f.AssertReleased("a");
            Assert.That(f.Loader.Loads, Is.EqualTo(2));
        }

        [Test]
        public void ExpiryReleasesOnlyExpiredHeadsWithinTheTickBudget()
        {
            f.Service.IdleAssetExpireTime = 10f;
            f.Service.IdleReleaseCountPerTick = 2;
            foreach (string key in new[] { "a", "b", "c" })
            {
                f.Text(key);
                f.Service.LoadLease<TextAsset>(key).Dispose();
            }
            f.Tick(5f);
            f.Text("d");
            f.Service.LoadLease<TextAsset>("d").Dispose();
            f.Tick(4f);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(4));
            f.Tick(1f);
            f.AssertReleased("a");
            f.AssertReleased("b");
            Assert.That(f.Info("c").State, Is.EqualTo(ResourceAssetState.Idle), "Budget limits releases per tick.");
            f.Tick();
            f.AssertReleased("c");
            Assert.That(f.Info("d").State, Is.EqualTo(ResourceAssetState.Idle));
            f.Tick(5f);
            f.AssertReleased("d");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void ZeroBudgetDisablesExpiryAndUnloadReleasesAllIdle()
        {
            f.Service.IdleAssetExpireTime = 0f;
            f.Service.IdleReleaseCountPerTick = 0;
            f.Text("a");
            f.Text("b");
            f.Service.LoadLease<TextAsset>("a").Dispose();
            f.Service.LoadLease<TextAsset>("b").Dispose();
            f.Tick(100f);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(2));
            f.Service.UnloadUnusedAssets(false);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(2));
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void NegativeCacheSettingsClampToZero()
        {
            f.Service.IdleAssetExpireTime = -1f;
            f.Service.IdleAssetCapacity = -1;
            f.Service.IdleReleaseCountPerTick = -1;
            Assert.That(f.Service.IdleAssetExpireTime, Is.Zero);
            Assert.That(f.Service.IdleAssetCapacity, Is.Zero);
            Assert.That(f.Service.IdleReleaseCountPerTick, Is.Zero);
        }

        [Test]
        public void ActiveAssetsNeverExpire()
        {
            f.Service.IdleAssetExpireTime = 0f;
            f.Text("a");
            using var lease = f.Service.LoadLease<TextAsset>("a");
            f.Tick(1000f);
            f.Service.UnloadUnusedAssets(true);
            Assert.That(lease.IsValid, Is.True);
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Active));
        }
    }
}
