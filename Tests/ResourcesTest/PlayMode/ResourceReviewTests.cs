#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceReviewTests
    {
        private ResourceFixture f;
        [SetUp] public void SetUp() => f = new ResourceFixture();
        [TearDown] public void TearDown() => f.Dispose();

        [UnityTest]
        public IEnumerator DownloadUrlOwnershipAndRetryAllocations()
        {
            foreach (bool fallback in new[] { false, true })
            {
                IRemoteService remote = new RemoteServices("https://primary.example/", fallback ? "https://fallback.example/" : null);
                IReadOnlyList<string> first = null;
                yield return AllocationCapture.Measure("remote-first-" + fallback, 1,
                    () => first = remote.GetRemoteUrls("first.bundle"), _ => { }, true);
                var policy = new DefaultDownloadUrlPolicy();
                string selected = null;
                string retryError = "retry";
                int retries = 1;
                Action retry = () =>
                {
                    for (int i = 0; i < retries; i++)
                    {
                        policy.OnRequestFailed(first[0], 503, retryError);
                        selected = policy.SelectUrl(first);
                    }
                };
                yield return AllocationCapture.Measure("remote-retry-first-" + fallback, 1, retry, _ => { }, true);
                foreach (int count in new[] { 100, 1000, 10000 })
                {
                    var results = new IReadOnlyList<string>[count];
                    string fileName = "next.bundle";
                    yield return AllocationCapture.Measure("remote-new-request-" + fallback, count, () =>
                    {
                        for (int i = 0; i < count; i++) results[i] = remote.GetRemoteUrls(fileName);
                    }, sample => Assert.That(sample.Allocations, Is.EqualTo(count * (fallback ? 3 : 2))));
                    Assert.That(first[0], Is.EqualTo("https://primary.example/first.bundle"));
                    Assert.That(results[0], Is.Not.SameAs(results[count - 1]));
                    policy = new DefaultDownloadUrlPolicy();
                    retries = count;
                    yield return AllocationCapture.Measure("remote-existing-request-retry-" + fallback, count, retry,
                        sample => Assert.That(sample.Bytes, Is.Zero));
                    Assert.That(selected, Is.EqualTo(first[0]));
                    if (fallback)
                    {
                        policy.OnRequestFailed(selected, 503, "retry");
                        Assert.That(policy.SelectUrl(first), Is.EqualTo("https://fallback.example/first.bundle"));
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator UpdatingIndexAtCapacityDoesNotAllocate()
        {
            var map = new ResourceUlongIntMap();
            for (ulong key = 1; key <= 11; key++) map.Set(key, 1);
            yield return AllocationCapture.Measure("index-update-at-threshold", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) map.Set(1, i);
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(map.Count, Is.EqualTo(11));
            Assert.That(map.TryGetValue(1, out int value), Is.True);
            Assert.That(value, Is.EqualTo(9999));
        }

        [UnityTest]
        public IEnumerator ResetFromPrefabAwakeDestroysTheInstanceAndLeaksNothing()
        {
            var source = f.Prefab("prefab");
            source.AddComponent<AwakeResourceProbe>();
            AwakeResourceProbe.OnAwake = f.Service.ForceUnloadAllAssets;
            try
            {
                Assert.That(f.Service.Instantiate("prefab"), Is.Null);
                yield return null;
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                Assert.That(UnityObjectQuery.FindAll<AwakeResourceProbe>().Length, Is.EqualTo(1));
                f.AssertInvariants();
            }
            finally { AwakeResourceProbe.OnAwake = null; }
        }

        [UnityTest]
        public IEnumerator ThrowingPropertyCallbackDuringResetStillReleasesStorage()
        {
            f.Material("m");
            f.Text("pending");
            var scope = f.Service.CreateScope();
            var image = f.GameObject().AddComponent<Image>();
            image.SetMaterial(scope, "m", ResourceBindingOptions.Synchronous);
            f.Loader.CompleteImmediately = false;
            var pending = f.Service.LoadLeaseAsync<TextAsset>("pending");
            UnityEngine.Events.UnityAction callback = () => throw new InvalidOperationException("reset clear failure");
            image.RegisterDirtyMaterialCallback(callback);
            try
            {
                var error = Assert.Throws<InvalidOperationException>(() => f.Service.ForceUnloadAllAssets());
                Assert.That(error.Message, Is.EqualTo("reset clear failure"));
                Assert.That(pending.GetAwaiter().IsCompleted, Is.True, "Pending loads still terminate.");
                Assert.That(f.Result(pending).IsValid, Is.False);
                Assert.That(f.Service.GetStats().ReferenceCount, Is.Zero);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                Assert.That(f.Service.Bindings.Count, Is.Zero);
                f.Tick();
                Assert.That(f.Service.CanLoadResources, Is.True);
                f.AssertInvariants();
            }
            finally { image.UnregisterDirtyMaterialCallback(callback); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrowingPropertyCallbackDuringScopeDisposeKeepsTheScopeConsistent()
        {
            f.Material("m");
            f.Text("t");
            var scope = f.Service.CreateScope();
            scope.Load<TextAsset>("t");
            var image = f.GameObject().AddComponent<Image>();
            image.SetMaterial(scope, "m", ResourceBindingOptions.Synchronous);
            UnityEngine.Events.UnityAction callback = () => throw new InvalidOperationException("dispose clear failure");
            image.RegisterDirtyMaterialCallback(callback);
            Assert.Throws<InvalidOperationException>(scope.Dispose);
            image.UnregisterDirtyMaterialCallback(callback);
            Assert.That(scope.IsAlive, Is.True, "The scope is not released until its bindings are.");
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            Assert.That(f.Info("m").RefCount, Is.Zero);
            f.AssertInvariants();
            scope.Dispose();
            Assert.That(scope.IsAlive, Is.False);
            Assert.That(f.Info("t").RefCount, Is.Zero);
            f.AssertInvariants();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReentrantDisposeFromAPropertyCallbackFreesTheScopeOnce()
        {
            f.Material("m");
            f.Text("t");
            var scope = f.Service.CreateScope();
            scope.Load<TextAsset>("t");
            var first = f.GameObject().AddComponent<Image>();
            var second = f.GameObject().AddComponent<Image>();
            first.SetMaterial(scope, "m", ResourceBindingOptions.Synchronous);
            second.SetMaterial(scope, "m", ResourceBindingOptions.Synchronous);
            int calls = 0;
            UnityEngine.Events.UnityAction callback = () =>
            {
                calls++;
                scope.Dispose();
            };
            first.RegisterDirtyMaterialCallback(callback);
            second.RegisterDirtyMaterialCallback(callback);
            scope.Dispose();
            first.UnregisterDirtyMaterialCallback(callback);
            second.UnregisterDirtyMaterialCallback(callback);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(scope.IsAlive, Is.False);
            var a = f.Service.CreateScope();
            var b = f.Service.CreateScope();
            Assert.That(a.Index, Is.Not.EqualTo(b.Index), "A double free would hand out the same slot twice.");
            Assert.That(f.Info("t").RefCount, Is.Zero);
            Assert.That(f.Info("m").RefCount, Is.Zero);
            f.AssertInvariants();
            a.Dispose();
            b.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator RebindFromAPropertyCallbackLeavesTheLatestBinding()
        {
            f.Sprite("a");
            var b = f.Sprite("b");
            using var heldA = f.Service.LoadLease<Sprite>("a");
            using var heldB = f.Service.LoadLease<Sprite>("b");
            var scope = f.Service.CreateScope();
            var image = f.GameObject().AddComponent<Image>();
            bool rebound = false;
            UnityEngine.Events.UnityAction callback = () =>
            {
                if (rebound) return;
                rebound = true;
                image.SetSprite(scope, "b");
            };
            image.RegisterDirtyVerticesCallback(callback);
            image.SetSprite(scope, "a");
            image.UnregisterDirtyVerticesCallback(callback);
            Assert.That(rebound, Is.True);
            Assert.That(image.sprite, Is.SameAs(b));
            Assert.That(f.Info("a").RefCount, Is.EqualTo(1));
            Assert.That(f.Info("b").RefCount, Is.EqualTo(2));
            f.AssertInvariants();
            scope.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator PendingBindingForADestroyedTargetReleasesItsReference()
        {
            f.Material("m");
            f.Loader.CompleteImmediately = false;
            var scope = f.Service.CreateScope();
            var image = f.GameObject().AddComponent<Image>();
            image.SetMaterial(scope, "m");
            Object.Destroy(image);
            yield return null;
            f.Loader.Complete("m");
            f.Tick();
            Assert.That(f.Info("m").RefCount, Is.Zero);
            Assert.That(f.Service.Bindings.Count, Is.Zero);
            f.AssertInvariants();
        }
    }

    public sealed class AwakeResourceProbe : MonoBehaviour
    {
        internal static Action OnAwake;
        private void Awake() => OnAwake?.Invoke();
    }
}
#endif
