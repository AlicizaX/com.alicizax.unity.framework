#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using AlicizaX.Audio.Runtime;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioLoadPerformanceTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator NewAudioAddressesReuseStorageWhenResourceLeasesAreAlreadyResident()
        {
            using var f = new AudioFixture(16);
            var leases = new ResourceAssetLease<AudioClip>[1024];
            var names = new string[leases.Length];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = "resident-" + i;
                f.Clip(names[i], 0.001f);
                leases[i] = f.Resources.Service.LoadLease<AudioClip>(names[i]);
            }
            try
            {
                MemoryPool.RemoveAll<AudioClipCacheEntry>();
                int loaded = 0;
                yield return AllocationCapture.Measure("audio-cold-cache-entry", 1,
                    () => { if (f.Audio.Preload(names[0], AudioCachePolicy.Ttl)) loaded++; },
                    sample => Assert.That(sample.Bytes, Is.LessThanOrEqualTo(16384)), true);
                yield return AllocationCapture.Measure("audio-entry-demand-growth", 15, () =>
                {
                    for (int i = 1; i < 16; i++) if (f.Audio.Preload(names[i], AudioCachePolicy.Ttl)) loaded++;
                }, sample => Assert.That(sample.Bytes, Is.LessThanOrEqualTo(15 * 512)));
                Assert.That(loaded, Is.EqualTo(16));
                yield return AllocationCapture.Measure("audio-1008-new-addresses", 1008, () =>
                {
                    for (int i = 16; i < names.Length; i++) if (f.Audio.Preload(names[i], AudioCachePolicy.Ttl)) loaded++;
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(loaded, Is.EqualTo(names.Length));
                Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(16));
                int callbacks = 0;
                Action<bool> completed = success => { if (success) callbacks++; };
                yield return AllocationCapture.Measure("audio-cached-async-preload", 10000, () =>
                {
                    for (int i = 0; i < 10000; i++) f.Audio.PreloadAsync(names[1023], AudioCachePolicy.Ttl, completed);
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(callbacks, Is.EqualTo(10000));
                Assert.That(f.Loader.Loads, Is.EqualTo(1024));
                f.CheckCache();
            }
            finally
            {
                foreach (var lease in leases) lease.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator PreloadCallbackBatchDoesNotAllocateMulticastDelegates()
        {
            using var f = new AudioFixture();
            var complete = (Func<AudioClipCacheEntry, bool, AudioLoadRequest>)Delegate.CreateDelegate(
                typeof(Func<AudioClipCacheEntry, bool, AudioLoadRequest>), typeof(AudioService).GetMethod("CompleteLoadRequests", BindingFlags.NonPublic | BindingFlags.Static));
            var callbacks = (Action<AudioLoadRequest, bool>)Delegate.CreateDelegate(
                typeof(Action<AudioLoadRequest, bool>), typeof(AudioService).GetMethod("CompletePreloads", BindingFlags.NonPublic | BindingFlags.Static));
            int calls = 0;
            Action<bool> callback = _ => calls++;
            var entry = MemoryPool.Acquire<AudioClipCacheEntry>();
            try
            {
                for (int i = 0; i < 1024; i++)
                {
                    var request = MemoryPool.Acquire<AudioLoadRequest>();
                    request.Completed = callback;
                    entry.AddPending(request);
                }
                yield return AllocationCapture.Measure("audio-dispatch-1024-preloads", 1024,
                    () => callbacks(complete(entry, true), true), sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(calls, Is.EqualTo(1024));
            }
            finally { MemoryPool.Release(entry); }
        }

        [UnityTest]
        public IEnumerator SuspendedLoadsAndRepeatedCancellationHaveMeasuredCosts()
        {
            using var f = new AudioFixture(16);
            f.Clip("a");
            f.Loader.CompleteImmediately = false;
            for (int pass = 0; pass < 3; pass++)
            {
                int callbacks = 0;
                Action<bool> completed = _ => callbacks++;
                yield return AllocationCapture.Measure("audio-pending-first-waiter-pass-" + pass, 1,
                    () => f.Audio.PreloadAsync("a", AudioCachePolicy.Ttl, completed),
                    sample => Assert.That(sample.Bytes - sample.BackendBytes, Is.LessThanOrEqualTo(pass == 0 ? 16384 : 512)));
                yield return AllocationCapture.Measure("audio-pending-64-waiters-pass-" + pass, 64, () =>
                {
                    for (int i = 0; i < 64; i++) f.Audio.PreloadAsync("a", AudioCachePolicy.Ttl, completed);
                }, sample => { if (pass > 0) Assert.That(sample.Bytes, Is.Zero); });
                f.Loader.Complete("a");
                yield return AudioFixture.Frames();
                Assert.That(callbacks, Is.EqualTo(65));
                f.Audio.Unload("a");
            }
            var options = new AudioPlayOptions { Async = true };
            for (int pass = 0; pass < 3; pass++)
            {
                yield return AllocationCapture.Measure("audio-cancel-miss-pass-" + pass, 1, () =>
                {
                    ulong handle = f.Audio.Play(AlicizaX.Audio.Runtime.AudioType.Sound, "a", true, 1, options);
                    f.Audio.Stop(handle);
                }, sample => Assert.That(sample.Bytes - sample.BackendBytes, Is.LessThanOrEqualTo(pass == 0 ? 16384 : 128)));
                yield return AudioFixture.Frames();
                Assert.That(f.Loader.LiveHandles, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator CacheMaintenanceCostIsProportionalToReleasedEntries()
        {
            foreach (int capacity in new[] { 16, 256, 4096 })
            {
                using var f = new AudioFixture(capacity, ttl: 1);
                for (int i = 0; i < capacity; i++)
                {
                    string name = "expiry-" + i;
                    f.Clip(name, 0.001f);
                    f.Audio.Preload(name, AudioCachePolicy.Ttl);
                }
                for (var entry = f.Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext) entry.LastUseTime -= 5;
                yield return AllocationCapture.Measure("audio-expire-" + capacity, capacity,
                    () => f.Tick(), sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(f.Debug.ClipCacheCount, Is.Zero);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                yield return AllocationCapture.Measure("audio-idle-tick-capacity-" + capacity, 10000, () =>
                {
                    for (int i = 0; i < 10000; i++) f.Tick();
                }, sample => Assert.That(sample.Bytes, Is.Zero));
            }
        }
    }
}
#endif
