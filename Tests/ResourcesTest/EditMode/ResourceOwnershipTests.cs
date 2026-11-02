using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceOwnershipTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [Test]
        public void ScopeDeduplicatesRepeatedLoadsAndReleasesOnDispose()
        {
            var asset = f.Text("a");
            var first = f.Service.CreateScope("first");
            var second = f.Service.CreateScope("second");
            Assert.That(first.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(first.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(first.Load(f.Service.Key<TextAsset>("a")), Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            Assert.That(second.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(2));
            Assert.That(f.Service.GetStats().ScopeEntryCount, Is.EqualTo(2));
            first.Dispose();
            first.Dispose();
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            second.Dispose();
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            f.AssertInvariants();
        }

        [Test]
        public void ManyAssetsAcrossScopesKeepIndependentChains()
        {
            const int count = 300;
            var first = f.Service.CreateScope();
            var second = f.Service.CreateScope();
            for (int i = 0; i < count; i++)
            {
                string key = "owned-" + i;
                var asset = f.Text(key);
                Assert.That(first.Load<TextAsset>(key), Is.SameAs(asset));
                Assert.That(second.Load<TextAsset>(key), Is.SameAs(asset));
            }
            f.AssertInvariants();
            first.Dispose();
            for (int i = 0; i < count; i++) Assert.That(f.Info("owned-" + i).RefCount, Is.EqualTo(1));
            var third = f.Service.CreateScope();
            for (int i = count - 1; i >= 0; i--) third.Load<TextAsset>("owned-" + i);
            second.Dispose();
            for (int i = 0; i < count; i++) Assert.That(f.Info("owned-" + i).RefCount, Is.EqualTo(1));
            third.Dispose();
            for (int i = 0; i < count; i++) Assert.That(f.Info("owned-" + i).RefCount, Is.Zero);
            Assert.That(f.Loader.Loads, Is.EqualTo(count));
            f.AssertInvariants();
        }

        [Test]
        public void DisposedScopeRejectsLoadsAndSlotReuseDoesNotReviveIt()
        {
            f.Text("a");
            var stale = f.Service.CreateScope();
            stale.Dispose();
            Assert.That(stale.IsAlive, Is.False);
            Assert.That(stale.Load<TextAsset>("a"), Is.Null);
            Assert.That(f.Loader.Loads, Is.Zero);
            var reused = f.Service.CreateScope();
            Assert.That(reused.Index, Is.EqualTo(stale.Index));
            Assert.That(reused.Generation, Is.Not.EqualTo(stale.Generation));
            Assert.That(reused.Load<TextAsset>("a"), Is.Not.Null);
            stale.Dispose();
            Assert.That(reused.IsAlive, Is.True);
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            Assert.That(stale.LoadAsync<TextAsset>("a").GetAwaiter().GetResult(), Is.Null);
            reused.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void ScopeAsyncLoadHitsCompleteSynchronously()
        {
            var asset = f.Text("a");
            var scope = f.Service.CreateScope();
            var miss = scope.LoadAsync<TextAsset>("a");
            Assert.That(miss.GetAwaiter().IsCompleted, Is.False, "A miss completes on the next tick.");
            f.Tick();
            Assert.That(f.Result(miss), Is.SameAs(asset));
            var dedupe = scope.LoadAsync<TextAsset>("a");
            Assert.That(dedupe.GetAwaiter().IsCompleted, Is.True);
            Assert.That(f.Result(dedupe), Is.SameAs(asset));
            var other = f.Service.CreateScope();
            var hit = other.LoadAsync(f.Service.Key<TextAsset>("a"));
            Assert.That(f.Result(hit), Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(2));
            scope.Dispose();
            other.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void LeaseCopiesShareOneReferenceAndDisposeIsIdempotent()
        {
            f.Text("a");
            var lease = f.Service.LoadLease<TextAsset>("a");
            var copy = lease;
            Assert.That(f.Service.GetStats().LeaseCount, Is.EqualTo(1));
            copy.Dispose();
            Assert.That(lease.IsValid, Is.False);
            Assert.That(copy.IsValid, Is.False);
            lease.Dispose();
            copy.Dispose();
            Assert.That(f.Info("a").RefCount, Is.Zero);
            Assert.That(f.Service.GetStats().LeaseCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void StaleLeaseCannotReleaseTheLeaseThatReusedItsEntry()
        {
            f.Text("a");
            var stale = f.Service.LoadLease<TextAsset>("a");
            stale.Dispose();
            var current = f.Service.LoadLease<TextAsset>("a");
            stale.Dispose();
            Assert.That(current.IsValid, Is.True);
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            current.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void DefaultHandlesAreInertValues()
        {
            AssetLease<TextAsset> lease = default;
            ResourceScope scope = default;
            Assert.That(lease.IsValid, Is.False);
            Assert.That(lease.Asset, Is.Null);
            Assert.That(scope.IsAlive, Is.False);
            Assert.DoesNotThrow(() => lease.Dispose());
            Assert.DoesNotThrow(() => scope.Dispose());
        }

        [Test]
        public void ForceUnloadAllInvalidatesReferencesButKeepsScopesUsable()
        {
            var asset = f.Text("a");
            f.Text("b");
            var scope = f.Service.CreateScope();
            scope.Load<TextAsset>("a");
            var lease = f.Service.LoadLease<TextAsset>("b");
            f.Service.ForceUnloadAllAssets();
            Assert.That(lease.IsValid, Is.False);
            Assert.That(scope.IsAlive, Is.True);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.AssertReleased("a");
            f.AssertReleased("b");
            lease.Dispose();
            Assert.That(f.Service.CanLoadResources, Is.True, "Without initialized packages the reset completes immediately.");
            Assert.That(scope.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            scope.Dispose();
            Assert.That(f.Info("a").RefCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void AppScopeLivesUntilShutdown()
        {
            var asset = f.Text("a");
            var app = f.Service.App;
            Assert.That(app.IsAlive, Is.True);
            Assert.That(app.Load<TextAsset>("a"), Is.SameAs(asset));
            f.Service.ForceUnloadAllAssets();
            Assert.That(app.IsAlive, Is.True);
            app.Load<TextAsset>("a");
            f.Service.Shutdown();
            Assert.That(app.IsAlive, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ShutdownRejectsEveryLoadEntry()
        {
            f.Text("a");
            var scope = f.Service.CreateScope();
            f.Service.Shutdown();
            Assert.That(f.Service.CanLoadResources, Is.False);
            Assert.That(scope.IsAlive, Is.False);
            Assert.That(f.Service.LoadLease<TextAsset>("a").IsValid, Is.False);
            Assert.That(f.Service.LoadLeaseAsync<TextAsset>("a").GetAwaiter().GetResult().IsValid, Is.False);
            Assert.That(f.Service.CreateScope().Load<TextAsset>("a"), Is.Null);
            Assert.That(f.Service.Instantiate("a"), Is.Null);
            Assert.That(f.Loader.Loads, Is.Zero);
        }

        [Test]
        public void UnavailableLoaderFailsWithoutStartingLoads()
        {
            f.Text("a");
            f.Loader.IsAvailable = false;
            var scope = f.Service.CreateScope();
            Assert.That(f.Service.CanLoadResources, Is.False);
            Assert.That(f.Service.LoadLease<TextAsset>("a").IsValid, Is.False);
            Assert.That(f.Service.LoadLeaseAsync<TextAsset>("a").GetAwaiter().GetResult().IsValid, Is.False);
            Assert.That(scope.Load<TextAsset>("a"), Is.Null);
            Assert.That(scope.LoadAsync<TextAsset>("a").GetAwaiter().GetResult(), Is.Null);
            Assert.That(f.Loader.Loads, Is.Zero);
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Released));
            f.Loader.IsAvailable = true;
            Assert.That(scope.Load<TextAsset>("a"), Is.Not.Null);
            scope.Dispose();
        }

        [Test]
        public void OwnerScopeIsCreatedOnceAndRecreatedAfterDispose()
        {
            var go = f.GameObject();
            var scope = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            var owner = go.GetComponent<ResourceOwner>();
            Assert.That(owner, Is.Not.Null);
            Assert.That(owner.Scope.Index, Is.EqualTo(scope.Index));
            Assert.That(f.Service.OwnerScope(go, ResourceScopeSource.Binding, null).Generation, Is.EqualTo(scope.Generation));
            Assert.That(go.GetComponents<ResourceOwner>().Length, Is.EqualTo(1));
            scope.Dispose();
            var next = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            Assert.That(next.IsAlive, Is.True);
            Assert.That(next.Generation, Is.Not.EqualTo(scope.Generation));
            Assert.That(owner.Scope.Generation, Is.EqualTo(next.Generation));
            next.Dispose();
        }

        [Test]
        public void OwnerScopeBelongsToTheRequestingService()
        {
            using var other = new ResourceFixture();
            var go = f.GameObject();
            var mine = f.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            var theirs = other.Service.OwnerScope(go, ResourceScopeSource.Binding, null);
            Assert.That(ReferenceEquals(theirs.Service, other.Service), Is.True);
            Assert.That(mine.IsAlive, Is.True);
            Assert.That(theirs.IsAlive, Is.True);
            mine.Dispose();
            theirs.Dispose();
        }

        [Test]
        public void DestroyedOwnerScopesAreReleasedByTheBudgetedSweep()
        {
            const int count = 100;
            f.Text("a");
            var objects = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                objects[i] = f.GameObject("owner-" + i);
                f.Service.OwnerScope(objects[i], ResourceScopeSource.Binding, null).Load<TextAsset>("a");
            }
            Assert.That(f.Info("a").RefCount, Is.EqualTo(count));
            for (int i = 0; i < count; i++) Object.DestroyImmediate(objects[i]);
            int scopeSlots = f.Service.ScopeSlotCount;
            int ticks = (scopeSlots + 31) / 32;
            for (int i = 0; i < ticks; i++) f.Tick();
            Assert.That(f.Info("a").RefCount, Is.Zero);
            Assert.That(f.Service.GetStats().ScopeCount, Is.EqualTo(1), "Only the App scope survives.");
            f.AssertInvariants();
        }

        [Test]
        public void NamedScopesAppearInDiagnostics()
        {
            f.Text("a");
            var scope = f.Service.CreateScope("Diagnostics");
            scope.Load<TextAsset>("a");
            var infos = new ResourceScopeInfo[8];
            int count = f.Service.GetScopeInfos(infos, 0);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (infos[i].Name != "Diagnostics") continue;
                found = true;
                Assert.That(infos[i].EntryCount, Is.EqualTo(1));
                Assert.That(infos[i].Generation, Is.EqualTo(scope.Generation));
                Assert.That(infos[i].HasOwner, Is.False);
            }
            Assert.That(found, Is.True);
            scope.Dispose();
        }
    }
}
