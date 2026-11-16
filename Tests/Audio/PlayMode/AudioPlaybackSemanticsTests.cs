using System.Collections;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioPlaybackSemanticsTests : AudioTestBase
    {
        [Test]
        public void FadeOutDuringFadeInContinuesFromTheCurrentEnvelope()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Music, "a", true, 1f, new AudioPlayOptions { FadeInSeconds = 1f });
            var source = f.Source(handle);
            f.Tick(0.25f);
            Assert.That(source.volume, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(f.Audio.Stop(handle, 0.5f), Is.True);
            f.Tick(0.25f);
            Assert.That(source.volume, Is.EqualTo(0.125f).Within(0.001f), "fade-out continues downward from the faded-in level");
            Assert.That(f.Audio.IsPlaying(handle), Is.True);
            f.Tick(0.25f);
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            Assert.That(source.clip, Is.Null);
            f.CheckIdle();
            f.CheckOwnership();
        }

        [Test]
        public void StopBeforeAnyFadeInProgressIsImmediate()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Music, "a", true, 1f, new AudioPlayOptions { FadeInSeconds = 1f });
            Assert.That(f.Audio.Stop(handle, 0.5f), Is.True);
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            f.CheckIdle();
            f.CheckOwnership();
        }

        [Test]
        public void VolumeFadeComposesWithFadeInAndSettlesOnTheTarget()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Music, "a", true, 1f, new AudioPlayOptions { FadeInSeconds = 0.5f });
            var source = f.Source(handle);
            Assert.That(f.Audio.SetVolume(handle, 0.5f, 0.5f), Is.True);
            f.Tick(0.25f);
            Assert.That(source.volume, Is.EqualTo(0.75f * 0.5f).Within(0.001f));
            f.Tick(0.25f);
            Assert.That(source.volume, Is.EqualTo(0.5f).Within(0.001f));
            f.Tick(0.25f);
            Assert.That(source.volume, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(f.Audio.SetVolume(handle, 0.2f), Is.True);
            Assert.That(source.volume, Is.EqualTo(0.2f).Within(0.001f));
            f.CheckOwnership();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Play3DWithoutOverrideUsesTheGroupSpatialSettingsForEveryOverload(bool clipOverload)
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a");
            var group = f.Groups[(int)AudioType.Sound];
            AudioFixture.Set(group, "m_SpatialBlend", 0.35f);
            group.minDistance = 3f;
            group.maxDistance = 40f;
            group.audioRolloffMode = AudioRolloffMode.Linear;
            f.Reinitialize();
            var position = new Vector3(1, 2, 3);
            ulong simple = clipOverload
                ? f.Audio.Play3D(AudioType.Sound, clip, position)
                : f.Audio.Play3D(AudioType.Sound, "a", position);
            ulong optioned = clipOverload
                ? f.Audio.Play3D(AudioType.Sound, clip, position, false, 1f, AudioSpatialOptions.Default, AudioPlayOptions.Default)
                : f.Audio.Play3D(AudioType.Sound, "a", position, false, 1f, AudioSpatialOptions.Default, AudioPlayOptions.Default);
            foreach (ulong handle in new[] { simple, optioned })
            {
                Assert.That(handle, Is.Not.Zero);
                var source = f.Source(handle);
                Assert.That(source.spatialBlend, Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(source.minDistance, Is.EqualTo(3f));
                Assert.That(source.maxDistance, Is.EqualTo(40f));
                Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
                Assert.That(Vector3.Distance(source.transform.position, position), Is.LessThan(0.0001f));
            }

            f.CheckOwnership();
        }

        [Test]
        public void SpatialOverrideIsSanitizedBeforeReachingTheSource()
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a");
            var spatial = new AudioSpatialOptions { Override = true, SpatialBlend = 2f, MinDistance = -1f, MaxDistance = 12f, RolloffMode = AudioRolloffMode.Linear };
            ulong handle = f.Audio.Play3D(AudioType.Sound, clip, Vector3.one, false, 1f, spatial, AudioPlayOptions.Default);
            var source = f.Source(handle);
            Assert.That(source.spatialBlend, Is.EqualTo(1f));
            Assert.That(source.minDistance, Is.Zero);
            Assert.That(source.maxDistance, Is.EqualTo(12f));
            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
        }

        [Test]
        public void FlatPlaybackOnARecycledSpatialVoiceIsFlat()
        {
            using var f = new AudioFixture(voices: 1);
            var clip = f.Clip("a");
            ulong spatial = f.Audio.Play3D(AudioType.Sound, clip, new Vector3(10, 0, 0), true);
            Assert.That(f.Source(spatial).spatialBlend, Is.EqualTo(1f));
            f.Audio.Stop(spatial);
            ulong flat = f.Audio.Play(AudioType.Sound, clip, true, 0.6f, new AudioPlayOptions { Pitch = -1f });
            var source = f.Source(flat);
            Assert.That(source.spatialBlend, Is.Zero);
            Assert.That(source.pitch, Is.EqualTo(1f), "non-positive pitch falls back to 1");
            Assert.That(source.volume, Is.EqualTo(0.6f).Within(0.001f));
            f.CheckActive(AudioType.Sound, 1, 1);
            f.CheckOwnership();
        }

        [Test]
        public void FollowOffsetIsExpressedInTheTargetLocalSpace()
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a");
            var target = f.Resources.Keep(new GameObject("follow-target")).transform;
            target.SetPositionAndRotation(new Vector3(5, 0, 0), Quaternion.Euler(0, 90, 0));
            target.localScale = new Vector3(2, 2, 2);
            var offset = new Vector3(0, 0, 1);
            ulong handle = f.Audio.PlayFollow(AudioType.Ambient, clip, target, offset, true);
            var source = f.Source(handle);
            Assert.That(Vector3.Distance(source.transform.position, new Vector3(7, 0, 0)), Is.LessThan(0.0001f));
            target.SetPositionAndRotation(new Vector3(-3, 1, 0), Quaternion.Euler(0, 180, 0));
            f.Tick();
            Assert.That(Vector3.Distance(source.transform.position, target.TransformPoint(offset)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(source.transform.position, new Vector3(-3, 1, -2)), Is.LessThan(0.0001f));
            f.CheckOwnership();
        }

        [Test]
        public void DeactivatedSourceObjectRecoversOnTheNextPlayback()
        {
            using var f = new AudioFixture(voices: 1);
            var clip = f.Clip("a");
            ulong old = f.Audio.Play(AudioType.Sound, clip, true);
            var source = f.Source(old);
            source.gameObject.SetActive(false);
            f.Tick();
            Assert.That(f.Audio.IsPlaying(old), Is.False);
            ulong next = f.Audio.Play(AudioType.Sound, clip, true);
            Assert.That(next, Is.Not.Zero);
            Assert.That(f.Source(next), Is.SameAs(source));
            Assert.That(source.gameObject.activeSelf, Is.True);
            Assert.That(source.enabled, Is.True);
            f.Tick();
            Assert.That(f.Audio.IsPlaying(next), Is.True);
            f.CheckOwnership();
        }

        [UnityTest]
        public IEnumerator VoicesEndingInTheSameTickAreAllReleased()
        {
            using var f = new AudioFixture(voices: 16);
            var clip = f.Clip("short", 0.02f);
            var handles = new ulong[16];
            for (int i = 0; i < handles.Length; i++) handles[i] = f.Audio.Play(AudioType.Sound, clip);
            f.CheckActive(AudioType.Sound, 16, 16);
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            bool anyPlaying = true;
            while (anyPlaying && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                anyPlaying = false;
                foreach (ulong handle in handles) anyPlaying |= f.Source(handle).isPlaying;
            }

            Assert.That(anyPlaying, Is.False);
            f.Tick();
            foreach (ulong handle in handles) Assert.That(f.Audio.IsPlaying(handle), Is.False);
            f.CheckActive(AudioType.Sound, 0, 16);
            f.CheckOwnership();
        }

        [UnityTest]
        public IEnumerator DestroyedFollowTargetsReleaseOnlyTheirOwnVoices()
        {
            using var f = new AudioFixture(voices: 8);
            var clip = f.Clip("a");
            var targets = new GameObject[8];
            var handles = new ulong[8];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = new GameObject("target-" + i);
                handles[i] = f.Audio.PlayFollow(AudioType.Ambient, clip, targets[i].transform, Vector3.zero, true);
            }

            for (int i = 0; i < targets.Length; i += 2) Object.Destroy(targets[i]);
            yield return null;
            f.Tick();
            for (int i = 0; i < handles.Length; i++) Assert.That(f.Audio.IsPlaying(handles[i]), Is.EqualTo((i & 1) != 0));
            f.CheckActive(AudioType.Ambient, 4, 8);
            f.CheckOwnership();
            for (int i = 1; i < targets.Length; i += 2) Object.Destroy(targets[i]);
            yield return null;
            f.Tick();
            f.CheckActive(AudioType.Ambient, 0, 8);
            f.CheckOwnership();
        }

        [UnityTest]
        public IEnumerator FadesCompleteWhileTheScaledClockIsFrozen()
        {
            float timeScale = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                using var f = new AudioFixture();
                f.Clip("a");
                ulong handle = f.Audio.Play(AudioType.Music, "a", true, 1f, new AudioPlayOptions { FadeInSeconds = 0.1f });
                var source = f.Source(handle);
                var tick = (IServiceTickable)f.Audio;
                double deadline = Time.realtimeSinceStartupAsDouble + 3;
                while (source.volume < 1f && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                    Assert.That(Time.deltaTime, Is.Zero);
                    tick.Tick(Time.deltaTime);
                }

                Assert.That(source.volume, Is.EqualTo(1f), "fade-in completes on the unscaled clock");
                Assert.That(f.Audio.Stop(handle, 0.1f), Is.True);
                while (f.Audio.IsPlaying(handle) && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                    tick.Tick(Time.deltaTime);
                }

                Assert.That(f.Audio.IsPlaying(handle), Is.False, "a fade-out requested while paused completes");
                f.CheckIdle();
                f.CheckOwnership();
            }
            finally
            {
                Time.timeScale = timeScale;
            }
        }

        [UnityTest]
        public IEnumerator FadeProgressTracksUnscaledTimeInsteadOfScaledTime()
        {
            float timeScale = Time.timeScale;
            Time.timeScale = 4f;
            try
            {
                using var f = new AudioFixture();
                f.Clip("a");
                const float fadeInSeconds = 0.5f;
                ulong handle = f.Audio.Play(AudioType.Music, "a", true, 1f, new AudioPlayOptions { FadeInSeconds = fadeInSeconds });
                var source = f.Source(handle);
                var tick = (IServiceTickable)f.Audio;
                float unscaled = 0f;
                float scaled = 0f;
                int ticks = 0;
                while (unscaled < 0.1f && ticks < 1000)
                {
                    yield return null;
                    unscaled += Time.unscaledDeltaTime;
                    scaled += Time.deltaTime;
                    tick.Tick(Time.deltaTime);
                    ticks++;
                }

                Assert.That(ticks, Is.GreaterThan(1));
                Assert.That(scaled, Is.GreaterThan(unscaled), "the scaled clock runs faster than the unscaled clock");
                Assert.That(source.volume, Is.EqualTo(Mathf.Min(1f, unscaled / fadeInSeconds)).Within(0.001f));
                f.CheckOwnership();
            }
            finally
            {
                Time.timeScale = timeScale;
            }
        }
    }
}
