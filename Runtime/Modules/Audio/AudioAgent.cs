using AlicizaX;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioAgent
    {
        private const float DefaultFadeOutSeconds = 0.15f;
        private const string SourceObjectName = "AudioSource";

        private struct Ramp
        {
            public float Value;
            public float Target;
            public float Rate;

            public Ramp(float value, float target, float rate)
            {
                Value = value;
                Target = target;
                Rate = rate;
            }

            public bool Advance(float deltaTime)
            {
                if (Rate <= 0f)
                {
                    return false;
                }

                float step = Rate * deltaTime;
                float remaining = Target - Value;
                if (remaining > step)
                {
                    Value += step;
                }
                else if (remaining < -step)
                {
                    Value -= step;
                }
                else
                {
                    Value = Target;
                    Rate = 0f;
                }

                return true;
            }
        }

        private AudioService _service;
        private AudioCategory _category;
        private GameObject _host;
        private AudioSource _source;
        private AudioLowPassFilter _lowPassFilter;
        private AudioClip _playingClip;
        private AudioClipCacheEntry _clipEntry;
        private AudioLoadRequest _loadRequest;
        private Transform _transform;
        private Transform _followTarget;
        private Vector3 _followOffset;
        private AudioAgentRuntimeState _state;
        private bool _spatial;
        private bool _occluded;
        private bool _loop;
        private ulong _handle;
        private Ramp _volume;
        private Ramp _envelope;
        private float _fadeOutSeconds;
        private float _nextOcclusionCheckTime;
        private int _playbackPriority;

        internal int Index;
        internal int GlobalIndex { get; private set; }
        internal AudioAgent PriorityPrev;
        internal AudioAgent PriorityNext;
        internal ulong Handle => _handle;
        internal bool IsPlayingState => _state != AudioAgentRuntimeState.Free;
        internal int PlaybackPriority => _playbackPriority;

        internal void Initialize(AudioService service, AudioCategory category, int index, int globalIndex)
        {
            _service = service;
            _category = category;
            Index = index;
            GlobalIndex = globalIndex;
            CreateSource();
        }

        internal ulong Play(in AudioPlayRequest request, int playbackPriority)
        {
            if (_state != AudioAgentRuntimeState.Free)
            {
                StopImmediate(false);
            }

            _handle = _service.Handles.Allocate(this);
            _state = AudioAgentRuntimeState.Loading;

            if (_source == null)
            {
                _host.SafeDestroySelf();
                CreateSource();
            }
            else if (!_source.isActiveAndEnabled)
            {
                _host.SetActive(true);
                _source.enabled = true;
            }

            float volume = Mathf.Clamp01(request.Volume);
            float fadeInSeconds = request.Options.FadeInSeconds;
            _volume = new Ramp(volume, volume, 0f);
            _envelope = fadeInSeconds > 0f ? new Ramp(0f, 1f, 1f / fadeInSeconds) : new Ramp(1f, 1f, 0f);
            _fadeOutSeconds = request.Options.FadeOutSeconds > 0f ? request.Options.FadeOutSeconds : DefaultFadeOutSeconds;
            _loop = request.Loop;
            _spatial = request.Spatial;
            _followTarget = request.FollowTarget;
            _followOffset = request.Position;
            _nextOcclusionCheckTime = 0f;
            _playbackPriority = playbackPriority;

            ApplySourceSettings(in request);
            _category.MarkOccupied(this);

            if (request.Address == null)
            {
                StartClip(request.Clip);
            }
            else if (!_service.RequestClip(request.Address, request.Options.Async, request.Options.CachePolicy, this))
            {
                StopImmediate(true);
            }

            return _handle;
        }

        internal void OnClipReady(AudioClipCacheEntry entry)
        {
            _loadRequest = null;
            _clipEntry = entry;
            _service.RetainClip(entry);
            StartClip(entry.Clip);
        }

        internal void OnClipLoadFailed()
        {
            _loadRequest = null;
            StopImmediate(true);
        }

        internal void SetLoadRequest(AudioLoadRequest request)
        {
            _loadRequest = request;
        }

        internal void Stop(bool fadeout)
        {
            Stop(fadeout ? _fadeOutSeconds : 0f);
        }

        internal void Stop(float fadeOutSeconds)
        {
            if (fadeOutSeconds <= 0f || _state == AudioAgentRuntimeState.Loading || _envelope.Value <= 0f)
            {
                StopImmediate(true);
                return;
            }

            _envelope = new Ramp(_envelope.Value, 0f, _envelope.Value / fadeOutSeconds);
            _state = AudioAgentRuntimeState.FadingOut;
        }

        internal void SetVolume(float volume, float fadeSeconds)
        {
            float target = Mathf.Clamp01(volume);
            if (fadeSeconds > 0f && _state != AudioAgentRuntimeState.Loading)
            {
                _volume = new Ramp(_volume.Value, target, Mathf.Abs(target - _volume.Value) / fadeSeconds);
                return;
            }

            _volume = new Ramp(target, target, 0f);
            if (_state != AudioAgentRuntimeState.Loading && _source != null)
            {
                ApplyRuntimeVolume();
            }
        }

        internal void Update(float deltaTime, float now, Transform listener)
        {
            if (_source == null || !_source.isActiveAndEnabled)
            {
                StopImmediate(true);
                return;
            }

            if (!ReferenceEquals(_followTarget, null))
            {
                if (_followTarget == null || !_followTarget.gameObject.activeInHierarchy)
                {
                    StopImmediate(true);
                    return;
                }

                _transform.position = _followTarget.TransformPoint(_followOffset);
            }

            if (_state == AudioAgentRuntimeState.Loading)
            {
                return;
            }

            if (_playingClip == null || (!_loop && !_source.isPlaying))
            {
                StopImmediate(true);
                return;
            }

            bool volumeChanged = false;
            if (_spatial && _category.Config.OcclusionEnabled && listener != null && now >= _nextOcclusionCheckTime)
            {
                volumeChanged = UpdateOcclusion(now, listener);
            }

            volumeChanged |= _volume.Advance(deltaTime);
            if (_envelope.Advance(deltaTime))
            {
                volumeChanged = true;
                if (_envelope.Rate <= 0f)
                {
                    if (_state == AudioAgentRuntimeState.FadingOut)
                    {
                        StopImmediate(true);
                        return;
                    }

                    _state = AudioAgentRuntimeState.Playing;
                }
            }

            if (volumeChanged)
            {
                ApplyRuntimeVolume();
            }
        }

        internal void FillDebugInfo(AudioAgentDebugInfo info)
        {
            info.Type = _category.Type;
            info.State = _state;
            info.Index = Index;
            info.Handle = _handle;
            info.Address = _clipEntry != null ? _clipEntry.Address : null;
            info.Spatial = _spatial;
            info.Occluded = _occluded;
            bool hasSource = _source != null;
            info.Clip = hasSource ? _source.clip : null;
            info.Volume = hasSource ? _source.volume : 0f;
            info.MinDistance = hasSource ? _source.minDistance : 0f;
            info.MaxDistance = hasSource ? _source.maxDistance : 0f;
        }

        private void CreateSource()
        {
            AudioGroupConfig config = _category.Config;
            _host = new GameObject(SourceObjectName);
            _transform = _host.transform;
            _transform.SetParent(_category.InstanceRoot, false);
            _source = _host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.outputAudioMixerGroup = config.MixerGroup;
            _source.dopplerLevel = config.DopplerLevel;
            _source.spread = config.Spread;
            _source.reverbZoneMix = config.ReverbZoneMix;
            if (config.OcclusionEnabled)
            {
                _lowPassFilter = _host.AddComponent<AudioLowPassFilter>();
                _lowPassFilter.cutoffFrequency = config.OcclusionLowPassCutoff;
                _lowPassFilter.enabled = false;
            }
        }

        private void StartClip(AudioClip clip)
        {
            if (_source == null)
            {
                StopImmediate(true);
                return;
            }

            _playingClip = clip;
            _source.clip = clip;
            _source.loop = _loop;
            _state = _envelope.Rate > 0f ? AudioAgentRuntimeState.FadingIn : AudioAgentRuntimeState.Playing;
            ApplyRuntimeVolume();
            _source.Play();
        }

        private void StopImmediate(bool notifyCategory)
        {
            if (_source != null)
            {
                _source.Stop();
                _source.clip = null;
            }

            CancelLoadRequest();
            ReleaseClip();
            _service.Handles.Release(this);

            if (notifyCategory)
            {
                _category.MarkFree(this);
            }

            _state = AudioAgentRuntimeState.Free;
            _handle = 0UL;
            _playingClip = null;
            _followTarget = null;
            if (_occluded)
            {
                _occluded = false;
                _lowPassFilter.enabled = false;
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

        private void ApplySourceSettings(in AudioPlayRequest request)
        {
            _source.priority = AudioCategory.MaxPlaybackPriority - _playbackPriority;
            _source.pitch = request.Options.Pitch > 0f ? request.Options.Pitch : 1f;
            if (!_spatial)
            {
                _source.spatialBlend = 0f;
                return;
            }

            AudioGroupConfig config = _category.Config;
            if (request.SpatialOptions.Override)
            {
                float minDistance = Mathf.Max(0f, request.SpatialOptions.MinDistance);
                _source.rolloffMode = request.SpatialOptions.RolloffMode;
                _source.minDistance = minDistance;
                _source.maxDistance = Mathf.Max(minDistance, request.SpatialOptions.MaxDistance);
                _source.spatialBlend = Mathf.Clamp01(request.SpatialOptions.SpatialBlend);
            }
            else
            {
                _source.rolloffMode = config.RolloffMode;
                _source.minDistance = config.MinDistance;
                _source.maxDistance = config.MaxDistance;
                _source.spatialBlend = config.SpatialBlend;
            }

            _transform.position = ReferenceEquals(_followTarget, null)
                ? request.Position
                : _followTarget.TransformPoint(_followOffset);
        }

        private bool UpdateOcclusion(float now, Transform listener)
        {
            AudioGroupConfig config = _category.Config;
            _nextOcclusionCheckTime = now + config.OcclusionCheckInterval;
            Vector3 origin = _transform.position;
            Vector3 direction = listener.position - origin;
            float distance = direction.magnitude;
            bool occluded = distance > 0.01f
                && Physics.Raycast(origin, direction / distance, distance, config.OcclusionMask, QueryTriggerInteraction.Ignore);
            if (_occluded == occluded)
            {
                return false;
            }

            _occluded = occluded;
            _lowPassFilter.enabled = occluded;
            return true;
        }

        private void ApplyRuntimeVolume()
        {
            float occlusionScale = _occluded ? _category.Config.OcclusionVolumeMultiplier : 1f;
            _source.volume = _volume.Value * _envelope.Value * occlusionScale;
        }
    }
}
