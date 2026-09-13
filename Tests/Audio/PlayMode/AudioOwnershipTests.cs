using System;
using System.Collections;
using AlicizaX.Audio.Runtime;
using AudioType = AlicizaX.Audio.Runtime.AudioType;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioOwnershipTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator SharedClipRetainsExactlyOneResourceLease()
        {
            using var f = new AudioFixture(4, voices: 32);
            f.Clip("a");
            var handles = new ulong[32];
            for (int i = 0; i < handles.Length; i++) handles[i] = f.Audio.Play(AudioType.Sound, "a", true);
            Assert.That(f.Entry("a").RefCount, Is.EqualTo(32));
            Assert.That(f.Resources.Info("a").DirectRefCount, Is.EqualTo(1));
            Assert.That(f.Audio.Unload("a", true), Is.False);
            f.Audio.ClearCache(true);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            foreach (ulong handle in handles) Assert.That(f.Audio.Stop(handle), Is.True);
            f.CheckIdle();
            f.Audio.ClearCache(true);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CancelledLoadCannotCompleteIntoReusedEntry()
        {
            using var f = new AudioFixture(1);
            f.Clip("old"); var expected = f.Clip("new");
            f.Loader.CompleteImmediately = false;
            ulong old = f.Audio.PlayAsync(AudioType.Sound, "old", true);
            f.Audio.Stop(old);
            ulong current = f.Audio.PlayAsync(AudioType.Sound, "new", true);
            f.Loader.Complete("old");
            yield return AudioFixture.Frames();
            var entry = f.Entry("new");
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Loading, Is.True, "Old completion must not finish the new address.");
            f.Loader.Complete("new");
            yield return AudioFixture.Frames();
            Assert.That(f.Entry("new").Clip, Is.SameAs(expected));
            Assert.That(f.Audio.IsPlaying(current), Is.True);
            f.Audio.StopAll(false);
        }

        [UnityTest]
        public IEnumerator LastCancellationReleasesBackendWithoutWaitingForCompletion()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            ulong handle = f.Audio.PlayAsync(AudioType.Sound, "a", true);
            f.Audio.Stop(handle);
            yield return AudioFixture.Frames();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AsyncSingleFlightSurvivesPartialCancellation()
        {
            using var f = new AudioFixture(4, voices: 64);
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            var handles = new ulong[64];
            for (int i = 0; i < handles.Length; i++) handles[i] = f.Audio.PlayAsync(AudioType.Sound, "a", true);
            for (int i = 0; i < 32; i++) f.Audio.Stop(handles[i]);
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            Assert.That(f.Entry("a").CountPending(), Is.EqualTo(32));
            f.Loader.Complete("a");
            yield return AudioFixture.Frames();
            Assert.That(f.Entry("a").RefCount, Is.EqualTo(32));
            f.Audio.StopAll(false);
            f.CheckIdle();
        }

        [UnityTest]
        public IEnumerator DestroyedFollowTargetReleasesLoopAndLease()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            var target = f.Resources.Keep(new GameObject("target"));
            ulong handle = f.Audio.PlayFollow(AudioType.Ambient, "a", target.transform, Vector3.zero, true);
            Object.Destroy(target);
            yield return null;
            f.Tick();
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            f.CheckIdle();
        }

        [UnityTest]
        public IEnumerator DestroyedSourceReleasesLoopAndLease()
        {
            using var f = new AudioFixture();
            f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Sound, "a", true);
            Object.Destroy(f.Source(handle).gameObject);
            yield return null;
            f.Tick();
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            f.CheckIdle();
        }

        [UnityTest]
        public IEnumerator ReinitializationDoesNotReuseLiveHandleIdentity()
        {
            using var f = new AudioFixture();
            var clip = f.Clip("a");
            ulong old = f.Audio.Play(AudioType.Sound, clip, true);
            f.Reinitialize();
            ulong current = f.Audio.Play(AudioType.Sound, clip, true);
            Assert.That(current, Is.Not.EqualTo(old));
            Assert.That(f.Audio.Stop(old), Is.False);
            Assert.That(f.Audio.IsPlaying(current), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShutdownCompletesPendingPreloadsOnceAndRejectsReentry()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            int calls = 0;
            f.Audio.PreloadAsync("a", AudioCachePolicy.Pin, success =>
            {
                calls++;
                Assert.That(success, Is.False);
                Assert.That(f.Audio.Preload("a"), Is.False);
            });
            AppServices.App.Unregister(f.Audio);
            Assert.That(calls, Is.EqualTo(1));
            yield return AudioFixture.Frames();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DefaultPolicyIsAppliedToOrdinaryPlayback()
        {
            using var f = new AudioFixture(policy: AudioCachePolicy.None);
            f.Clip("a");
            ulong handle = f.Audio.Play(AudioType.Sound, "a", true);
            f.Audio.Stop(handle);
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FailedAsyncLoadFreesAgentsAndInvokesAllWaiters()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            ulong handle = f.Audio.PlayAsync(AudioType.Sound, "a", true);
            int callbacks = 0;
            f.Audio.PreloadAsync("a", AudioCachePolicy.Ttl, success => { Assert.That(success, Is.False); callbacks++; });
            f.Loader.Complete("a", false);
            yield return AudioFixture.Frames();
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(f.Audio.IsPlaying(handle), Is.False);
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            f.CheckIdle();
        }
    }
}
