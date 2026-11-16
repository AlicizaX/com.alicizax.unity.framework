#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Reflection;
using AlicizaX.Audio.Runtime;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using AudioType = AlicizaX.Audio.Runtime.AudioType;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AlicizaX.Audio.Tests
{
    [Explicit, Category("Benchmark")]
    public sealed class AudioBenchmarkTests
    {
        private const int Voices = 64;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void PlaybackAndTickMedians()
        {
            Assert.That(AppServices.IsAvailable, Is.False);
            MemoryPoolRegistry.InitializeMainThread();
            var root = new GameObject("audio-benchmark");
            var listener = root.AddComponent<AudioListener>();
            var mixer = Resources.Load<AudioMixer>("AudioMixer");
            var groups = new AudioGroupConfig[(int)AudioType.Max];
            for (int i = 0; i < groups.Length; i++)
            {
                var group = new AudioGroupConfig { AudioType = (AudioType)i };
                typeof(AudioGroupConfig).GetField("m_MixerGroup", Members).SetValue(group, mixer.FindMatchingGroups(((AudioType)i).ToString())[0]);
                typeof(AudioGroupConfig).GetField("m_MaxSourceCount", Members).SetValue(group, Voices);
                typeof(AudioGroupConfig).GetField("m_InitialSourceCount", Members).SetValue(group, Voices);
                groups[i] = group;
            }

            var config = new AudioServiceConfig { ClipCacheCapacity = 32, ClipCacheTtl = 30f };
            var clip = AudioClip.Create("benchmark", 8000, 1, 8000, false);
            var targets = new Transform[Voices];
            for (int i = 0; i < targets.Length; i++) targets[i] = new GameObject("target-" + i).transform;
            var resources = new ResourceService();
            try
            {
                AppServices.GetOrCreateRegistry().Register<IResourceService>(resources);
                IAudioService audio = CreateService(groups, listener, root.transform, mixer, config);
                var tick = (IServiceTickable)audio;
                var handles = new ulong[Voices];

                Measure("play-stop-2d-x1000", () =>
                {
                    for (int i = 0; i < 1000; i++) audio.Stop(audio.Play(AudioType.Sound, clip, true));
                });
                Measure("play-stop-3d-x1000", () =>
                {
                    for (int i = 0; i < 1000; i++) audio.Stop(audio.Play3D(AudioType.Sound, clip, new Vector3(i, 0, 0), true));
                });
                Measure("play-stop-follow-x1000", () =>
                {
                    for (int i = 0; i < 1000; i++) audio.Stop(audio.PlayFollow(AudioType.Ambient, clip, targets[i & (Voices - 1)], Vector3.up, true));
                });

                for (int i = 0; i < Voices; i++) handles[i] = audio.Play(AudioType.Sound, clip, true);
                Measure("steal-full-category-x1000", () =>
                {
                    for (int i = 0; i < 1000; i++) audio.Play(AudioType.Sound, clip, true);
                });
                audio.StopAll(false);

                for (int i = 0; i < Voices; i++) handles[i] = audio.Play(AudioType.Sound, clip, true);
                Measure("tick-64-flat-voices-x1000", () =>
                {
                    for (int i = 0; i < 1000; i++) tick.Tick(0.016f);
                });
                audio.StopAll(false);

                for (int i = 0; i < Voices; i++) handles[i] = audio.PlayFollow(AudioType.Ambient, clip, targets[i], Vector3.up, true);
                Measure("tick-64-follow-voices-x1000", () =>
                {
                    for (int i = 0; i < 1000; i++) tick.Tick(0.016f);
                });
                audio.StopAll(false);

                var fading = new AudioPlayOptions { FadeInSeconds = 100000f };
                for (int i = 0; i < Voices; i++) handles[i] = audio.Play(AudioType.Music, clip, true, 1f, fading);
                Measure("tick-64-fading-voices-x1000", () =>
                {
                    for (int i = 0; i < 1000; i++) tick.Tick(0.016f);
                });
                Measure("set-volume-64-x100", () =>
                {
                    for (int r = 0; r < 100; r++)
                        for (int i = 0; i < Voices; i++) audio.SetVolume(handles[i], (r & 1) == 0 ? 0.5f : 1f);
                });
                audio.StopAll(false);
            }
            finally
            {
                AppServices.Shutdown();
                foreach (var target in targets) Object.DestroyImmediate(target.gameObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(clip);
            }
        }

        private static IAudioService CreateService(AudioGroupConfig[] groups, AudioListener listener, Transform root, AudioMixer mixer, AudioServiceConfig config)
        {
            Type type = typeof(AudioServiceConfig).Assembly.GetType("AlicizaX.Audio.Runtime.AudioService", true);
            ConstructorInfo configured = type.GetConstructor(Members, null,
                new[] { typeof(AudioGroupConfig[]), typeof(AudioListener), typeof(Transform), typeof(AudioMixer), typeof(AudioServiceConfig) }, null);
            if (configured != null)
            {
                var service = (IAudioService)configured.Invoke(new object[] { groups, listener, root, mixer, config });
                AppServices.Register<IAudioService>(service);
                Debug.Log("AUDIOBENCH,version,configured-constructor");
                return service;
            }

            var legacy = (IAudioService)Activator.CreateInstance(type, true);
            type.GetMethod("Initialize", Members).Invoke(legacy, new object[] { groups, listener, root, mixer, config });
            AppServices.Register<IAudioService>(legacy);
            Debug.Log("AUDIOBENCH,version,two-phase-initialize");
            return legacy;
        }

        private static void Measure(string name, Action action)
        {
            action();
            var samples = new double[21];
            var watch = new Stopwatch();
            for (int i = 0; i < samples.Length; i++)
            {
                watch.Restart();
                action();
                watch.Stop();
                samples[i] = watch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(samples);
            Debug.Log($"AUDIOBENCH,{name},median-ms,{samples[10]:F4},p90-ms,{samples[18]:F4}");
        }
    }
}
#endif
