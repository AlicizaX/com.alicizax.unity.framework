using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AlicizaX.Audio.Runtime;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioLeakAcceptanceTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator MixedPlaybackCancellationAndEvictionConserveEveryLedger()
        {
            using var f = new AudioFixture(16, voices: 8);
            var names = new string[48];
            for (int i = 0; i < names.Length; i++) { names[i] = "mixed-" + i; f.Clip(names[i]); }
            var handles = new ulong[64];
            var random = new System.Random(731);
            for (int step = 0; step < 4096; step++)
            {
                int slot = random.Next(handles.Length);
                string name = names[random.Next(names.Length)];
                switch (step % 8)
                {
                    case 0:
                    case 1:
                    case 2:
                        f.Loader.CompleteImmediately = (step & 1) == 0;
                        var options = new AudioPlayOptions { Async = true, Priority = random.Next(1, 257), CachePolicy = (AudioCachePolicy)random.Next(1, 4) };
                        handles[slot] = f.Audio.Play((AudioType)random.Next(5), name, true, 1, options);
                        break;
                    case 3: f.Audio.Stop(handles[slot]); break;
                    case 4:
                        if (f.Loader.Providers.ContainsKey(name)) f.Loader.Complete(name, step % 24 != 4);
                        break;
                    case 5: f.Audio.ClearCache(step % 16 == 5); break;
                    case 6: f.Audio.Unload(name); break;
                    case 7: f.Tick(); break;
                }
                f.CheckOwnership();
                if ((step & 31) == 31) yield return null;
                if ((step & 255) == 255)
                {
                    f.Audio.Shutdown();
                    yield return AudioFixture.Frames();
                    Assert.That(f.Loader.LiveHandles, Is.Zero, "Resources service is still running.");
                    AudioFixture.CheckAudioPoolsReleased();
                    f.Reinitialize();
                }
            }
        }

        [UnityTest]
        public IEnumerator AudioOnlyShutdownLeavesBorrowedResourcesAndOtherServicesAlive()
        {
            using var f = new AudioFixture(32, voices: 32);
            var borrowed = f.Clip("borrowed");
            f.Clip("shared"); f.Clip("pending");
            using var external = f.Resources.Service.LoadLease<AudioClip>("shared");
            var pool = f.Pools.GetObjectPool<AudioSourceObject>("Audio Source Pool");
            Assert.That(f.Audio.Play(AudioType.Music, borrowed, true), Is.Not.Zero);
            Assert.That(f.Audio.Play(AudioType.Sound, "shared", true), Is.Not.Zero);
            f.Loader.CompleteImmediately = false;
            f.Audio.PreloadAsync("pending", AudioCachePolicy.Pin);
            f.Audio.Shutdown();
            yield return AudioFixture.Frames();
            Assert.That(AppServices.App.Require<IResourceService>(), Is.SameAs(f.Resources.Service));
            Assert.That(external.IsValid && borrowed != null, Is.True);
            Assert.That(f.Resources.Info("shared").DirectRefCount, Is.EqualTo(1));
            Assert.That(f.Loader.LiveHandles, Is.EqualTo(1));
            Assert.That(pool.Count, Is.Zero);
            AudioFixture.CheckAudioPoolsReleased();
            Assert.That(f.Root.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            Assert.That(f.Root.transform.childCount, Is.Zero);
            external.Dispose();
            Assert.That(f.Loader.LiveHandles, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PendingCallbacksAndTargetsAreCollectibleWithoutDestroyingResourceService()
        {
            using var f = new AudioFixture();
            f.Clip("pending"); f.Loader.CompleteImmediately = false;
            WeakReference callback = RegisterCallback(f);
            f.Audio.Shutdown();
            yield return AudioFixture.Frames();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.That(callback.IsAlive, Is.False);
            Assert.That(f.Loader.LiveHandles, Is.Zero);
            AudioFixture.CheckAudioPoolsReleased();
            Assert.That(AppServices.App.Require<IResourceService>(), Is.SameAs(f.Resources.Service));
        }

        private sealed class CallbackTarget { internal void Complete(bool success) { } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RegisterCallback(AudioFixture f)
        {
            var target = new CallbackTarget();
            f.Audio.PreloadAsync("pending", AudioCachePolicy.Pin, target.Complete);
            return new WeakReference(target);
        }

        [UnityTest]
        public IEnumerator SceneUnloadReleasesFollowReferencesAndReinitializationDoesNotLeakNativeSources()
        {
            using var f = new AudioFixture(voices: 16);
            f.Clip("a");
            var baseline = SourceIds();
            for (int wave = 0; wave < 24; wave++)
            {
                var scene = SceneManager.CreateScene("audio-wave-" + wave);
                var target = new GameObject("scene-follow-target");
                SceneManager.MoveGameObjectToScene(target, scene);
                for (int i = 0; i < 16; i++) Assert.That(f.Audio.PlayFollow(AudioType.Sound, "a", target.transform, Vector3.zero, true), Is.Not.Zero);
                f.CheckActive(AudioType.Sound, 16, 16);
                yield return SceneManager.UnloadSceneAsync(scene);
                f.Tick();
                f.CheckIdle();
                f.CheckOwnership();
                f.Audio.Shutdown();
                yield return AudioFixture.Frames();
                Assert.That(SourceIds().SetEquals(baseline), Is.True, "Global source inventory grew after scene/shutdown wave " + wave);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                AudioFixture.CheckAudioPoolsReleased();
                f.Reinitialize();
            }
        }

        private static HashSet<int> SourceIds()
        {
            var ids = new HashSet<int>();
            foreach (var source in UnityEngine.Resources.FindObjectsOfTypeAll<AudioSource>()) ids.Add(source.GetInstanceID());
            return ids;
        }
    }
}
