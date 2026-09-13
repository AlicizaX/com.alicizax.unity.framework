using System;
using System.Collections;
using System.Collections.Generic;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;
using Object = UnityEngine.Object;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioBusinessTests : AudioTestBase
    {
        [TestCase(AudioType.Sound, AudioCachePolicy.None)]
        [TestCase(AudioType.Sound, AudioCachePolicy.Ttl)]
        [TestCase(AudioType.Sound, AudioCachePolicy.Pin)]
        [TestCase(AudioType.UISound, AudioCachePolicy.None)]
        [TestCase(AudioType.UISound, AudioCachePolicy.Ttl)]
        [TestCase(AudioType.UISound, AudioCachePolicy.Pin)]
        [TestCase(AudioType.Music, AudioCachePolicy.None)]
        [TestCase(AudioType.Music, AudioCachePolicy.Ttl)]
        [TestCase(AudioType.Music, AudioCachePolicy.Pin)]
        [TestCase(AudioType.Voice, AudioCachePolicy.None)]
        [TestCase(AudioType.Voice, AudioCachePolicy.Ttl)]
        [TestCase(AudioType.Voice, AudioCachePolicy.Pin)]
        [TestCase(AudioType.Ambient, AudioCachePolicy.None)]
        [TestCase(AudioType.Ambient, AudioCachePolicy.Ttl)]
        [TestCase(AudioType.Ambient, AudioCachePolicy.Pin)]
        public void CategoryPolicyAndDirectClipOwnership(AudioType category, AudioCachePolicy policy)
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a");
            var options = new AudioPlayOptions { CachePolicy = policy };
            ulong addressHandle = f.Audio.Play(category, "a", true, 0.5f, options);
            ulong borrowedHandle = f.Audio.Play(category, clip, true);
            Assert.That(addressHandle, Is.Not.Zero);
            Assert.That(borrowedHandle, Is.Not.Zero);
            Assert.That(f.Entry("a").RefCount, Is.EqualTo(1));
            f.Audio.Stop(addressHandle);
            Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(policy == AudioCachePolicy.None ? 0 : 1));
            Assert.That(f.Audio.IsPlaying(borrowedHandle), Is.True);
            f.Audio.Stop(borrowedHandle);
            Assert.That(clip, Is.Not.Null, "Caller-owned clips must never be destroyed by Audio.");
            f.CheckIdle();
        }

        [TestCase(0)] [TestCase(1)] [TestCase(31)] [TestCase(32)]
        [TestCase(63)] [TestCase(64)] [TestCase(127)] [TestCase(128)]
        [TestCase(255)] [TestCase(256)]
        public void PriorityBoundariesAndFifoStealing(int priority)
        {
            using var f = new AudioFixture(voices: 2);
            var clip = f.Clip("a");
            AudioFixture.Set(f.Groups[0], "m_SourcePriority", 256);
            var options = new AudioPlayOptions { Priority = priority };
            ulong oldest = f.Audio.Play(AudioType.Sound, clip, true, 1, options);
            ulong newer = f.Audio.Play(AudioType.Sound, clip, true, 1, options);
            ulong latest = f.Audio.Play(AudioType.Sound, clip, true, 1, options);
            Assert.That(f.Audio.IsPlaying(oldest), Is.False);
            Assert.That(f.Audio.IsPlaying(newer), Is.True);
            Assert.That(f.Audio.IsPlaying(latest), Is.True);
            f.Audio.Stop(newer);
            f.Audio.Stop(latest);
            f.CheckIdle();
        }

        [Test]
        public void RandomPriorityChurnMatchesReferenceModel()
        {
            using var f = new AudioFixture(voices: 32);
            var clip = f.Clip("a");
            var active = new List<(ulong handle, int priority)>();
            var random = new System.Random(716);
            for (int i = 0; i < 10000; i++)
            {
                if (active.Count > 0 && random.Next(4) == 0)
                {
                    int index = random.Next(active.Count);
                    Assert.That(f.Audio.Stop(active[index].handle), Is.True);
                    active.RemoveAt(index);
                    continue;
                }
                int priority = random.Next(1, 257);
                int victim = 0;
                for (int j = 1; j < active.Count; j++)
                    if (active[j].priority < active[victim].priority) victim = j;
                var options = new AudioPlayOptions { Priority = priority };
                ulong handle = f.Audio.Play(AudioType.Sound, clip, true, 1, options);
                if (active.Count == 32 && priority < active[victim].priority)
                {
                    Assert.That(handle, Is.Zero);
                    continue;
                }
                if (active.Count == 32)
                {
                    Assert.That(f.Audio.IsPlaying(active[victim].handle), Is.False);
                    active.RemoveAt(victim);
                }
                Assert.That(handle, Is.Not.Zero);
                active.Add((handle, priority));
            }
            foreach (var voice in active) Assert.That(f.Audio.IsPlaying(voice.handle), Is.True);
            f.Audio.StopAll(false);
            f.CheckIdle();
        }

        [Test]
        public void InvalidPlaybackDoesNotStealAnExistingVoice()
        {
            using var f = new AudioFixture(voices: 1);
            var clip = f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Sound, clip, true);
            Assert.That(f.Audio.Play(AudioType.Sound, (string)null), Is.Zero);
            Assert.That(f.Audio.Play(AudioType.Sound, (AudioClip)null), Is.Zero);
            Assert.That(f.Audio.Play((AudioType)999, clip), Is.Zero);
            Assert.That(f.Audio.PlayFollow(AudioType.Sound, clip, null, Vector3.zero), Is.Zero);
            Assert.That(f.Audio.IsPlaying(handle), Is.True);
            Assert.That(f.Audio.Stop(ulong.MaxValue), Is.False);
            Assert.That(f.Audio.SetVolume(0, 1), Is.False);
        }

        [Test]
        public void FadeAndVolumeTransitionsReleaseAtTheirDeadline()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            var options = new AudioPlayOptions { FadeInSeconds = 0.5f, Pitch = 1.25f };
            ulong handle = f.Audio.Play(AudioType.Music, "a", true, 0.8f, options);
            var source = f.Source(handle);
            Assert.That(source.volume, Is.Zero.Within(0.001f));
            Assert.That(source.pitch, Is.EqualTo(1.25f));
            f.Tick(0.25f);
            Assert.That(source.volume, Is.EqualTo(0.4f).Within(0.001f));
            f.Audio.SetVolume(handle, 0.4f, 0.5f);
            f.Tick(0.25f);
            Assert.That(source.volume, Is.EqualTo(0.6f).Within(0.001f));
            f.Audio.Stop(handle, 0.5f);
            f.Tick(0.25f);
            Assert.That(f.Audio.IsPlaying(handle), Is.True);
            f.Tick(0.25f);
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            Assert.That(source.clip, Is.Null);
            f.CheckIdle();
        }

        [UnityTest]
        public IEnumerator NaturalCompletionAndInactiveTargetsReleaseReferences()
        {
            using var f = new AudioFixture();
            f.Clip("short", 0.02f);
            ulong handle = f.Audio.Play(AudioType.Sound, "short");
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            while (f.Audio.IsPlaying(handle) && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                f.Tick();
            }
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            var target = f.Resources.Keep(new GameObject("follow"));
            handle = f.Audio.PlayFollow(AudioType.Ambient, "short", target.transform, Vector3.one, true);
            target.transform.position = new Vector3(2, 3, 4);
            f.Tick();
            Assert.That(f.Source(handle).transform.position, Is.EqualTo(target.transform.position + Vector3.one));
            target.SetActive(false);
            f.Tick();
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            f.CheckIdle();
        }

        [Test]
        public void SpatialOverrideAndCategoryDisableCleanUpOnlyTheirGroup()
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a");
            var spatial = new AudioSpatialOptions { Override = true, SpatialBlend = 0.75f, MinDistance = 3, MaxDistance = 20, RolloffMode = AudioRolloffMode.Linear };
            ulong sound = f.Audio.Play3D(AudioType.Sound, clip, new Vector3(1, 2, 3), true, 1, spatial, default);
            ulong music = f.Audio.Play(AudioType.Music, clip, true);
            var source = f.Source(sound);
            Assert.That(source.minDistance, Is.EqualTo(3));
            Assert.That(source.maxDistance, Is.EqualTo(20));
            Assert.That(source.spatialBlend, Is.EqualTo(0.75f));
            f.Audio.SetCategoryEnable(AudioType.Sound, false);
            Assert.That(f.Audio.IsPlaying(sound), Is.False);
            Assert.That(f.Audio.Play(AudioType.Sound, clip), Is.Zero);
            Assert.That(f.Audio.IsPlaying(music), Is.True);
            f.Audio.SetCategoryEnable(AudioType.Sound, true);
            Assert.That(f.Audio.Play(AudioType.Sound, clip), Is.Not.Zero);
        }

        [UnityTest]
        public IEnumerator EmitterCanReplayAfterNaturalCompletionAndReleasesOnDisable()
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a", 0.02f);
            var host = f.Resources.Keep(new GameObject("emitter"));
            host.SetActive(false);
            var emitter = host.AddComponent<AudioEmitter>();
            AudioFixture.Set(emitter, "m_Clip", clip);
            var mode = typeof(AudioEmitter).GetNestedType("AudioEmitterClipMode", System.Reflection.BindingFlags.NonPublic);
            AudioFixture.Set(emitter, "m_ClipMode", Enum.ToObject(mode, 1));
            AudioFixture.Set(emitter, "m_Loop", false);
            host.SetActive(true);
            ulong first = emitter.Handle;
            Assert.That(emitter.IsPlaying, Is.True);
            f.Source(first).Stop();
            f.Tick();
            Assert.That(emitter.IsPlaying, Is.False);
            emitter.Play();
            Assert.That(emitter.Handle, Is.Not.EqualTo(first));
            Assert.That(emitter.IsPlaying, Is.True);
            host.SetActive(false);
            f.Tick(1);
            f.CheckIdle();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ServiceShutdownReturnsDestroyedSourcesToPool()
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Sound, clip, true);
            var pool = f.Pools.GetObjectPool<AudioSourceObject>("Audio Source Pool");
            Object.Destroy(f.Source(handle).gameObject);
            yield return null;
            AppServices.App.Unregister(f.Audio);
            Assert.That(pool.Count, Is.Zero);
            Assert.That(f.Root, Is.Not.Null, "A borrowed root must survive service shutdown.");
        }
    }
}
