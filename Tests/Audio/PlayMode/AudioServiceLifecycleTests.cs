using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioServiceLifecycleTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator SwitchingFromOwnedToBorrowedRootDestroysOnlyOwnedHierarchy()
        {
            using var f = new AudioFixture(initialVoices: 2);
            f.Audio.Initialize(f.Groups, f.Listener, null, f.Mixer, f.Config);
            var owned = f.Audio.InstanceRoot;
            Assert.That(owned, Is.Not.SameAs(f.Root.transform));
            f.Reinitialize();
            yield return null;
            Assert.That(owned == null, Is.True);
            Assert.That(f.Root != null, Is.True);
            Assert.That(f.Root.transform.childCount, Is.EqualTo((int)AudioType.Max));
        }

        [UnityTest]
        public IEnumerator ReinitializeWavesDoNotAccumulateSourcesOrRoots()
        {
            using var f = new AudioFixture(voices: 16, initialVoices: 2);
            var clip = f.Clip("a");
            for (int wave = 0; wave < 32; wave++)
            {
                for (int i = 0; i < 16; i++) f.Audio.Play(AudioType.Sound, clip, true);
                f.Reinitialize();
                yield return null;
                Assert.That(f.Root.transform.childCount, Is.EqualTo((int)AudioType.Max));
                Assert.That(f.Root.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(10));
                f.CheckIdle();
            }
        }

        [UnityTest]
        public IEnumerator DuplicateComponentRegistrationRollsBackItsPrivateHierarchy()
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Music, clip, true);
            var host = f.Resources.Keep(new GameObject("duplicate-audio"));
            host.SetActive(false);
            var component = host.AddComponent<AudioComponent>();
            AudioFixture.Set(component, "m_AudioMixer", f.Mixer);
            AudioFixture.Set(component, "m_AudioListener", f.Listener);
            AudioFixture.Set(component, "m_AudioGroupConfigs", f.Groups);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Scope .* already contains contract"));
            host.SetActive(true);
            yield return null;
            Assert.That(host.transform.childCount, Is.Zero);
            Assert.That(f.Audio.IsPlaying(handle), Is.True);
            Assert.That(AppServices.App.Require<IAudioService>(), Is.SameAs(f.Audio));
        }

        [UnityTest]
        public IEnumerator ShutdownReentryFromCancellationCallbackDoesNotRepeatCleanup()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Clip("b"); f.Loader.CompleteImmediately = false;
            int callbacks = 0;
            f.Audio.PreloadAsync("a", AudioCachePolicy.Pin, success => { Assert.That(success, Is.False); f.Audio.Shutdown(); callbacks++; });
            f.Audio.PreloadAsync("b", AudioCachePolicy.Pin, success => { Assert.That(success, Is.False); callbacks++; });
            f.Audio.Shutdown();
            Assert.That(callbacks, Is.EqualTo(2));
            yield return AudioFixture.Frames();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ShutdownCallbackCanClearRemainingIdleCache()
        {
            using var f = new AudioFixture();
            f.Clip("pending"); f.Clip("idle"); f.Clip("last");
            f.Loader.CompleteImmediately = false;
            int callbacks = 0;
            f.Audio.PreloadAsync("pending", AudioCachePolicy.Pin, success =>
            {
                Assert.That(success, Is.False);
                f.Audio.ClearCache(true);
                callbacks++;
            });
            f.Loader.CompleteImmediately = true;
            Assert.That(f.Audio.Preload("idle", AudioCachePolicy.Ttl), Is.True);
            f.Loader.CompleteImmediately = false;
            f.Audio.PreloadAsync("last", AudioCachePolicy.Pin, success =>
            {
                Assert.That(success, Is.False);
                callbacks++;
            });
            f.Audio.Shutdown();
            Assert.That(callbacks, Is.EqualTo(2));
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            yield return AudioFixture.Frames();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LowMemoryCannotEvictPlayingOrPendingResources()
        {
            using var f = new AudioFixture();
            f.Clip("playing"); f.Clip("pending");
            ulong handle = f.Audio.Play(AudioType.Sound, "playing", true);
            f.Loader.CompleteImmediately = false;
            f.Audio.PreloadAsync("pending", AudioCachePolicy.Ttl);
            AudioFixture.Call(f.Audio, "OnLowMemory");
            Assert.That(f.Audio.IsPlaying(handle), Is.True);
            Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(2));
            f.Loader.Complete("pending");
            yield return AudioFixture.Frames();
            f.Audio.Stop(handle);
            AudioFixture.Call(f.Audio, "OnLowMemory");
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CancellationCallbackObservesDetachedEntryOwnership()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            ulong handle = f.Audio.PlayAsync(AudioType.Sound, "a", true);
            var entry = f.Entry("a");
            bool detached = false;
            using var registration = entry.Cancellation.Token.Register(() => detached = entry.Owner == null && f.Debug.ClipCacheCount == 0);
            f.Audio.Stop(handle);
            Assert.That(detached, Is.True);
            yield return AudioFixture.Frames();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DisabledAudioSourceReleasesItsLoopReference()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Sound, "a", true);
            f.Source(handle).enabled = false;
            f.Tick();
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            f.CheckIdle();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledSourceCanBeUsedByTheNextPlayback()
        {
            using var f = new AudioFixture(voices: 1);
            f.Clip("a");
            ulong old = f.Audio.Play(AudioType.Sound, "a", true);
            var source = f.Source(old);
            source.enabled = false;
            f.Tick();
            Assert.That(f.Audio.IsPlaying(old), Is.False);
            ulong next = f.Audio.Play(AudioType.Sound, "a", true);
            Assert.That(next, Is.Not.Zero);
            Assert.That(source.enabled, Is.True);
            f.Tick();
            Assert.That(f.Audio.IsPlaying(next), Is.True);
            f.CheckOwnership();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyedSourcesDoNotPoisonCapacityOrAccumulateEmptyObjects()
        {
            using var f = new AudioFixture(voices: 1);
            f.Clip("a");
            for (int wave = 0; wave < 12; wave++)
            {
                ulong old = f.Audio.Play(AudioType.Sound, "a", true);
                Assert.That(old, Is.Not.Zero);
                var source = f.Source(old);
                UnityEngine.Object.Destroy((wave & 1) == 0 ? (UnityEngine.Object)source : source.gameObject);
                yield return null;
                f.Tick();
                Assert.That(f.Audio.IsPlaying(old), Is.False);
                ulong next = f.Audio.Play(AudioType.Sound, "a", true);
                Assert.That(next, Is.Not.Zero);
                Assert.That(f.Source(next), Is.Not.Null);
                yield return null;
                f.CheckActive(AudioType.Sound, 1, 1);
                f.CheckOwnership();
                Assert.That(f.Root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(7));
                f.Audio.Stop(next);
            }
        }

        [UnityTest]
        public IEnumerator FinishedClipDoesNotRetainAReferenceUntilLongFadeInCompletes()
        {
            using var f = new AudioFixture();
            f.Clip("short", 0.04f);
            var options = new AudioPlayOptions { FadeInSeconds = 60, CachePolicy = AudioCachePolicy.None };
            ulong handle = f.Audio.Play(AudioType.Sound, "short", false, 1, options);
            Assert.That(handle, Is.Not.Zero);
            yield return new WaitForSecondsRealtime(0.15f);
            f.Tick(0.15f);
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            f.CheckOwnership();
        }

        [UnityTest]
        public IEnumerator ShutdownRemovesEventAndPoolRootsKeepingServiceAlive()
        {
            WeakReference reference = CreateReleasedService();
            yield return AudioFixture.Frames();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(reference.IsAlive, Is.False, "Service retained by event, async operation, or pooled object after shutdown.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateReleasedService()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            f.Audio.PlayAsync(AudioType.Sound, "a", true);
            return new WeakReference(f.Audio);
        }
    }
}
