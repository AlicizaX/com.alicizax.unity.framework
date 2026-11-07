using System;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceDiagnosticsTests
    {
        private ResourceFixture f;

        [SetUp] public void SetUp() => f = new ResourceFixture();

        [TearDown] public void TearDown() => f.Dispose();

        private ResourceScopeInfo ScopeInfo(int index)
        {
            var infos = new ResourceScopeInfo[64];
            int count = f.Service.GetScopeInfos(infos, 0);
            for (int i = 0; i < count; i++)
                if (infos[i].Index == index) return infos[i];
            Assert.Fail("Scope " + index + " is not live.");
            return default;
        }

        [Test]
        public void ScopeSourcesIdentifyWhereEachScopeCameFrom()
        {
            f.Prefab("ui/Home");
            f.Sprite("icon");
            AppServices.GetOrCreateRegistry();
            AppServices.Register<IResourceService>(f.Service);
            GameObject instance = null;
            try
            {
                instance = f.Service.Instantiate("ui/Home");
                var image = f.GameObject("icon").AddComponent<Image>();
                image.SetSprite("icon", ResourceBindingOptions.Synchronous);
                var named = f.Service.CreateScope("Panel");
                var unnamed = f.Service.CreateScope();

                ResourceScopeInfo app = ScopeInfo(f.Service.App.Index);
                Assert.That(app.Source, Is.EqualTo(ResourceScopeSource.Manual));
                Assert.That(app.Name, Is.EqualTo("App"));

                ResourceScopeInfo instanced = ScopeInfo(instance.GetComponent<ResourceOwner>().Scope.Index);
                Assert.That(instanced.Source, Is.EqualTo(ResourceScopeSource.Instance));
                Assert.That(instanced.Name, Is.EqualTo("ui/Home"));
                Assert.That(instanced.OwnerObject, Is.SameAs(instance));
                Assert.That(instanced.EntryCount, Is.EqualTo(1));

                ResourceScopeInfo bound = ScopeInfo(image.GetComponent<ResourceOwner>().Scope.Index);
                Assert.That(bound.Source, Is.EqualTo(ResourceScopeSource.Binding));
                Assert.That(bound.Name, Is.Null);
                Assert.That(bound.OwnerObject, Is.SameAs(image.gameObject));
                Assert.That(bound.BindingCount, Is.EqualTo(1));

                Assert.That(ScopeInfo(named.Index).Source, Is.EqualTo(ResourceScopeSource.Manual));
                Assert.That(ScopeInfo(named.Index).Name, Is.EqualTo("Panel"));
                Assert.That(ScopeInfo(unnamed.Index).Source, Is.EqualTo(ResourceScopeSource.Manual));
                Assert.That(ScopeInfo(unnamed.Index).Name, Is.Null);
                named.Dispose();
                unnamed.Dispose();
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AppServices.Shutdown();
            }
        }

        [Test]
        public void ReusedScopeSlotDoesNotInheritTheSourceOfItsPreviousOwner()
        {
            f.Prefab("ui/Home");
            GameObject instance = f.Service.Instantiate("ui/Home");
            int index = instance.GetComponent<ResourceOwner>().Scope.Index;
            Object.DestroyImmediate(instance);
            f.Tick();
            var reused = f.Service.CreateScope();
            Assert.That(reused.Index, Is.EqualTo(index));
            Assert.That(ScopeInfo(index).Source, Is.EqualTo(ResourceScopeSource.Manual));
            Assert.That(ScopeInfo(index).Name, Is.Null);
            reused.Dispose();
        }

        [Test]
        public void RequestCountersClassifyEveryAcquisition()
        {
            f.Text("a");
            f.Text("b");
            f.Prefab("p");
            f.Sprite("s");
            f.Sprite("t");
            ResourceStats empty = f.Service.GetStats();
            Assert.That(empty.HitCount + empty.IdleHitCount + empty.JoinCount + empty.MissCount, Is.Zero);

            var first = f.Service.LoadLease<TextAsset>("a");
            var second = f.Service.LoadLease<TextAsset>("a");
            first.Dispose();
            second.Dispose();
            f.Service.LoadLease<TextAsset>("a").Dispose();
            AssertCounters(hits: 1, idleHits: 1, joins: 0, misses: 1);

            f.Loader.CompleteImmediately = false;
            var asyncFirst = f.Service.LoadLeaseAsync<TextAsset>("b");
            var asyncSecond = f.Service.LoadLeaseAsync<TextAsset>("b");
            var sync = f.Service.LoadLease<TextAsset>("b");
            f.Result(asyncFirst).Dispose();
            f.Result(asyncSecond).Dispose();
            sync.Dispose();
            AssertCounters(hits: 1, idleHits: 1, joins: 2, misses: 2);

            var scope = f.Service.CreateScope("counters");
            scope.Load<TextAsset>("a");
            scope.Load<TextAsset>("a");
            f.Result(scope.LoadAsync<TextAsset>("a"));
            AssertCounters(hits: 3, idleHits: 2, joins: 2, misses: 2);

            GameObject one = f.Service.Instantiate("p");
            GameObject two = f.Service.Instantiate("p");
            GameObject three = f.Result(f.Service.InstantiateAsync("p"));
            AssertCounters(hits: 5, idleHits: 2, joins: 2, misses: 3);
            Assert.That(f.Info("p").RefCount, Is.EqualTo(3), "Each instance holds exactly one reference.");

            var target = f.GameObject().AddComponent<SpriteRenderer>();
            target.SetSprite(scope, "s", ResourceBindingOptions.Synchronous);
            target.SetSprite(scope, "t", ResourceBindingOptions.Synchronous);
            target.SetSprite(scope, "s");
            AssertCounters(hits: 5, idleHits: 3, joins: 2, misses: 5);

            f.Loader.ThrowOnLoad = true;
            Assert.Throws<InvalidOperationException>(() => f.Service.LoadLease<TextAsset>("missing"));
            f.Loader.ThrowOnLoad = false;
            AssertCounters(hits: 5, idleHits: 3, joins: 2, misses: 6);
            Assert.That(f.Service.GetStats().MissCount, Is.EqualTo(f.Loader.Loads), "Every backend request is counted as exactly one miss.");

            Object.DestroyImmediate(one);
            Object.DestroyImmediate(two);
            Object.DestroyImmediate(three);
            scope.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void ForceUnloadTurnsTheNextRequestIntoAMiss()
        {
            f.Text("a");
            f.Service.LoadLease<TextAsset>("a").Dispose();
            f.Service.ForceUnloadAllAssets();
            f.Tick();
            f.Service.LoadLease<TextAsset>("a").Dispose();
            AssertCounters(hits: 0, idleHits: 0, joins: 0, misses: 2);
        }

        private void AssertCounters(long hits, long idleHits, long joins, long misses)
        {
            ResourceStats stats = f.Service.GetStats();
            Assert.That(stats.HitCount, Is.EqualTo(hits), "HitCount");
            Assert.That(stats.IdleHitCount, Is.EqualTo(idleHits), "IdleHitCount");
            Assert.That(stats.JoinCount, Is.EqualTo(joins), "JoinCount");
            Assert.That(stats.MissCount, Is.EqualTo(misses), "MissCount");
        }
    }
}
