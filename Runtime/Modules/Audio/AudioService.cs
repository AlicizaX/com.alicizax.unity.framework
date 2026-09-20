using System;
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
        private const int HandleIndexBits = 20;
        private const ulong HandleIndexMask = (1UL << HandleIndexBits) - 1UL;

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
        private readonly AudioSourceObject[][] _sourceObjects = new AudioSourceObject[(int)AudioType.Max][];
        private readonly AudioGroupConfig[] _configByType = new AudioGroupConfig[(int)AudioType.Max];

        private IResourceService _resourceService;
        private IObjectPool<AudioSourceObject> _sourcePool;
        private AudioMixer _audioMixer;
        private Transform _instanceRoot;
        private AudioAgent[] _handleAgents = Array.Empty<AudioAgent>();
        private ulong _nextHandleGeneration;
        private int[] _clipBuckets = Array.Empty<int>();
        private AudioClipCacheEntry[] _clipEntries = Array.Empty<AudioClipCacheEntry>();
        private int[] _clipFreeSlots = Array.Empty<int>();
        private AudioListener _listenerCache;
        private AudioClipCacheEntry _lruHead;
        private AudioClipCacheEntry _lruTail;
        private AudioClipCacheEntry _allHead;
        private AudioClipCacheEntry _allTail;
        private int _clipBucketMask;
        private int _clipFreeCount;
        private int _clipCacheCount;
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
        int IAudioDebugService.ClipCacheCount => _clipCacheCount;
        int IAudioDebugService.ClipCacheCapacity => _clipCacheCapacity;
        float IAudioDebugService.ClipCacheTtl => _clipTtl;
        AudioCachePolicy IAudioDebugService.DefaultCachePolicy => _defaultCachePolicy;
        int IAudioDebugService.HandleCapacity => _handleAgents.Length;
        bool IAudioDebugService.Initialized => _initialized;
        bool IAudioDebugService.UnityAudioDisabled => false;
        AudioClipCacheEntry IAudioDebugService.FirstClipCacheEntry => _allHead;
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
            IObjectPoolService objectPoolService = AppServices.App.Require<IObjectPoolService>();
            _sourcePool = objectPoolService.GetOrCreatePool<AudioSourceObject>(
                new ObjectPoolCreateOptions(SourcePoolName, false, 10f, int.MaxValue, float.MaxValue, 10));
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

            if ((ulong)totalHandleCount > HandleIndexMask)
            {
                throw new GameFrameworkException("Audio agent count exceeds handle capacity.");
            }

            _handleAgents = new AudioAgent[totalHandleCount];
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
                _sourceObjects[i] = new AudioSourceObject[config.MaxSourceCount];
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

        public bool Preload(string address, AudioCachePolicy policy = AudioCachePolicy.Pin)
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
            {
                return false;
            }

            if (!CanUnloadCacheEntry(entry))
            {
                return false;
            }

            RemoveClipEntry(entry);
            return true;
        }

        public void ClearCache(bool force = false)
        {
            AudioClipCacheEntry entry = _allHead;
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

        internal AudioSourceObject AcquireSourceObject(AudioCategory category, int index)
        {
            AudioSourceObject sourceObject = _sourcePool.Spawn(SourceObjectName);
            if (sourceObject == null)
            {
                sourceObject = CreateSourceObject(category);
                _sourcePool.Register(sourceObject, true);
            }

            PrepareSourceObject(sourceObject, category);
            _sourceObjects[category.TypeIndex][index] = sourceObject;
            return sourceObject;
        }

        internal void ReleaseSourceObject(int typeIndex, int index)
        {
            AudioSourceObject sourceObject = _sourceObjects[typeIndex][index];
            _sourceObjects[typeIndex][index] = null;
            _sourcePool.Unspawn(sourceObject);
        }

        internal AudioSourceObject ReplaceSourceObject(AudioCategory category, int index)
        {
            ReleaseSourceObject(category.TypeIndex, index);
            _sourcePool.ReleaseAllUnused();
            return AcquireSourceObject(category, index);
        }

        internal ulong AllocateHandle(AudioAgent agent)
        {
            _nextHandleGeneration = (_nextHandleGeneration + 1UL) & (ulong.MaxValue >> HandleIndexBits);
            if (_nextHandleGeneration == 0UL)
            {
                _nextHandleGeneration = 1UL;
            }

            _handleAgents[agent.GlobalIndex] = agent;
            return (_nextHandleGeneration << HandleIndexBits) | (uint)(agent.GlobalIndex + 1);
        }

        internal void ReleaseHandle(AudioAgent agent)
        {
            _handleAgents[agent.GlobalIndex] = null;
        }

        private AudioAgent ResolveHandle(ulong handle)
        {
            int index = (int)((handle & HandleIndexMask) - 1UL);
            if ((uint)index >= (uint)_handleAgents.Length)
            {
                return null;
            }

            AudioAgent agent = _handleAgents[index];
            return agent != null && agent.Handle == handle ? agent : null;
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
            info.UnityAudioDisabled = false;
            info.Enable = Enable;
            info.Volume = Volume;
            info.CategoryCount = _categories.Length;
            info.ActiveAgentCount = CountActiveAgents();
            info.ActiveSourceCount = info.ActiveAgentCount;
            info.TotalSourceCount = CountTotalSources();
            info.HandleCapacity = _handleAgents.Length;
            info.ClipCacheCount = _clipCacheCount;
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
            int bucketCount = NextPowerOfTwo(_clipCacheCapacity << 1);
            if (bucketCount < 16)
            {
                bucketCount = 16;
            }

            _clipBuckets = new int[bucketCount];
            for (int i = 0; i < _clipBuckets.Length; i++)
            {
                _clipBuckets[i] = -1;
            }

            _clipEntries = new AudioClipCacheEntry[_clipCacheCapacity];
            _clipFreeSlots = new int[_clipCacheCapacity];
            for (int i = 0; i < _clipFreeSlots.Length; i++)
            {
                _clipFreeSlots[i] = _clipFreeSlots.Length - 1 - i;
            }

            _clipFreeCount = _clipFreeSlots.Length;
            _clipBucketMask = bucketCount - 1;
            _clipCacheCount = 0;
        }

        private bool TryGetClipEntry(string address, out AudioClipCacheEntry entry)
        {
            return TryGetClipEntry(address, ComputeAddressHash(address), out entry);
        }

        private bool TryGetClipEntry(string address, int hash, out AudioClipCacheEntry entry)
        {
            if (_clipBuckets.Length == 0)
            {
                entry = null;
                return false;
            }

            int slotIndex = _clipBuckets[hash & _clipBucketMask];
            while (slotIndex >= 0)
            {
                AudioClipCacheEntry current = _clipEntries[slotIndex];
                if (current.AddressHash == hash && string.Equals(current.Address, address, StringComparison.Ordinal))
                {
                    entry = current;
                    return true;
                }

                slotIndex = current.HashNextIndex;
            }

            entry = null;
            return false;
        }

        private void AddToClipTable(AudioClipCacheEntry entry)
        {
            int bucket = entry.AddressHash & _clipBucketMask;
            entry.HashNextIndex = _clipBuckets[bucket];
            _clipBuckets[bucket] = entry.SlotIndex;
            _clipCacheCount++;
        }

        private void RemoveFromClipTable(AudioClipCacheEntry entry)
        {
            int bucket = entry.AddressHash & _clipBucketMask;
            int currentIndex = _clipBuckets[bucket];
            int previousIndex = -1;
            while (currentIndex >= 0)
            {
                AudioClipCacheEntry current = _clipEntries[currentIndex];
                if (ReferenceEquals(current, entry))
                {
                    if (previousIndex < 0)
                    {
                        _clipBuckets[bucket] = current.HashNextIndex;
                    }
                    else
                    {
                        _clipEntries[previousIndex].HashNextIndex = current.HashNextIndex;
                    }

                    current.HashNextIndex = -1;
                    _clipCacheCount--;
                    return;
                }

                previousIndex = currentIndex;
                currentIndex = current.HashNextIndex;
            }
        }

        private static int ComputeAddressHash(string address)
        {
            unchecked
            {
                int hash = 5381;
                for (int i = 0; i < address.Length; i++)
                {
                    hash = ((hash << 5) + hash) ^ address[i];
                }

                return hash & 0x7fffffff;
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        private AudioClipCacheEntry GetOrCreateClipEntry(string address, AudioCachePolicy cachePolicy)
        {
            int hash = ComputeAddressHash(address);
            if (TryGetClipEntry(address, hash, out AudioClipCacheEntry entry))
            {
                return entry;
            }

            int slotIndex = AcquireClipSlot();
            if (slotIndex < 0)
            {
                return null;
            }

            entry = MemoryPool.Acquire<AudioClipCacheEntry>();
            entry.Initialize(this, address, hash, cachePolicy, slotIndex);
            _clipEntries[slotIndex] = entry;
            AddToClipTable(entry);
            AddToAllList(entry);
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
            if (_clipFreeCount > 0)
            {
                return _clipFreeSlots[--_clipFreeCount];
            }

            if (_lruHead == null)
            {
                return -1;
            }

            RemoveClipEntry(_lruHead);
            return _clipFreeSlots[--_clipFreeCount];
        }

        private void ReleaseClipSlot(AudioClipCacheEntry entry)
        {
            int slotIndex = entry.SlotIndex;
            _clipEntries[slotIndex] = null;
            _clipFreeSlots[_clipFreeCount++] = slotIndex;
            entry.SlotIndex = -1;
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
            catch
            {
                OnClipLoadCompleted(entry, version, default);
                throw;
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
            while (_lruHead != null && now - _lruHead.LastUseTime >= _clipTtl)
            {
                RemoveClipEntry(_lruHead);
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
            RemoveFromClipTable(entry);
            RemoveFromLru(entry);
            RemoveFromAllList(entry);
            ReleaseClipSlot(entry);
            AudioLoadRequest callbacks = CompleteLoadRequests(entry, false);
            MemoryPool.Release(entry);
            CompletePreloads(callbacks, false);
        }

        private void AddToLruTail(AudioClipCacheEntry entry)
        {
            entry.InLru = true;
            entry.LruPrev = _lruTail;
            entry.LruNext = null;
            if (_lruTail != null)
            {
                _lruTail.LruNext = entry;
            }
            else
            {
                _lruHead = entry;
            }

            _lruTail = entry;
        }

        private void RemoveFromLru(AudioClipCacheEntry entry)
        {
            if (!entry.InLru)
            {
                return;
            }

            AudioClipCacheEntry prev = entry.LruPrev;
            AudioClipCacheEntry next = entry.LruNext;
            if (prev != null)
            {
                prev.LruNext = next;
            }
            else
            {
                _lruHead = next;
            }

            if (next != null)
            {
                next.LruPrev = prev;
            }
            else
            {
                _lruTail = prev;
            }

            entry.LruPrev = null;
            entry.LruNext = null;
            entry.InLru = false;
        }

        private void MoveLruToTail(AudioClipCacheEntry entry)
        {
            if (!entry.InLru || ReferenceEquals(_lruTail, entry))
            {
                return;
            }

            RemoveFromLru(entry);
            AddToLruTail(entry);
        }

        private void AddToAllList(AudioClipCacheEntry entry)
        {
            entry.AllPrev = _allTail;
            entry.AllNext = null;
            if (_allTail != null)
            {
                _allTail.AllNext = entry;
            }
            else
            {
                _allHead = entry;
            }

            _allTail = entry;
        }

        private void RemoveFromAllList(AudioClipCacheEntry entry)
        {
            AudioClipCacheEntry prev = entry.AllPrev;
            AudioClipCacheEntry next = entry.AllNext;
            if (prev != null)
            {
                prev.AllNext = next;
            }
            else
            {
                _allHead = next;
            }

            if (next != null)
            {
                next.AllPrev = prev;
            }
            else
            {
                _allTail = prev;
            }

            entry.AllPrev = null;
            entry.AllNext = null;
        }

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
            return AudioSourceObject.Create(SourceObjectName, source, lowPassFilter);
        }

        private static void PrepareSourceObject(AudioSourceObject sourceObject, AudioCategory category)
        {
            AudioSource source = sourceObject.Source;
            if (source == null)
            {
                return;
            }

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

            AudioClipCacheEntry entry = _allHead;
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

            UnregisterLowMemoryCallback();
            _isShuttingDown = true;
            _initialized = false;
            StopAll(false);

            for (int i = 0; i < _categories.Length; i++)
            {
                AudioCategory category = _categories[i];
                if (category != null)
                {
                    category.Shutdown();
                    _categories[i] = null;
                }
            }

            while (_allHead != null)
            {
                RemoveClipEntry(_allHead);
            }
            if (_sourcePool != null)
            {
                _sourcePool.ReleaseAllUnused();
            }

            Array.Clear(_handleAgents, 0, _handleAgents.Length);
            _handleAgents = Array.Empty<AudioAgent>();
            Array.Clear(_clipBuckets, 0, _clipBuckets.Length);
            _clipBuckets = Array.Empty<int>();
            Array.Clear(_clipEntries, 0, _clipEntries.Length);
            _clipEntries = Array.Empty<AudioClipCacheEntry>();
            Array.Clear(_clipFreeSlots, 0, _clipFreeSlots.Length);
            _clipFreeSlots = Array.Empty<int>();
            _clipBucketMask = 0;
            _clipFreeCount = 0;
            _clipCacheCount = 0;
            _clipCacheCapacity = DefaultCacheCapacity;
            _clipTtl = DefaultClipTtl;
            _defaultCachePolicy = AudioCachePolicy.Ttl;
            Array.Clear(_configByType, 0, _configByType.Length);
            _resourceService = null;
            _sourcePool = null;
            _audioMixer = null;
            _listenerCache = null;
            for (int i = 0; i < _sourceObjects.Length; i++)
            {
                _sourceObjects[i] = null;
            }

            DestroyOwnedRoot();

            _isShuttingDown = false;
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
