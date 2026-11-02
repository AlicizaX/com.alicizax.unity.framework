#if UNITY_EDITOR
using System;
using System.Diagnostics;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceOwnerPerformanceTests
    {
        private const int Operations = 20000;
        private const int Samples = 7;
        private const double MaxScaleRatio = 3.0;

        private static double NanosecondsPerOperation(Action<int> workload)
        {
            workload(Operations);
            var results = new double[Samples];
            var clock = new Stopwatch();
            for (int sample = 0; sample < Samples; sample++)
            {
                clock.Restart();
                workload(Operations);
                clock.Stop();
                results[sample] = clock.Elapsed.TotalMilliseconds * 1000000.0 / Operations;
            }
            Array.Sort(results);
            return results[Samples / 2];
        }

        private static void AssertConstant(string name, double small, double large)
        {
            TestContext.WriteLine($"SCALE,{name},small={small:F1}ns,large={large:F1}ns,ratio={large / small:F2}");
            Assert.That(large / small, Is.LessThan(MaxScaleRatio), name + " must not grow with resident population.");
        }

        private static ResourceFixture Populate(int assets, int scopes, out AssetRef<TextAsset> key, out ResourceScope scope)
        {
            var f = new ResourceFixture();
            var template = f.Text("hot");
            for (int i = 0; i < assets; i++)
            {
                string location = "resident-" + i;
                f.Loader.Assets.Add(location, template);
                f.Service.App.Load<TextAsset>(location);
            }
            for (int i = 0; i < scopes; i++) f.Service.CreateScope().Load<TextAsset>("resident-" + (i % Math.Max(1, assets)));
            key = f.Service.Key<TextAsset>("hot");
            scope = f.Service.CreateScope();
            scope.Load(key);
            return f;
        }

        [Test]
        public void HitPathsStayConstantAsResidentPopulationGrows()
        {
            using var small = Populate(16, 16, out var smallKey, out var smallScope);
            using var large = Populate(16384, 4096, out var largeKey, out var largeScope);
            AssertConstant("lease-hit",
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) small.Service.LoadLease(smallKey).Dispose(); }),
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) large.Service.LoadLease(largeKey).Dispose(); }));
            AssertConstant("lease-hit-string",
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) small.Service.LoadLease<TextAsset>("hot").Dispose(); }),
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) large.Service.LoadLease<TextAsset>("hot").Dispose(); }));
            AssertConstant("scope-dedupe-hit",
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) smallScope.Load(smallKey); }),
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) largeScope.Load(largeKey); }));
            AssertConstant("scope-cycle",
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) { var s = small.Service.CreateScope(); s.Load(smallKey); s.Dispose(); } }),
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) { var s = large.Service.CreateScope(); s.Load(largeKey); s.Dispose(); } }));
        }

        [Test]
        public void MaintenanceTickStaysConstantAsIdleAndOwnerPopulationGrows()
        {
            using var small = new ResourceFixture();
            using var large = new ResourceFixture();
            Fill(small, 16, 64);
            Fill(large, 8192, 2048);
            AssertConstant("maintenance-tick",
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) small.Tick(); }),
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) large.Tick(); }));
        }

        private static void Fill(ResourceFixture f, int count, int owners)
        {
            f.Service.IdleAssetCapacity = count;
            f.Service.IdleAssetExpireTime = 1000f;
            var template = f.Text("template");
            for (int i = 0; i < count; i++)
            {
                string location = "idle-" + i;
                f.Loader.Assets.Add(location, template);
                f.Service.LoadLease<TextAsset>(location).Dispose();
            }
            for (int i = 0; i < owners; i++) f.Service.OwnerScope(f.GameObject("owner-" + i), ResourceScopeSource.Binding, null);
        }

        [Test]
        public void BindingReplaceStaysConstantAsBoundTargetsGrow()
        {
            using var small = new ResourceFixture();
            using var large = new ResourceFixture();
            var smallTarget = Bindings(small, 16, out var smallScope, out var smallA, out var smallB);
            var largeTarget = Bindings(large, 4096, out var largeScope, out var largeA, out var largeB);
            AssertConstant("binding-replace",
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) smallTarget.SetSprite(smallScope, (i & 1) == 0 ? smallA : smallB); }),
                NanosecondsPerOperation(n => { for (int i = 0; i < n; i++) largeTarget.SetSprite(largeScope, (i & 1) == 0 ? largeA : largeB); }));
        }

        private static SpriteRenderer Bindings(ResourceFixture f, int count, out ResourceScope scope, out AssetRef<Sprite> a, out AssetRef<Sprite> b)
        {
            f.Sprite("a");
            f.Sprite("b");
            a = f.Service.Key<Sprite>("a");
            b = f.Service.Key<Sprite>("b");
            f.Service.App.Load(a);
            f.Service.App.Load(b);
            scope = f.Service.CreateScope();
            for (int i = 0; i < count; i++) f.GameObject("bound-" + i).AddComponent<SpriteRenderer>().SetSprite(scope, a);
            var target = f.GameObject("hot").AddComponent<SpriteRenderer>();
            target.SetSprite(scope, a);
            return target;
        }

        [Test]
        public void PendingLoadsPollAndCompleteInOneTick()
        {
            using var f = new ResourceFixture();
            f.Loader.CompleteImmediately = false;
            var template = f.Text("template");
            for (int i = 0; i < 4096; i++)
            {
                string location = "pending-" + i;
                f.Loader.Assets.Add(location, template);
                f.Service.LoadLeaseAsync<TextAsset>(location);
            }
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) f.Tick();
            clock.Stop();
            double idleTick = clock.Elapsed.TotalMilliseconds * 1000.0;
            TestContext.WriteLine($"PERF,pending-poll-tick,4096,{idleTick / 1000:F2}us");
            foreach (var request in f.Loader.Requests) f.Loader.Complete(request);
            f.Tick();
            Assert.That(f.Service.GetStats().LoadingCount, Is.Zero);
            Assert.That(f.Service.GetStats().LeaseCount, Is.EqualTo(4096));
        }
    }
}
#endif
