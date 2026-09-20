using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    [PrebuildSetup(typeof(AudioNativeAssetSetup))]
    [PostBuildCleanup(typeof(AudioNativeAssetSetup))]
    public sealed class AudioNativeMemoryTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator ImportedClipsAndSourcesReleaseAcrossRepeatedBundleLoads()
        {
            string path = AudioNativeAssetSetup.BundlePath;
            Assert.That(File.Exists(path), Is.True, "Audio fixture prebuild did not produce the requested bundle.");
            using var f = new AudioFixture(16, voices: 8);
            yield return UnityEngine.Resources.UnloadUnusedAssets();
            var loadTimes = new double[24];
            for (int wave = 0; wave < loadTimes.Length; wave++)
            {
                var policy = (AudioCachePolicy)(1 + wave % 3);
                var ids = LoadAndRelease(f, path, policy, out long nativeBytes, out loadTimes[wave]);
                yield return AudioFixture.Frames();
                yield return UnityEngine.Resources.UnloadUnusedAssets();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var survivors = new HashSet<int>();
                long retainedBytes = 0;
                foreach (var clip in UnityEngine.Resources.FindObjectsOfTypeAll<AudioClip>())
                    if (ids.Contains(clip.GetInstanceID())) { survivors.Add(clip.GetInstanceID()); retainedBytes += Profiler.GetRuntimeMemorySizeLong(clip); }
                Assert.That(survivors, Is.Empty, "Imported native clips survived unload wave " + wave);
                Assert.That(retainedBytes, Is.Zero);
                f.Audio.Shutdown();
                yield return AudioFixture.Frames();
                Assert.That(f.Root.GetComponentsInChildren<AudioSource>(true), Is.Empty);
                AudioFixture.CheckAudioPoolsReleased();
                TestContext.WriteLine($"NATIVE_WAVE,{wave},{policy},{nativeBytes},{retainedBytes},{loadTimes[wave]:F4},{GC.GetTotalMemory(false)}");
                f.Reinitialize();
            }
            Array.Sort(loadTimes);
            TestContext.WriteLine($"NATIVE_LOAD_BATCH_MS,24,{loadTimes[11]:F4},{loadTimes[22]:F4},{loadTimes[23]:F4}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static HashSet<int> LoadAndRelease(AudioFixture f, string path, AudioCachePolicy policy, out long nativeBytes, out double milliseconds)
        {
            var ids = new HashSet<int>();
            var watch = Stopwatch.StartNew();
            var bundle = AssetBundle.LoadFromFile(path);
            Assert.That(bundle, Is.Not.Null);
            nativeBytes = 0;
            bool released = false;
            try
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    string name = "audit-native-" + mode;
                    var clip = bundle.LoadAsset<AudioClip>(name);
                    Assert.That(clip, Is.Not.Null);
                    Assert.That(clip.loadType, Is.EqualTo((AudioClipLoadType)mode));
                    ids.Add(clip.GetInstanceID());
                    nativeBytes += Profiler.GetRuntimeMemorySizeLong(clip);
                    // These objects come from a real bundle and are not added to the fixture's destruction list.
                    f.Loader.Assets.Add(name, clip);
                    var options = new AudioPlayOptions { CachePolicy = policy };
                    for (int i = 0; i < 8; i++) Assert.That(f.Audio.Play((AudioType)mode, name, true, 1, options), Is.Not.Zero);
                    f.CheckActive((AudioType)mode, 8, 8);
                }
                watch.Stop();
                f.CheckOwnership();
                f.Audio.StopAll(false);
                if (policy == AudioCachePolicy.Pin)
                {
                    f.Audio.ClearCache();
                    Assert.That(f.Debug.ClipCacheCount, Is.EqualTo(3));
                    f.Audio.ClearCache(true);
                }
                else if (policy == AudioCachePolicy.Ttl)
                {
                    for (var entry = f.Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext) entry.LastUseTime -= f.Config.ClipCacheTtl + 1;
                    f.Tick();
                }
                Assert.That(f.Loader.LiveHandles, Is.Zero, "Audio must release before the bundle backend is detached.");
                f.CheckOwnership();
                f.Loader.Assets.Clear();
                f.Loader.ClearRequests();
                bundle.Unload(false);
                released = true;
            }
            finally { if (!released) bundle.Unload(true); }
            milliseconds = watch.Elapsed.TotalMilliseconds;
            return ids;
        }
    }
}
