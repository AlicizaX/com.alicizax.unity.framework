using System;
using System.Collections;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;
using Object = UnityEngine.Object;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioLifetimeBoundaryTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator DestroyedLoopClipReleasesPlayersAndCacheUnderEveryPolicy()
        {
            foreach (var policy in new[] { AudioCachePolicy.None, AudioCachePolicy.Ttl, AudioCachePolicy.Pin })
            {
                using var f = new AudioFixture(1, voices: 32, policy: policy);
                var clip = f.Clip("destroyed-loop");
                var handles = new ulong[64];
                for (int i = 0; i < 32; i++)
                {
                    handles[i] = f.Audio.Play(AudioType.Sound, "destroyed-loop", true);
                    handles[i + 32] = f.Audio.Play(AudioType.Music, clip, true);
                }
                Assert.That(handles, Is.All.Not.Zero);
                Object.Destroy(clip);
                yield return null;
                f.Tick();
                foreach (ulong handle in handles) Assert.That(f.Audio.IsPlaying(handle), Is.False, policy.ToString());
                Assert.That(f.Debug.ClipCacheCount, Is.Zero, "An invalid clip cannot remain pinned or enter the idle cache.");
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                f.CheckOwnership();
            }
        }

        [UnityTest]
        public IEnumerator ReleasingDestroyedPlayersDoesNotCancelTheirReplacementLoad()
        {
            using var f = new AudioFixture(1, voices: 2, policy: AudioCachePolicy.None);
            var clip = f.Clip("reload");
            ulong old = f.Audio.Play(AudioType.Sound, "reload", true);
            Object.Destroy(clip);
            yield return null;
            f.Resources.Service.ForceUnloadAllAssets();
            f.Loader.Assets.Remove("reload");
            var replacement = f.Clip("reload");
            f.Loader.CompleteImmediately = false;
            ulong current = f.Audio.PlayAsync(AudioType.Sound, "reload", true);
            Assert.That(current, Is.Not.Zero);
            f.Tick();
            Assert.That(f.Audio.IsPlaying(old), Is.False);
            Assert.That(f.Audio.IsPlaying(current), Is.True);
            Assert.That(f.Entry("reload").CountPending(), Is.EqualTo(1));
            f.CheckOwnership();
            f.Loader.Complete("reload");
            yield return AudioFixture.Frames();
            Assert.That(f.Source(current).clip, Is.SameAs(replacement));
            Assert.That(f.Entry("reload").RefCount, Is.EqualTo(1));
            f.Audio.Stop(current);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FailedReplacementDoesNotRecycleAnEntryStillOwnedByOldPlayers()
        {
            using var f = new AudioFixture(1, voices: 32, policy: AudioCachePolicy.Pin);
            var clip = f.Clip("reload-failure");
            for (int i = 0; i < 32; i++) f.Audio.Play(AudioType.Sound, "reload-failure", true);
            var entry = f.Entry("reload-failure");
            Object.Destroy(clip);
            yield return null;
            f.Resources.Service.ForceUnloadAllAssets();
            f.Loader.Assets.Remove("reload-failure");
            f.Clip("reload-failure");
            f.Loader.CompleteImmediately = false;
            bool? completed = null;
            f.Audio.PreloadAsync("reload-failure", AudioCachePolicy.Pin, success => completed = success);
            f.Loader.Complete("reload-failure", false);
            yield return AudioFixture.Frames();
            Assert.That(completed, Is.False);
            Assert.That(f.Entry("reload-failure"), Is.SameAs(entry));
            Assert.That(entry.RefCount, Is.EqualTo(32));
            f.CheckOwnership();
            f.Tick();
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.CheckOwnership();
        }

        [TestCase(-1f, 1f)]
        [TestCase(0f, 1f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(3f, 3f)]
        public void RequestPitchIsAppliedAndResetWhenTheSourceIsReused(float requested, float expected)
        {
            using var f = new AudioFixture(voices: 1);
            var clip = f.Clip("pitch");
            var options = new AudioPlayOptions { Pitch = requested };
            ulong handle = f.Audio.Play3D(AudioType.Sound, clip, Vector3.one, true, 1, default, options);
            var source = f.Source(handle);
            Assert.That(source.pitch, Is.EqualTo(expected));
            f.Audio.Stop(handle);
            handle = f.Audio.Play(AudioType.Sound, clip, true);
            Assert.That(f.Source(handle), Is.SameAs(source));
            Assert.That(source.pitch, Is.EqualTo(1));
            Assert.That(source.spatialBlend, Is.Zero);
            f.CheckOwnership();
        }

        [Test]
        public void InvalidResourceLeaseCannotRemainPinnedAfterItsLastPlayerStops()
        {
            using var f = new AudioFixture(1, policy: AudioCachePolicy.Pin);
            var clip = f.Clip("revoked");
            ulong handle = f.Audio.Play(AudioType.Music, "revoked", true);
            f.Resources.Service.ForceUnloadAllAssets();
            Assert.That(clip != null, Is.True, "The test backend revokes the lease without destroying the borrowed native clip.");
            Assert.That(f.Entry("revoked").Lease.IsValid, Is.False);
            f.Audio.Stop(handle);
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.CheckOwnership();
        }

        [UnityTest]
        public IEnumerator ComponentDestructionReleasesOwnedServiceWhileResourceWorldSurvives()
        {
            using var f = new AudioFixture(initialVoices: 2);
            Assert.That(AppServices.App.Unregister(f.Audio), Is.True);
            var host = f.Resources.Keep(new GameObject("component-lifetime"));
            host.SetActive(false);
            var component = host.AddComponent<AudioComponent>();
            AudioFixture.Set(component, "m_AudioMixer", f.Mixer);
            AudioFixture.Set(component, "m_AudioListener", f.Listener);
            AudioFixture.Set(component, "m_AudioGroupConfigs", f.Groups);
            host.SetActive(true);
            var service = (AudioService)AppServices.App.Require<IAudioService>();
            f.Clip("playing"); f.Clip("pending");
            Assert.That(service.Play(AudioType.Music, "playing", true), Is.Not.Zero);
            f.Loader.CompleteImmediately = false;
            int callbacks = 0;
            service.PreloadAsync("pending", AudioCachePolicy.Pin, success => { Assert.That(success, Is.False); callbacks++; });
            Object.Destroy(component);
            yield return AudioFixture.Frames();
            Assert.That(AppServices.App.TryGet<IAudioService>(out _), Is.False);
            Assert.That(AppServices.App.Require<AlicizaX.Resource.Runtime.IResourceService>(), Is.SameAs(f.Resources.Service));
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(host.transform.childCount, Is.Zero);
            Assert.That(f.Pools.GetObjectPool<AudioSourceObject>("Audio Source Pool").Count, Is.Zero);
            AudioFixture.CheckAudioPoolsReleased();
        }

        [UnityTest]
        public IEnumerator ThousandsOfPreloadWaitersCompleteOnceAndReleaseEveryRequest()
        {
            foreach (int count in new[] { 128, 1024, 4096 })
            foreach (var policy in new[] { AudioCachePolicy.None, AudioCachePolicy.Ttl, AudioCachePolicy.Pin })
            for (int outcome = 0; outcome < 3; outcome++)
            {
                using var f = new AudioFixture(1);
                f.Clip("batch");
                f.Loader.CompleteImmediately = false;
                int callbacks = 0, successes = 0;
                Action<bool> completed = success => { callbacks++; if (success) successes++; };
                for (int i = 0; i < count; i++) f.Audio.PreloadAsync("batch", policy, completed);
                Assert.That(f.Loader.Loads, Is.EqualTo(1));
                Assert.That(f.Entry("batch").CountPending(), Is.EqualTo(count));
                if (outcome == 2) f.Audio.Shutdown();
                else f.Loader.Complete("batch", outcome == 0);
                yield return AudioFixture.Frames();
                Assert.That(callbacks, Is.EqualTo(count));
                Assert.That(successes, Is.EqualTo(outcome == 0 ? count : 0));
                f.Audio.ClearCache(true);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                f.CheckOwnership();
                AudioFixture.CheckAudioPoolsReleased();
            }
        }
    }
}
