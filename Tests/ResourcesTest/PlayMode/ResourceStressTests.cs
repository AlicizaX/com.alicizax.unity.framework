#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceStressTests
    {
        private const int TextCount = 8;
        private const int SpriteCount = 4;
        private const int TargetCount = 6;
        private const int MaxPending = 64;

        private sealed class ModelScope
        {
            internal ResourceScope Scope;
            internal readonly HashSet<int> Assets = new HashSet<int>();
        }

        private struct PendingLease
        {
            internal UniTask<AssetLease<TextAsset>> Task;
            internal CancellationTokenSource Source;
            internal int Epoch;
        }

        private struct PendingScopeLoad
        {
            internal UniTask<TextAsset> Task;
            internal ModelScope Owner;
            internal int Text;
            internal int Epoch;
        }

        private sealed class Model
        {
            internal readonly ResourceFixture F;
            internal readonly System.Random Random;
            internal readonly string[] Texts = new string[TextCount];
            internal readonly string[] Sprites = new string[SpriteCount];
            internal readonly List<AssetLease<TextAsset>> Leases = new List<AssetLease<TextAsset>>();
            internal readonly List<AssetLease<TextAsset>> Disposed = new List<AssetLease<TextAsset>>();
            internal readonly List<ModelScope> Scopes = new List<ModelScope>();
            internal readonly List<PendingLease> PendingLeases = new List<PendingLease>();
            internal readonly List<PendingScopeLoad> PendingScopes = new List<PendingScopeLoad>();
            internal readonly SpriteRenderer[] Targets = new SpriteRenderer[TargetCount];
            internal int Epoch;
            internal int Completed;
            internal int Canceled;

            internal Model(ResourceFixture f, int seed)
            {
                F = f;
                Random = new System.Random(seed);
                f.Loader.CompleteImmediately = false;
                for (int i = 0; i < TextCount; i++) f.Text(Texts[i] = "text-" + i);
                for (int i = 0; i < SpriteCount; i++) f.Sprite(Sprites[i] = "sprite-" + i);
                for (int i = 0; i < TargetCount; i++) Targets[i] = f.GameObject("target-" + i).AddComponent<SpriteRenderer>();
            }

            internal void Step()
            {
                switch (Random.Next(20))
                {
                    case 0:
                    case 1:
                        var lease = F.Service.LoadLease<TextAsset>(Texts[Random.Next(TextCount)]);
                        if (lease.IsValid) Leases.Add(lease);
                        else Assert.That(lease.Asset, Is.Null);
                        break;
                    case 2:
                    case 3:
                        DisposeLease();
                        break;
                    case 4:
                        if (PendingLeases.Count >= MaxPending) break;
                        var source = Random.Next(3) == 0 ? new CancellationTokenSource() : null;
                        PendingLeases.Add(new PendingLease
                        {
                            Task = F.Service.LoadLeaseAsync<TextAsset>(Texts[Random.Next(TextCount)], source?.Token ?? default),
                            Source = source,
                            Epoch = Epoch,
                        });
                        break;
                    case 5:
                        CancelOne();
                        break;
                    case 6:
                        if (Scopes.Count < 16) Scopes.Add(new ModelScope { Scope = F.Service.CreateScope() });
                        break;
                    case 7:
                        DisposeScope();
                        break;
                    case 8:
                        ScopeLoad();
                        break;
                    case 9:
                        ScopeLoadAsync();
                        break;
                    case 10:
                    case 11:
                        Bind();
                        break;
                    case 12:
                    case 13:
                    case 14:
                        CompleteRequest();
                        break;
                    case 15:
                    case 16:
                        F.Tick(Random.Next(4) == 0 ? 30f : 0.1f);
                        break;
                    case 17:
                        F.Service.UnloadUnusedAssets(Random.Next(2) == 0);
                        break;
                    case 18:
                        if (Random.Next(50) == 0) ForceUnloadAll();
                        break;
                    default:
                        F.Service.IdleAssetCapacity = Random.Next(5);
                        break;
                }

                Harvest();
            }

            private void DisposeLease()
            {
                if (Random.Next(4) == 0 && Disposed.Count > 0)
                {
                    var stale = Disposed[Random.Next(Disposed.Count)];
                    stale.Dispose();
                    Assert.That(stale.IsValid, Is.False);
                    return;
                }

                if (Leases.Count == 0) return;
                int index = Random.Next(Leases.Count);
                var lease = Leases[index];
                Leases[index] = Leases[Leases.Count - 1];
                Leases.RemoveAt(Leases.Count - 1);
                Assert.That(lease.IsValid, Is.True);
                lease.Dispose();
                if (Disposed.Count < 64) Disposed.Add(lease);
            }

            private void CancelOne()
            {
                foreach (var pending in PendingLeases)
                {
                    if (pending.Source == null || pending.Source.IsCancellationRequested) continue;
                    pending.Source.Cancel();
                    Canceled++;
                    return;
                }
            }

            private void DisposeScope()
            {
                if (Scopes.Count == 0) return;
                int index = Random.Next(Scopes.Count);
                var scope = Scopes[index];
                Scopes.RemoveAt(index);
                scope.Scope.Dispose();
                Assert.That(scope.Scope.IsAlive, Is.False);
            }

            private void ScopeLoad()
            {
                if (Scopes.Count == 0) return;
                var scope = Scopes[Random.Next(Scopes.Count)];
                int text = Random.Next(TextCount);
                var asset = scope.Scope.Load<TextAsset>(Texts[text]);
                if (asset != null) scope.Assets.Add(text);
            }

            private void ScopeLoadAsync()
            {
                if (Scopes.Count == 0 || PendingScopes.Count >= MaxPending) return;
                var scope = Scopes[Random.Next(Scopes.Count)];
                int text = Random.Next(TextCount);
                PendingScopes.Add(new PendingScopeLoad { Task = scope.Scope.LoadAsync<TextAsset>(Texts[text]), Owner = scope, Text = text, Epoch = Epoch });
            }

            private void Bind()
            {
                var target = Targets[Random.Next(TargetCount)];
                var scope = Scopes.Count == 0 || Random.Next(4) == 0 ? F.Service.App : Scopes[Random.Next(Scopes.Count)].Scope;
                int choice = Random.Next(SpriteCount + 1);
                var options = Random.Next(5) == 0 ? ResourceBindingOptions.Synchronous : ResourceBindingOptions.None;
                target.SetSprite(scope, choice == SpriteCount ? "" : Sprites[choice], options);
            }

            private void CompleteRequest()
            {
                bool sprite = Random.Next(3) == 0;
                string location = sprite ? Sprites[Random.Next(SpriteCount)] : Texts[Random.Next(TextCount)];
                if (!F.Loader.HasRequest(location)) return;
                F.Loader.Complete(F.Loader.RequestFor(location), Random.Next(200) != 0);
            }

            private void ForceUnloadAll()
            {
                F.Service.ForceUnloadAllAssets();
                Epoch++;
                foreach (var lease in Leases) Assert.That(lease.IsValid, Is.False);
                Leases.Clear();
                foreach (var scope in Scopes) scope.Assets.Clear();
                foreach (var target in Targets) Assert.That(target.sprite, Is.Null);
                Assert.That(F.Loader.LiveHandles, Is.Zero);
            }

            internal void Harvest()
            {
                for (int i = PendingLeases.Count - 1; i >= 0; i--)
                {
                    var pending = PendingLeases[i];
                    if (!pending.Task.GetAwaiter().IsCompleted) continue;
                    PendingLeases.RemoveAt(i);
                    var lease = pending.Task.GetAwaiter().GetResult();
                    pending.Source?.Dispose();
                    Completed++;
                    if (!lease.IsValid) continue;
                    Assert.That(pending.Epoch, Is.EqualTo(Epoch), "A lease from before a reset cannot survive it.");
                    Leases.Add(lease);
                }

                for (int i = PendingScopes.Count - 1; i >= 0; i--)
                {
                    var pending = PendingScopes[i];
                    if (!pending.Task.GetAwaiter().IsCompleted) continue;
                    PendingScopes.RemoveAt(i);
                    var asset = pending.Task.GetAwaiter().GetResult();
                    Completed++;
                    if (asset == null) continue;
                    Assert.That(asset.name, Is.EqualTo(Texts[pending.Text]));
                    if (pending.Owner.Scope.IsAlive && pending.Epoch == Epoch) pending.Owner.Assets.Add(pending.Text);
                }
            }

            internal void Verify()
            {
                F.AssertInvariants();
                var stats = F.Service.GetStats();
                Assert.That(stats.LeaseCount, Is.EqualTo(Leases.Count));
                int entries = 0;
                foreach (var scope in Scopes) entries += scope.Assets.Count;
                Assert.That(stats.ScopeEntryCount, Is.EqualTo(entries));
                foreach (var lease in Leases) Assert.That(lease.IsValid && lease.Asset != null, Is.True);
            }

            internal void Drain()
            {
                for (int round = 0; round < 8 && (PendingLeases.Count > 0 || PendingScopes.Count > 0 || F.Service.GetStats().LoadingCount > 0); round++)
                {
                    foreach (var request in F.Loader.Requests) F.Loader.Complete(request);
                    F.Tick();
                    Harvest();
                }
                Assert.That(PendingLeases.Count + PendingScopes.Count, Is.Zero, "Every async load must terminate.");
                Assert.That(F.Service.GetStats().LoadingCount, Is.Zero);
                Assert.That(F.Service.GetStats().WaiterCount, Is.Zero);
                Verify();
                foreach (var lease in Leases) lease.Dispose();
                Leases.Clear();
                foreach (var scope in Scopes) scope.Scope.Dispose();
                Scopes.Clear();
                foreach (var target in Targets) target.SetSprite(F.Service.App, "");
                F.Service.UnloadUnusedAssets(true);
                var stats = F.Service.GetStats();
                Assert.That(stats.ReferenceCount, Is.Zero);
                Assert.That(stats.IdleCount, Is.Zero);
                Assert.That(F.Loader.LiveHandles, Is.Zero);
                F.AssertInvariants();
            }
        }

        [Test]
        public void ModelBasedFuzzConverges([NUnit.Framework.Range(0, 15)] int seed)
        {
            const int steps = 100000;
            UnityEngine.Debug.unityLogger.logEnabled = false;
            try
            {
                using var f = new ResourceFixture();
                var model = new Model(f, seed);
                for (int step = 0; step < steps; step++)
                {
                    model.Step();
                    if (step % 5000 == 4999) model.Verify();
                }
                model.Drain();
                TestContext.WriteLine($"FUZZ,{seed},{steps},completed={model.Completed},canceled={model.Canceled},loads={f.Loader.Loads},assets={f.Service.AssetCount}");
                Assert.That(model.Completed, Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Debug.unityLogger.logEnabled = true;
            }
        }

        [UnityTest]
        public IEnumerator UniqueKeysAndContinuousEvictionConverge()
        {
            foreach (int count in new[] { 100, 1000 })
            {
                using var f = new ResourceFixture();
                f.Service.IdleAssetCapacity = 64;
                var keys = new AssetRef<TextAsset>[count];
                var asset = f.Text("template");
                for (int i = 0; i < count; i++)
                {
                    string location = "key-" + i;
                    f.Loader.Assets.Add(location, asset);
                    keys[i] = f.Service.Key<TextAsset>(location);
                }
                f.Service.LoadLease<TextAsset>("template").Dispose();
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    yield return AllocationCapture.Measure("unique-key-churn-cycle-" + cycle, count, () =>
                    {
                        for (int i = 0; i < count; i++) f.Service.LoadLease(keys[i]).Dispose();
                    }, sample => Assert.That(sample.Bytes - sample.BackendBytes, Is.Zero));
                    Assert.That(f.Loader.LiveHandles, Is.EqualTo(Math.Min(count, 64)));
                    Assert.That(f.Service.GetStats().IdleCount, Is.EqualTo(Math.Min(count, 64)));
                    f.Service.UnloadUnusedAssets(true);
                    Assert.That(f.Loader.LiveHandles, Is.Zero);
                    Assert.That(f.Service.AssetCount, Is.EqualTo(count + 1), "Keys are interned once and never grow on reuse.");
                    f.AssertInvariants();
                }
            }
        }

        [UnityTest]
        public IEnumerator MassConcurrentWaitersAndCancellationConverge()
        {
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                using var f = new ResourceFixture();
                f.Text("a");
                f.Loader.CompleteImmediately = false;
                var scope = f.Service.CreateScope();
                using var source = new CancellationTokenSource();
                var leases = new UniTask<AssetLease<TextAsset>>[count];
                var scoped = new UniTask<TextAsset>[count];
                for (int i = 0; i < count; i++)
                {
                    leases[i] = f.Service.LoadLeaseAsync<TextAsset>("a", (i & 1) == 0 ? source.Token : default);
                    scoped[i] = scope.LoadAsync<TextAsset>("a");
                }
                Assert.That(f.Loader.Loads, Is.EqualTo(1));
                source.Cancel();
                f.Tick();
                for (int i = 0; i < count; i += 2) Assert.That(f.Result(leases[i]).IsValid, Is.False);
                f.Loader.Complete("a");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                f.Tick();
                clock.Stop();
                TestContext.WriteLine($"PERF,complete-waiters,{count + count / 2},{clock.Elapsed.TotalMilliseconds:F4}");
                for (int i = 1; i < count; i += 2) f.Result(leases[i]).Dispose();
                for (int i = 0; i < count; i++) Assert.That(f.Result(scoped[i]), Is.Not.Null);
                Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
                scope.Dispose();
                f.AssertInvariants();
                yield return null;
            }
        }
    }
}
#endif
