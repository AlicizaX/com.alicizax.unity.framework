using System;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioService : ServiceBase, IAudioService, IAudioDebugService, IServiceTickable
    {
        private static readonly AudioSpatialOptions DefaultSpatialOptions = default;
        private static readonly AudioPlayOptions DefaultPlayOptions = default;
        private static readonly AudioPlayOptions AsyncPlayOptions = new AudioPlayOptions { Async = true };

        private readonly AudioGroupConfig[] _configs;
        private readonly AudioCategory[] _categories = new AudioCategory[(int)AudioType.Max];
        private readonly AudioHandleTable _handleTable;
        private readonly AudioClipCacheTable _clipTable;
        private readonly AudioMixer _audioMixer;
        private readonly AudioListener _listener;
        private readonly Transform _instanceRoot;
        private readonly float _clipTtl;
        private readonly AudioCachePolicy _defaultCachePolicy;
        private IResourceService _resourceService;
        private float _volume = 1f;
        private bool _enable = true;
        private bool _disposed;

        internal Transform InstanceRoot => _instanceRoot;
        internal Transform ListenerTransform => _listener != null && _listener.enabled && _listener.gameObject.activeInHierarchy
            ? _listener.transform
            : null;
        internal AudioHandleTable Handles => _handleTable;
        internal AudioClipCacheTable ClipTable => _clipTable;
        int IAudioDebugService.CategoryCount => _categories.Length;
        int IAudioDebugService.ClipCacheCount => _clipTable.Count;
        int IAudioDebugService.ClipCacheCapacity => _clipTable.Capacity;
        AudioClipCacheEntry IAudioDebugService.FirstClipCacheEntry => _clipTable.AllHead;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                AudioListener.volume = _enable ? _volume : 0f;
            }
        }

        public bool Enable
        {
            get => _enable;
            set
            {
                _enable = value;
                AudioListener.volume = _enable ? _volume : 0f;
            }
        }

        internal AudioService(AudioGroupConfig[] groupConfigs, AudioListener listener, Transform instanceRoot, AudioMixer audioMixer,
            AudioServiceConfig serviceConfig)
        {
            if (audioMixer == null)
            {
                throw new GameFrameworkException("AudioMixer is not assigned. Please assign an AudioMixer in the inspector.");
            }

            if (listener == null)
            {
                throw new GameFrameworkException("AudioListener is not assigned. Please assign an AudioListener in the inspector.");
            }

            if (instanceRoot == null)
            {
                throw new GameFrameworkException("Audio instance root is invalid.");
            }

            _configs = BuildConfigMap(groupConfigs);
            int totalSourceCount = 0;
            for (int i = 0; i < _configs.Length; i++)
            {
                totalSourceCount += _configs[i].MaxSourceCount;
            }

            _handleTable = new AudioHandleTable(totalSourceCount);
            _clipTable = new AudioClipCacheTable(Mathf.Max(1, serviceConfig.ClipCacheCapacity));
            _listener = listener;
            _instanceRoot = instanceRoot;
            _audioMixer = audioMixer;
            _clipTtl = Mathf.Max(0f, serviceConfig.ClipCacheTtl);
            _defaultCachePolicy = ResolveCachePolicy(serviceConfig.DefaultClipCachePolicy, AudioCachePolicy.Ttl);
        }

        protected override void OnInitialize()
        {
            _resourceService = Require<IResourceService>();
            _instanceRoot.localScale = Vector3.one;
            int globalIndexOffset = 0;
            for (int i = 0; i < _categories.Length; i++)
            {
                _categories[i] = new AudioCategory(this, _configs[i], _audioMixer, _instanceRoot, globalIndexOffset);
                globalIndexOffset += _configs[i].MaxSourceCount;
            }

            Application.lowMemory += OnLowMemory;
        }

        protected override void OnDispose()
        {
            _disposed = true;
            Application.lowMemory -= OnLowMemory;
            for (int i = 0; i < _categories.Length; i++)
            {
                _categories[i].Shutdown();
            }

            while (_clipTable.AllHead != null)
            {
                RemoveClipEntry(_clipTable.AllHead);
            }
        }

        public ulong Play(AudioType type, string path, bool loop = false, float volume = 1f)
            => Play(new AudioPlayRequest(type, path, null, null, Vector3.zero, loop, false, volume, DefaultSpatialOptions, DefaultPlayOptions));

        public ulong PlayAsync(AudioType type, string path, bool loop = false, float volume = 1f)
            => Play(new AudioPlayRequest(type, path, null, null, Vector3.zero, loop, false, volume, DefaultSpatialOptions, AsyncPlayOptions));

        public ulong Play(AudioType type, string path, bool loop, float volume, in AudioPlayOptions options)
            => Play(new AudioPlayRequest(type, path, null, null, Vector3.zero, loop, false, volume, DefaultSpatialOptions, options));

        public ulong Play(AudioType type, AudioClip clip, bool loop = false, float volume = 1f)
            => Play(new AudioPlayRequest(type, null, clip, null, Vector3.zero, loop, false, volume, DefaultSpatialOptions, DefaultPlayOptions));

        public ulong Play(AudioType type, AudioClip clip, bool loop, float volume, in AudioPlayOptions options)
            => Play(new AudioPlayRequest(type, null, clip, null, Vector3.zero, loop, false, volume, DefaultSpatialOptions, options));

        public ulong Play3D(AudioType type, string path, in Vector3 position, bool loop = false, float volume = 1f)
            => Play(new AudioPlayRequest(type, path, null, null, position, loop, true, volume, DefaultSpatialOptions, DefaultPlayOptions));

        public ulong Play3DAsync(AudioType type, string path, in Vector3 position, bool loop = false, float volume = 1f)
            => Play(new AudioPlayRequest(type, path, null, null, position, loop, true, volume, DefaultSpatialOptions, AsyncPlayOptions));

        public ulong Play3D(AudioType type, string path, in Vector3 position, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
            => Play(new AudioPlayRequest(type, path, null, null, position, loop, true, volume, spatial, options));

        public ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, bool loop = false, float volume = 1f)
            => Play(new AudioPlayRequest(type, null, clip, null, position, loop, true, volume, DefaultSpatialOptions, DefaultPlayOptions));

        public ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
            => Play(new AudioPlayRequest(type, null, clip, null, position, loop, true, volume, spatial, options));

        public ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f)
            => target == null ? 0UL : Play(new AudioPlayRequest(type, path, null, target, localOffset, loop, true, volume, DefaultSpatialOptions, DefaultPlayOptions));

        public ulong PlayFollowAsync(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f)
            => target == null ? 0UL : Play(new AudioPlayRequest(type, path, null, target, localOffset, loop, true, volume, DefaultSpatialOptions, AsyncPlayOptions));

        public ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
            => target == null ? 0UL : Play(new AudioPlayRequest(type, path, null, target, localOffset, loop, true, volume, spatial, options));

        public ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f)
            => target == null ? 0UL : Play(new AudioPlayRequest(type, null, clip, target, localOffset, loop, true, volume, DefaultSpatialOptions, DefaultPlayOptions));

        public ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
            => target == null ? 0UL : Play(new AudioPlayRequest(type, null, clip, target, localOffset, loop, true, volume, spatial, options));

        private ulong Play(in AudioPlayRequest request)
        {
            int index = (int)request.Type;
            if (_disposed || (uint)index >= (uint)_categories.Length)
            {
                return 0UL;
            }

            bool hasSource = request.Address == null ? request.Clip != null : request.Address.Length > 0;
            return hasSource ? _categories[index].Play(in request) : 0UL;
        }

        public bool Stop(ulong handle, bool fadeout = false)
        {
            AudioAgent agent = _handleTable.Resolve(handle);
            if (agent == null)
            {
                return false;
            }

            agent.Stop(fadeout);
            return true;
        }

        public bool Stop(ulong handle, float fadeOutSeconds)
        {
            AudioAgent agent = _handleTable.Resolve(handle);
            if (agent == null)
            {
                return false;
            }

            agent.Stop(fadeOutSeconds);
            return true;
        }

        public bool SetVolume(ulong handle, float volume, float fadeSeconds = 0f)
        {
            AudioAgent agent = _handleTable.Resolve(handle);
            if (agent == null)
            {
                return false;
            }

            agent.SetVolume(volume, fadeSeconds);
            return true;
        }

        public bool IsPlaying(ulong handle)
        {
            AudioAgent agent = _handleTable.Resolve(handle);
            return agent != null && agent.IsPlayingState;
        }

        public void Stop(AudioType type, bool fadeout)
        {
            int index = (int)type;
            if ((uint)index < (uint)_categories.Length)
            {
                _categories[index].Stop(fadeout);
            }
        }

        public void StopAll(bool fadeout)
        {
            for (int i = 0; i < _categories.Length; i++)
            {
                _categories[i].Stop(fadeout);
            }
        }

        public void Warmup(AudioType type, int count)
        {
            int index = (int)type;
            if (!_disposed && (uint)index < (uint)_categories.Length)
            {
                _categories[index].Warmup(count);
            }
        }

        public bool Preload(string address, AudioCachePolicy policy = AudioCachePolicy.Ttl)
        {
            if (!TryPreparePreload(address, policy, out AudioClipCacheEntry entry))
            {
                return false;
            }

            if (entry.IsLoaded)
            {
                return true;
            }

            return !entry.Loading && BeginLoad(entry, false);
        }

        public void PreloadAsync(string address, AudioCachePolicy policy, Action<bool> completed = null)
        {
            if (!TryPreparePreload(address, policy, out AudioClipCacheEntry entry))
            {
                completed?.Invoke(false);
                return;
            }

            if (entry.IsLoaded)
            {
                completed?.Invoke(true);
                return;
            }

            AudioLoadRequest request = MemoryPool.Acquire<AudioLoadRequest>();
            request.Completed = completed;
            entry.AddPending(request);

            if (!entry.Loading)
            {
                BeginLoad(entry, true);
            }
        }

        public bool Unload(string address, bool force = false)
        {
            if (string.IsNullOrEmpty(address) || !_clipTable.TryGet(address, AudioClipCacheTable.Hash(address), out AudioClipCacheEntry entry))
                return false;

            // force 只允许取消仅有 preload 等待者的条目，绝不摘除正在播放的 agent 依赖的资源。
            if (!CanUnloadCacheEntry(entry) && (!force || entry.RefCount != 0 || HasAgentWaiter(entry)))
                return false;

            RemoveClipEntry(entry);
            return true;
        }

        public void ClearCache(bool force = false)
        {
            AudioClipCacheEntry entry = _clipTable.AllHead;
            while (entry != null)
            {
                AudioClipCacheEntry next = entry.AllNext;
                if ((force || !entry.Pinned) && CanUnloadCacheEntry(entry))
                {
                    RemoveClipEntry(entry);
                }

                entry = next;
            }
        }

        public float GetCategoryVolume(AudioType type)
        {
            int index = (int)type;
            return (uint)index < (uint)_categories.Length ? _categories[index].Volume : 0f;
        }

        public void SetCategoryVolume(AudioType type, float value)
        {
            int index = (int)type;
            if ((uint)index < (uint)_categories.Length)
            {
                _categories[index].SetVolume(value);
            }
        }

        public bool GetCategoryEnable(AudioType type)
        {
            int index = (int)type;
            return (uint)index < (uint)_categories.Length && _categories[index].Enabled;
        }

        public void SetCategoryEnable(AudioType type, bool value)
        {
            int index = (int)type;
            if ((uint)index < (uint)_categories.Length)
            {
                _categories[index].Enabled = value;
            }
        }

        void IServiceTickable.Tick(float deltaTime)
        {
            Advance(Time.unscaledDeltaTime, Time.realtimeSinceStartup);
        }

        internal void Advance(float unscaledDeltaTime, float now)
        {
            Transform listener = ListenerTransform;
            for (int i = 0; i < _categories.Length; i++)
            {
                _categories[i].Update(unscaledDeltaTime, now, listener);
            }

            while (_clipTable.LruHead != null && now - _clipTable.LruHead.LastUseTime >= _clipTtl)
            {
                RemoveClipEntry(_clipTable.LruHead);
            }
        }

        internal bool RequestClip(string address, bool async, AudioCachePolicy cachePolicy, AudioAgent agent)
        {
            AudioCachePolicy resolvedPolicy = ResolveCachePolicy(cachePolicy, _defaultCachePolicy);
            AudioClipCacheEntry entry = GetOrCreateClipEntry(address, resolvedPolicy);
            if (entry == null)
            {
                return false;
            }

            UpgradeCachePolicy(entry, resolvedPolicy);
            if (entry.IsLoaded)
            {
                agent.OnClipReady(entry);
                return true;
            }

            AudioLoadRequest request = MemoryPool.Acquire<AudioLoadRequest>();
            request.Agent = agent;
            entry.AddPending(request);
            agent.SetLoadRequest(request);
            if (!entry.Loading)
            {
                try
                {
                    BeginLoad(entry, async);
                }
                catch (Exception exception)
                {
                    AlicizaX.Log.Exception(exception);
                }
            }

            return true;
        }

        internal void CancelLoadRequest(AudioLoadRequest request)
        {
            AudioClipCacheEntry entry = request.Entry;
            entry.RemovePending(request);
            MemoryPool.Release(request);
            if (entry.PendingHead == null && entry.RefCount == 0 && !entry.Pinned)
            {
                RemoveClipEntry(entry);
            }
        }

        internal void RetainClip(AudioClipCacheEntry entry)
        {
            entry.RefCount++;
            _clipTable.RemoveFromLru(entry);
        }

        internal void ReleaseClip(AudioClipCacheEntry entry)
        {
            if (--entry.RefCount != 0 || entry.Loading)
            {
                return;
            }

            if (!entry.CacheAfterUse || !entry.IsLoaded)
            {
                RemoveClipEntry(entry);
                return;
            }

            entry.LastUseTime = Time.realtimeSinceStartup;
            if (!entry.Pinned)
            {
                _clipTable.AddToLruTail(entry);
            }
        }

        void IAudioDebugService.FillServiceDebugInfo(AudioServiceDebugInfo info)
        {
            info.Initialized = !_disposed;
            info.Enable = _enable;
            info.Volume = _volume;
            info.CategoryCount = _categories.Length;
            info.ActiveAgentCount = 0;
            info.TotalSourceCount = 0;
            for (int i = 0; i < _categories.Length; i++)
            {
                info.ActiveAgentCount += _categories[i].ActiveCount;
                info.TotalSourceCount += _categories[i].CreatedCount;
            }

            info.HandleCapacity = _handleTable.Capacity;
            info.ClipCacheCount = _clipTable.Count;
            info.ClipCacheCapacity = _clipTable.Capacity;
            info.ClipCacheTtl = _clipTtl;
            info.DefaultCachePolicy = _defaultCachePolicy;
            info.LoadingClipCount = 0;
            info.CachePolicyNoneCount = 0;
            info.CachePolicyTtlCount = 0;
            info.CachePolicyPinCount = 0;
            for (AudioClipCacheEntry entry = _clipTable.AllHead; entry != null; entry = entry.AllNext)
            {
                if (entry.Loading)
                {
                    info.LoadingClipCount++;
                }

                switch (entry.CachePolicy)
                {
                    case AudioCachePolicy.None:
                        info.CachePolicyNoneCount++;
                        break;
                    case AudioCachePolicy.Pin:
                        info.CachePolicyPinCount++;
                        break;
                    default:
                        info.CachePolicyTtlCount++;
                        break;
                }
            }

            info.Listener = _listener;
            info.InstanceRoot = _instanceRoot;
        }

        void IAudioDebugService.FillCategoryDebugInfo(int typeIndex, AudioCategoryDebugInfo info)
        {
            _categories[typeIndex].FillDebugInfo(info);
        }

        bool IAudioDebugService.FillAgentDebugInfo(int typeIndex, int agentIndex, AudioAgentDebugInfo info)
        {
            if (!_categories[typeIndex].TryGetAgent(agentIndex, out AudioAgent agent))
            {
                return false;
            }

            agent.FillDebugInfo(info);
            return true;
        }

        void IAudioDebugService.FillClipCacheDebugInfo(AudioClipCacheEntry entry, AudioClipCacheDebugInfo info)
        {
            entry.FillDebugInfo(info);
        }

        private void OnLowMemory()
        {
            ClearCache();
        }

        private static bool CanUnloadCacheEntry(AudioClipCacheEntry entry)
        {
            return entry.RefCount == 0 && !entry.Loading && entry.PendingHead == null;
        }

        private static bool HasAgentWaiter(AudioClipCacheEntry entry)
        {
            for (AudioLoadRequest request = entry.PendingHead; request != null; request = request.Next)
            {
                if (request.Agent != null)
                    return true;
            }

            return false;
        }

        private bool TryPreparePreload(string address, AudioCachePolicy policy, out AudioClipCacheEntry entry)
        {
            entry = null;
            if (_disposed || string.IsNullOrEmpty(address))
            {
                return false;
            }

            AudioCachePolicy resolvedPolicy = ResolveCachePolicy(policy, _defaultCachePolicy);
            entry = GetOrCreateClipEntry(address, resolvedPolicy);
            if (entry == null)
            {
                return false;
            }

            UpgradeCachePolicy(entry, resolvedPolicy);
            entry.LastUseTime = Time.realtimeSinceStartup;
            _clipTable.MoveLruToTail(entry);
            return true;
        }

        private bool OnClipLoadCompleted(AudioClipCacheEntry entry, ulong version, AssetLease<AudioClip> lease)
        {
            if (entry.Version != version)
            {
                lease.Dispose();
                return false;
            }

            entry.Loading = false;
            AudioClip clip = lease.Asset;
            bool success = clip != null;
            if (success)
            {
                entry.Lease = lease;
                entry.Clip = clip;
            }
            else
            {
                lease.Dispose();
            }

            RetainClip(entry);
            AudioLoadRequest callbacks = CompleteLoadRequests(entry, success);
            ReleaseClip(entry);
            CompletePreloads(callbacks, success);
            return success;
        }

        private static AudioLoadRequest CompleteLoadRequests(AudioClipCacheEntry entry, bool success)
        {
            AudioLoadRequest request = entry.PendingHead;
            entry.PendingHead = null;
            entry.PendingTail = null;
            AudioLoadRequest callbackHead = null;
            AudioLoadRequest callbackTail = null;
            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                request.Entry = null;
                request.Prev = null;
                request.Next = null;
                if (request.Agent != null)
                {
                    if (success)
                    {
                        request.Agent.OnClipReady(entry);
                    }
                    else
                    {
                        request.Agent.OnClipLoadFailed();
                    }
                }

                if (request.Completed == null)
                {
                    MemoryPool.Release(request);
                }
                else
                {
                    if (callbackTail == null)
                    {
                        callbackHead = request;
                    }
                    else
                    {
                        callbackTail.Next = request;
                    }

                    callbackTail = request;
                }

                request = next;
            }

            return callbackHead;
        }

        private static void CompletePreloads(AudioLoadRequest request, bool success)
        {
            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                Action<bool> completed = request.Completed;
                MemoryPool.Release(request);
                try
                {
                    completed(success);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }

                request = next;
            }
        }

        private AudioClipCacheEntry GetOrCreateClipEntry(string address, AudioCachePolicy cachePolicy)
        {
            int hash = AudioClipCacheTable.Hash(address);
            if (_clipTable.TryGet(address, hash, out AudioClipCacheEntry entry))
                return entry;

            int slotIndex = AcquireClipSlot();
            if (slotIndex < 0)
            {
                AlicizaX.Log.Warning($"Audio clip cache is full ({_clipTable.Capacity} slots, all pinned or loading); '{address}' cannot be admitted.");
                return null;
            }

            entry = MemoryPool.Acquire<AudioClipCacheEntry>();
            entry.Initialize(address, hash, cachePolicy, slotIndex);
            _clipTable.Add(entry);
            _clipTable.AddToAllList(entry);
            return entry;
        }

        private void UpgradeCachePolicy(AudioClipCacheEntry entry, AudioCachePolicy policy)
        {
            if (policy == AudioCachePolicy.None)
            {
                return;
            }

            AudioCachePolicy current = entry.CachePolicy;
            if (current == AudioCachePolicy.Pin)
            {
                return;
            }

            if (policy == AudioCachePolicy.Pin)
            {
                entry.CachePolicy = AudioCachePolicy.Pin;
                _clipTable.RemoveFromLru(entry);
                return;
            }

            if (current == AudioCachePolicy.None)
            {
                entry.CachePolicy = AudioCachePolicy.Ttl;
            }
        }

        private static AudioCachePolicy ResolveCachePolicy(AudioCachePolicy policy, AudioCachePolicy fallback)
        {
            return policy is AudioCachePolicy.None or AudioCachePolicy.Ttl or AudioCachePolicy.Pin ? policy : fallback;
        }

        private int AcquireClipSlot()
        {
            int slot = _clipTable.AcquireSlot();
            if (slot >= 0)
                return slot;

            AudioClipCacheEntry victim = _clipTable.LruHead;
            if (victim == null)
                return -1;

            RemoveClipEntry(victim);
            return _clipTable.AcquireSlot();
        }

        private bool BeginLoad(AudioClipCacheEntry entry, bool async)
        {
            entry.Loading = true;
            ulong version = entry.Version;
            if (async)
            {
                entry.Cancellation ??= new CancellationTokenSource();
                LoadClipAsync(entry, version, entry.Cancellation.Token).Forget();
                return true;
            }

            AssetLease<AudioClip> lease;
            try
            {
                lease = _resourceService.LoadLease<AudioClip>(entry.Address);
            }
            catch
            {
                OnClipLoadCompleted(entry, version, default);
                throw;
            }

            return OnClipLoadCompleted(entry, version, lease);
        }

        private async UniTaskVoid LoadClipAsync(AudioClipCacheEntry entry, ulong version, CancellationToken cancellationToken)
        {
            AssetLease<AudioClip> lease;
            try
            {
                lease = await _resourceService.LoadLeaseAsync<AudioClip>(entry.Address, cancellationToken);
            }
            catch (Exception exception)
            {
                AlicizaX.Log.Exception(exception);
                OnClipLoadCompleted(entry, version, default);
                return;
            }

            OnClipLoadCompleted(entry, version, lease);
        }

        private void RemoveClipEntry(AudioClipCacheEntry entry)
        {
            _clipTable.Remove(entry);
            _clipTable.RemoveFromLru(entry);
            _clipTable.RemoveFromAllList(entry);
            _clipTable.ReleaseSlot(entry);
            AudioLoadRequest callbacks = CompleteLoadRequests(entry, false);
            MemoryPool.Release(entry);
            CompletePreloads(callbacks, false);
        }

        private static AudioGroupConfig[] BuildConfigMap(AudioGroupConfig[] configs)
        {
            if (configs == null || configs.Length == 0)
            {
                throw new GameFrameworkException("AudioGroupConfig[] is invalid.");
            }

            var map = new AudioGroupConfig[(int)AudioType.Max];
            for (int i = 0; i < configs.Length; i++)
            {
                AudioGroupConfig config = configs[i];
                if (config == null)
                {
                    continue;
                }

                int index = (int)config.AudioType;
                if ((uint)index < (uint)map.Length)
                {
                    if (map[index] != null)
                    {
                        throw new GameFrameworkException("AudioGroupConfig[] contains duplicate AudioType.");
                    }

                    map[index] = config;
                }
            }

            for (int i = 0; i < map.Length; i++)
            {
                if (map[i] == null)
                {
                    throw new GameFrameworkException("AudioGroupConfig[] must contain every AudioType.");
                }

                if (map[i].MixerGroup == null)
                {
                    throw new GameFrameworkException("AudioGroupConfig.MixerGroup is invalid.");
                }
            }

            return map;
        }
    }
}
