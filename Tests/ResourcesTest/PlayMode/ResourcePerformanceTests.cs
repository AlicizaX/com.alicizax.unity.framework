#if UNITY_EDITOR
using System;
using System.Collections;
using System.Threading;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourcePerformanceTests
    {
        private static readonly int[] Counts = { 100, 1000, 10000 };

        private static void Zero(AllocationSample sample)
        {
            Assert.That(sample.Bytes, Is.Zero, "Steady state must not allocate.");
        }

        private static void FrameworkZero(AllocationSample sample)
        {
            Assert.That(sample.Bytes - sample.BackendBytes, Is.Zero, "Only the controlled backend may allocate.");
        }

        [UnityTest]
        public IEnumerator IndexChurnAllocations()
        {
            foreach (int count in Counts)
            {
                var map = new ResourceUlongIntMap();
                map.ReserveCapacity(16);
                int removed = 0;
                yield return AllocationCapture.Measure("index-churn", count, () =>
                {
                    for (ulong i = 1; i <= (ulong)count; i++)
                    {
                        map.Set(i, 1);
                        if (map.Remove(i)) removed++;
                    }
                }, Zero);
                Assert.That(removed, Is.EqualTo(count));
                Assert.That(map.Count, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator CachedLoadPathsDoNotAllocate()
        {
            using var f = new ResourceFixture();
            string location = "a";
            f.Text(location);
            string package = f.Service.DefaultPackageName;
            var key = f.Service.Key<TextAsset>(location);
            using var held = f.Service.LoadLease(key);
            var scope = f.Service.CreateScope();
            scope.Load(key);
            var paths = new (string Name, Action<int> Run)[]
            {
                ("lease-hit-ref", n => { for (int i = 0; i < n; i++) f.Service.LoadLease(key).Dispose(); }),
                ("lease-hit-string", n => { for (int i = 0; i < n; i++) f.Service.LoadLease<TextAsset>(location, package).Dispose(); }),
                ("lease-async-hit", n => { for (int i = 0; i < n; i++) f.Service.LoadLeaseAsync(key).GetAwaiter().GetResult().Dispose(); }),
                ("scope-hit-dedupe", n => { for (int i = 0; i < n; i++) scope.Load(key); }),
                ("scope-hit-string", n => { for (int i = 0; i < n; i++) scope.Load<TextAsset>(location); }),
                ("scope-async-hit", n => { for (int i = 0; i < n; i++) scope.LoadAsync(key).GetAwaiter().GetResult(); }),
                ("scope-cycle", n =>
                {
                    for (int i = 0; i < n; i++)
                    {
                        var temporary = f.Service.CreateScope();
                        temporary.Load(key);
                        temporary.Dispose();
                    }
                }),
            };
            foreach (var path in paths)
            {
                yield return AllocationCapture.Measure(path.Name + "-first-use", 1, () => path.Run(1), _ => { });
                foreach (int count in Counts)
                    yield return AllocationCapture.Measure(path.Name, count, () => path.Run(count), Zero);
            }
            Assert.That(f.Info(location).RefCount, Is.EqualTo(2));
            scope.Dispose();
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator BindingSteadyStateDoesNotAllocate()
        {
            using var f = new ResourceFixture();
            f.Sprite("a");
            f.Sprite("b");
            f.Material("m1");
            f.Material("m2");
            var a = f.Service.Key<Sprite>("a");
            var b = f.Service.Key<Sprite>("b");
            using var heldA = f.Service.LoadLease(a);
            using var heldB = f.Service.LoadLease(b);
            using var heldM1 = f.Service.LoadLease<Material>("m1");
            using var heldM2 = f.Service.LoadLease<Material>("m2");
            var scope = f.Service.CreateScope();
            var target = f.GameObject().AddComponent<SpriteRenderer>();
            var renderer = f.GameObject().AddComponent<MeshRenderer>();
            string spriteLocation = "a";
            string firstMaterial = "m1";
            string secondMaterial = "m2";
            string none = string.Empty;
            target.SetSprite(scope, a);
            renderer.SetSharedMaterial(scope, "m1");
            foreach (int count in Counts)
            {
                yield return AllocationCapture.Measure("binding-replace", count, () =>
                {
                    for (int i = 0; i < count; i++) target.SetSprite(scope, (i & 1) == 0 ? b : a);
                }, Zero);
                yield return AllocationCapture.Measure("binding-string-hit", count, () =>
                {
                    for (int i = 0; i < count; i++) target.SetSprite(scope, spriteLocation);
                }, Zero);
                yield return AllocationCapture.Measure("binding-clear-rebind", count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        target.SetSprite(scope, none);
                        target.SetSprite(scope, a);
                    }
                }, Zero);
                yield return AllocationCapture.Measure("binding-shared-material-replace", count, () =>
                {
                    for (int i = 0; i < count; i++) renderer.SetSharedMaterial(scope, (i & 1) == 0 ? secondMaterial : firstMaterial);
                }, Zero);
                Assert.That(f.Service.Bindings.Count, Is.EqualTo(2));
            }
            scope.Dispose();
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator MaterialInstanceBindingAddsNothingBeyondUnityInstantiate()
        {
            using var f = new ResourceFixture();
            var material = f.Material("m");
            using var held = f.Service.LoadLease<Material>("m");
            var scope = f.Service.CreateScope();
            var renderer = f.GameObject().AddComponent<MeshRenderer>();
            string location = "m";
            renderer.SetMaterialInstance(scope, location);
            const int count = 1000;
            AllocationSample baseline = default;
            yield return AllocationCapture.Measure("unity-instantiate-destroy", count, () =>
            {
                for (int i = 0; i < count; i++) Object.Destroy(Object.Instantiate(material));
            }, sample => baseline = sample);
            yield return AllocationCapture.Measure("binding-material-instance-replace", count, () =>
            {
                for (int i = 0; i < count; i++) renderer.SetMaterialInstance(scope, location);
            }, sample => Assert.That(sample.Bytes, Is.LessThanOrEqualTo(baseline.Bytes)));
            scope.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MissCyclesAllocateOnlyInTheBackend()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Sprite("s");
            var key = f.Service.Key<TextAsset>("a");
            var sprite = f.Service.Key<Sprite>("s");
            var bindingScope = f.Service.CreateScope();
            var target = f.GameObject().AddComponent<SpriteRenderer>();
            yield return AllocationCapture.Measure("first-async-lease-miss", 1, () => AsyncLeaseMiss(f, key), _ => { });
            yield return AllocationCapture.Measure("first-scope-async-miss", 1, () => ScopeAsyncMiss(f, key), _ => { });
            yield return AllocationCapture.Measure("first-binding-miss", 1, () => BindingMiss(f, bindingScope, target, sprite), _ => { });
            foreach (int count in Counts)
            {
                yield return AllocationCapture.Measure("sync-miss", count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        f.Service.LoadLease(key).Dispose();
                        f.Service.UnloadUnusedAssets(true);
                    }
                }, FrameworkZero);
                yield return AllocationCapture.Measure("async-lease-miss", count, () =>
                {
                    for (int i = 0; i < count; i++) AsyncLeaseMiss(f, key);
                }, FrameworkZero);
                yield return AllocationCapture.Measure("scope-async-miss", count, () =>
                {
                    for (int i = 0; i < count; i++) ScopeAsyncMiss(f, key);
                }, FrameworkZero);
                yield return AllocationCapture.Measure("binding-miss", count, () =>
                {
                    for (int i = 0; i < count; i++) BindingMiss(f, bindingScope, target, sprite);
                }, FrameworkZero);
            }
            bindingScope.Dispose();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.AssertInvariants();
        }

        private static void AsyncLeaseMiss(ResourceFixture f, AssetRef<TextAsset> key)
        {
            var task = f.Service.LoadLeaseAsync(key);
            f.Tick();
            task.GetAwaiter().GetResult().Dispose();
            f.Service.UnloadUnusedAssets(true);
        }

        private static void ScopeAsyncMiss(ResourceFixture f, AssetRef<TextAsset> key)
        {
            var scope = f.Service.CreateScope();
            var task = scope.LoadAsync(key);
            f.Tick();
            task.GetAwaiter().GetResult();
            scope.Dispose();
            f.Service.UnloadUnusedAssets(true);
        }

        private static void BindingMiss(ResourceFixture f, ResourceScope scope, SpriteRenderer target, AssetRef<Sprite> sprite)
        {
            target.SetSprite(scope, sprite);
            f.Tick();
            target.SetSprite(scope, string.Empty);
            f.Service.UnloadUnusedAssets(true);
        }

        [UnityTest]
        public IEnumerator CancellationCycleAllocatesOnlyOutsideTheFramework()
        {
            using var f = new ResourceFixture();
            f.Text("a");
            f.Loader.CompleteImmediately = false;
            var key = f.Service.Key<TextAsset>("a");
            var sources = new CancellationTokenSource[Counts[Counts.Length - 1] + 1];
            for (int i = 0; i < sources.Length; i++) sources[i] = new CancellationTokenSource();
            int next = 0;
            yield return AllocationCapture.Measure("first-cancel", 1, () => CancelCycle(f, key, sources[next++]), _ => { });
            foreach (int count in Counts)
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    sources[i].Dispose();
                    sources[i] = new CancellationTokenSource();
                }
                next = 0;
                yield return AllocationCapture.Measure("cancel-cycle", count, () =>
                {
                    for (int i = 0; i < count; i++) CancelCycle(f, key, sources[next++]);
                }, FrameworkZero);
            }
            foreach (var source in sources) source.Dispose();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.AssertInvariants();
        }

        private static void CancelCycle(ResourceFixture f, AssetRef<TextAsset> key, CancellationTokenSource source)
        {
            var task = f.Service.LoadLeaseAsync(key, source.Token);
            using (ControlledLoader.BackendMarker.Auto()) source.Cancel();
            f.Tick();
            task.GetAwaiter().GetResult();
            f.Loader.CompleteLast();
            f.Tick();
            f.Service.UnloadUnusedAssets(true);
        }

        [UnityTest]
        public IEnumerator MaintenanceTicksDoNotAllocate()
        {
            using var f = new ResourceFixture();
            for (int i = 0; i < 64; i++)
            {
                string location = "idle-" + i;
                f.Text(location);
                f.Service.LoadLease<TextAsset>(location).Dispose();
            }
            for (int i = 0; i < 64; i++) f.Service.OwnerScope(f.GameObject("owner-" + i), ResourceScopeSource.Binding, null);
            f.Text("e");
            var expiring = f.Service.Key<TextAsset>("e");
            f.Service.IdleAssetExpireTime = 1000f;
            foreach (int count in Counts)
            {
                yield return AllocationCapture.Measure("maintenance-tick", count, () =>
                {
                    for (int i = 0; i < count; i++) f.Tick();
                }, Zero);
                yield return AllocationCapture.Measure("idle-expiry-cycle", count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        f.Service.LoadLease(expiring).Dispose();
                        f.Tick(2000f);
                    }
                }, FrameworkZero);
                yield return AllocationCapture.Measure("force-unload-all", count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        f.Service.LoadLease(expiring).Dispose();
                        f.Service.ForceUnloadAllAssets();
                    }
                }, FrameworkZero);
            }
            f.AssertInvariants();
        }

        [UnityTest]
        public IEnumerator FirstUseNewKeysAndGrowthAreMeasuredSeparately()
        {
            ResourceService service = null;
            yield return AllocationCapture.Measure("service-construct", 1, () => service = new ResourceService(), _ => { });
            service.Shutdown();
            using var f = new ResourceFixture();
            var locations = new string[1024];
            for (int i = 0; i < locations.Length; i++) locations[i] = "new-key-" + i;
            string defaultPackage = string.Empty;
            yield return AllocationCapture.Measure("new-key-first", 1, () => f.Service.KeyId<TextAsset>(locations[0], defaultPackage), _ => { });
            yield return AllocationCapture.Measure("new-key-batch", locations.Length - 1, () =>
            {
                for (int i = 1; i < locations.Length; i++) f.Service.KeyId<TextAsset>(locations[i], defaultPackage);
            }, _ => { });
            yield return AllocationCapture.Measure("known-key-lookup", locations.Length, () =>
            {
                for (int i = 0; i < locations.Length; i++) f.Service.KeyId<TextAsset>(locations[i], defaultPackage);
            }, Zero);
            f.Text("a");
            var key = f.Service.Key<TextAsset>("a");
            var leases = new AssetLease<TextAsset>[1024];
            var scopes = new ResourceScope[1024];
            foreach (int count in new[] { 16, 256, 1024 })
            {
                yield return AllocationCapture.Measure("lease-capacity-growth", count, () => LeaseBatch(f, key, leases, count), _ => { });
                yield return AllocationCapture.Measure("lease-capacity-reuse", count, () => LeaseBatch(f, key, leases, count), Zero);
                yield return AllocationCapture.Measure("scope-capacity-growth", count, () => ScopeBatch(f, key, scopes, count), _ => { });
                yield return AllocationCapture.Measure("scope-capacity-reuse", count, () => ScopeBatch(f, key, scopes, count), Zero);
            }
            var owner = f.GameObject("first-owner");
            yield return AllocationCapture.Measure("new-owner", 1, () => f.Service.OwnerScope(owner, ResourceScopeSource.Binding, null), _ => { });
            yield return AllocationCapture.Measure("known-owner", 1, () => f.Service.OwnerScope(owner, ResourceScopeSource.Binding, null), Zero);
            f.AssertInvariants();
        }

        private static void LeaseBatch(ResourceFixture f, AssetRef<TextAsset> key, AssetLease<TextAsset>[] leases, int count)
        {
            for (int i = 0; i < count; i++) leases[i] = f.Service.LoadLease(key);
            for (int i = 0; i < count; i++) leases[i].Dispose();
        }

        private static void ScopeBatch(ResourceFixture f, AssetRef<TextAsset> key, ResourceScope[] scopes, int count)
        {
            for (int i = 0; i < count; i++)
            {
                scopes[i] = f.Service.CreateScope();
                scopes[i].Load(key);
            }
            for (int i = 0; i < count; i++) scopes[i].Dispose();
        }
    }
}
#endif
