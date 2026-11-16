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
            var info = new AudioServiceDebugInfo();
            f.Debug.FillServiceDebugInfo(info);
            Assert.That(info.DefaultCachePolicy, Is.EqualTo(normalized));
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
            MemoryPool<AudioClipCacheEntry>.Instance.Trim(0);
            Assert.That(entry.Cancellation, Is.Null);
            Assert.Throws<ObjectDisposedException>(() => { var ignored = reusable.Token; });
        }

        [Test]
        public void ConstructionRejectsInvalidArgumentsBeforeCreatingAnyState()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            Assert.That(f.Audio.Preload("a"), Is.True);
            int children = f.Root.transform.childCount;
            var duplicate = (AudioGroupConfig[])f.Groups.Clone();
            duplicate[1] = f.Groups[0];
            var missing = new AudioGroupConfig[f.Groups.Length - 1];
            Array.Copy(f.Groups, missing, missing.Length);
            Assert.Throws<GameFrameworkException>(() => new AudioService(f.Groups, f.Listener, f.Root.transform, null, f.Config));
            Assert.Throws<GameFrameworkException>(() => new AudioService(f.Groups, null, f.Root.transform, f.Mixer, f.Config));
            Assert.Throws<GameFrameworkException>(() => new AudioService(f.Groups, f.Listener, null, f.Mixer, f.Config));
            Assert.Throws<GameFrameworkException>(() => new AudioService(null, f.Listener, f.Root.transform, f.Mixer, f.Config));
            Assert.Throws<GameFrameworkException>(() => new AudioService(Array.Empty<AudioGroupConfig>(), f.Listener, f.Root.transform, f.Mixer, f.Config));
            Assert.Throws<GameFrameworkException>(() => new AudioService(duplicate, f.Listener, f.Root.transform, f.Mixer, f.Config));
            Assert.Throws<GameFrameworkException>(() => new AudioService(missing, f.Listener, f.Root.transform, f.Mixer, f.Config));
            Assert.That(f.Root.transform.childCount, Is.EqualTo(children));
            Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(1));
            Assert.That(f.Audio.Preload("a"), Is.True);
            Assert.That(AppServices.Require<IAudioService>(), Is.SameAs(f.Audio));
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
            var remaining = new System.Collections.Generic.List<AudioLoadRequest>(requests);
            foreach (int index in new[] { 2, 0, 4, 1, 3 })
            {
                entry.RemovePending(requests[index]);
                remaining.Remove(requests[index]);
                Assert.That(requests[index].Entry, Is.Null);
                Assert.That(requests[index].Prev, Is.Null);
                Assert.That(requests[index].Next, Is.Null);
                AudioLoadRequest previous = null;
                int position = 0;
                for (var request = entry.PendingHead; request != null; request = request.Next)
                {
                    Assert.That(request, Is.SameAs(remaining[position++]));
                    Assert.That(request.Prev, Is.SameAs(previous));
                    previous = request;
                }
                Assert.That(position, Is.EqualTo(remaining.Count));
                Assert.That(entry.PendingTail, Is.SameAs(previous));
                MemoryPool.Release(requests[index]);
            }
            Assert.That(entry.PendingHead, Is.Null);
            Assert.That(entry.PendingTail, Is.Null);
            MemoryPool.Release(entry);
        }
    }
}
