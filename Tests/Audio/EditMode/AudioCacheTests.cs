using System;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioCacheTests : AudioTestBase
    {
        [TestCase(AudioCachePolicy.None, 0)]
        [TestCase(AudioCachePolicy.Ttl, 1)]
        [TestCase(AudioCachePolicy.Pin, 1)]
        public void PreloadReportsLoadSuccessRegardlessOfRetention(AudioCachePolicy policy, int retained)
        {
            using var f = new AudioFixture();
            f.Clip("clip");
            Assert.That(f.Audio.Preload("clip", policy), Is.True);
            Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(retained));
            f.Audio.ClearCache(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void CapacityEvictsLeastRecentlyUsedIdleEntry()
        {
            using var f = new AudioFixture(2);
            foreach (string key in new[] { "a", "b", "c" }) f.Clip(key);
            f.Audio.Preload("a", AudioCachePolicy.Ttl);
            f.Audio.Preload("b", AudioCachePolicy.Ttl);
            f.Audio.Preload("a", AudioCachePolicy.Ttl);
            f.Audio.Preload("c", AudioCachePolicy.Ttl);
            Assert.That(f.Entry("b"), Is.Null);
            Assert.That(f.Entry("a"), Is.Not.Null);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(2));
            f.CheckCache();
        }

        [Test]
        public void PinnedCapacityRejectsNewLoadsWithoutLeaking()
        {
            using var f = new AudioFixture(1);
            f.Clip("pin"); f.Clip("new");
            Assert.That(f.Audio.Preload("pin"), Is.True);
            Assert.That(f.Audio.Preload("new"), Is.False);
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            f.Audio.ClearCache();
            Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(1));
            Assert.That(f.Audio.Unload("pin"), Is.True);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [Test]
        public void TtlExpiresOnlyIdleEntriesAndTouchRenewsDeadline()
        {
            using var f = new AudioFixture(ttl: 10);
            f.Clip("old"); f.Clip("recent"); f.Clip("pin");
            f.Audio.Preload("old", AudioCachePolicy.Ttl);
            f.Audio.Preload("recent", AudioCachePolicy.Ttl);
            f.Audio.Preload("pin");
            f.Entry("old").LastUseTime -= 20;
            f.Entry("recent").LastUseTime -= 20;
            f.Audio.Preload("recent", AudioCachePolicy.Ttl);
            f.Tick();
            Assert.That(f.Entry("old"), Is.Null);
            Assert.That(f.Entry("recent"), Is.Not.Null);
            Assert.That(f.Entry("pin"), Is.Not.Null);
        }

        [Test]
        public void LoadExceptionRemovesEntryAndAllowsRetry()
        {
            using var f = new AudioFixture(1);
            f.Clip("a");
            f.Loader.ThrowOnLoad = true;
            Assert.Throws<InvalidOperationException>(() => f.Audio.Preload("a"));
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            f.Loader.ThrowOnLoad = false;
            Assert.That(f.Audio.Preload("a"), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        public void InvalidAddressesDoNotCreateEntries(string address)
        {
            using var f = new AudioFixture();
            Assert.That(f.Audio.Preload(address), Is.False);
            bool? result = null;
            f.Audio.PreloadAsync(address, AudioCachePolicy.Pin, value => result = value);
            Assert.That(result, Is.False);
            Assert.That(f.Audio.Unload(address), Is.False);
            Assert.That(f.Loader.Loads, Is.Zero);
        }

        [Test]
        public void LowMemoryClearsTtlAndPreservesExplicitPins()
        {
            using var f = new AudioFixture();
            f.Clip("ttl"); f.Clip("pin");
            f.Audio.Preload("ttl", AudioCachePolicy.Ttl);
            f.Audio.Preload("pin");
            AudioFixture.Call(f.Audio, "OnLowMemory");
            Assert.That(f.Entry("ttl"), Is.Null);
            Assert.That(f.Entry("pin"), Is.Not.Null);
        }

        [Test]
        public void ResourceIdleCacheIsASeparateRetentionLayer()
        {
            using var f = new AudioFixture();
            f.Resources.Service.IdleAssetCapacity = 4;
            f.Clip("a");
            f.Audio.Preload("a");
            f.Audio.Unload("a");
            Assert.That(f.Resources.Info("a").DirectRefCount, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            f.Resources.Service.UnloadUnusedAssets(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(64)]
        [TestCase(1024)]
        public void CacheChurnMaintainsBoundedOwnership(int capacity)
        {
            using var f = new AudioFixture(capacity);
            for (int i = 0; i < capacity * 3; i++)
            {
                string key = "clip-" + i;
                f.Clip(key, 0.01f);
                Assert.That(f.Audio.Preload(key, AudioCachePolicy.Ttl), Is.True);
            }
            f.CheckCache();
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(capacity));
            f.Audio.ClearCache(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }
    }
}
