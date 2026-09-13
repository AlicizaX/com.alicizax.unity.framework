using System.Collections;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioSpatialLifecycleTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator OcclusionUpdatesFilterAndVolumeAndResetsBeforeReuse()
        {
            using var f = new AudioFixture();
            var group = f.Groups[(int)AudioType.Ambient];
            AudioFixture.Set(group, "m_OcclusionEnabled", true);
            AudioFixture.Set(group, "m_OcclusionCheckInterval", 0f);
            f.Reinitialize();
            f.Clip("a");
            var wall = f.Resources.Keep(new GameObject("wall"));
            wall.transform.position = new Vector3(5, 0, 0);
            wall.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            ulong handle = f.Audio.Play3D(AudioType.Ambient, "a", new Vector3(10, 0, 0), true, 0.8f);
            var source = f.Source(handle);
            var filter = source.GetComponent<AudioLowPassFilter>();
            f.Tick();
            Assert.That(filter.enabled, Is.True);
            Assert.That(filter.cutoffFrequency, Is.EqualTo(group.OcclusionLowPassCutoff));
            Assert.That(source.volume, Is.EqualTo(0.8f * group.OcclusionVolumeMultiplier).Within(0.001f));
            wall.transform.position = new Vector3(5, 20, 0);
            Physics.SyncTransforms();
            f.Tick();
            Assert.That(filter.enabled, Is.False);
            Assert.That(source.volume, Is.EqualTo(0.8f).Within(0.001f));
            wall.transform.position = new Vector3(5, 0, 0);
            Physics.SyncTransforms();
            f.Tick();
            Assert.That(filter.enabled, Is.True);
            f.Audio.Stop(handle);
            Assert.That(filter.enabled, Is.False);
            ulong next = f.Audio.Play(AudioType.Ambient, "a", true);
            f.Tick();
            Assert.That(f.Source(next), Is.SameAs(source));
            Assert.That(filter.enabled, Is.False);
            Assert.That(source.spatialBlend, Is.Zero);
            f.Audio.Stop(next);
            f.CheckIdle();
            yield return null;
        }

        [UnityTest]
        public IEnumerator EmitterTriggerHysteresisHandlesStealingAndListenerDisable()
        {
            using var f = new AudioFixture(voices: 1);
            f.Clip("a");
            var host = f.Resources.Keep(new GameObject("range-emitter"));
            host.SetActive(false);
            var emitter = host.AddComponent<AudioEmitter>();
            AudioFixture.Set(emitter, "m_Address", "a");
            AudioFixture.Set(emitter, "m_Async", false);
            AudioFixture.Set(emitter, "m_UseTriggerRange", true);
            AudioFixture.Set(emitter, "m_StopWithFadeout", false);
            AudioFixture.Set(emitter, "m_TriggerRange", 10f);
            AudioFixture.Set(emitter, "m_TriggerHysteresis", 2f);
            host.transform.position = new Vector3(11, 0, 0);
            host.SetActive(true);
            AudioFixture.Call(emitter, "Update");
            Assert.That(emitter.IsPlaying, Is.False);
            host.transform.position = new Vector3(10, 0, 0);
            AudioFixture.Call(emitter, "Update");
            ulong first = emitter.Handle;
            Assert.That(emitter.IsPlaying, Is.True);
            host.transform.position = new Vector3(11, 0, 0);
            AudioFixture.Call(emitter, "Update");
            Assert.That(emitter.Handle, Is.EqualTo(first));
            host.transform.position = new Vector3(13, 0, 0);
            AudioFixture.Call(emitter, "Update");
            Assert.That(emitter.IsPlaying, Is.False);
            host.transform.position = new Vector3(9, 0, 0);
            AudioFixture.Call(emitter, "Update");
            ulong stolen = f.Audio.Play(AudioType.Ambient, "a", true);
            Assert.That(emitter.IsPlaying, Is.False);
            AudioFixture.Call(emitter, "Update");
            Assert.That(emitter.IsPlaying, Is.True);
            Assert.That(f.Audio.IsPlaying(stolen), Is.False);
            f.Listener.enabled = false;
            AudioFixture.Call(emitter, "Update");
            Assert.That(emitter.IsPlaying, Is.False);
            f.CheckIdle();
            yield return null;
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SpatialFollowingAndOcclusionTicksAllocateZeroBytes()
        {
            using var f = new AudioFixture(voices: 64);
            var group = f.Groups[(int)AudioType.Ambient];
            AudioFixture.Set(group, "m_OcclusionEnabled", true);
            AudioFixture.Set(group, "m_OcclusionCheckInterval", 0f);
            f.Reinitialize();
            var clip = f.Clip("a");
            var target = f.Resources.Keep(new GameObject("moving-target"));
            target.transform.position = new Vector3(20, 0, 0);
            for (int i = 0; i < 64; i++)
                Assert.That(f.Audio.PlayFollow(AudioType.Ambient, clip, target.transform, new Vector3(0, i, 0), true), Is.Not.Zero);
            f.CheckActive(AudioType.Ambient, 64, 64);
            f.Tick();
            yield return AllocationCapture.Measure("audio-follow-occlusion-64-voices", 1000, () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    target.transform.position = new Vector3(20 + (i & 1), 0, 0);
                    f.Tick();
                }
            }, sample => Assert.That(sample.Bytes, Is.Zero));
            f.CheckActive(AudioType.Ambient, 64, 64);
            f.CheckOwnership();
            target.SetActive(false);
            f.Tick();
            f.CheckIdle();
        }
#endif
    }
}
