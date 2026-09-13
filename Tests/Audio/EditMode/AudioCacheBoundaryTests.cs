using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioCacheBoundaryTests : AudioTestBase
    {
        [Test]
        public void HashCollisionsCanBeRemovedAtEveryChainPosition()
        {
            using var f = new AudioFixture(16);
            var keys = new List<string>();
            for (int i = 0; keys.Count < 16; i++)
            {
                string key = "collision-" + i;
                int hash = 5381;
                foreach (char c in key) hash = unchecked(((hash << 5) + hash) ^ c);
                if ((hash & 31) != 0) continue;
                keys.Add(key); f.Clip(key, 0.001f);
                Assert.That(f.Audio.Preload(key), Is.True);
            }
            foreach (int index in new[] { 0, 15, 7, 1, 14, 6, 2, 13, 5, 3, 12, 4, 11, 8, 10, 9 })
            {
                Assert.That(f.Audio.Unload(keys[index]), Is.True);
                Assert.That(f.Audio.Unload(keys[index]), Is.False);
                f.CheckCache();
            }
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [TestCase(AudioCachePolicy.None)]
        [TestCase(AudioCachePolicy.Ttl)]
        [TestCase(AudioCachePolicy.Pin)]
        [TestCase((AudioCachePolicy)999)]
        public void ServiceDefaultIsNormalizedAndUsed(AudioCachePolicy policy)
        {
            using var f = new AudioFixture(policy: policy);
            f.Clip("a");
            Assert.That(f.Audio.Preload("a", AudioCachePolicy.Default), Is.True);
            var normalized = policy == (AudioCachePolicy)999 ? AudioCachePolicy.Ttl : policy;
            Assert.That(f.Debug.DefaultCachePolicy, Is.EqualTo(normalized));
            Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(normalized == AudioCachePolicy.None ? 0 : 1));
        }

        [Test]
        public void PolicyUpgradeRemovesPinFromEvictionAndNeverDowngradesIt()
        {
            using var f = new AudioFixture(1, ttl: 0);
            f.Clip("a"); f.Clip("b");
            f.Audio.Preload("a", AudioCachePolicy.Ttl);
            f.Audio.Preload("a", AudioCachePolicy.Pin);
            f.Audio.Preload("a", AudioCachePolicy.None);
            f.Tick();
            Assert.That(f.Entry("a").Pinned, Is.True);
            Assert.That(f.Entry("a").InLru, Is.False);
            Assert.That(f.Audio.Preload("b"), Is.False);
            Assert.That(f.Audio.Unload("a"), Is.True);
        }

        [Test]
        public void CachedCancellationSourceIsDisposedWhenEntryIsEvictedFromMemoryPool()
        {
            MemoryPoolRegistry.InitializeMainThread();
            var entry = MemoryPool.Acquire<AudioClipCacheEntry>();
            entry.Cancellation = new CancellationTokenSource();
            entry.Loading = false;
            var reusable = entry.Cancellation;
            MemoryPool.Release(entry);
            Assert.That(entry.Cancellation, Is.SameAs(reusable));
            MemoryPool.RemoveAll<AudioClipCacheEntry>();
            Assert.That(entry.Cancellation, Is.Null);
            Assert.Throws<ObjectDisposedException>(() => { var ignored = reusable.Token; });
        }

        [Test]
        public void InitializationFailureReleasesOldStateAndCanBeRetried()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Audio.Preload("a");
            Assert.Throws<GameFrameworkException>(() => f.Audio.Initialize(f.Groups, f.Listener, f.Root.transform, null, f.Config));
            Assert.That(f.Debug.Initialized, Is.False);
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Root.transform.childCount, Is.Zero);
            f.Reinitialize();
            Assert.That(f.Audio.Preload("a"), Is.True);
        }

        [Test]
        public void PendingListUnlinksHeadMiddleAndTailWithoutLosingRequests()
        {
            MemoryPoolRegistry.InitializeMainThread();
            var entry = MemoryPool.Acquire<AudioClipCacheEntry>();
            var requests = new AudioLoadRequest[5];
            for (int i = 0; i < requests.Length; i++)
            {
                requests[i] = MemoryPool.Acquire<AudioLoadRequest>();
                entry.AddPending(requests[i]);
            }
            foreach (int index in new[] { 2, 0, 4, 1, 3 })
            {
                Assert.That(entry.RemovePending(requests[index]), Is.True);
                Assert.That(entry.RemovePending(requests[index]), Is.False);
                MemoryPool.Release(requests[index]);
            }
            Assert.That(entry.PendingHead, Is.Null);
            Assert.That(entry.PendingTail, Is.Null);
            MemoryPool.Release(entry);
        }
    }
}
