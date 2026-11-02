#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed partial class ResourceBackendTests
    {
        private const long ManagedGrowthBudget = 1L << 20;
        private const long NativeGrowthBudget = 4L << 20;
        private const double SoakSeconds = 60;

        private struct Snapshot
        {
            public int SceneObjects;
            public int Materials;
            public int Owners;
            public int Renderers;
            public int ScopeSlots;
            public int BindingSlots;
            public long Managed;
            public long Native;

            public override string ToString() =>
                $"scene-objects={SceneObjects},materials={Materials},owners={Owners},renderers={Renderers},scope-slots={ScopeSlots},binding-slots={BindingSlots},managed={Managed},native={Native}";
        }

        [UnityTest]
        public IEnumerator RepeatedRealCyclesReturnToBaselineWithoutMemoryGrowth()
        {
            const int warmup = 2;
            const int cycles = 10;
            const int instancesPerCycle = 128;
            var samples = new List<Snapshot>();
            for (int cycle = 0; cycle < warmup + cycles; cycle++)
            {
                yield return RunCycle(instancesPerCycle);
                yield return Quiesce();
                Snapshot snapshot = Capture();
                TestContext.WriteLine($"LEAK-CYCLE,{cycle},{snapshot}");
                if (cycle >= warmup) samples.Add(snapshot);
            }

            AssertNoGrowth("LEAK", samples, true);
        }

        [UnityTest]
        public IEnumerator RandomizedSoakConvergesWithoutLeaksOrMemoryGrowth()
        {
            var random = new System.Random(20261102);
            var soak = new SoakState(this, random);
            var samples = new List<Snapshot>();
            var clock = Stopwatch.StartNew();
            int round = 0;
            long operations = 0;
            while (clock.Elapsed.TotalSeconds < SoakSeconds || round < 12)
            {
                for (int frame = 0; frame < 90; frame++)
                {
                    for (int op = 0; op < 8; op++)
                    {
                        soak.Step();
                        soak.Sample();
                    }

                    operations += 8;
                    soak.Poll();
                    soak.Sample();
                    if ((frame & 15) == 0) AssertReferenceInvariant();
                    yield return null;
                }

                yield return soak.Drain();
                yield return Quiesce();
                Snapshot snapshot = Capture();
                TestContext.WriteLine($"SOAK-ROUND,{round},{clock.Elapsed.TotalSeconds:F1}s,ops={operations},{snapshot}");
                if (round >= 2) samples.Add(snapshot);
                round++;
            }

            ResourceStats stats = service.GetStats();
            long requests = stats.HitCount + stats.IdleHitCount + stats.JoinCount + stats.MissCount;
            TestContext.WriteLine($"SOAK-SUMMARY,seconds={clock.Elapsed.TotalSeconds:F1},rounds={round},ops={operations},resets={soak.Resets}," +
                                  $"requests={requests},hit={stats.HitCount},idle-hit={stats.IdleHitCount},join={stats.JoinCount},miss={stats.MissCount}," +
                                  $"hit-rate={(stats.HitCount + stats.IdleHitCount) * 100.0 / Math.Max(1, requests):F1}%");
            Assert.That(clock.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(SoakSeconds));
            AssertNoGrowth("SOAK", samples, false);
            Snapshot last = samples[samples.Count - 1];
            TestContext.WriteLine($"SOAK-SLOTS,scope-slots={last.ScopeSlots},peak-live-scopes={soak.PeakScopes},binding-slots={last.BindingSlots},peak-live-bindings={soak.PeakBindings}");
            Assert.That(last.ScopeSlots, Is.EqualTo(soak.PeakScopes), "Scope slot capacity must equal the peak number of simultaneously live scopes.");
            Assert.That(last.BindingSlots, Is.EqualTo(soak.PeakBindings), "Binding slot capacity must equal the peak number of simultaneously live bindings.");
        }

        private IEnumerator RunCycle(int count)
        {
            string prefab = folder + "/prefab.prefab";
            string material = folder + "/material.mat";
            string sprite = folder + "/sprite.png";
            string text = folder + "/text.txt";
            var scope = service.CreateScope("cycle");
            Assert.That(scope.Load<TextAsset>(text), Is.Not.Null);
            var instances = new GameObject[count];
            var pending = new UniTask<GameObject>[count / 2];
            for (int i = 0; i < pending.Length; i++)
                pending[i] = service.InstantiateAsync(prefab, root.transform);
            for (int i = pending.Length; i < count; i++)
                instances[i] = service.Instantiate(prefab, root.transform);
            for (int i = 0; i < pending.Length; i++)
            {
                int index = i;
                yield return ResourceFixture.WaitFor(pending[i], instance => instances[index] = instance);
            }

            for (int i = 0; i < count; i++)
            {
                Assert.That(instances[i], Is.Not.Null);
                ResourceScope owner = instances[i].GetComponent<ResourceOwner>().Scope;
                instances[i].AddComponent<MeshRenderer>().SetMaterialInstance(owner, material, ResourceBindingOptions.Synchronous);
                var child = new GameObject("sprite");
                child.transform.SetParent(instances[i].transform);
                child.AddComponent<SpriteRenderer>().SetSprite(owner, sprite, ResourceBindingOptions.Synchronous);
            }

            using (var lease = service.LoadLease<TextAsset>(text))
                Assert.That(lease.IsValid, Is.True);
            Assert.That(References(), Is.EqualTo(1 + count * 3));
            yield return null;
            for (int i = 0; i < count; i++) Object.Destroy(instances[i]);
            scope.Dispose();
        }

        private IEnumerator Quiesce()
        {
            yield return null;
            yield return null;
            Assert.That(References(), Is.Zero, "Every reference must be released once its owners are gone.");
            ResourceStats stats = service.GetStats();
            Assert.That(stats.BindingCount, Is.Zero);
            Assert.That(stats.LoadingCount, Is.Zero);
            Assert.That(stats.WaiterCount, Is.Zero);
            Assert.That(stats.ScopeCount, Is.EqualTo(1), "Only the App scope may remain.");
            for (int frame = 0; frame < 180 && !service.CanLoadResources; frame++)
                yield return null;
            service.UnloadUnusedAssets(true);
            yield return WaitForBackendRelease();
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private Snapshot Capture()
        {
            int sceneObjects = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.scene.IsValid()) sceneObjects++;
            return new Snapshot
            {
                SceneObjects = sceneObjects,
                Materials = Resources.FindObjectsOfTypeAll<Material>().Length,
                Owners = Resources.FindObjectsOfTypeAll<ResourceOwner>().Length,
                Renderers = Resources.FindObjectsOfTypeAll<Renderer>().Length,
                ScopeSlots = service.ScopeSlotCount,
                BindingSlots = service.Bindings.SlotCount,
                Managed = GC.GetTotalMemory(true),
                Native = Profiler.GetTotalAllocatedMemoryLong(),
            };
        }

        private static void AssertNoGrowth(string label, List<Snapshot> samples, bool fixedWorkload)
        {
            Assert.That(samples.Count, Is.GreaterThanOrEqualTo(6));
            Snapshot first = samples[0];
            for (int i = 1; i < samples.Count; i++)
            {
                Snapshot sample = samples[i];
                Assert.That(sample.SceneObjects, Is.EqualTo(first.SceneObjects), label + " scene objects at sample " + i);
                Assert.That(sample.Materials, Is.EqualTo(first.Materials), label + " materials at sample " + i);
                Assert.That(sample.Owners, Is.EqualTo(first.Owners), label + " owners at sample " + i);
                Assert.That(sample.Renderers, Is.EqualTo(first.Renderers), label + " renderers at sample " + i);
                if (!fixedWorkload) continue;
                Assert.That(sample.ScopeSlots, Is.EqualTo(first.ScopeSlots), label + " scope slot capacity at sample " + i);
                Assert.That(sample.BindingSlots, Is.EqualTo(first.BindingSlots), label + " binding slot capacity at sample " + i);
            }

            int third = samples.Count / 3;
            long managedGrowth = Average(samples, samples.Count - third, third, true) - Average(samples, 0, third, true);
            long nativeGrowth = Average(samples, samples.Count - third, third, false) - Average(samples, 0, third, false);
            TestContext.WriteLine($"{label}-GROWTH,managed={managedGrowth},native={nativeGrowth},samples={samples.Count}");
            Assert.That(managedGrowth, Is.LessThan(ManagedGrowthBudget), label + " managed heap keeps growing across quiescent points.");
            Assert.That(nativeGrowth, Is.LessThan(NativeGrowthBudget), label + " native memory keeps growing across quiescent points.");
        }

        private static long Average(List<Snapshot> samples, int start, int count, bool managed)
        {
            long sum = 0;
            for (int i = start; i < start + count; i++) sum += managed ? samples[i].Managed : samples[i].Native;
            return sum / count;
        }

        private void AssertReferenceInvariant()
        {
            ResourceStats stats = service.GetStats();
            Assert.That(stats.ReferenceCount, Is.EqualTo(stats.ScopeEntryCount + stats.LeaseCount + stats.BindingReferenceCount),
                "Record references must equal scope entries + leases + bindings.");
        }

        private sealed class SoakState
        {
            private readonly ResourceBackendTests owner;
            private readonly System.Random random;
            private readonly List<GameObject> instances = new List<GameObject>();
            private readonly List<ResourceScope> scopes = new List<ResourceScope>();
            private readonly List<AssetLease<TextAsset>> leases = new List<AssetLease<TextAsset>>();
            private readonly List<UniTask<GameObject>> pendingInstances = new List<UniTask<GameObject>>();
            private readonly List<UniTask<AssetLease<TextAsset>>> pendingLeases = new List<UniTask<AssetLease<TextAsset>>>();
            private readonly List<UniTask<TextAsset>> pendingScopeLoads = new List<UniTask<TextAsset>>();
            private readonly List<CancellationTokenSource> cancellations = new List<CancellationTokenSource>();
            private readonly string prefab;
            private readonly string material;
            private readonly string sprite;
            private readonly string text;
            internal int Resets;
            internal int PeakScopes;
            internal int PeakBindings;

            internal SoakState(ResourceBackendTests owner, System.Random random)
            {
                this.owner = owner;
                this.random = random;
                prefab = owner.folder + "/prefab.prefab";
                material = owner.folder + "/material.mat";
                sprite = owner.folder + "/sprite.png";
                text = owner.folder + "/text.txt";
            }

            private ResourceService Service => owner.service;

            private CancellationToken MaybeCanceledToken()
            {
                if (random.Next(4) != 0) return CancellationToken.None;
                var source = new CancellationTokenSource();
                cancellations.Add(source);
                if (random.Next(2) == 0) source.Cancel();
                return source.Token;
            }

            private GameObject RandomInstance()
            {
                for (int tries = 0; tries < 4 && instances.Count > 0; tries++)
                {
                    GameObject instance = instances[random.Next(instances.Count)];
                    if (instance != null) return instance;
                }
                return null;
            }

            internal void Step()
            {
                switch (random.Next(12))
                {
                    case 0:
                    case 1:
                        if (instances.Count < 256) Track(Service.Instantiate(prefab, owner.root.transform));
                        break;
                    case 2:
                        if (instances.Count < 256) pendingInstances.Add(Service.InstantiateAsync(prefab, owner.root.transform, MaybeCanceledToken()));
                        break;
                    case 3:
                        if (instances.Count > 0)
                        {
                            int index = random.Next(instances.Count);
                            if (instances[index] != null) Object.Destroy(instances[index]);
                            instances.RemoveAt(index);
                        }
                        break;
                    case 4:
                    {
                        GameObject instance = RandomInstance();
                        if (instance == null) break;
                        var renderer = instance.GetComponent<MeshRenderer>();
                        if (renderer == null) renderer = instance.AddComponent<MeshRenderer>();
                        ResourceScope scope = instance.GetComponent<ResourceOwner>().Scope;
                        if (random.Next(2) == 0) renderer.SetMaterialInstance(scope, material);
                        else renderer.SetSharedMaterial(scope, random.Next(3) == 0 ? null : material);
                        break;
                    }
                    case 5:
                    {
                        GameObject instance = RandomInstance();
                        if (instance == null) break;
                        Transform child = instance.transform.childCount > 0 ? instance.transform.GetChild(0) : null;
                        if (child == null)
                        {
                            child = new GameObject("sprite").transform;
                            child.SetParent(instance.transform);
                            child.gameObject.AddComponent<SpriteRenderer>();
                        }
                        child.GetComponent<SpriteRenderer>().SetSprite(instance.GetComponent<ResourceOwner>().Scope, random.Next(4) == 0 ? null : sprite,
                            random.Next(2) == 0 ? ResourceBindingOptions.Synchronous : ResourceBindingOptions.None);
                        break;
                    }
                    case 6:
                        if (scopes.Count < 32) scopes.Add(Service.CreateScope("soak"));
                        break;
                    case 7:
                        if (scopes.Count > 0)
                        {
                            ResourceScope scope = scopes[random.Next(scopes.Count)];
                            if (random.Next(2) == 0) scope.Load<TextAsset>(text);
                            else pendingScopeLoads.Add(scope.LoadAsync<TextAsset>(text, MaybeCanceledToken()));
                        }
                        break;
                    case 8:
                        if (scopes.Count > 0)
                        {
                            int index = random.Next(scopes.Count);
                            scopes[index].Dispose();
                            scopes.RemoveAt(index);
                        }
                        break;
                    case 9:
                        if (leases.Count < 64)
                        {
                            if (random.Next(2) == 0) leases.Add(Service.LoadLease<TextAsset>(text));
                            else pendingLeases.Add(Service.LoadLeaseAsync<TextAsset>(text, MaybeCanceledToken()));
                        }
                        break;
                    case 10:
                        if (leases.Count > 0)
                        {
                            int index = random.Next(leases.Count);
                            leases[index].Dispose();
                            leases.RemoveAt(index);
                        }
                        break;
                    case 11:
                        if (random.Next(400) == 0 && Service.CanLoadResources)
                        {
                            Service.ForceUnloadAllAssets();
                            Resets++;
                        }
                        break;
                }
            }

            internal void Sample()
            {
                PeakScopes = Math.Max(PeakScopes, Service.GetStats().ScopeCount);
                PeakBindings = Math.Max(PeakBindings, Service.Bindings.Count);
            }

            private void Track(GameObject instance)
            {
                if (instance != null) instances.Add(instance);
            }

            internal void Poll()
            {
                for (int i = pendingInstances.Count - 1; i >= 0; i--)
                {
                    var awaiter = pendingInstances[i].GetAwaiter();
                    if (!awaiter.IsCompleted) continue;
                    pendingInstances.RemoveAt(i);
                    Track(awaiter.GetResult());
                }

                for (int i = pendingLeases.Count - 1; i >= 0; i--)
                {
                    var awaiter = pendingLeases[i].GetAwaiter();
                    if (!awaiter.IsCompleted) continue;
                    pendingLeases.RemoveAt(i);
                    leases.Add(awaiter.GetResult());
                }

                for (int i = pendingScopeLoads.Count - 1; i >= 0; i--)
                {
                    var awaiter = pendingScopeLoads[i].GetAwaiter();
                    if (!awaiter.IsCompleted) continue;
                    pendingScopeLoads.RemoveAt(i);
                    awaiter.GetResult();
                }
            }

            internal IEnumerator Drain()
            {
                for (int frame = 0; frame < 600 && (pendingInstances.Count > 0 || pendingLeases.Count > 0 || pendingScopeLoads.Count > 0); frame++)
                {
                    Poll();
                    Sample();
                    yield return null;
                }

                Assert.That(pendingInstances.Count + pendingLeases.Count + pendingScopeLoads.Count, Is.Zero, "Pending soak operations did not terminate.");
                foreach (GameObject instance in instances)
                    if (instance != null) Object.Destroy(instance);
                instances.Clear();
                foreach (ResourceScope scope in scopes) scope.Dispose();
                scopes.Clear();
                foreach (AssetLease<TextAsset> lease in leases) lease.Dispose();
                leases.Clear();
                foreach (CancellationTokenSource source in cancellations) source.Dispose();
                cancellations.Clear();
            }
        }
    }
}
#endif
