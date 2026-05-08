using System;
using AlicizaX.ObjectPool;
using AlicizaX.Resource.Runtime;
using UnityEngine;
using UnityEngine.Audio;
using YooAsset;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioService : ServiceBase, IAudioService, IAudioDebugService, IServiceTickable
    {
        private const string SourcePoolName = "Audio Source Pool";
        private const string InstanceRootName = "[AudioService Instances]";
        private const string SourceObjectName = "AudioSource";
        private const int DefaultCacheCapacity = 128;
        private const float DefaultClipTtl = 30f;
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
        private AudioGroupConfig[] _configs;
        private AudioAgent[] _handleAgents = Array.Empty<AudioAgent>();
        private uint[] _handleGenerations = Array.Empty<uint>();
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
        private float _volume = 1f;
        private bool _enable = true;
        private bool _unityAudioDisabled;
        private bool _initialized;
        private bool _isShuttingDown;
        private bool _ownsInstanceRoot;

        internal Transform InstanceRoot => _instanceRoot;
        internal Transform ListenerTransform => _listenerCache != null && _listenerCache.enabled && _listenerCache.gameObject.activeInHierarchy
            ? _listenerCache.transform
            : null;
        int IAudioDebugService.CategoryCount => _categories.Length;
        int IAudioDebugService.ClipCacheCount => _clipCacheCount;
        int IAudioDebugService.ClipCacheCapacity => _clipCacheCapacity;
        int IAudioDebugService.HandleCapacity => _handleAgents.Length;
        bool IAudioDebugService.Initialized => _initialized;
        bool IAudioDebugService.UnityAudioDisabled => _unityAudioDisabled;
        AudioClipCacheEntry IAudioDebugService.FirstClipCacheEntry => _allHead;
        public int Priority => 0;

        public float Volume
        {
            get => _unityAudioDisabled ? 0f : _volume;
            set
            {
                if (_unityAudioDisabled)
                {
                    return;
                }

                _volume = Mathf.Clamp01(value);
                AudioListener.volume = _enable ? _volume : 0f;
            }
        }

        public bool Enable
        {
            get => !_unityAudioDisabled && _enable;
            set
            {
                if (_unityAudioDisabled)
                {
                    return;
                }

                _enable = value;
                AudioListener.volume = _enable ? _volume : 0f;
            }
        }


        protected override void OnInitialize() { }

        protected override void OnDestroyService()
        {
            Shutdown(true);
        }

        internal void Initialize(AudioGroupConfig[] audioGroupConfigs, AudioListener audioListener, Transform instanceRoot = null, AudioMixer audioMixer = null)
        {
            if (audioGroupConfigs == null || audioGroupConfigs.Length == 0)
            {
                throw new GameFrameworkException("AudioGroupConfig[] is invalid.");
            }

            if (audioListener == null)
            {
                throw new GameFrameworkException("AudioListener is invalid. Please provide a valid AudioListener.");
            }

            Shutdown(false);

            _configs = audioGroupConfigs;
            _listenerCache = audioListener;
            BuildConfigMap();

            InitializeObjectPools();
            InitializeInstanceRoot(instanceRoot);
            InitializeAudioMixer(audioMixer);

            if (_unityAudioDisabled)
            {
                _initialized = true;
                return;
            }

            InitializeHandleSystem();
            InitializeCategories();

            _initialized = true;
        }

        private void InitializeObjectPools()
        {
            _resourceService = AppServices.Require<IResourceService>();
            IObjectPoolService objectPoolService = AppServices.Require<IObjectPoolService>();
            _sourcePool = objectPoolService.HasObjectPool<AudioSourceObject>(SourcePoolName)
                ? objectPoolService.GetObjectPool<AudioSourceObject>(SourcePoolName)
                : objectPoolService.CreatePool<AudioSourceObject>(new ObjectPoolCreateOptions(SourcePoolName, false, 10f, int.MaxValue, float.MaxValue, 10));
        }

        private void InitializeInstanceRoot(Transform instanceRoot)
        {
            if (instanceRoot != null)
            {
                _instanceRoot = instanceRoot;
                _ownsInstanceRoot = false;
            }
            else if (_instanceRoot == null)
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
            _unityAudioDisabled = IsUnityAudioDisabled();
            if (_unityAudioDisabled)
            {
                return;
            }

            _audioMixer = audioMixer;
            if (_audioMixer == null)
            {
                throw new GameFrameworkException("AudioMixer is invalid. Please provide a valid AudioMixer.");
            }
        }

        private void InitializeHandleSystem()
        {
            int totalAgentCount = 0;
            for (int i = 0; i < (int)AudioType.Max; i++)
            {
                AudioGroupConfig config = _configByType[i];
                totalAgentCount += config.AgentHelperCount;
            }

            if ((ulong)totalAgentCount > HandleIndexMask)
            {
                throw new GameFrameworkException("Audio agent count exceeds handle capacity.");
            }

            _handleAgents = new AudioAgent[totalAgentCount];
            _handleGenerations = new uint[totalAgentCount];
            InitializeClipCacheTable();
            MemoryPool.Add<AudioAgent>(totalAgentCount);
            MemoryPool.Add<AudioPlayRequest>(totalAgentCount);
            MemoryPool.Add<AudioLoadRequest>(totalAgentCount);
            MemoryPool.Add<AudioClipCacheEntry>(_clipCacheCapacity);
        }

        private void InitializeCategories()
        {
            int globalIndexOffset = 0;
            for (int i = 0; i < (int)AudioType.Max; i++)
            {
                AudioGroupConfig config = _configByType[i];
                _categoryVolumes[i] = Mathf.Clamp(config.Volume, 0.0001f, 1f);
                _categoryEnables[i] = !config.Mute;
                _sourceObjects[i] = new AudioSourceObject[config.AgentHelperCount];
                _categories[i] = new AudioCategory(this, config, globalIndexOffset);
                globalIndexOffset += config.AgentHelperCount;
                ApplyMixerVolume(config, _categoryVolumes[i], _categoryEnables[i]);
            }
        }

        public ulong Play(AudioType type, string path, bool loop = false, float volume = 1f)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set2D(type, path, loop, volume, false, true);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        public ulong Play(AudioType type, AudioClip clip, bool loop = false, float volume = 1f)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set2D(type, clip, loop, volume);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        public ulong Play3D(AudioType type, string path, in Vector3 position, bool loop = false, float volume = 1f)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, path, position, loop, volume, false, true);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        internal ulong Play3D(AudioType type, string path, in Vector3 position, float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend = 1f, bool loop = false, float volume = 1f, bool async = false, bool cacheClip = true)
        {
            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, path, position, minDistance, maxDistance, rolloffMode, spatialBlend, loop, volume, async, cacheClip);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        public ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, bool loop = false, float volume = 1f)
        {
            if (clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, clip, position, loop, volume);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        internal ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend = 1f, bool loop = false, float volume = 1f)
        {
            if (clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.Set3D(type, clip, position, minDistance, maxDistance, rolloffMode, spatialBlend, loop, volume);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        public ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f)
        {
            if (target == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, path, target, localOffset, loop, volume, false, true);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        public ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f)
        {
            if (target == null || clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, clip, target, localOffset, loop, volume);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        internal ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend = 1f, bool loop = false, float volume = 1f)
        {
            if (target == null || clip == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, clip, target, localOffset, minDistance, maxDistance, rolloffMode, spatialBlend, loop, volume);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        internal ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend = 1f, bool loop = false, float volume = 1f, bool async = false, bool cacheClip = true)
        {
            if (target == null)
            {
                return 0UL;
            }

            AudioPlayRequest request = MemoryPool.Acquire<AudioPlayRequest>();
            request.SetFollow(type, path, target, localOffset, minDistance, maxDistance, rolloffMode, spatialBlend, loop, volume, async, cacheClip);
            ulong handle = Play(request);
            MemoryPool.Release(request);
            return handle;
        }

        internal ulong Play(AudioPlayRequest request)
        {
            if (!_initialized || _unityAudioDisabled || request == null)
            {
                return 0UL;
            }

            int index = (int)request.Type;
            if ((uint)index >= (uint)_categories.Length)
            {
                return 0UL;
            }

            AudioCategory category = _categories[index];
            return category != null ? category.Play(request) : 0UL;
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

        private void ClearCache(bool force)
        {
            AudioClipCacheEntry entry = _allHead;
            while (entry != null)
            {
                AudioClipCacheEntry next = entry.AllNext;
                if (force || (entry.RefCount <= 0 && !entry.Loading && entry.PendingHead == null))
                {
                    RemoveClipEntry(entry);
                }
                entry = next;
            }
        }

        void IServiceTickable.Tick(float deltaTime)
        {
            if (!_initialized || _unityAudioDisabled)
            {
                return;
            }

            for (int i = 0; i < _categories.Length; i++)
            {
                AudioCategory category = _categories[i];
                if (category != null)
                {
                    category.Update(deltaTime);
                }
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
            AudioSourceObject[] sourceObjects = _sourceObjects[typeIndex];
            if (sourceObjects == null)
            {
                return;
            }

            AudioSourceObject sourceObject = sourceObjects[index];
            if (sourceObject == null)
            {
                return;
            }

            sourceObjects[index] = null;
            if (sourceObject.Source != null)
            {
                sourceObject.Source.transform.SetParent(_instanceRoot, false);
            }

            if (_sourcePool == null)
            {
                return;
            }

            if (_isShuttingDown && sourceObject.Target == null)
            {
                return;
            }

            _sourcePool.Unspawn(sourceObject);
        }

        internal ulong AllocateHandle(AudioAgent agent)
        {
            int index = agent.GlobalIndex;
            if ((uint)index >= (uint)_handleAgents.Length)
            {
                return 0UL;
            }

            uint generation = _handleGenerations[index] + 1U;
            if (generation == 0)
            {
                generation = 1U;
            }

            _handleGenerations[index] = generation;
            _handleAgents[index] = agent;
            return ((ulong)generation << HandleIndexBits) | (uint)(index + 1);
        }

        internal void ReleaseHandle(ulong handle, AudioAgent agent)
        {
            int index = (int)((handle & HandleIndexMask) - 1UL);
            if ((uint)index < (uint)_handleAgents.Length && ReferenceEquals(_handleAgents[index], agent))
            {
                _handleAgents[index] = null;
            }
        }

        private AudioAgent ResolveHandle(ulong handle)
        {
            if (handle == 0)
            {
                return null;
            }

            int index = (int)((handle & HandleIndexMask) - 1UL);
            uint generation = (uint)(handle >> HandleIndexBits);
            if ((uint)index >= (uint)_handleAgents.Length || _handleGenerations[index] != generation)
            {
                return null;
            }

            return _handleAgents[index];
        }

        internal bool RequestClip(string address, bool async, bool cacheClip, AudioAgent agent, int generation, out AudioClipCacheEntry loadedEntry, out AudioLoadRequest loadRequest)
        {
            loadedEntry = null;
            loadRequest = null;
            if (!_initialized || _unityAudioDisabled || string.IsNullOrEmpty(address) || _clipBuckets.Length == 0)
            {
                return false;
            }

            AudioClipCacheEntry entry = GetOrCreateClipEntry(address, false);
            if (entry == null)
            {
                return false;
            }

            entry.CacheAfterUse = entry.CacheAfterUse || cacheClip;
            TouchClip(entry);

            if (entry.IsLoaded)
            {
                loadedEntry = entry;
                return true;
            }

            AudioLoadRequest request = MemoryPool.Acquire<AudioLoadRequest>();
            request.Agent = agent;
            request.Generation = generation;
            entry.AddPending(request);
            loadRequest = request;

            if (entry.Handle == null)
            {
                BeginLoad(entry, async);
                if (request.Entry == null)
                {
                    if (entry.IsLoaded)
                    {
                        loadedEntry = entry;
                        loadRequest = null;
                        return true;
                    }

                    return false;
                }
            }

            return true;
        }

        internal void CancelLoadRequest(AudioLoadRequest request)
        {
            if (request == null)
            {
                return;
            }

            AudioClipCacheEntry entry = request.Entry;
            if (entry != null && entry.RemovePending(request))
            {
                AudioAgent agent = request.Agent;
                if (agent != null)
                {
                    agent.OnClipLoadCancelled(request);
                }

                MemoryPool.Release(request);
                if (entry.RefCount <= 0 && entry.PendingHead == null)
                {
                    if (entry.Loading)
                    {
                        if (!entry.Pinned)
                        {
                            RemoveClipEntry(entry);
                        }

                        return;
                    }

                    if (entry.CacheAfterUse && entry.IsLoaded)
                    {
                        AddToLruTail(entry);
                    }
                    else
                    {
                        RemoveClipEntry(entry);
                    }
                }
            }
        }

        internal void RetainClip(AudioClipCacheEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            entry.RefCount++;
            RemoveFromLru(entry);
            TouchClip(entry);
        }

        internal void ReleaseClip(AudioClipCacheEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.RefCount > 0)
            {
                entry.RefCount--;
            }

            TouchClip(entry);
            if (entry.RefCount <= 0)
            {
                if (entry.CacheAfterUse)
                {
                    AddToLruTail(entry);
                }
                else
                {
                    RemoveClipEntry(entry);
                }
            }
        }

        internal void OnClipLoadCompleted(AudioClipCacheEntry entry, AssetHandle handle)
        {
            if (entry == null || !TryGetClipEntry(entry.Address, out AudioClipCacheEntry mapped) || !ReferenceEquals(mapped, entry))
            {
                if (handle != null && handle.IsValid)
                {
                    handle.Dispose();
                }

                return;
            }

            entry.Loading = false;
            if (handle != null)
            {
                handle.Completed -= entry.CompletedCallback;
            }

            bool success = handle != null && handle.IsValid && handle.AssetObject is AudioClip;
            if (success)
            {
                entry.Handle = handle;
                entry.Clip = (AudioClip)handle.AssetObject;
                TouchClip(entry);
            }

            AudioLoadRequest request = entry.PendingHead;
            entry.PendingHead = null;
            entry.PendingTail = null;

            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                request.Entry = null;
                request.Prev = null;
                request.Next = null;
                if (success)
                {
                    AudioAgent requestAgent = request.Agent;
                    if (requestAgent != null)
                    {
                        requestAgent.OnClipReady(entry, request.Generation);
                    }
                }
                else
                {
                    AudioAgent requestAgent = request.Agent;
                    if (requestAgent != null)
                    {
                        requestAgent.OnClipLoadFailed(request.Generation);
                    }
                }

                MemoryPool.Release(request);
                request = next;
            }

            if (!success)
            {
                RemoveClipEntry(entry);
            }
            else if (entry.RefCount <= 0 && entry.CacheAfterUse)
            {
                AddToLruTail(entry);
            }
            else if (success && entry.RefCount <= 0)
            {
                RemoveClipEntry(entry);
            }
        }

        public float GetCategoryVolume(AudioType type)
        {
            if (_unityAudioDisabled)
            {
                return 0f;
            }

            int index = (int)type;
            return (uint)index < (uint)_categoryVolumes.Length ? _categoryVolumes[index] : 0f;
        }

        public void SetCategoryVolume(AudioType type, float value)
        {
            if (_unityAudioDisabled)
            {
                return;
            }

            int index = (int)type;
            if ((uint)index >= (uint)_categoryVolumes.Length)
            {
                return;
            }

            float volume = Mathf.Clamp(value, 0.0001f, 1f);
            _categoryVolumes[index] = volume;
            AudioGroupConfig config = GetConfig(type);
            ApplyMixerVolume(config, volume, _categoryEnables[index]);
        }

        public bool GetCategoryEnable(AudioType type)
        {
            if (_unityAudioDisabled)
            {
                return false;
            }

            int index = (int)type;
            return (uint)index < (uint)_categoryEnables.Length && _categoryEnables[index];
        }

        public void SetCategoryEnable(AudioType type, bool value)
        {
            if (_unityAudioDisabled)
            {
                return;
            }

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

            AudioGroupConfig config = GetConfig(type);
            ApplyMixerVolume(config, _categoryVolumes[index], value);
        }

        void IAudioDebugService.FillServiceDebugInfo(AudioServiceDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            info.Initialized = _initialized;
            info.UnityAudioDisabled = _unityAudioDisabled;
            info.Enable = Enable;
            info.Volume = Volume;
            info.CategoryCount = _categories.Length;
            info.ActiveAgentCount = CountActiveAgents();
            info.HandleCapacity = _handleAgents.Length;
            info.ClipCacheCount = _clipCacheCount;
            info.ClipCacheCapacity = _clipCacheCapacity;
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

            float volume = (uint)typeIndex < (uint)_categoryVolumes.Length ? _categoryVolumes[typeIndex] : 0f;
            category.FillDebugInfo(volume, info);
            return true;
        }

        bool IAudioDebugService.FillAgentDebugInfo(int typeIndex, int agentIndex, AudioAgentDebugInfo info)
        {
            if (info == null || (uint)typeIndex >= (uint)_categories.Length)
            {
                return false;
            }

            AudioCategory category = _categories[typeIndex];
            if (category == null || !category.TryGetAgent(agentIndex, out AudioAgent agent) || agent == null)
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
            if (_clipBuckets.Length == 0)
            {
                return;
            }

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

        private AudioClipCacheEntry GetOrCreateClipEntry(string address, bool pinned)
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
            entry.Initialize(this, address, hash, pinned, slotIndex);
            _clipEntries[slotIndex] = entry;
            AddToClipTable(entry);
            AddToAllList(entry);
            if (pinned && entry.IsLoaded)
            {
                AddToLruTail(entry);
            }
            return entry;
        }

        private int AcquireClipSlot()
        {
            if (_clipFreeCount > 0)
            {
                return _clipFreeSlots[--_clipFreeCount];
            }

            EvictOneClipEntry();
            if (_clipFreeCount > 0)
            {
                return _clipFreeSlots[--_clipFreeCount];
            }

            return -1;
        }

        private void ReleaseClipSlot(AudioClipCacheEntry entry)
        {
            int slotIndex = entry.SlotIndex;
            if ((uint)slotIndex >= (uint)_clipEntries.Length)
            {
                return;
            }

            _clipEntries[slotIndex] = null;
            _clipFreeSlots[_clipFreeCount++] = slotIndex;
            entry.SlotIndex = -1;
        }

        private void EvictOneClipEntry()
        {
            float now = Time.realtimeSinceStartup;
            AudioClipCacheEntry current = _lruHead;
            while (current != null)
            {
                AudioClipCacheEntry next = current.LruNext;
                if (CanEvict(current, now, false))
                {
                    RemoveClipEntry(current);
                    return;
                }

                current = next;
            }
        }

        private void BeginLoad(AudioClipCacheEntry entry, bool async)
        {
            entry.Loading = async;
            if (async)
            {
                entry.Handle = _resourceService.LoadAssetAsyncHandle<AudioClip>(entry.Address);
                if (entry.Handle == null)
                {
                    OnClipLoadCompleted(entry, null);
                    return;
                }

                entry.Handle.Completed += entry.CompletedCallback;
                return;
            }

            AssetHandle handle = _resourceService.LoadAssetSyncHandle<AudioClip>(entry.Address);
            entry.Handle = handle;
            OnClipLoadCompleted(entry, handle);
        }

        private void TouchClip(AudioClipCacheEntry entry)
        {
            entry.LastUseTime = Time.realtimeSinceStartup;
            if (entry.RefCount <= 0 && entry.IsLoaded)
            {
                MoveLruToTail(entry);
            }
        }

        private void TrimClipCache()
        {
            float now = Time.realtimeSinceStartup;
            AudioClipCacheEntry current = _lruHead;
            while (current != null)
            {
                AudioClipCacheEntry next = current.LruNext;
                if (!CanEvict(current, now, true))
                {
                    break;
                }

                RemoveClipEntry(current);
                current = next;
            }
        }

        private bool CanEvict(AudioClipCacheEntry entry, float now, bool requireExpired)
        {
            if (entry == null || entry.RefCount > 0 || entry.Pinned || entry.Loading)
            {
                return false;
            }

            if (!requireExpired)
            {
                return true;
            }

            return now - entry.LastUseTime >= _clipTtl;
        }

        private void RemoveClipEntry(AudioClipCacheEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            RemoveFromClipTable(entry);
            RemoveFromLru(entry);
            RemoveFromAllList(entry);
            ReleaseClipSlot(entry);
            MemoryPool.Release(entry);
        }

        private void AddToLruTail(AudioClipCacheEntry entry)
        {
            if (entry.InLru || entry.RefCount > 0 || entry.Pinned || entry.Loading || !entry.CacheAfterUse)
            {
                return;
            }

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
            AudioLowPassFilter lowPassFilter = host.AddComponent<AudioLowPassFilter>();
            lowPassFilter.enabled = false;
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
        }

        private AudioGroupConfig GetConfig(AudioType type)
        {
            int index = (int)type;
            if ((uint)index >= (uint)_categories.Length)
            {
                return null;
            }

            AudioCategory category = _categories[index];
            if (category != null)
            {
                return category.Config;
            }

            return FindConfig(type);
        }

        private void BuildConfigMap()
        {
            Array.Clear(_configByType, 0, _configByType.Length);

            for (int i = 0; i < _configs.Length; i++)
            {
                AudioGroupConfig config = _configs[i];
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

        private AudioGroupConfig FindConfig(AudioType type)
        {
            int index = (int)type;
            return (uint)index < (uint)_configByType.Length ? _configByType[index] : null;
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

        private void Shutdown(bool destroyRoot)
        {
            _isShuttingDown = true;
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

            ClearCache(true);
            if (_sourcePool != null)
            {
                _sourcePool.ReleaseAllUnused();
            }

            Array.Clear(_handleAgents, 0, _handleAgents.Length);
            Array.Clear(_handleGenerations, 0, _handleGenerations.Length);
            _handleAgents = Array.Empty<AudioAgent>();
            _handleGenerations = Array.Empty<uint>();
            Array.Clear(_clipBuckets, 0, _clipBuckets.Length);
            _clipBuckets = Array.Empty<int>();
            Array.Clear(_clipEntries, 0, _clipEntries.Length);
            _clipEntries = Array.Empty<AudioClipCacheEntry>();
            Array.Clear(_clipFreeSlots, 0, _clipFreeSlots.Length);
            _clipFreeSlots = Array.Empty<int>();
            _clipBucketMask = 0;
            _clipFreeCount = 0;
            _clipCacheCount = 0;
            Array.Clear(_configByType, 0, _configByType.Length);
            _resourceService = null;
            _sourcePool = null;
            _audioMixer = null;
            _listenerCache = null;
            _initialized = false;
            for (int i = 0; i < _sourceObjects.Length; i++)
            {
                _sourceObjects[i] = null;
            }

            if (destroyRoot)
            {
                DestroyOwnedRoot();
            }

            _isShuttingDown = false;
        }

        private void DestroyOwnedRoot()
        {
            if (_ownsInstanceRoot && _instanceRoot != null)
            {
                UnityEngine.Object.Destroy(_instanceRoot.gameObject);
            }

            _instanceRoot = null;
            _ownsInstanceRoot = false;
        }
        internal static string GetCategoryRootName(AudioType type)
        {
            int index = (int)type;
            return (uint)index < (uint)CategoryRootNames.Length ? CategoryRootNames[index] : CategoryRootNames[0];
        }

        private static bool IsUnityAudioDisabled()
        {
            return false;
        }

    }
}
