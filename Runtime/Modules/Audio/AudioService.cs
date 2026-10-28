using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.ObjectPool;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioService : ServiceBase, IAudioService, IAudioDebugService, IServiceTickable
    {
        private const string SourcePoolName = "Audio Source Pool";
        private const string InstanceRootName = "[AudioService Instances]";
        private const string SourceObjectName = "AudioSource";
        private const int DefaultCacheCapacity = AudioServiceConfig.DefaultClipCacheCapacity;
        private const float DefaultClipTtl = AudioServiceConfig.DefaultClipCacheTtl;

        private static readonly string[] VolumeParameterNames =
        {
            "SoundVolume",
            "UISoundVolume",
            "MusicVolume",
            "VoiceVolume",
            "AmbientVolume"
        };

        private static readonly string[] CategoryRootNames =
        {
            "Audio Category - Sound",
            "Audio Category - UISound",
            "Audio Category - Music",
            "Audio Category - Voice",
            "Audio Category - Ambient"
        };

        private readonly AudioCategory[] _categories = new AudioCategory[(int)AudioType.Max];
        private readonly float[] _categoryVolumes = new float[(int)AudioType.Max];
        private readonly bool[] _categoryEnables = new bool[(int)AudioType.Max];
        private readonly AudioGroupConfig[] _configByType = new AudioGroupConfig[(int)AudioType.Max];

        private IResourceService _resourceService;
        private IObjectPoolService _objectPoolService;
        private IObjectPool<AudioSourceObject> _sourcePool;
        private AudioMixer _audioMixer;
        private Transform _instanceRoot;
        private readonly AudioHandleTable _handleTable = new AudioHandleTable();
        private readonly AudioClipCacheTable _clipTable = new AudioClipCacheTable();
        private AudioListener _listenerCache;
        private int _clipCacheCapacity = DefaultCacheCapacity;
        private float _clipTtl = DefaultClipTtl;
        private AudioCachePolicy _defaultCachePolicy = AudioCachePolicy.Ttl;
        private float _volume = 1f;
        private bool _enable = true;
        private bool _initialized;
        private bool _isShuttingDown;
        private bool _ownsInstanceRoot;
        private bool _lowMemoryCallbackRegistered;

        internal Transform InstanceRoot => _instanceRoot;
        internal Transform ListenerTransform => _listenerCache != null && _listenerCache.enabled && _listenerCache.gameObject.activeInHierarchy
            ? _listenerCache.transform
            : null;
        int IAudioDebugService.CategoryCount => _categories.Length;
        int IAudioDebugService.ClipCacheCount => _clipTable.Count;
        internal AudioClipCacheTable ClipTable => _clipTable;
        int IAudioDebugService.ClipCacheCapacity => _clipCacheCapacity;
        float IAudioDebugService.ClipCacheTtl => _clipTtl;
        AudioCachePolicy IAudioDebugService.DefaultCachePolicy => _defaultCachePolicy;
        int IAudioDebugService.HandleCapacity => _handleTable.Capacity;
        internal AudioAgent[] HandleAgents => _handleTable.Agents;
        bool IAudioDebugService.Initialized => _initialized;
        AudioClipCacheEntry IAudioDebugService.FirstClipCacheEntry => _clipTable.AllHead;
        public int Priority => 0;

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


        protected override void OnInitialize() { }

        protected override void OnDestroyService()
        {
            Shutdown();
        }

        internal void Initialize(AudioGroupConfig[] audioGroupConfigs, AudioListener audioListener, Transform instanceRoot = null, AudioMixer audioMixer = null, AudioServiceConfig serviceConfig = null)
        {
            if (audioGroupConfigs == null || audioGroupConfigs.Length == 0)
            {
                throw new GameFrameworkException("AudioGroupConfig[] is invalid.");
            }

            if (audioListener == null)
            {
                throw new GameFrameworkException("AudioListener is invalid. Please provide a valid AudioListener.");
            }

            if (_isShuttingDown)
            {
                throw new InvalidOperationException("Audio service is shutting down.");
            }

            Shutdown();

            try
            {
                ApplyServiceConfig(serviceConfig);
                _listenerCache = audioListener;
                BuildConfigMap(audioGroupConfigs);
                InitializeAudioMixer(audioMixer);
                InitializeObjectPools();
                InitializeInstanceRoot(instanceRoot);
                InitializeHandleSystem();
                InitializeCategories();
                _initialized = true;
                RegisterLowMemoryCallback();
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        private void InitializeObjectPools()
        {
            _resourceService = AppServices.App.Require<IResourceService>();
            _objectPoolService = AppServices.App.Require<IObjectPoolService>();
            _sourcePool = _objectPoolService.GetOrCreatePool<AudioSourceObject>(
                new ObjectPoolCreateOptions(SourcePoolName));
        }

        private void ApplyServiceConfig(AudioServiceConfig config)
        {
            if (config == null)
            {
                _clipCacheCapacity = DefaultCacheCapacity;
                _clipTtl = DefaultClipTtl;
                _defaultCachePolicy = AudioCachePolicy.Ttl;
                return;
            }

            _clipCacheCapacity = Mathf.Max(1, config.ClipCacheCapacity);
            _clipTtl = Mathf.Max(0f, config.ClipCacheTtl);
            _defaultCachePolicy = NormalizeDefaultCachePolicy(config.DefaultClipCachePolicy);
        }

        private void InitializeInstanceRoot(Transform instanceRoot)
        {
            if (instanceRoot != null)
            {
                _instanceRoot = instanceRoot;
                _ownsInstanceRoot = false;
            }
            else
            {
                _instanceRoot = new GameObject(InstanceRootName).transform;
                _ownsInstanceRoot = true;
            }

            _instanceRoot.localScale = Vector3.one;

            if (_ownsInstanceRoot)
            {
                UnityEngine.Object.DontDestroyOnLoad(_instanceRoot.gameObject);
            }
        }

        private void InitializeAudioMixer(AudioMixer audioMixer)
        {
            _audioMixer = audioMixer;
            if (_audioMixer == null)
            {
                throw new GameFrameworkException("AudioMixer is invalid. Please provide a valid AudioMixer.");
            }
        }

        private void InitializeHandleSystem()
        {
            int totalHandleCount = 0;
            for (int i = 0; i < (int)AudioType.Max; i++)
            {
                AudioGroupConfig config = _configByType[i];
                totalHandleCount += config.MaxSourceCount;
            }

            _handleTable.Initialize(totalHandleCount);
            InitializeClipCacheTable();
        }

        private void InitializeCategories()
        {
            int globalIndexOffset = 0;
            for (int i = 0; i < (int)AudioType.Max; i++)
            {
                AudioGroupConfig config = _configByType[i];
                _categoryVolumes[i] = Mathf.Clamp(config.Volume, 0.0001f, 1f);
                _categoryEnables[i] = !config.Mute;
                _categories[i] = new AudioCategory(this, config, globalIndexOffset);
                globalIndexOffset += config.MaxSourceCount;
                ApplyMixerVolume(config, _categoryVolumes[i], _categoryEnables[i]);
            }
        }

        public ulong Play(AudioType type, string path, bool loop = false, float volume = 1f)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set2D(type, path, loop, volume, false, true);
            return Play(request);
        }

        public ulong PlayAsync(AudioType type, string path, bool loop = false, float volume = 1f)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set2D(type, path, loop, volume, true, true);
            return Play(request);
        }

        public ulong Play(AudioType type, string path, bool loop, float volume, in AudioPlayOptions options)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set2D(type, path, loop, volume, true, options);
            return Play(request);
        }

        public ulong Play(AudioType type, AudioClip clip, bool loop = false, float volume = 1f)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set2D(type, clip, loop, volume);
            return Play(request);
        }

        public ulong Play(AudioType type, AudioClip clip, bool loop, float volume, in AudioPlayOptions options)
        {
            if (clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set2D(type, clip, loop, volume, options);
            return Play(request);
        }

        public ulong Play3D(AudioType type, string path, in Vector3 position, bool loop = false, float volume = 1f)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, path, position, loop, volume, false, true);
            return Play(request);
        }

        public ulong Play3DAsync(AudioType type, string path, in Vector3 position, bool loop = false, float volume = 1f)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, path, position, loop, volume, true, true);
            return Play(request);
        }

        public ulong Play3D(AudioType type, string path, in Vector3 position, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, path, position, loop, volume, true, spatial, options);
            return Play(request);
        }

        public ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, bool loop = false, float volume = 1f)
        {
            if (clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, clip, position, loop, volume);
            return Play(request);
        }

        public ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
        {
            if (clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, clip, position, loop, volume, spatial, options);
            return Play(request);
        }

        public ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f)
        {
            if (target == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, path, target, localOffset, loop, volume, false, true);
            return Play(request);
        }

        public ulong PlayFollowAsync(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f)
        {
            if (target == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, path, target, localOffset, loop, volume, true, true);
            return Play(request);
        }

        public ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
        {
            if (target == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, path, target, localOffset, loop, volume, true, spatial, options);
            return Play(request);
        }

        public ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f)
        {
            if (target == null || clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, clip, target, localOffset, loop, volume);
            return Play(request);
        }

        public ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
        {
            if (target == null || clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, clip, target, localOffset, loop, volume, spatial, options);
            return Play(request);
        }

        internal ulong Play(AudioPlayRequest request)
        {
            try
            {
                int index = (int)request.Type;
                if (!_initialized || (uint)index >= (uint)_categories.Length ||
                    (request.Clip == null && string.IsNullOrEmpty(request.Address)))
                {
                    return 0UL;
                }

                return _categories[index].Play(request);
            }
            finally
            {
                MemoryPool.Release(request);
            }
        }

        public bool Stop(ulong handle, bool fadeout = false)
        {
            AudioAgent agent = ResolveHandle(handle);
            if (agent == null)
            {
                return false;
            }

            agent.Stop(fadeout);
            return true;
        }

        public bool Stop(ulong handle, float fadeOutSeconds)
        {
            AudioAgent agent = ResolveHandle(handle);
            if (agent == null)
            {
                return false;
            }

            agent.Stop(fadeOutSeconds);
            return true;
        }

        public bool SetVolume(ulong handle, float volume, float fadeSeconds = 0f)
        {
            AudioAgent agent = ResolveHandle(handle);
            if (agent == null)
            {
                return false;
            }

            agent.SetVolume(volume, fadeSeconds);
            return true;
        }

        public bool IsPlaying(ulong handle)
        {
            AudioAgent agent = ResolveHandle(handle);
            return agent != null && agent.IsPlayingState;
        }

        public void Stop(AudioType type, bool fadeout)
        {
            int index = (int)type;
            if ((uint)index < (uint)_categories.Length && _categories[index] != null)
            {
                _categories[index].Stop(fadeout);
            }
        }

        public void StopAll(bool fadeout)
        {
            for (int i = 0; i < _categories.Length; i++)
            {
                AudioCategory category = _categories[i];
                if (category != null)
                {
                    category.Stop(fadeout);
                }
            }
        }

        public void Warmup(AudioType type, int count)
        {
            if (!_initialized || count <= 0)
            {
                return;
            }

            int index = (int)type;
            if ((uint)index >= (uint)_categories.Length)
            {
                return;
            }

            _categories[index].Warmup(count);
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

            if (entry.Loading)
            {
                return false;
            }

            return BeginLoad(entry, false);
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
            if (string.IsNullOrEmpty(address) || !TryGetClipEntry(address, out AudioClipCacheEntry entry))
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

        private void OnLowMemory()
        {
            if (_initialized)
            {
                ClearCache();
            }
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


        void IServiceTickable.Tick(float deltaTime)
        {
            if (!_initialized)
            {
                return;
            }

            for (int i = 0; i < _categories.Length; i++)
            {
                _categories[i].Update(deltaTime);
            }

            TrimClipCache();
        }

        internal AudioSourceObject AcquireSourceObject(AudioCategory category)
        {
            AudioSourceObject sourceObject = _sourcePool.Spawn();
            if (sourceObject == null)
            {
                sourceObject = CreateSourceObject(category);
                if (sourceObject == null || !_sourcePool.Register(sourceObject, true))
                    return null;
            }

            if (!PrepareSourceObject(sourceObject, category))
            {
                _sourcePool.Unspawn(sourceObject);
                return null;
            }

            return sourceObject;
        }

        internal void ReleaseSourceObject(AudioSourceObject sourceObject)
        {
            if (sourceObject != null)
                _sourcePool.Unspawn(sourceObject);
        }

        internal AudioSourceObject ReplaceSourceObject(AudioCategory category, AudioSourceObject previous)
        {
            ReleaseSourceObject(previous);

            // Unity 目标被外部销毁的包装对象无法再次 Spawn；它的 Unspawn 是空操作，
            // 因此先回收空闲包装对象，再创建替代对象。
            _sourcePool.ReleaseAllUnused();
            return AcquireSourceObject(category);
        }

        internal ulong AllocateHandle(AudioAgent agent)
        {
            return _handleTable.Allocate(agent);
        }

        internal void ReleaseHandle(AudioAgent agent)
        {
            _handleTable.Release(agent);
        }

        private AudioAgent ResolveHandle(ulong handle)
        {
            return _handleTable.Resolve(handle);
        }

        internal bool RequestClip(string address, bool async, AudioCachePolicy cachePolicy, AudioAgent agent, int generation)
        {
            AudioCachePolicy resolvedPolicy = ResolveCachePolicy(cachePolicy);
            AudioClipCacheEntry entry = GetOrCreateClipEntry(address, resolvedPolicy);
            if (entry == null)
            {
                return false;
            }

            UpgradeCachePolicy(entry, resolvedPolicy);
            if (entry.IsLoaded)
            {
                return agent.OnClipReady(entry, generation);
            }

            AudioLoadRequest request = MemoryPool.Acquire<AudioLoadRequest>();
            request.Agent = agent;
            request.Generation = generation;
            entry.AddPending(request);
            agent.SetLoadRequest(request);
            if (!entry.Loading)
            {
                return BeginLoad(entry, async);
            }

            return true;
        }

        private bool TryPreparePreload(string address, AudioCachePolicy policy, out AudioClipCacheEntry entry)
        {
            entry = null;
            if (!_initialized || string.IsNullOrEmpty(address))
            {
                return false;
            }

            AudioCachePolicy resolvedPolicy = ResolveCachePolicy(policy);
            entry = GetOrCreateClipEntry(address, resolvedPolicy);
            if (entry == null)
            {
                return false;
            }

            UpgradeCachePolicy(entry, resolvedPolicy);
            TouchClip(entry);
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
            RemoveFromLru(entry);
        }

        internal void ReleaseClip(AudioClipCacheEntry entry)
        {
            if (--entry.RefCount != 0 || entry.Loading)
            {
                return;
            }

            if (!entry.CacheAfterUse || !entry.Lease.IsValid)
            {
                RemoveClipEntry(entry);
                return;
            }

            entry.LastUseTime = Time.realtimeSinceStartup;
            if (!entry.Pinned)
            {
                AddToLruTail(entry);
            }
        }

        private bool OnClipLoadCompleted(AudioClipCacheEntry entry, ulong version, ResourceAssetLease<AudioClip> lease)
        {
            if (!ReferenceEquals(entry.Owner, this) || entry.Version != version)
            {
                lease.Dispose();
                return false;
            }

            entry.Loading = false;
            AudioClip clip = lease.Asset;
            bool success = clip != null;
            if (!success)
            {
                lease.Dispose();
            }

            entry.Lease.Dispose();
            entry.Lease = lease;
            entry.Clip = clip;
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
                        request.Agent.OnClipReady(entry, request.Generation);
                    }
                    else
                    {
                        request.Agent.OnClipLoadFailed(request.Generation);
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

        public float GetCategoryVolume(AudioType type)
        {
            int index = (int)type;
            return (uint)index < (uint)_categoryVolumes.Length ? _categoryVolumes[index] : 0f;
        }

        public void SetCategoryVolume(AudioType type, float value)
        {
            int index = (int)type;
            if ((uint)index >= (uint)_categoryVolumes.Length)
            {
                return;
            }

            float volume = Mathf.Clamp(value, 0.0001f, 1f);
            _categoryVolumes[index] = volume;
            AudioGroupConfig config = _configByType[index];
            ApplyMixerVolume(config, volume, _categoryEnables[index]);
        }

        public bool GetCategoryEnable(AudioType type)
        {
            int index = (int)type;
            return (uint)index < (uint)_categoryEnables.Length && _categoryEnables[index];
        }

        public void SetCategoryEnable(AudioType type, bool value)
        {
            int index = (int)type;
            if ((uint)index >= (uint)_categoryEnables.Length)
            {
                return;
            }

            _categoryEnables[index] = value;
            AudioCategory category = _categories[index];
            if (category != null)
            {
                category.Enabled = value;
            }

            AudioGroupConfig config = _configByType[index];
            ApplyMixerVolume(config, _categoryVolumes[index], value);
        }

        void IAudioDebugService.FillServiceDebugInfo(AudioServiceDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            info.Initialized = _initialized;
            info.Enable = Enable;
            info.Volume = Volume;
            info.CategoryCount = _categories.Length;
            info.ActiveAgentCount = CountActiveAgents();
            info.ActiveSourceCount = info.ActiveAgentCount;
            info.TotalSourceCount = CountTotalSources();
            info.HandleCapacity = _handleTable.Capacity;
            info.ClipCacheCount = _clipTable.Count;
            info.ClipCacheCapacity = _clipCacheCapacity;
            info.ClipCacheTtl = _clipTtl;
            info.DefaultCachePolicy = _defaultCachePolicy;
            FillCacheAggregateDebugInfo(info);
            info.Listener = _listenerCache;
            info.InstanceRoot = _instanceRoot;
        }

        bool IAudioDebugService.FillCategoryDebugInfo(int typeIndex, AudioCategoryDebugInfo info)
        {
            if (info == null || (uint)typeIndex >= (uint)_categories.Length)
            {
                return false;
            }

            AudioCategory category = _categories[typeIndex];
            if (category == null)
            {
                info.Clear();
                info.Type = (AudioType)typeIndex;
                return false;
            }

            category.FillDebugInfo(_categoryVolumes[typeIndex], info);
            return true;
        }

        bool IAudioDebugService.FillAgentDebugInfo(int typeIndex, int agentIndex, AudioAgentDebugInfo info)
        {
            if (info == null || (uint)typeIndex >= (uint)_categories.Length)
            {
                return false;
            }

            AudioCategory category = _categories[typeIndex];
            if (category == null || !category.TryGetAgent(agentIndex, out AudioAgent agent))
            {
                info.Clear();
                return false;
            }

            agent.FillDebugInfo(info);
            return true;
        }

        bool IAudioDebugService.FillClipCacheDebugInfo(AudioClipCacheEntry entry, AudioClipCacheDebugInfo info)
        {
            if (entry == null || info == null)
            {
                return false;
            }

            entry.FillDebugInfo(info);
            return true;
        }

        private void ApplyMixerVolume(AudioGroupConfig config, float volume, bool enabled)
        {
            if (_audioMixer == null || config == null)
            {
                return;
            }

            string parameter = string.IsNullOrEmpty(config.ExposedVolumeParameter)
                ? VolumeParameterNames[(int)config.AudioType]
                : config.ExposedVolumeParameter;
            _audioMixer.SetFloat(parameter, enabled ? Mathf.Log10(volume) * 20f : -80f);
        }

        private void InitializeClipCacheTable()
        {
            _clipTable.Initialize(_clipCacheCapacity);
        }

        private bool TryGetClipEntry(string address, out AudioClipCacheEntry entry)
        {
            return _clipTable.TryGet(address, AudioClipCacheTable.Hash(address), out entry);
        }

        private bool TryGetClipEntry(string address, int hash, out AudioClipCacheEntry entry)
        {
            return _clipTable.TryGet(address, hash, out entry);
        }

        private AudioClipCacheEntry GetOrCreateClipEntry(string address, AudioCachePolicy cachePolicy)
        {
            int hash = AudioClipCacheTable.Hash(address);
            if (_clipTable.TryGet(address, hash, out AudioClipCacheEntry entry))
                return entry;

            int slotIndex = AcquireClipSlot();
            if (slotIndex < 0)
            {
                AlicizaX.Log.Warning($"Audio clip cache is full ({_clipCacheCapacity} slots, all pinned or loading); '{address}' cannot be admitted.");
                return null;
            }

            entry = MemoryPool.Acquire<AudioClipCacheEntry>();
            entry.Initialize(this, address, hash, cachePolicy, slotIndex);
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
                RemoveFromLru(entry);
                return;
            }

            if (current == AudioCachePolicy.None)
            {
                entry.CachePolicy = AudioCachePolicy.Ttl;
            }
        }

        private AudioCachePolicy ResolveCachePolicy(AudioCachePolicy policy)
        {
            switch (policy)
            {
                case AudioCachePolicy.None:
                case AudioCachePolicy.Ttl:
                case AudioCachePolicy.Pin:
                    return policy;
                case AudioCachePolicy.Default:
                default:
                    return _defaultCachePolicy;
            }
        }

        private static AudioCachePolicy NormalizeDefaultCachePolicy(AudioCachePolicy policy)
        {
            switch (policy)
            {
                case AudioCachePolicy.None:
                case AudioCachePolicy.Ttl:
                case AudioCachePolicy.Pin:
                    return policy;
                case AudioCachePolicy.Default:
                default:
                    return AudioCachePolicy.Ttl;
            }
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

        private void ReleaseClipSlot(AudioClipCacheEntry entry)
        {
            _clipTable.ReleaseSlot(entry);
        }

        private bool BeginLoad(AudioClipCacheEntry entry, bool async)
        {
            RemoveFromLru(entry);
            entry.Loading = true;
            if (async)
            {
                entry.Cancellation ??= new CancellationTokenSource();
                BeginLoadAsync(entry, entry.Version, entry.Cancellation.Token).Forget();
                return true;
            }

            ResourceAssetLease<AudioClip> lease;
            try
            {
                lease = _resourceService.LoadLease<AudioClip>(entry.Address);
            }
            catch
            {
                OnClipLoadCompleted(entry, entry.Version, default);
                throw;
            }

            return OnClipLoadCompleted(entry, entry.Version, lease);
        }

        private async UniTaskVoid BeginLoadAsync(AudioClipCacheEntry entry, ulong version, CancellationToken cancellationToken)
        {
            ResourceAssetLease<AudioClip> lease;
            try
            {
                lease = await _resourceService.LoadLeaseAsync<AudioClip>(entry.Address, cancellationToken);
            }
            catch (Exception exception)
            {
                // 异步抛出时已没有调用方可以观察；OnClipLoadCompleted 会让所有等待者失败。
                // 这里只记录一次并完成，避免产生未观察异常。
                AlicizaX.Log.Exception(exception);
                OnClipLoadCompleted(entry, version, default);
                return;
            }

            OnClipLoadCompleted(entry, version, lease);
        }

        private void TouchClip(AudioClipCacheEntry entry)
        {
            entry.LastUseTime = Time.realtimeSinceStartup;
            MoveLruToTail(entry);
        }

        private void TrimClipCache()
        {
            float now = Time.realtimeSinceStartup;
            while (_clipTable.LruHead != null && now - _clipTable.LruHead.LastUseTime >= _clipTtl)
            {
                RemoveClipEntry(_clipTable.LruHead);
            }
        }

        private void RegisterLowMemoryCallback()
        {
            if (_lowMemoryCallbackRegistered)
            {
                return;
            }

            Application.lowMemory += OnLowMemory;
            _lowMemoryCallbackRegistered = true;
        }

        private void UnregisterLowMemoryCallback()
        {
            if (!_lowMemoryCallbackRegistered)
            {
                return;
            }

            Application.lowMemory -= OnLowMemory;
            _lowMemoryCallbackRegistered = false;
        }

        private void RemoveClipEntry(AudioClipCacheEntry entry)
        {
            _clipTable.Remove(entry);
            _clipTable.RemoveFromLru(entry);
            _clipTable.RemoveFromAllList(entry);
            ReleaseClipSlot(entry);
            AudioLoadRequest callbacks = CompleteLoadRequests(entry, false);
            MemoryPool.Release(entry);
            CompletePreloads(callbacks, false);
        }

        private void AddToLruTail(AudioClipCacheEntry entry) => _clipTable.AddToLruTail(entry);

        private void RemoveFromLru(AudioClipCacheEntry entry) => _clipTable.RemoveFromLru(entry);

        private void MoveLruToTail(AudioClipCacheEntry entry) => _clipTable.MoveLruToTail(entry);

        private void AddToAllList(AudioClipCacheEntry entry) => _clipTable.AddToAllList(entry);

        private void RemoveFromAllList(AudioClipCacheEntry entry) => _clipTable.RemoveFromAllList(entry);

        private AudioSourceObject CreateSourceObject(AudioCategory category)
        {
            GameObject host = new GameObject(SourceObjectName);
            host.transform.SetParent(category.InstanceRoot, false);
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = category.MixerGroup;
            source.rolloffMode = category.Config.RolloffMode;
            source.minDistance = category.Config.MinDistance;
            source.maxDistance = category.Config.MaxDistance;
            AudioLowPassFilter lowPassFilter = null;
            if (category.Config.OcclusionEnabled)
            {
                lowPassFilter = host.AddComponent<AudioLowPassFilter>();
                lowPassFilter.enabled = false;
            }
            host.SetActive(true);
            return AudioSourceObject.Create(string.Empty, source, lowPassFilter);
        }

        private static bool PrepareSourceObject(AudioSourceObject sourceObject, AudioCategory category)
        {
            AudioSource source = sourceObject.Source;
            if (source == null)
                return false;

            Transform transform = source.transform;
            transform.SetParent(category.InstanceRoot, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            source.outputAudioMixerGroup = category.MixerGroup;
            source.rolloffMode = category.Config.RolloffMode;
            source.minDistance = category.Config.MinDistance;
            source.maxDistance = category.Config.MaxDistance;

            if (category.Config.OcclusionEnabled && sourceObject.LowPassFilter == null)
            {
                sourceObject.AttachLowPassFilter();
            }

            return true;
        }

        private void BuildConfigMap(AudioGroupConfig[] configs)
        {
            Array.Clear(_configByType, 0, _configByType.Length);

            for (int i = 0; i < configs.Length; i++)
            {
                AudioGroupConfig config = configs[i];
                if (config == null)
                {
                    continue;
                }

                int index = (int)config.AudioType;
                if ((uint)index < (uint)_configByType.Length)
                {
                    if (_configByType[index] != null)
                    {
                        throw new GameFrameworkException("AudioGroupConfig[] contains duplicate AudioType.");
                    }

                    _configByType[index] = config;
                }
            }

            for (int i = 0; i < _configByType.Length; i++)
            {
                AudioGroupConfig config = _configByType[i];
                if (config == null)
                {
                    throw new GameFrameworkException("AudioGroupConfig[] must contain every AudioType.");
                }

                if (config.MixerGroup == null)
                {
                    throw new GameFrameworkException("AudioGroupConfig.MixerGroup is invalid.");
                }
            }
        }

        private int CountActiveAgents()
        {
            int count = 0;
            for (int i = 0; i < _categories.Length; i++)
            {
                AudioCategory category = _categories[i];
                if (category != null)
                {
                    count += category.ActiveCount;
                }
            }

            return count;
        }

        private int CountTotalSources()
        {
            int count = 0;
            for (int i = 0; i < _categories.Length; i++)
            {
                AudioCategory category = _categories[i];
                if (category != null)
                {
                    count += category.CreatedCount;
                }
            }

            return count;
        }

        private void FillCacheAggregateDebugInfo(AudioServiceDebugInfo info)
        {
            info.LoadingClipCount = 0;
            info.PinnedClipCount = 0;
            info.CachePolicyNoneCount = 0;
            info.CachePolicyTtlCount = 0;
            info.CachePolicyPinCount = 0;

            AudioClipCacheEntry entry = _clipTable.AllHead;
            while (entry != null)
            {
                if (entry.Loading)
                {
                    info.LoadingClipCount++;
                }

                if (entry.Pinned)
                {
                    info.PinnedClipCount++;
                }

                switch (entry.CachePolicy)
                {
                    case AudioCachePolicy.None:
                        info.CachePolicyNoneCount++;
                        break;
                    case AudioCachePolicy.Pin:
                        info.CachePolicyPinCount++;
                        break;
                    case AudioCachePolicy.Ttl:
                    case AudioCachePolicy.Default:
                    default:
                        info.CachePolicyTtlCount++;
                        break;
                }

                entry = entry.AllNext;
            }
        }

        internal void Shutdown()
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;
            List<Exception> errors = null;
            try
            {
                try { UnregisterLowMemoryCallback(); }
                catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }

                _initialized = false;
                try { StopAll(false); }
                catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }

                for (int i = 0; i < _categories.Length; i++)
                {
                    AudioCategory category = _categories[i];
                    if (category == null)
                        continue;
                    try { category.Shutdown(); }
                    catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
                    finally { _categories[i] = null; }
                }

                while (_clipTable.AllHead != null)
                {
                    try { RemoveClipEntry(_clipTable.AllHead); }
                    catch (Exception exception)
                    {
                        (errors ??= new List<Exception>()).Add(exception);
                        // RemoveClipEntry 可能在摘链前失败；直接跳出，避免死循环。
                        break;
                    }
                }

                if (_sourcePool != null)
                {
                    try { _sourcePool.ReleaseAllUnused(); }
                    catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }

                    try { _objectPoolService?.DestroyObjectPool<AudioSourceObject>(SourcePoolName); }
                    catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
                }
            }
            finally
            {
                // 字段清理必须无条件执行：抛异常的回调不能让服务永久停留在
                // “正在关闭”状态并残留旧数组。
                _handleTable.Clear();
                _clipTable.Clear();
                _clipCacheCapacity = DefaultCacheCapacity;
                _clipTtl = DefaultClipTtl;
                _defaultCachePolicy = AudioCachePolicy.Ttl;
                Array.Clear(_configByType, 0, _configByType.Length);
                _resourceService = null;
                _sourcePool = null;
                _objectPoolService = null;
                _audioMixer = null;
                _listenerCache = null;

                try { DestroyOwnedRoot(); }
                catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }

                _isShuttingDown = false;
            }

            if (errors != null)
                throw new AggregateException(errors);
        }

        private void DestroyOwnedRoot()
        {
            if (_ownsInstanceRoot && _instanceRoot != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_instanceRoot.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_instanceRoot.gameObject);
                }
            }

            _instanceRoot = null;
            _ownsInstanceRoot = false;
        }
        internal static string GetCategoryRootName(AudioType type)
        {
            return CategoryRootNames[(int)type];
        }
    }
}
