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

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceColdStartTests
    {
        [UnityTest]
        public IEnumerator ServiceConstructionAndFirstResourceStorage()
        {
            ResourceService service = null;
            yield return AllocationCapture.Measure("service-construction-first", 1, () => service = new ResourceService(), _ => { }, true);
            using var fixture = new ResourceFixture(service);
            fixture.Text("cold");
            ResourceAssetLease<TextAsset> lease = default;
            yield return AllocationCapture.Measure("cold-sync-load", 1,
                () => lease = service.LoadLease<TextAsset>("cold"), _ => { }, true);
            Assert.That(lease.IsValid, Is.True);
            yield return AllocationCapture.Measure("cold-async-cache-hit", 1,
                () => service.LoadLeaseAsync<TextAsset>("cold").GetAwaiter().GetResult().Dispose(), _ => { }, true);
            string location = "cold";
            string packageName = service.DefaultPackageName;
            yield return AllocationCapture.Measure("cold-stable-cache-hit", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) service.LoadLease<TextAsset>(location, packageName).Dispose();
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(fixture.Loader.Loads, Is.EqualTo(1));
            Assert.That(fixture.Info(location).DirectRefCount, Is.EqualTo(1));
            lease.Dispose();
            service.UnloadUnusedAssets(true);
            Assert.That(fixture.Loader.LiveHandles, Is.Zero);
        }
    }

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
        public IEnumerator SubSpriteReentryPreservesNewBindingAndLayout()
        {
            f.Sprite("atlas");
            var expected = f.Sprite("new");
            var owner = f.Owner();
            var image = owner.gameObject.AddComponent<Image>();
            var expectedSize = new Vector2(81, 62);
            image.rectTransform.sizeDelta = expectedSize;
            f.Loader.CompleteImmediately = false;
            var previous = f.Bindings.BindSubSpriteAsync(owner, image, new ResourceKey("atlas"), "atlas", ResourceBindingOptions.SetNativeSize);
            bool reentered = false;
            image.RegisterDirtyMaterialCallback(() =>
            {
                if (reentered) return;
                reentered = true;
                f.Bindings.BindSprite(owner, image, new ResourceKey("new"));
            });
            f.Loader.Complete("atlas");
            yield return ResourceFixture.Wait(previous, status => Assert.That(status, Is.EqualTo(ResourceBindStatus.StaleOwner)));
            Assert.That(image.sprite, Is.SameAs(expected));
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(expectedSize));
            Assert.That(f.Info("atlas").RefCountTotal, Is.Zero);
            Assert.That(f.Info("new").RefCountTotal, Is.EqualTo(1));
            UnityEngine.Object.Destroy(owner.gameObject);
            yield return null;
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PooledPrefabSurvivesReentrantResetAndCanRegisterAgain()
        {
            var prefab = f.Keep(new GameObject("source"));
            f.Loader.Assets.Add("prefab", prefab);
            f.Material("old");
            var expected = f.Material("new");
            var instance = f.Keep(f.Service.LoadGameObject("prefab"));
            var owner = instance.GetComponent<ResourceOwner>();
            var image = instance.AddComponent<Image>();
            f.Bindings.BindImageMaterial(owner, image, new ResourceKey("old"));
            image.RegisterDirtyMaterialCallback(f.Service.ForceUnloadAllAssets);
            owner.ReleaseBindings();
            image.UnregisterDirtyMaterialCallback(f.Service.ForceUnloadAllAssets);
            Assert.That(owner.IsRegistered, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            instance.SetActive(false);
            yield return null;
            instance.SetActive(true);
            Assert.That(f.Bindings.BindImageMaterial(owner, image, new ResourceKey("new")), Is.EqualTo(ResourceBindStatus.Success));
            Assert.That(image.material, Is.SameAs(expected));
            Assert.That(f.Info("new").BindingRefCount, Is.EqualTo(1));
            UnityEngine.Object.Destroy(instance);
            yield return null;
            f.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }
    }
}
#endif
