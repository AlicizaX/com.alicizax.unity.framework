using AlicizaX;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioAgent : MemoryObject
    {
        private const float DefaultFadeOutSeconds = 0.15f;
        private const float MinFadeOutSeconds = 0.0001f;
        private const float MaxCutoffFrequency = 22000f;

        private AudioService _service;
        private AudioCategory _category;
        private AudioSourceObject _sourceObject;
        private AudioSource _source;
        private AudioLowPassFilter _lowPassFilter;
        private AudioClipCacheEntry _clipEntry;
        private AudioLoadRequest _loadRequest;
        private Transform _transform;
        private Transform _followTarget;
        private Vector3 _followOffset;
        private AudioAgentRuntimeState _state;
        private bool _spatial;
        private bool _occluded;
        private bool _loop;
        private int _generation;
        private ulong _handle;
        private float _baseVolume;
        private float _pitch;
        private float _volumeFadeStart;
        private float _volumeFadeTarget;
        private float _volumeFadeTimer;
        private float _volumeFadeDuration;
        private float _fadeInTimer;
        private float _fadeInDuration;
        private float _fadeTimer;
        private float _fadeDuration;
        private float _startedAt;
        private float _nextOcclusionCheckTime;
        private int _playbackPriority;

        internal int Index { get; private set; }
        internal int GlobalIndex { get; private set; }
        internal int HeapIndex { get; set; }
        internal int ActiveIndex { get; set; }
        internal ulong Handle => _handle;
        internal int Generation => _generation;
        internal bool IsFree => _state == AudioAgentRuntimeState.Free;
        internal bool IsPlayingState => _state == AudioAgentRuntimeState.Playing || _state == AudioAgentRuntimeState.Loading || _state == AudioAgentRuntimeState.FadingIn || _state == AudioAgentRuntimeState.FadingOut;
        internal float StartedAt => _startedAt;
        internal int PlaybackPriority => _playbackPriority;

        public AudioAgent() { }

        internal void Initialize(AudioService service, AudioCategory category, int index, int globalIndex, AudioSourceObject sourceObject)
        {
            _service = service;
            _category = category;
            Index = index;
            GlobalIndex = globalIndex;
            HeapIndex = -1;
            ActiveIndex = -1;
            BindSource(sourceObject);
            ResetState();
        }

        internal ulong Play(AudioPlayRequest request)
        {
            StopImmediate(false);

            _generation++;
            if (_generation == int.MaxValue)
            {
                _generation = 1;
            }

            _handle = _service.AllocateHandle(this);
            _state = AudioAgentRuntimeState.Loading;
            _startedAt = Time.realtimeSinceStartup;
            _baseVolume = Mathf.Clamp01(request.Volume);
            _volumeFadeStart = _baseVolume;
            _volumeFadeTarget = _baseVolume;
            _volumeFadeTimer = 0f;
            _volumeFadeDuration = 0f;
            _pitch = request.Pitch <= 0f ? 1f : request.Pitch;
            _fadeInDuration = request.FadeInSeconds > 0f ? request.FadeInSeconds : 0f;
            _fadeInTimer = 0f;
            _fadeDuration = request.FadeOutSeconds > 0f ? request.FadeOutSeconds : DefaultFadeOutSeconds;
            _loop = request.Loop;
            _spatial = request.Spatial || request.FollowTarget != null || request.UseWorldPosition;
            _followTarget = request.FollowTarget;
            _followOffset = request.Position;
            _nextOcclusionCheckTime = 0f;
            _occluded = false;

            ApplySourceSettings(request);
            _category.MarkOccupied(this);

            if (request.Clip != null)
            {
                StartClip(request.Clip);
                return _handle;
            }

            if (string.IsNullOrEmpty(request.Address))
            {
                StopImmediate(true);
                return 0UL;
            }

            if (!_service.RequestClip(request.Address, request.Async, request.CachePolicy, this, _generation, out AudioClipCacheEntry entry, out AudioLoadRequest loadRequest))
            {
                StopImmediate(true);
                return 0UL;
            }

            _loadRequest = loadRequest;
            if (entry != null)
            {
                OnClipReady(entry, _generation);
            }

            return _handle;
        }

        internal bool OnClipReady(AudioClipCacheEntry entry, int generation)
        {
            if (_state != AudioAgentRuntimeState.Loading || generation != _generation || entry == null || entry.Clip == null)
            {
                return false;
            }

            _loadRequest = null;
            _clipEntry = entry;
            _service.RetainClip(entry);
            StartClip(entry.Clip);
            return true;
        }

        internal void OnClipLoadFailed(int generation)
        {
            if (_state == AudioAgentRuntimeState.Loading && generation == _generation)
            {
                _loadRequest = null;
                StopImmediate(true);
            }
        }

        internal void OnClipLoadCancelled(AudioLoadRequest request)
        {
            if (ReferenceEquals(_loadRequest, request))
            {
                _loadRequest = null;
            }
        }

        internal void Stop(bool fadeout)
        {
            if (_state == AudioAgentRuntimeState.Free)
            {
                return;
            }

            if (!fadeout || _state == AudioAgentRuntimeState.Loading)
            {
                StopImmediate(true);
                return;
            }

            _fadeTimer = _fadeDuration;
            _state = AudioAgentRuntimeState.FadingOut;
        }

        internal void Stop(float fadeOutSeconds)
        {
            if (_state == AudioAgentRuntimeState.Free)
            {
                return;
            }

            if (fadeOutSeconds <= 0f || _state == AudioAgentRuntimeState.Loading)
            {
                StopImmediate(true);
                return;
            }

            _fadeDuration = fadeOutSeconds;
            _fadeTimer = fadeOutSeconds;
            _state = AudioAgentRuntimeState.FadingOut;
        }

        internal void SetVolume(float volume, float fadeSeconds)
        {
            if (_state == AudioAgentRuntimeState.Free)
            {
                return;
            }

            float target = Mathf.Clamp01(volume);
            if (fadeSeconds <= 0f || _state == AudioAgentRuntimeState.Loading)
            {
                _baseVolume = target;
                _volumeFadeStart = target;
                _volumeFadeTarget = target;
                _volumeFadeTimer = 0f;
                _volumeFadeDuration = 0f;
                ApplyCurrentRuntimeVolume();
                return;
            }

            _volumeFadeStart = _baseVolume;
            _volumeFadeTarget = target;
            _volumeFadeTimer = 0f;
            _volumeFadeDuration = fadeSeconds;
        }

        internal void Update(float deltaTime)
        {
            if (_state == AudioAgentRuntimeState.Free)
            {
                return;
            }

            UpdateFollowTarget();
            UpdateOcclusion();
            bool volumeChanged = UpdateVolumeFade(deltaTime);

            if (_state == AudioAgentRuntimeState.FadingIn)
            {
                _fadeInTimer += deltaTime;
                if (_fadeInTimer >= _fadeInDuration)
                {
                    _state = AudioAgentRuntimeState.Playing;
                    ApplyRuntimeVolume(1f);
                }
                else
                {
                    float scale = _fadeInTimer / Mathf.Max(_fadeInDuration, MinFadeOutSeconds);
                    ApplyRuntimeVolume(scale);
                }
                return;
            }

            if (_state == AudioAgentRuntimeState.Playing)
            {
                if (!_loop && _source != null && !_source.isPlaying)
                {
                    StopImmediate(true);
                    return;
                }

                if (volumeChanged)
                {
                    ApplyRuntimeVolume(1f);
                }

                return;
            }

            if (_state != AudioAgentRuntimeState.FadingOut)
            {
                return;
            }

            _fadeTimer -= deltaTime;
            if (_fadeTimer <= 0f)
            {
                StopImmediate(true);
                return;
            }

            float fadeScale = _fadeTimer / Mathf.Max(_fadeDuration, MinFadeOutSeconds);
            ApplyRuntimeVolume(fadeScale);
        }

        internal void Shutdown()
        {
            StopImmediate(true);
            _sourceObject = null;
            _source = null;
            _lowPassFilter = null;
            _transform = null;
            _service = null;
            _category = null;
        }

        public override void Clear()
        {
            _service = null;
            _category = null;
            _sourceObject = null;
            _source = null;
            _lowPassFilter = null;
            _clipEntry = null;
            _loadRequest = null;
            _transform = null;
            _followTarget = null;
            _followOffset = Vector3.zero;
            _state = AudioAgentRuntimeState.Free;
            _spatial = false;
            _occluded = false;
            _loop = false;
            Index = 0;
            GlobalIndex = 0;
            HeapIndex = -1;
            ActiveIndex = -1;
            _generation = 0;
            _handle = 0UL;
            _baseVolume = 1f;
            _pitch = 1f;
            _volumeFadeStart = 1f;
            _volumeFadeTarget = 1f;
            _volumeFadeTimer = 0f;
            _volumeFadeDuration = 0f;
            _fadeInTimer = 0f;
            _fadeInDuration = 0f;
            _fadeTimer = 0f;
            _fadeDuration = 0f;
            _startedAt = 0f;
            _nextOcclusionCheckTime = 0f;
            _playbackPriority = 0;
        }

        internal void FillDebugInfo(AudioAgentDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            info.Type = _category != null ? _category.Type : AudioType.Sound;
            info.State = _state;
            info.Index = Index;
            info.GlobalIndex = GlobalIndex;
            info.ActiveIndex = ActiveIndex;
            info.Handle = _handle;
            info.Address = _clipEntry != null ? _clipEntry.Address : null;
            info.Clip = _source != null ? _source.clip : null;
            info.FollowTarget = _followTarget;
            info.Position = _transform != null ? _transform.position : Vector3.zero;
            info.Loop = _loop;
            info.Spatial = _spatial;
            info.Occluded = _occluded;
            info.Volume = _source != null ? _source.volume : 0f;
            info.Pitch = _source != null ? _source.pitch : 0f;
            info.SpatialBlend = _source != null ? _source.spatialBlend : 0f;
            info.MinDistance = _source != null ? _source.minDistance : 0f;
            info.MaxDistance = _source != null ? _source.maxDistance : 0f;
            info.StartedAt = _startedAt;
            info.Priority = _playbackPriority;
        }

        private void BindSource(AudioSourceObject sourceObject)
        {
            _sourceObject = sourceObject;
            _source = sourceObject.Source;
            _lowPassFilter = sourceObject.LowPassFilter;
            _transform = _source.transform;
        }

        private void StartClip(AudioClip clip)
        {
            if (_source == null || clip == null)
            {
                StopImmediate(true);
                return;
            }

            _source.clip = clip;
            _source.loop = _loop;

            if (_fadeInDuration > 0f)
            {
                _fadeInTimer = 0f;
                _state = AudioAgentRuntimeState.FadingIn;
                ApplyRuntimeVolume(0f);
            }
            else
            {
                _state = AudioAgentRuntimeState.Playing;
                ApplyRuntimeVolume(1f);
            }

            _source.Play();
        }

        private void StopImmediate(bool notifyCategory)
        {
            if (_state == AudioAgentRuntimeState.Free)
            {
                return;
            }

            _generation++;
            if (_generation == int.MaxValue)
            {
                _generation = 1;
            }

            if (_source != null)
            {
                _source.Stop();
                _source.clip = null;
            }

            CancelLoadRequest();
            ReleaseClip();

            ulong handle = _handle;
            if (handle != 0)
            {
                _service.ReleaseHandle(handle, this);
            }

            ResetState();

            if (notifyCategory)
            {
                _category.MarkFree(this);
            }
        }

        private void ReleaseClip()
        {
            AudioClipCacheEntry entry = _clipEntry;
            if (entry != null)
            {
                _clipEntry = null;
                _service.ReleaseClip(entry);
            }
        }

        private void CancelLoadRequest()
        {
            AudioLoadRequest request = _loadRequest;
            if (request == null)
            {
                return;
            }

            _loadRequest = null;
            _service.CancelLoadRequest(request);
        }

        private void ResetState()
        {
            _state = AudioAgentRuntimeState.Free;
            _followTarget = null;
            _followOffset = Vector3.zero;
            _spatial = false;
            _occluded = false;
            _loop = false;
            _handle = 0;
            _baseVolume = 1f;
            _pitch = 1f;
            _volumeFadeStart = 1f;
            _volumeFadeTarget = 1f;
            _volumeFadeTimer = 0f;
            _volumeFadeDuration = 0f;
            _fadeInTimer = 0f;
            _fadeInDuration = 0f;
            _fadeTimer = 0f;
            _fadeDuration = 0f;
            _startedAt = 0f;
            _nextOcclusionCheckTime = 0f;
            _playbackPriority = 0;

            if (_source != null)
            {
                _source.volume = 1f;
                _source.pitch = 1f;
                _source.loop = false;
            }

            if (_lowPassFilter != null)
            {
                _lowPassFilter.enabled = false;
                _lowPassFilter.cutoffFrequency = MaxCutoffFrequency;
            }
        }

        private void ApplySourceSettings(AudioPlayRequest request)
        {
            AudioGroupConfig config = _category.Config;
            _source.playOnAwake = false;
            _source.mute = false;
            _source.bypassEffects = false;
            _source.bypassListenerEffects = false;
            _source.bypassReverbZones = false;
            _playbackPriority = ResolvePlaybackPriority(request, config);
            _source.priority = ResolveUnitySourcePriority(_playbackPriority);
            _source.pitch = _pitch;
            _source.rolloffMode = request.OverrideSpatialSettings ? request.RolloffMode : config.RolloffMode;
            _source.minDistance = request.OverrideSpatialSettings ? request.MinDistance : config.MinDistance;
            _source.maxDistance = request.OverrideSpatialSettings ? request.MaxDistance : config.MaxDistance;
            _source.dopplerLevel = config.DopplerLevel;
            _source.spread = config.Spread;
            _source.reverbZoneMix = config.ReverbZoneMix;
            _source.outputAudioMixerGroup = _category.MixerGroup;
            _source.spatialBlend = _spatial ? ResolveSpatialBlend(request, config) : 0f;

            Transform transform = _source.transform;
            if (_followTarget != null)
            {
                transform.SetParent(_category.InstanceRoot, false);
                transform.position = _followTarget.position + _followOffset;
                transform.rotation = _followTarget.rotation;
            }
            else
            {
                transform.SetParent(_category.InstanceRoot, false);
                if (request.UseWorldPosition)
                {
                    transform.position = request.Position;
                }
                else
                {
                    transform.localPosition = Vector3.zero;
                }
            }
        }

        private static float ResolveSpatialBlend(AudioPlayRequest request, AudioGroupConfig config)
        {
            if (request.SpatialBlend >= 0f)
            {
                return Mathf.Clamp01(request.SpatialBlend);
            }

            return config.SpatialBlend;
        }

        private static int ResolvePlaybackPriority(AudioPlayRequest request, AudioGroupConfig config)
        {
            if (request.Priority > 0)
            {
                return Mathf.Clamp(request.Priority, 0, 256);
            }

            return Mathf.Clamp(256 - config.SourcePriority, 0, 256);
        }

        private static int ResolveUnitySourcePriority(int playbackPriority)
        {
            return Mathf.Clamp(256 - playbackPriority, 0, 256);
        }

        private void UpdateFollowTarget()
        {
            if (_followTarget == null)
            {
                return;
            }

            if (_transform == null)
            {
                return;
            }

            if (!_followTarget.gameObject.activeInHierarchy)
            {
                StopImmediate(true);
                return;
            }

            _transform.position = _followTarget.position + _followOffset;
            _transform.rotation = _followTarget.rotation;
        }

        private void UpdateOcclusion()
        {
            AudioGroupConfig config = _category.Config;
            if (!_spatial || !config.OcclusionEnabled || _transform == null)
            {
                return;
            }

            Transform listener = _service.ListenerTransform;
            if (listener == null)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextOcclusionCheckTime)
            {
                return;
            }

            _nextOcclusionCheckTime = now + config.OcclusionCheckInterval;
            Vector3 origin = _transform.position;
            Vector3 target = listener.position;
            Vector3 direction = target - origin;
            float distance = direction.magnitude;
            if (distance <= 0.01f)
            {
                SetOccluded(false, config);
                return;
            }

            bool occluded = Physics.Raycast(origin, direction / distance, distance, config.OcclusionMask, QueryTriggerInteraction.Ignore);
            SetOccluded(occluded, config);
        }

        private void SetOccluded(bool occluded, AudioGroupConfig config)
        {
            if (_occluded == occluded)
            {
                return;
            }

            _occluded = occluded;
            if (_lowPassFilter != null)
            {
                _lowPassFilter.enabled = occluded;
                _lowPassFilter.cutoffFrequency = occluded ? config.OcclusionLowPassCutoff : MaxCutoffFrequency;
            }

            ApplyCurrentRuntimeVolume();
        }

        private bool UpdateVolumeFade(float deltaTime)
        {
            if (_volumeFadeDuration <= 0f)
            {
                return false;
            }

            _volumeFadeTimer += deltaTime;
            if (_volumeFadeTimer >= _volumeFadeDuration)
            {
                _baseVolume = _volumeFadeTarget;
                _volumeFadeTimer = 0f;
                _volumeFadeDuration = 0f;
                return true;
            }

            float t = _volumeFadeTimer / Mathf.Max(_volumeFadeDuration, MinFadeOutSeconds);
            _baseVolume = Mathf.Lerp(_volumeFadeStart, _volumeFadeTarget, t);
            return true;
        }

        private void ApplyCurrentRuntimeVolume()
        {
            float fadeScale = 1f;
            if (_state == AudioAgentRuntimeState.FadingIn)
            {
                fadeScale = _fadeInDuration > 0f ? _fadeInTimer / Mathf.Max(_fadeInDuration, MinFadeOutSeconds) : 1f;
            }
            else if (_state == AudioAgentRuntimeState.FadingOut)
            {
                fadeScale = _fadeTimer / Mathf.Max(_fadeDuration, MinFadeOutSeconds);
            }

            ApplyRuntimeVolume(fadeScale);
        }

        private void ApplyRuntimeVolume(float fadeScale)
        {
            if (_source == null)
            {
                return;
            }

            float occlusionScale = _occluded ? _category.Config.OcclusionVolumeMultiplier : 1f;
            _source.volume = _baseVolume * occlusionScale * fadeScale;
        }
    }
}
