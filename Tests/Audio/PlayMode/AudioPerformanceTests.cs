#if UNITY_EDITOR
using System;
using System.Collections;
using AlicizaX.Audio.Runtime;
using AudioType = AlicizaX.Audio.Runtime.AudioType;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioPerformanceTests : AudioTestBase
    {
        private static object allocationSink;
        private AudioConfiguration audioConfiguration;

        [UnitySetUp]
        public IEnumerator ConfigureStressVoices()
        {
            audioConfiguration = AudioSettings.GetConfiguration();
            var stressConfiguration = audioConfiguration;
            stressConfiguration.numVirtualVoices = 1024;
            Assert.That(AudioSettings.Reset(stressConfiguration), Is.True);
            yield return AudioFixture.Frames();
        }

        [UnityTearDown]
        public IEnumerator RestoreAudioConfiguration()
        {
            AudioSettings.Reset(audioConfiguration);
            yield return AudioFixture.Frames();
        }

        [UnityTest]
        public IEnumerator AllocationRecorderDetectsKnownAllocation()
        {
            long counterBytes = 0;
            yield return AllocationCapture.Measure("audio-calibration", 1,
                () =>
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    allocationSink = new byte[4096];
                    counterBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                }, sample =>
                {
                    Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(4096));
                    TestContext.WriteLine($"ALLOCATION_COUNTER_CALIBRATION,thread-counter,{counterBytes},profiler,{sample.Bytes}");
                });
            allocationSink = null;
        }

        [UnityTest]
        public IEnumerator CachedPlaybackStopAndReferenceCountsAllocateZeroBytes()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            f.Audio.Preload("a", AudioCachePolicy.Ttl);
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                int accepted = 0, volumes = 0, playing = 0, stopped = 0;
                yield return AllocationCapture.Measure("audio-cached-play-stop", count, () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        ulong handle = f.Audio.Play(AudioType.UISound, "a", true);
                        if (handle != 0) accepted++;
                        if (f.Audio.SetVolume(handle, 0.7f)) volumes++;
                        if (f.Audio.IsPlaying(handle)) playing++;
                        if (f.Audio.Stop(handle)) stopped++;
                    }
                }, sample => { if (count > 100) Assert.That(sample.Bytes, Is.Zero); });
                Assert.That(new[] { accepted, volumes, playing, stopped }, Is.All.EqualTo(count));
                f.CheckOwnership();
            }
            f.CheckIdle();
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator HitsAndRetainReleaseDoNotScaleWithCacheCapacity()
        {
            foreach (int capacity in new[] { 16, 64, 256 })
            {
                using var f = new AudioFixture(capacity);
                var names = new string[capacity];
                var entries = new AudioClipCacheEntry[capacity];
                for (int i = 0; i < capacity; i++)
                {
                    string address = "c" + i;
                    f.Clip(address, 0.001f);
                    f.Audio.Preload(address, AudioCachePolicy.Ttl);
                    names[i] = address;
                    entries[i] = f.Entry(address);
                }
                int hits = 0;
                yield return AllocationCapture.Measure("audio-hit-ref-capacity-" + capacity, 10000, () =>
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        int index = (i * 7919) & (capacity - 1);
                        if (f.Audio.Preload(names[index], AudioCachePolicy.Ttl)) hits++;
                        f.Audio.RetainClip(entries[index]);
                        f.Audio.ReleaseClip(entries[index]);
                    }
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(hits, Is.EqualTo(10000));
                Assert.That(f.Loader.Loads, Is.EqualTo(capacity));
                f.CheckOwnership();
            }
        }

        [UnityTest]
        public IEnumerator FirstUseCapacityGrowthAndColdPoolReuseAreMeasuredSeparately()
        {
            using var f = new AudioFixture(capacity: 64, voices: 64);
            var clip = f.Clip("a");
            MemoryPool.RemoveAll<AudioPlayRequest>();
            ulong first = 0;
            yield return AllocationCapture.Measure("audio-first-request-first-source", 1,
                () => first = f.Audio.Play(AudioType.Sound, clip, true), sample => Assert.That(sample.Bytes, Is.LessThanOrEqualTo(16384)));
            Assert.That(first, Is.Not.Zero);
            f.CheckActive(AudioType.Sound, 1, 1);
            f.Audio.StopAll(false);
            int accepted = 0;
            yield return AllocationCapture.Measure("audio-source-growth-63", 64, () =>
            {
                for (int i = 0; i < 64; i++) if (f.Audio.Play(AudioType.Sound, clip, true) != 0) accepted++;
            }, sample => Assert.That(sample.Bytes, Is.LessThanOrEqualTo(64 * 1024)));
            Assert.That(accepted, Is.EqualTo(64));
            f.CheckActive(AudioType.Sound, 64, 64);
            f.Audio.StopAll(false);
            int stopped = 0;
            yield return AllocationCapture.Measure("audio-reuse-after-demand-growth", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) if (f.Audio.Stop(f.Audio.Play(AudioType.Sound, clip, true))) stopped++;
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(stopped, Is.EqualTo(10000));
            f.CheckOwnership();
        }

        [UnityTest]
        public IEnumerator SaturatedVoiceStealingAndActiveTicksAllocateZeroBytes()
        {
            foreach (int voices in new[] { 16, 64, 256 })
            {
                using var f = new AudioFixture(voices: voices);
                var clip = f.Clip("a");
                var old = new ulong[voices];
                for (int i = 0; i < voices; i++) Assert.That(old[i] = f.Audio.Play(AudioType.Sound, clip, true), Is.Not.Zero);
                f.CheckActive(AudioType.Sound, voices, voices);
                int accepted = 0;
                yield return AllocationCapture.Measure("audio-steal-voices-" + voices, 10000, () =>
                {
                    for (int i = 0; i < 10000; i++) if (f.Audio.Play(AudioType.Sound, clip, true) != 0) accepted++;
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                Assert.That(accepted, Is.EqualTo(10000));
                foreach (ulong handle in old) Assert.That(f.Audio.IsPlaying(handle), Is.False, "Old voice was not stolen.");
                f.CheckActive(AudioType.Sound, voices, voices);
                yield return AllocationCapture.Measure("audio-tick-voices-" + voices, 100, () =>
                {
                    for (int i = 0; i < 100; i++) f.Tick();
                }, sample => Assert.That(sample.Bytes, Is.Zero));
                f.CheckActive(AudioType.Sound, voices, voices);
                f.CheckOwnership();
                f.Audio.StopAll(false);
                f.CheckIdle();
            }
        }
    }
}
#endif
