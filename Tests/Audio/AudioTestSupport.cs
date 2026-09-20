using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using AlicizaX.Audio.Runtime;
using AudioType = AlicizaX.Audio.Runtime.AudioType;
using AlicizaX.ObjectPool;
using AlicizaX.Resource.Runtime;
using AlicizaX.Resource.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("AlicizaX.Framework.Audio.Editor.Tests")]

namespace AlicizaX.Audio.Tests
{
    public abstract class AudioTestBase
    {
        [TearDown]
        public void ReleaseAudioFixture() => AudioFixture.Active?.Dispose();
    }

    internal sealed class AudioFixture : IDisposable
    {
        internal static AudioFixture Active;
        internal readonly ResourceFixture Resources = new ResourceFixture();
        internal readonly AudioService Audio = new AudioService();
        internal readonly ObjectPoolService Pools = new ObjectPoolService();
        internal readonly AudioGroupConfig[] Groups = new AudioGroupConfig[(int)AudioType.Max];
        internal readonly AudioMixer Mixer;
        internal readonly GameObject Root;
        internal readonly AudioListener Listener;
        internal readonly AudioServiceConfig Config;
        private readonly float listenerVolume = AudioListener.volume;
        private readonly string[] mixerParameters = new string[(int)AudioType.Max];
        private readonly float[] mixerVolumes = new float[(int)AudioType.Max];
        private bool disposed;
        internal IAudioDebugService Debug => Audio;
        internal ControlledLoader Loader => Resources.Loader;

        internal AudioFixture(int capacity = 8, float ttl = 30, int voices = 8, int initialVoices = 0,
            AudioCachePolicy policy = AudioCachePolicy.Ttl, bool initialize = true)
        {
            Assert.That(AppServices.HasWorld, Is.False, "Tests must not take over a running service world.");
            Active = this;
            MemoryPoolRegistry.InitializeMainThread();
            Root = Resources.Keep(new GameObject("Audio audit"));
            Listener = Root.AddComponent<AudioListener>();
            Mixer = UnityEngine.Resources.Load<AudioMixer>("AudioMixer");
            Assert.That(Mixer, Is.Not.Null);
            for (int i = 0; i < Groups.Length; i++)
            {
                mixerParameters[i] = ((AudioType)i) + "Volume";
                Assert.That(Mixer.GetFloat(mixerParameters[i], out mixerVolumes[i]), Is.True);
                var group = new AudioGroupConfig { AudioType = (AudioType)i };
                Set(group, "m_MixerGroup", Mixer.FindMatchingGroups(((AudioType)i).ToString())[0]);
                Set(group, "m_MaxSourceCount", voices);
                Set(group, "m_InitialSourceCount", initialVoices);
                Groups[i] = group;
            }
            Config = new AudioServiceConfig { ClipCacheCapacity = capacity, ClipCacheTtl = ttl, DefaultClipCachePolicy = policy };
            Resources.Service.IdleAssetCapacity = 0;
            var world = AppServices.EnsureWorld();
            world.App.Register<IResourceService>(Resources.Service);
            world.App.Register<IObjectPoolService>(Pools);
            world.App.Register<IAudioService>(Audio);
            if (initialize) Reinitialize();
        }

        internal void Reinitialize(Transform root = null) => Audio.Initialize(Groups, Listener, root != null ? root : Root.transform, Mixer, Config);
        internal AudioClip Clip(string address, float seconds = 1)
        {
            var clip = Resources.Keep(AudioClip.Create(address, (int)(8000 * seconds), 1, 8000, false));
            Loader.Assets.Add(address, clip);
            return clip;
        }
        internal AudioClipCacheEntry Entry(string address)
        {
            for (var entry = Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext)
                if (entry.Address == address) return entry;
            return null;
        }
        internal AudioSource Source(ulong handle)
        {
            foreach (var agent in Read<AudioAgent[]>(Audio, "_handleAgents"))
                if (agent != null && agent.Handle == handle) return Read<AudioSource>(agent, "_source");
            return null;
        }
        internal void CheckActive(AudioType type, int active, int created = -1)
        {
            var info = new AudioCategoryDebugInfo();
            Assert.That(Debug.FillCategoryDebugInfo((int)type, info), Is.True);
            Assert.That(info.ActiveCount, Is.EqualTo(active));
            if (created >= 0) Assert.That(info.CreatedCount, Is.EqualTo(created));
            Assert.That(info.FreeCount + info.ActiveCount, Is.EqualTo(info.CreatedCount));
        }
        internal void Tick(float delta = 0.02f) => ((IServiceTickable)Audio).Tick(delta);
        internal void CheckIdle()
        {
            var info = new AudioServiceDebugInfo();
            Debug.FillServiceDebugInfo(info);
            Assert.That(info.ActiveAgentCount, Is.Zero);
            for (var entry = Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext)
            {
                Assert.That(entry.RefCount, Is.Zero, entry.Address);
                Assert.That(entry.CountPending(), Is.Zero, entry.Address);
            }
        }
        internal void CheckCache()
        {
            int count = 0;
            AudioClipCacheEntry previous = null;
            var seen = new System.Collections.Generic.HashSet<AudioClipCacheEntry>();
            for (var entry = Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext)
            {
                Assert.That(seen.Add(entry), Is.True, "Cache cycle or duplicate entry.");
                Assert.That(entry.AllPrev, Is.SameAs(previous));
                Assert.That(entry.RefCount, Is.GreaterThanOrEqualTo(0));
                if (entry.InLru)
                {
                    Assert.That(entry.RefCount, Is.Zero);
                    Assert.That(entry.Loading, Is.False);
                    Assert.That(entry.Pinned, Is.False);
                    Assert.That(entry.PendingHead, Is.Null);
                }
                previous = entry;
                count++;
            }
            Assert.That(count, Is.EqualTo(Debug.ClipCacheCount));
            Assert.That(count, Is.LessThanOrEqualTo(Debug.ClipCacheCapacity));
            Assert.That(previous, Is.SameAs(Read<AudioClipCacheEntry>(Audio, "_allTail")));
            var lru = new HashSet<AudioClipCacheEntry>();
            previous = null;
            for (var entry = Read<AudioClipCacheEntry>(Audio, "_lruHead"); entry != null; entry = entry.LruNext)
            {
                Assert.That(lru.Add(entry), Is.True, "LRU cycle or duplicate.");
                Assert.That(seen.Contains(entry) && entry.InLru, Is.True);
                Assert.That(entry.LruPrev, Is.SameAs(previous));
                if (previous != null) Assert.That(entry.LastUseTime, Is.GreaterThanOrEqualTo(previous.LastUseTime));
                previous = entry;
            }
            Assert.That(previous, Is.SameAs(Read<AudioClipCacheEntry>(Audio, "_lruTail")));
            foreach (var entry in seen) Assert.That(entry.InLru, Is.EqualTo(lru.Contains(entry)));
            var slots = Read<AudioClipCacheEntry[]>(Audio, "_clipEntries");
            var buckets = Read<int[]>(Audio, "_clipBuckets");
            var hashed = new HashSet<AudioClipCacheEntry>();
            for (int bucket = 0; bucket < buckets.Length; bucket++)
                for (int slot = buckets[bucket]; slot >= 0; slot = slots[slot].HashNextIndex)
                {
                    Assert.That(slot, Is.LessThan(slots.Length));
                    var entry = slots[slot];
                    Assert.That(entry, Is.Not.Null);
                    Assert.That(hashed.Add(entry) && seen.Contains(entry), Is.True, "Hash cycle or detached entry.");
                    Assert.That(entry.SlotIndex, Is.EqualTo(slot));
                    Assert.That(entry.AddressHash & (buckets.Length - 1), Is.EqualTo(bucket));
                }
            Assert.That(hashed.SetEquals(seen), Is.True);
            var freeSlots = Read<int[]>(Audio, "_clipFreeSlots");
            int freeCount = Read<int>(Audio, "_clipFreeCount");
            var free = new HashSet<int>();
            for (int i = 0; i < freeCount; i++)
            {
                int slot = freeSlots[i];
                Assert.That(free.Add(slot), Is.True);
                Assert.That(slots[slot], Is.Null);
            }
            Assert.That(freeCount + count, Is.EqualTo(slots.Length));
        }
        internal void CheckOwnership()
        {
            CheckCache();
            var references = new Dictionary<AudioClipCacheEntry, int>();
            var pending = new HashSet<AudioLoadRequest>();
            var handles = Read<AudioAgent[]>(Audio, "_handleAgents");
            var occupied = new HashSet<AudioAgent>();
            foreach (var category in Read<AudioCategory[]>(Audio, "_categories"))
            {
                if (category == null) continue;
                var agents = Read<AudioAgent[]>(category, "_agents");
                var active = Read<AudioAgent[]>(category, "_activeAgents");
                var free = Read<int[]>(category, "_freeStack");
                int freeCount = Read<int>(category, "_freeCount");
                Assert.That(category.CreatedCount, Is.EqualTo(category.ActiveCount + freeCount));
                var unused = new HashSet<int>();
                for (int i = 0; i < freeCount; i++)
                {
                    Assert.That(unused.Add(free[i]), Is.True);
                    Assert.That(agents[free[i]].ActiveIndex, Is.EqualTo(-1));
                    Assert.That(agents[free[i]].Handle, Is.Zero);
                    Assert.That(Read<AudioClip>(agents[free[i]], "_playingClip"), Is.Null);
                }
                for (int i = 0; i < category.ActiveCount; i++)
                {
                    var agent = active[i];
                    Assert.That(occupied.Add(agent), Is.True);
                    Assert.That(agent.ActiveIndex, Is.EqualTo(i));
                    Assert.That(agent.Handle, Is.Not.Zero);
                    Assert.That(handles[agent.GlobalIndex], Is.SameAs(agent));
                    var entry = Read<AudioClipCacheEntry>(agent, "_clipEntry");
                    if (entry != null) references[entry] = references.TryGetValue(entry, out int n) ? n + 1 : 1;
                    var request = Read<AudioLoadRequest>(agent, "_loadRequest");
                    if (request != null) Assert.That(pending.Add(request), Is.True);
                }
                for (int i = category.ActiveCount; i < active.Length; i++) Assert.That(active[i], Is.Null);
                var priorityAgents = new HashSet<AudioAgent>();
                var tails = Read<AudioAgent[]>(category, "_priorityTails");
                var masks = Read<uint[]>(category, "_priorityMasks");
                uint groups = 0;
                for (int priority = 0; priority < tails.Length; priority++)
                {
                    var tail = tails[priority];
                    Assert.That((masks[priority >> 5] & (1U << (priority & 31))) != 0, Is.EqualTo(tail != null));
                    if (tail == null) continue;
                    groups |= 1U << (priority >> 5);
                    var agent = tail;
                    do
                    {
                        Assert.That(priorityAgents.Add(agent), Is.True, "Priority cycle crosses lists.");
                        Assert.That(occupied.Contains(agent), Is.True);
                        Assert.That(agent.PlaybackPriority, Is.EqualTo(priority));
                        Assert.That(agent.PriorityNext.PriorityPrev, Is.SameAs(agent));
                        agent = agent.PriorityNext;
                    } while (agent != tail);
                }
                Assert.That(priorityAgents.Count, Is.EqualTo(category.ActiveCount));
                Assert.That(Read<uint>(category, "_priorityGroups"), Is.EqualTo(groups));
            }
            int liveHandles = 0;
            foreach (var agent in handles) if (agent != null) { liveHandles++; Assert.That(occupied.Contains(agent), Is.True); }
            Assert.That(liveHandles, Is.EqualTo(occupied.Count));
            for (var entry = Debug.FirstClipCacheEntry; entry != null; entry = entry.AllNext)
            {
                Assert.That(entry.Owner, Is.SameAs(Audio));
                Assert.That(entry.RefCount, Is.EqualTo(references.TryGetValue(entry, out int n) ? n : 0), entry.Address);
                AudioLoadRequest previous = null;
                var requests = new HashSet<AudioLoadRequest>();
                for (var request = entry.PendingHead; request != null; request = request.Next)
                {
                    Assert.That(requests.Add(request), Is.True);
                    Assert.That(request.Entry, Is.SameAs(entry));
                    Assert.That(request.Prev, Is.SameAs(previous));
                    if (request.Agent != null) Assert.That(pending.Remove(request), Is.True);
                    previous = request;
                }
                Assert.That(entry.PendingTail, Is.SameAs(previous));
            }
            Assert.That(pending, Is.Empty, "Agent retained a detached load request.");
        }
        internal static void CheckAudioPoolsReleased()
        {
            var infos = new MemoryPoolInfo[MemoryPool.Count];
            int count = MemoryPool.GetAllMemoryPoolInfos(infos);
            for (int i = 0; i < count; i++)
                if (infos[i].Type.Namespace == typeof(AudioService).Namespace)
                    Assert.That(infos[i].UsingCount, Is.Zero, infos[i].Type.Name);
        }
        internal static T Read<T>(object instance, string name) =>
            (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        internal static void Set(object instance, string name, object value) =>
            instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        internal static void Call(object instance, string name) =>
            instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        internal static IEnumerator Frames(int count = 3)
        {
            for (int i = 0; i < count; i++) yield return null;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Active = null;
            try
            {
                Audio.Shutdown();
                Assert.That(Debug.ClipCacheCount, Is.Zero);
                CheckAudioPoolsReleased();
            }
            finally
            {
                try { AppServices.Shutdown(); }
                finally
                {
                    AudioListener.volume = listenerVolume;
                    try
                    {
                        if (Mixer != null)
                            for (int i = 0; i < mixerParameters.Length; i++)
                                if (mixerParameters[i] != null) Mixer.SetFloat(mixerParameters[i], mixerVolumes[i]);
                    }
                    finally { Resources.Dispose(); }
                }
            }
        }
    }
}
