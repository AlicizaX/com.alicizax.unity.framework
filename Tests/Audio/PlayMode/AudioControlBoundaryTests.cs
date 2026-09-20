using System.Collections;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioControlBoundaryTests : AudioTestBase
    {
        [Test]
        public void GlobalMutePreservesPlaybackAndRestoresTheLatestClampedVolume()
        {
            using var f = new AudioFixture();
            ulong handle = f.Audio.Play(AudioType.Music, f.Clip("a"), true);
            f.Audio.Volume = 2f;
            Assert.That(f.Audio.Volume, Is.EqualTo(1f));
            Assert.That(AudioListener.volume, Is.EqualTo(1f));
            f.Audio.Enable = false;
            f.Audio.Volume = 0.25f;
            Assert.That(AudioListener.volume, Is.Zero);
            Assert.That(f.Audio.IsPlaying(handle), Is.True);
            f.Audio.Enable = true;
            Assert.That(AudioListener.volume, Is.EqualTo(0.25f));
            f.Audio.Volume = -1f;
            Assert.That(f.Audio.Volume, Is.Zero);
            Assert.That(AudioListener.volume, Is.Zero);
        }

        [TestCase(AudioType.Sound)]
        [TestCase(AudioType.UISound)]
        [TestCase(AudioType.Music)]
        [TestCase(AudioType.Voice)]
        [TestCase(AudioType.Ambient)]
        public void CategoryVolumeClampAndMuteUpdateOnlyTheRequestedMixerParameter(AudioType type)
        {
            using var f = new AudioFixture();
            string parameter = type + "Volume";
            f.Audio.SetCategoryVolume(type, -1);
            Assert.That(f.Audio.GetCategoryVolume(type), Is.EqualTo(0.0001f));
            Assert.That(f.Mixer.GetFloat(parameter, out float decibels), Is.True);
            Assert.That(decibels, Is.EqualTo(-80f).Within(0.001f));
            f.Audio.SetCategoryVolume(type, 2);
            Assert.That(f.Audio.GetCategoryVolume(type), Is.EqualTo(1));
            f.Audio.SetCategoryEnable(type, false);
            f.Audio.SetCategoryVolume(type, 0.5f);
            f.Mixer.GetFloat(parameter, out decibels);
            Assert.That(decibels, Is.EqualTo(-80f).Within(0.001f));
            f.Audio.SetCategoryEnable(type, true);
            f.Mixer.GetFloat(parameter, out decibels);
            Assert.That(decibels, Is.EqualTo(-6.0206f).Within(0.001f));
            for (int i = 0; i < (int)AudioType.Max; i++)
                if ((AudioType)i != type) Assert.That(f.Audio.GetCategoryVolume((AudioType)i), Is.EqualTo(1f));
        }

        [Test]
        public void WarmupBoundsCapacityWithoutLeasingClipsOrStealingAnActiveVoice()
        {
            using var f = new AudioFixture(voices: 4);
            f.Audio.Warmup(AudioType.Sound, -1);
            f.Audio.Warmup((AudioType)999, 10);
            f.CheckActive(AudioType.Sound, 0, 0);
            f.Audio.Warmup(AudioType.Sound, 2);
            f.CheckActive(AudioType.Sound, 0, 2);
            ulong playing = f.Audio.Play(AudioType.Sound, f.Clip("a"), true);
            f.Audio.Warmup(AudioType.Sound, 100);
            f.Audio.Warmup(AudioType.Sound, 1);
            f.CheckActive(AudioType.Sound, 1, 4);
            Assert.That(f.Audio.IsPlaying(playing), Is.True);
            Assert.That(f.Loader.Loads, Is.Zero);
            f.CheckActive(AudioType.Music, 0, 0);
            f.CheckOwnership();
        }

        [UnityTest]
        public IEnumerator TtlExpiresWhileScaledTimeIsPaused()
        {
            float scale = Time.timeScale;
            using var f = new AudioFixture(ttl: 0.05f);
            try
            {
                Time.timeScale = 0;
                f.Clip("a");
                Assert.That(f.Audio.Preload("a", AudioCachePolicy.Ttl), Is.True);
                Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
                double scaled = Time.timeAsDouble;
                yield return new WaitForSecondsRealtime(0.1f);
                f.Tick();
                Assert.That(Time.timeAsDouble, Is.EqualTo(scaled));
                Assert.That(f.Entry("a"), Is.Null);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
            }
            finally { Time.timeScale = scale; }
        }
    }
}
