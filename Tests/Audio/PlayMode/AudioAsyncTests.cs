using System;
using System.Collections;
using System.Text.RegularExpressions;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioAsyncTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator ThrowingPreloadCallbackDoesNotSuppressOtherCallbacks()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            int calls = 0;
            f.Audio.PreloadAsync("a", AudioCachePolicy.Ttl, _ => throw new InvalidOperationException("preload callback failure"));
            for (int i = 0; i < 32; i++) f.Audio.PreloadAsync("a", AudioCachePolicy.Ttl, success => { Assert.That(success, Is.True); calls++; });
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: preload callback failure"));
            f.Loader.Complete("a");
            yield return AudioFixture.Frames();
            Assert.That(calls, Is.EqualTo(32));
            Assert.That(f.Entry("a").CountPending(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator CallbackCanClearCacheAndReinitializeWithoutInvalidatingDelivery()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Clip("b"); f.Loader.CompleteImmediately = false;
            int calls = 0;
            f.Audio.PreloadAsync("a", AudioCachePolicy.Ttl, success =>
            {
                Assert.That(success, Is.True);
                f.Audio.ClearCache(true);
                f.Reinitialize();
                Assert.That(f.Audio.Preload("b"), Is.True);
                calls++;
            });
            f.Audio.PreloadAsync("a", AudioCachePolicy.Ttl, success => { Assert.That(success, Is.True); calls++; });
            f.Loader.Complete("a");
            yield return AudioFixture.Frames();
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(f.Entry("a"), Is.Null);
            Assert.That(f.Entry("b"), Is.Not.Null);
            f.CheckCache();
        }

        [UnityTest]
        public IEnumerator AsyncLoaderExceptionCompletesFailureAndPermitsRetry()
        {
            using var f = new AudioFixture(1);
            f.Clip("a"); f.Loader.ThrowOnLoad = true;
            bool? completed = null;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: controlled load failure"));
            f.Audio.PreloadAsync("a", AudioCachePolicy.Pin, success => completed = success);
            yield return AudioFixture.Frames();
            Assert.That(completed, Is.False);
            Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            f.Loader.ThrowOnLoad = false;
            Assert.That(f.Audio.Preload("a"), Is.True);
        }

        [UnityTest]
        public IEnumerator SyncAndAsyncWaitersJoinOneLoadAcrossCategories()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            ulong sound = f.Audio.PlayAsync(AudioType.Sound, "a", true);
            ulong music = f.Audio.Play(AudioType.Music, "a", true);
            Assert.That(f.Audio.Preload("a"), Is.False);
            Assert.That(f.Audio.Unload("a", true), Is.False);
            f.Audio.ClearCache(true);
            Assert.That(f.Loader.Loads, Is.EqualTo(1));
            f.Loader.Complete("a");
            yield return AudioFixture.Frames();
            Assert.That(f.Entry("a").RefCount, Is.EqualTo(2));
            f.Audio.Stop(sound); f.Audio.Stop(music);
            f.CheckIdle();
        }

        [UnityTest]
        public IEnumerator PinSurvivesLastPlayerCancellationUntilExplicitUnload()
        {
            using var f = new AudioFixture();
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            var options = new AudioPlayOptions { Async = true, CachePolicy = AudioCachePolicy.Pin };
            ulong handle = f.Audio.Play(AudioType.Sound, "a", true, 1, options);
            f.Audio.Stop(handle);
            f.Loader.Complete("a");
            yield return AudioFixture.Frames();
            Assert.That(f.Entry("a").IsLoaded, Is.True);
            Assert.That(f.Entry("a").RefCount, Is.Zero);
            Assert.That(f.Audio.Unload("a"), Is.True);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ReinitializeCancelsOldWaiterWhileSharedLoadServesNewRequest()
        {
            using var f = new AudioFixture(1);
            f.Clip("a"); f.Loader.CompleteImmediately = false;
            int oldCalls = 0;
            bool? oldResult = null;
            f.Audio.PreloadAsync("a", AudioCachePolicy.Pin, success => { oldResult = success; oldCalls++; });
            var oldRequest = f.Loader.RequestFor("a");
            f.Reinitialize();
            Assert.That(oldCalls, Is.EqualTo(1));
            Assert.That(oldResult, Is.False);
            int newCalls = 0;
            bool? newResult = null;
            f.Audio.PreloadAsync("a", AudioCachePolicy.Pin, success => { newResult = success; newCalls++; });
            var newRequest = f.Loader.RequestFor("a");
            Assert.That(newRequest, Is.SameAs(oldRequest));
            f.Loader.Complete(newRequest);
            yield return AudioFixture.Frames();
            Assert.That(newCalls, Is.EqualTo(1));
            Assert.That(newResult, Is.True);
            Assert.That(oldCalls, Is.EqualTo(1));
            Assert.That(newCalls, Is.EqualTo(1));
            Assert.That(f.Entry("a").IsLoaded, Is.True);
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CancelAndRetryStormLeavesNoPendingHandles()
        {
            using var f = new AudioFixture(64, voices: 64);
            for (int i = 0; i < 64; i++) f.Clip("c" + i, 0.001f);
            f.Loader.CompleteImmediately = false;
            for (int wave = 0; wave < 32; wave++)
            {
                for (int i = 0; i < 64; i++)
                {
                    ulong handle = f.Audio.PlayAsync(AudioType.Sound, "c" + i, true);
                    Assert.That(handle, Is.Not.Zero);
                    f.Audio.Stop(handle, true);
                }
                yield return AudioFixture.Frames(2);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                Assert.That(f.Debug.ClipCacheCount, Is.Zero);
            }
            f.CheckIdle();
        }
    }
}
