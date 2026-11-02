using System;
using System.Text.RegularExpressions;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceTerminationTests
    {
        private ResourceFixture f;

        [SetUp]
        public void SetUp()
        {
            f = new ResourceFixture();
            f.Loader.CompleteImmediately = false;
        }

        [TearDown] public void TearDown() => f.Dispose();

        [Test]
        public void ConcurrentWaitersShareOneBackendLoad()
        {
            var asset = f.Text("a");
            var first = f.Service.CreateScope();
            var second = f.Service.CreateScope();
            var leases = new UniTask<AssetLease<TextAsset>>[4];
            for (int i = 0; i < leases.Length; i++) leases[i] = f.Service.LoadLeaseAsync<TextAsset>("a");
            var scoped = new[]
            {
                first.LoadAsync<TextAsset>("a"), first.LoadAsync<TextAsset>("a"),
                second.LoadAsync<TextAsset>("a"), second.LoadAsync(f.Service.Key<TextAsset>("a")),
            };
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Service.GetStats().WaiterCount, Is.EqualTo(8));
            f.Tick();
            foreach (var task in leases) Assert.That(task.GetAwaiter().IsCompleted, Is.False);
            f.Loader.Complete("a");
            f.Tick();
            foreach (var task in leases)
            {
                var lease = f.Result(task);
                Assert.That(lease.IsValid, Is.True);
                Assert.That(lease.Asset, Is.SameAs(asset));
            }
            foreach (var task in scoped) Assert.That(f.Result(task), Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(6), "Four leases plus one entry per scope.");
            Assert.That(f.Service.GetStats().WaiterCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void PartialCancellationAbortsOnlyTheCanceledWaiter()
        {
            f.Text("a");
            using var cts = new CancellationTokenSource();
            var canceled = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            var surviving = f.Service.LoadLeaseAsync<TextAsset>("a");
            cts.Cancel();
            f.Tick();
            Assert.That(f.Result(canceled).IsValid, Is.False, "Cancellation returns an empty lease.");
            Assert.That(surviving.GetAwaiter().IsCompleted, Is.False);
            f.Loader.Complete("a");
            f.Tick();
            var lease = f.Result(surviving);
            Assert.That(lease.IsValid, Is.True);
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            lease.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void FullCancellationReleasesTheBackendLoad()
        {
            var asset = f.Text("a");
            var scope = f.Service.CreateScope();
            using var cts = new CancellationTokenSource();
            var lease = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            var scoped = scope.LoadAsync<TextAsset>("a", cts.Token);
            cts.Cancel();
            f.Tick();
            Assert.That(f.Result(lease).IsValid, Is.False);
            Assert.That(f.Result(scoped), Is.Null);
            f.AssertReleased("a");
            Assert.That(f.Loader.LiveHandles, Is.Zero, "Nobody waits for the load, so the backend handle is released.");
            Assert.That(f.Service.GetStats().LoadingCount, Is.Zero);
            f.Loader.Complete("a");
            f.Tick();
            f.AssertReleased("a");
            f.Loader.CompleteImmediately = true;
            Assert.That(scope.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(f.Loader.Loads, Is.EqualTo(2));
            scope.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void CancellationKeepsTheLoadWhileAnotherWaiterRemains()
        {
            f.Text("a");
            using var cts = new CancellationTokenSource();
            var canceled = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            var scope = f.Service.CreateScope();
            var waiting = scope.LoadAsync<TextAsset>("a");
            cts.Cancel();
            f.Tick();
            Assert.That(f.Result(canceled).IsValid, Is.False);
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Loading));
            f.Loader.Complete("a");
            f.Tick();
            Assert.That(f.Result(waiting), Is.Not.Null);
            scope.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void PreCanceledTokenNeverStartsALoad()
        {
            f.Text("a");
            var scope = f.Service.CreateScope();
            var token = new CancellationToken(true);
            Assert.That(f.Service.LoadLeaseAsync<TextAsset>("a", token).GetAwaiter().GetResult().IsValid, Is.False);
            Assert.That(scope.LoadAsync<TextAsset>("a", token).GetAwaiter().GetResult(), Is.Null);
            Assert.That(f.Loader.Loads, Is.Zero);
        }

        [Test]
        public void CancellationAfterCompletionKeepsTheResult()
        {
            f.Text("a");
            using var cts = new CancellationTokenSource();
            var task = f.Service.LoadLeaseAsync<TextAsset>("a", cts.Token);
            f.Loader.Complete("a");
            f.Tick();
            cts.Cancel();
            f.Tick();
            var lease = f.Result(task);
            Assert.That(lease.IsValid, Is.True);
            lease.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void SyncLoadJoinsAnInFlightAsyncLoad()
        {
            var asset = f.Text("a");
            var pending = f.Service.LoadLeaseAsync<TextAsset>("a");
            var scope = f.Service.CreateScope();
            var lease = f.Service.LoadLease<TextAsset>("a");
            Assert.That(lease.Asset, Is.SameAs(asset));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Loader.SynchronousLoads, Is.Zero);
            Assert.That(f.Loader.WaitCalls, Is.EqualTo(1));
            Assert.That(pending.GetAwaiter().IsCompleted, Is.True, "Joining completes the async waiters immediately.");
            var joined = f.Result(pending);
            Assert.That(joined.Asset, Is.SameAs(asset));
            Assert.That(scope.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(3));
            Assert.That(f.Service.GetStats().LoadingCount, Is.Zero);
            f.Tick();
            Assert.That(f.Info("a").RefCount, Is.EqualTo(3));
            lease.Dispose();
            joined.Dispose();
            scope.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void SyncScopeLoadJoinsAnInFlightScopeLoad()
        {
            var asset = f.Text("a");
            var scope = f.Service.CreateScope();
            var pending = scope.LoadAsync<TextAsset>("a");
            Assert.That(scope.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(f.Result(pending), Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1), "Both loads share the scope entry.");
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            scope.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void FailedLoadReturnsEmptyAndRetryStartsANewLoad()
        {
            var asset = f.Text("a");
            var scope = f.Service.CreateScope();
            var lease = f.Service.LoadLeaseAsync<TextAsset>("a");
            var scoped = scope.LoadAsync<TextAsset>("a");
            f.Loader.Complete("a", false);
            LogAssert.Expect(LogType.Warning, new Regex("Resource load failed"));
            f.Tick();
            Assert.That(f.Result(lease).IsValid, Is.False);
            Assert.That(f.Result(scoped), Is.Null);
            f.AssertReleased("a");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.Loader.CompleteImmediately = true;
            Assert.That(scope.Load<TextAsset>("a"), Is.SameAs(asset));
            Assert.That(f.Loader.Loads, Is.EqualTo(2));
            scope.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void SynchronousFailureReturnsEmpty()
        {
            f.Text("a");
            var lease = f.Service.LoadLeaseAsync<TextAsset>("a");
            f.Loader.Complete("a", false);
            LogAssert.Expect(LogType.Warning, new Regex("Resource load failed"));
            Assert.That(f.Service.LoadLease<TextAsset>("a").IsValid, Is.False);
            Assert.That(f.Result(lease).IsValid, Is.False);
            f.AssertReleased("a");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void TypeMismatchIsAFailedLoad()
        {
            f.Text("a");
            f.Loader.CompleteImmediately = true;
            LogAssert.Expect(LogType.Warning, new Regex("Resource load failed.*Material"));
            Assert.That(f.Service.LoadLease<Material>("a").IsValid, Is.False);
            f.AssertReleased("a");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void LoaderExceptionPropagatesWithoutCorruptingState()
        {
            var asset = f.Text("a");
            f.Loader.ThrowOnLoad = true;
            Assert.Throws<InvalidOperationException>(() => f.Service.LoadLease<TextAsset>("a"));
            Assert.Throws<InvalidOperationException>(() => f.Service.LoadLeaseAsync<TextAsset>("a"));
            f.AssertReleased("a");
            Assert.That(f.Service.GetStats().LoadingCount, Is.Zero);
            f.Loader.ThrowOnLoad = false;
            f.Loader.CompleteImmediately = true;
            using var lease = f.Service.LoadLease<TextAsset>("a");
            Assert.That(lease.Asset, Is.SameAs(asset));
            f.AssertInvariants();
        }

        [Test]
        public void ScopeDisposedDuringLoadReturnsNullAndKeepsNoReference()
        {
            f.Text("a");
            var scope = f.Service.CreateScope();
            var task = scope.LoadAsync<TextAsset>("a");
            scope.Dispose();
            f.Loader.Complete("a");
            f.Tick();
            Assert.That(f.Result(task), Is.Null);
            Assert.That(f.Info("a").State, Is.EqualTo(ResourceAssetState.Idle));
            Assert.That(f.Info("a").RefCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void ForceUnloadAllDuringLoadAbortsWaitersAndReleasesTheHandle()
        {
            f.Text("a");
            var scope = f.Service.CreateScope();
            var lease = f.Service.LoadLeaseAsync<TextAsset>("a");
            var scoped = scope.LoadAsync<TextAsset>("a");
            f.Service.ForceUnloadAllAssets();
            Assert.That(f.Result(lease).IsValid, Is.False);
            Assert.That(f.Result(scoped), Is.Null);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.AssertReleased("a");
            f.Tick();
            Assert.That(f.Service.GetStats().LoadingCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void ShutdownDuringLoadAbortsWaiters()
        {
            f.Text("a");
            var scope = f.Service.CreateScope();
            var lease = f.Service.LoadLeaseAsync<TextAsset>("a");
            var scoped = scope.LoadAsync<TextAsset>("a");
            f.Service.Shutdown();
            Assert.That(f.Result(lease).IsValid, Is.False);
            Assert.That(f.Result(scoped), Is.Null);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void ResetInsideACompletionContinuationAbortsTheRemainingWaiters()
        {
            f.Text("a");
            var scope = f.Service.CreateScope();
            var aborted = f.Service.LoadLeaseAsync<TextAsset>("a");
            var abortedScope = scope.LoadAsync<TextAsset>("a");
            var reentrant = f.Service.LoadLeaseAsync<TextAsset>("a");
            int calls = 0;
            reentrant.GetAwaiter().OnCompleted(() =>
            {
                calls++;
                f.Service.ForceUnloadAllAssets();
            });
            f.Loader.Complete("a");
            f.Tick();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(f.Result(reentrant).IsValid, Is.False, "The reset invalidated the lease that triggered it.");
            Assert.That(f.Result(aborted).IsValid, Is.False);
            Assert.That(f.Result(abortedScope), Is.Null);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Service.GetStats().ReferenceCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void ScopeDisposedInsideACompletionContinuationRejectsItsPendingWaiter()
        {
            var asset = f.Text("a");
            var victim = f.Service.CreateScope();
            var pending = victim.LoadAsync<TextAsset>("a");
            var trigger = f.Service.LoadLeaseAsync<TextAsset>("a");
            trigger.GetAwaiter().OnCompleted(victim.Dispose);
            f.Loader.Complete("a");
            f.Tick();
            Assert.That(f.Result(pending), Is.Null);
            var lease = f.Result(trigger);
            Assert.That(lease.Asset, Is.SameAs(asset));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            lease.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void ContinuationCanLoadTheSameAssetAgain()
        {
            var asset = f.Text("a");
            var trigger = f.Service.LoadLeaseAsync<TextAsset>("a");
            AssetLease<TextAsset> nested = default;
            trigger.GetAwaiter().OnCompleted(() => nested = f.Service.LoadLease<TextAsset>("a"));
            f.Loader.Complete("a");
            f.Tick();
            Assert.That(nested.Asset, Is.SameAs(asset));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            f.Result(trigger).Dispose();
            nested.Dispose();
            f.AssertInvariants();
        }

        [Test]
        public void SeveralLoadsFinishingInOneTickAllComplete()
        {
            const int count = 40;
            var tasks = new UniTask<AssetLease<TextAsset>>[count];
            for (int i = 0; i < count; i++)
            {
                f.Text("k" + i);
                tasks[i] = f.Service.LoadLeaseAsync<TextAsset>("k" + i);
            }
            for (int i = count - 1; i >= 0; i -= 2) f.Loader.Complete("k" + i);
            f.Tick();
            for (int i = 0; i < count; i++) Assert.That(tasks[i].GetAwaiter().IsCompleted, Is.EqualTo((i & 1) == 1));
            for (int i = 0; i < count; i += 2) f.Loader.Complete("k" + i);
            f.Tick();
            for (int i = 0; i < count; i++) f.Result(tasks[i]).Dispose();
            Assert.That(f.Service.GetStats().LoadingCount, Is.Zero);
            f.AssertInvariants();
        }

        [Test]
        public void AsyncHitOnIdleAssetCompletesWithoutBackendLoad()
        {
            f.Loader.CompleteImmediately = true;
            var asset = f.Text("a");
            f.Service.LoadLease<TextAsset>("a").Dispose();
            var task = f.Service.LoadLeaseAsync<TextAsset>("a");
            Assert.That(task.GetAwaiter().IsCompleted, Is.True);
            var lease = f.Result(task);
            Assert.That(lease.Asset, Is.SameAs(asset));
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            lease.Dispose();
        }
    }
}
